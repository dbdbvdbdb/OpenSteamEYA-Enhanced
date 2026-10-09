using System.Text;
using System.Text.Json;

namespace OpenSteamEya.AccountManagement;

public static class ImportExport
{
    public const string TxtHeader = "# OpenSteamEYA-TXT-v1 escaped";
    public const string TxtMetadataHeader = "# OpenSteamEYA-TXT-v2 escaped loginName,password,steamId,nickname,note";
    public static ImportPreview Parse(string text, string format)
    {
        var rows = new List<ImportRow>(); var issues = new List<ImportIssue>();
        void Add(int line, ManagedAccount account)
        {
            account.LoginName = account.LoginName.Trim();
            if (string.IsNullOrWhiteSpace(account.LoginName)) { issues.Add(new(line, "登录账号为空")); return; }
            if (account.SteamId.Length > 0 && (account.SteamId.Length != 17 || !ulong.TryParse(account.SteamId, out _)))
            { issues.Add(new(line, "SteamID 格式无效")); return; }
            // Never return parser exception text: it may contain an input password.
            rows.Add(new(line, account));
        }
        if (text.Length > 32 * 1024 * 1024) return new([], [new(1, "文件超过 32MB 限制")]);
        switch (format.ToLowerInvariant().TrimStart('.'))
        {
            case "json":
                try
                {
                    using var document = JsonDocument.Parse(text);
                    if (document.RootElement.ValueKind != JsonValueKind.Array)
                        return new([], [new(1, "JSON 顶层必须为账号数组")]);
                    int line = 0;
                    foreach (var element in document.RootElement.EnumerateArray())
                    {
                        line++;
                        try
                        {
                            var account = element.Deserialize(ManagerJsonContext.Default.ManagedAccount);
                            if (account is null || account.LoginName is null || account.Password is null || account.SteamId is null || account.Note is null || account.Nickname is null)
                                throw new JsonException();
                            // Portable imports cannot inject a mapping, resource path or persisted record ID.
                            account.Id = Guid.NewGuid(); account.LinkedKey = null; account.Background = new(); AccountGroups.Set(account, []);
                            Add(line, account);
                        }
                        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
                        { issues.Add(new(line, "账号对象字段格式错误")); }
                    }
                }
                catch (JsonException ex) { issues.Add(new((int)(ex.LineNumber ?? 0) + 1, "JSON 格式错误")); }
                break;
            case "csv":
                try
                {
                    var records = ReadCsv(text);
                    if (records.Count == 0) break;
                    var headers = records[0].Fields.Select(x => x.Trim().TrimStart('\uFEFF').ToLowerInvariant()).ToList();
                    if (headers.Distinct().Count() != headers.Count) return new([], [new(1, "CSV 表头字段重复")]);
                    int login = headers.IndexOf("loginname"), password = headers.IndexOf("password");
                    if (login < 0 || password < 0) return new([], [new(1, "CSV 需包含 loginName,password 表头")]);
                    foreach (var record in records.Skip(1))
                    {
                        if (record.Fields.Count != headers.Count) { issues.Add(new(record.Line, "CSV 列数与表头不一致")); continue; }
                        string Value(string key) { int i = headers.IndexOf(key); return i < 0 ? "" : record.Fields[i]; }
                        Add(record.Line, new() { LoginName = record.Fields[login], Password = record.Fields[password],
                            SteamId = Value("steamid"), Nickname = Value("nickname"), Note = Value("note") });
                    }
                }
                catch (CsvException ex) { issues.Add(new(ex.Line, "CSV 引号或分隔结构错误")); rows.Clear(); }
                break;
            case "txt":
                var lines = text.TrimStart('\uFEFF').Replace("\r\n", "\n").Split('\n');
                bool metadata = lines.FirstOrDefault() == TxtMetadataHeader;
                bool escaped = metadata || lines.FirstOrDefault() == TxtHeader;
                for (int i = escaped ? 1 : 0; i < lines.Length; i++)
                {
                    var line = lines[i].TrimEnd('\r');
                    if (line.Length == 0) continue;
                    int delimiter = line.IndexOf("----", StringComparison.Ordinal);
                    if (delimiter < 0) { issues.Add(new(i + 1, "缺少 ---- 分隔符")); continue; }
                    try
                    {
                        if (metadata)
                        {
                            var fields = line.Split("----", StringSplitOptions.None);
                            if (fields.Length != 5) throw new FormatException();
                            Add(i + 1, new() { LoginName = Unescape(fields[0]), Password = Unescape(fields[1]), SteamId = Unescape(fields[2]), Nickname = Unescape(fields[3]), Note = Unescape(fields[4]) });
                            continue;
                        }
                        var name = line[..delimiter]; var pass = line[(delimiter + 4)..];
                        Add(i + 1, new() { LoginName = escaped ? Unescape(name) : name, Password = escaped ? Unescape(pass) : pass });
                    }
                    catch (FormatException) { issues.Add(new(i + 1, "TXT 转义格式错误")); }
                }
                break;
            default: issues.Add(new(1, "仅支持 TXT、CSV、JSON")); break;
        }
        return new(rows, issues);
    }

