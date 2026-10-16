using System.Text;

namespace SteamEyaWinUI.Cs2Settings;

// A lossless KeyValues editor. Unchanged tokens, comments, whitespace and encoding remain byte-for-byte intact.
internal sealed class ConfigText
{
    internal sealed record Entry(string Section, string Key, string Value, int Start, int Length);
    private sealed record Token(string Value, int Start, int Length, char Kind);
    private readonly string _text;
    private readonly Encoding _encoding;
    private readonly byte[] _bom;
    private readonly List<Token> _tokens = [];
    private readonly Dictionary<string, int> _ends = new(StringComparer.Ordinal);
    public List<Entry> Entries { get; } = [];
    public ConfigText(byte[] data)
    {
        if (data.Length > 2 * 1024 * 1024) throw new InvalidDataException("配置超过 2 MiB，无法安全编辑。");
        int offset = 0;
        if (data.AsSpan().StartsWith(new byte[] { 0xff, 0xfe })) { _encoding = new UnicodeEncoding(false, false, true); offset = 2; }
        else if (data.AsSpan().StartsWith(new byte[] { 0xfe, 0xff })) { _encoding = new UnicodeEncoding(true, false, true); offset = 2; }
        else { _encoding = new UTF8Encoding(false, true); if (data.AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf })) offset = 3; }
        _bom = data[..offset]; _text = _encoding.GetString(data, offset, data.Length - offset);
        Tokenize(); int index = 0; Parse(ref index, "", 0);
    }
    private void Tokenize()
    {
        for (int p = 0; p < _text.Length;)
        {
            if (char.IsWhiteSpace(_text[p])) { p++; continue; }
            if (_text[p] == '/' && p + 1 < _text.Length && _text[p + 1] == '/')
            { while (p < _text.Length && _text[p] != '\n') p++; continue; }
            if (_text[p] == '/' && p + 1 < _text.Length && _text[p + 1] == '*')
            {
                int close = _text.IndexOf("*/", p + 2, StringComparison.Ordinal);
                if (close < 0) throw new InvalidDataException("配置注释未闭合。");
                p = close + 2; continue;
            }
            if (_text[p] is '{' or '}') { _tokens.Add(new("", p, 1, _text[p++])); continue; }
            int start = p; var value = new StringBuilder();
            if (_text[p] == '"')
            {
                p++; bool closed = false;
                while (p < _text.Length)
                {
                    char c = _text[p++];
                    if (c == '"') { closed = true; break; }
                    if (c == '\\' && p < _text.Length && _text[p] is '"' or '\\') c = _text[p++];
                    value.Append(c);
                }
                if (!closed) throw new InvalidDataException("配置引号未闭合。");
            }
            else
            {
                while (p < _text.Length && !char.IsWhiteSpace(_text[p]) && _text[p] is not '{' and not '}') value.Append(_text[p++]);
                if (value.ToString().StartsWith('#') || value.ToString().StartsWith('[')) throw new InvalidDataException("配置含不支持的指令，保留原文件。");
            }
            _tokens.Add(new(value.ToString(), start, p - start, 's'));
        }
    }
    private void Parse(ref int index, string section, int depth)
    {
        if (depth > 16) throw new InvalidDataException("配置层级过深。");
        while (index < _tokens.Count)
        {
            var key = _tokens[index++];
            if (key.Kind == '}')
            {
                if (depth == 0 || !_ends.TryAdd(section, key.Start)) throw new InvalidDataException("配置存在重复区域或多余括号。");
                return;
            }
            if (key.Kind != 's' || index >= _tokens.Count) throw new InvalidDataException("配置结构不完整。");
            var value = _tokens[index++];
            if (value.Kind == '{') Parse(ref index, section.Length == 0 ? key.Value : section + "/" + key.Value, depth + 1);
            else if (value.Kind == 's') Entries.Add(new(section, key.Value, value.Value, value.Start, value.Length));
            else throw new InvalidDataException("配置缺少键值。");
        }
        if (depth != 0) throw new InvalidDataException("配置括号未闭合。");
    }
    public Entry? Find(string section, string key)
    {
        var matches = Entries.Where(e => e.Section == section && e.Key == key).ToArray();
        if (matches.Length > 1) throw new InvalidDataException("配置存在重复键：" + key);
        return matches.SingleOrDefault();
    }
    public bool HasSection(string section) => _ends.ContainsKey(section);
    public void EnsureUnique()
    {
        if (_ends.Keys.GroupBy(s => s, StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1) ||
            Entries.GroupBy(e => (e.Section.ToUpperInvariant(), e.Key.ToUpperInvariant())).Any(g => g.Count() > 1))
            throw new InvalidDataException("配置存在重复键（包含大小写重复），不能安全编辑。");
    }
    public byte[] Patch(string section, IReadOnlyDictionary<string, string> changes, bool allowInsert = false)
    {
        if (!_ends.TryGetValue(section, out int end)) throw new InvalidDataException("配置缺少区域：" + section);
        var edits = new List<(int Start, int Length, string Text)>(); var insert = new StringBuilder();
        var newline = _text.Contains("\r\n") ? "\r\n" : "\n";
        foreach (var (key, value) in changes)
        {
            if (key.Any(char.IsControl) || value.Any(char.IsControl)) throw new InvalidDataException("设置值不能包含换行或控制字符。");
            var entry = Find(section, key);
            if (entry is null)
            {
                if (!allowInsert) throw new InvalidDataException("原配置缺少设置：" + key);
                insert.Append("\t\t").Append(Quote(key)).Append("\t\t").Append(Quote(value)).Append(newline);
            }
            else if (entry.Value != value) edits.Add((entry.Start, entry.Length, Quote(value)));
        }
        if (insert.Length > 0) edits.Add((end, 0, newline + insert + "\t"));
        var result = new StringBuilder(_text);
        foreach (var edit in edits.OrderByDescending(e => e.Start)) result.Remove(edit.Start, edit.Length).Insert(edit.Start, edit.Text);
        return [.. _bom, .. _encoding.GetBytes(result.ToString())];
    }
    private static string Quote(string value) => "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
}
