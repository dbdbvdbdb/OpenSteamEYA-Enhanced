namespace OpenSteamEya.AccountManagement;

/// <summary>Paste-only account----password format. Quotes and backslashes are literal password characters.</summary>
public static class BatchAccountInput
{
    public static ImportPreview Parse(string text)
    {
        if (text.Length > 32 * 1024 * 1024) return new([], [new(1, "输入超过 32MB 限制")]);
        var rows = new List<ImportRow>(); var issues = new List<ImportIssue>();
        using var reader = new StringReader(text.TrimStart('\uFEFF'));
        int lineNumber = 0;
        while (reader.ReadLine() is { } line)
        {
            lineNumber++;
            if (string.IsNullOrWhiteSpace(line)) continue;
            int delimiter = line.IndexOf("----", StringComparison.Ordinal);
            if (delimiter < 0) { issues.Add(new(lineNumber, "缺少 ---- 分隔符")); continue; }
            string loginName = line[..delimiter].Trim();
            if (loginName.Length == 0) { issues.Add(new(lineNumber, "登录账号为空")); continue; }
            rows.Add(new(lineNumber, new() { LoginName = loginName, Password = line[(delimiter + 4)..] }));
        }
        return new(rows, issues);
    }
}