    public static string Export(IEnumerable<ManagedAccount> accounts, string format, bool includeNote = true, bool includeSteamId = true)
    {
        var items = accounts.Select(AccountStore.Clone).ToList();
        foreach (var item in items)
        {
            if (!includeNote) item.Note = "";
            if (!includeSteamId) item.SteamId = "";
            item.LinkedKey = null; item.Background = new(); AccountGroups.Set(item, []);
        }
        switch (format.ToLowerInvariant().TrimStart('.'))
        {
            case "json": return JsonSerializer.Serialize(items, ManagerJsonContext.Default.ListManagedAccount);
            case "txt":
                if (includeNote || includeSteamId) return TxtMetadataHeader + "\r\n" + string.Join("\r\n", items.Select(x =>
                    string.Join("----", new[] { x.LoginName, x.Password, x.SteamId, x.Nickname, x.Note }.Select(Escape)))) + "\r\n";
                return TxtHeader + "\r\n" + string.Join("\r\n", items.Select(x => Escape(x.LoginName) + "----" + Escape(x.Password))) + "\r\n";
            case "csv":
                var header = new List<string> { "loginName", "password", "nickname" };
                if (includeSteamId) header.Add("steamId"); if (includeNote) header.Add("note");
                var output = new StringBuilder(string.Join(",", header) + "\r\n");
                foreach (var item in items)
                {
                    var fields = new List<string> { item.LoginName, item.Password, item.Nickname };
                    if (includeSteamId) fields.Add(item.SteamId); if (includeNote) fields.Add(item.Note);
                    output.AppendJoin(",", fields.Select(Quote)).Append("\r\n");
                }
                return output.ToString();
            default: throw new InvalidDataException("不支持的导出格式。");
        }
    }
    private static string Quote(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";
    private static string Escape(string value) => value.Replace("\\", "\\\\").Replace("-", "\\h").Replace("\r", "\\r").Replace("\n", "\\n");
    private static string Unescape(string value)
    {
        var output = new StringBuilder();
        for (int i = 0; i < value.Length; i++)
        {
            if (value[i] != '\\') { output.Append(value[i]); continue; }
            if (++i >= value.Length) throw new FormatException();
            output.Append(value[i] switch { '\\' => '\\', 'h' => '-', 'r' => '\r', 'n' => '\n', _ => throw new FormatException() });
        }
        return output.ToString();
    }
    private sealed record CsvRecord(int Line, List<string> Fields);
    private sealed class CsvException(int line) : Exception { public int Line { get; } = line; }
    private static List<CsvRecord> ReadCsv(string text)
    {
        var records = new List<CsvRecord>(); var fields = new List<string>(); var field = new StringBuilder();
        bool quoted = false, closed = false; int line = 1, startLine = 1;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (quoted)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                    else { quoted = false; closed = true; }
                }
                else { field.Append(c); if (c == '\n') line++; }
                continue;
            }
            if (c == '"')
            { if (field.Length != 0 || closed) throw new CsvException(line); quoted = true; continue; }
            if (c == ',' || c == '\r' || c == '\n')
            {
                fields.Add(field.ToString()); field.Clear(); closed = false;
                if (c == ',') continue;
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                if (fields.Any(x => x.Length > 0)) records.Add(new(startLine, fields));
                fields = []; line++; startLine = line; continue;
            }
            if (closed) throw new CsvException(line);
            field.Append(c);
        }
        if (quoted) throw new CsvException(startLine);
        if (field.Length > 0 || fields.Count > 0 || closed) { fields.Add(field.ToString()); records.Add(new(startLine, fields)); }
        return records;
    }
}
