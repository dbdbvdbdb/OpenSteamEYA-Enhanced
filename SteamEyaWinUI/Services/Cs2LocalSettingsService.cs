using System.Globalization;
using System.Text;

namespace SteamEyaWinUI.Services;

internal enum Cs2SettingCategory { Keys, Video, Game, Mouse, Crosshair, Audio, Other }
internal sealed record Cs2LocalAccount(uint AccountId)
{
    public string SteamId64 => (76561197960265728UL + AccountId).ToString(CultureInfo.InvariantCulture);
}
internal sealed record Cs2SettingsIssue(string Path, string Reason);
internal sealed record Cs2AccountScan(IReadOnlyList<Cs2LocalAccount> Accounts, IReadOnlyList<Cs2SettingsIssue> Issues);
internal sealed record Cs2LocalSetting(Cs2SettingCategory Category, string Section, string Key, string Value);
internal sealed record Cs2LocalConfigFile(string Path, string RelativePath, bool IsCloudCache,
    DateTime LastWriteTimeUtc, IReadOnlyList<Cs2LocalSetting> Settings);
internal sealed record Cs2LocalSettingsSnapshot(IReadOnlyList<Cs2LocalConfigFile> Files, IReadOnlyList<Cs2SettingsIssue> Issues);

/// <summary>Reads only CS2 configuration files. Never executes commands or writes to Steam/userdata.</summary>
internal sealed class Cs2LocalSettingsService
{
    public const int MaxFileBytes = 4 * 1024 * 1024;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly string[] ConfigFolders = ["local/cfg", "remote", "remote/cfg"];

    public Cs2AccountScan ScanAccounts(string userdataPath, CancellationToken cancellationToken = default)
    {
        var accounts = new List<Cs2LocalAccount>();
        var issues = new List<Cs2SettingsIssue>();
        if (!Directory.Exists(userdataPath)) return new(accounts, issues);
        try
        {
            foreach (var directory in Directory.EnumerateDirectories(userdataPath))
            {
                cancellationToken.ThrowIfCancellationRequested();
                string name = Path.GetFileName(directory);
                if (!uint.TryParse(name, NumberStyles.None, CultureInfo.InvariantCulture, out uint id) || id == 0 ||
                    id.ToString(CultureInfo.InvariantCulture) != name) continue;
                // Include accounts with an existing 730 directory even if no readable files remain,
                // so an empty/missing configuration can be explained after selecting the account.
                if (Directory.Exists(Path.Combine(directory, "730"))) accounts.Add(new(id));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            issues.Add(new(userdataPath, "ReadFailed"));
        }
        return new(accounts.OrderBy(x => x.AccountId).ToArray(), issues);
    }

    public Cs2LocalSettingsSnapshot ReadAccount(string userdataPath, uint accountId,
        CancellationToken cancellationToken = default)
    {
        if (accountId == 0) throw new ArgumentOutOfRangeException(nameof(accountId));
        var files = new List<Cs2LocalConfigFile>();
        var issues = new List<Cs2SettingsIssue>();
        string root = Path.Combine(userdataPath, accountId.ToString(CultureInfo.InvariantCulture), "730");
        foreach (string relativeFolder in ConfigFolders)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string folder = Path.Combine(root, relativeFolder.Replace('/', Path.DirectorySeparatorChar));
            if (!Directory.Exists(folder)) continue;
            try
            {
                // Materialize enumeration inside the try: access failures can occur on MoveNext.
                foreach (string path in Directory.GetFiles(folder).Where(IsSupportedFile).Order(StringComparer.OrdinalIgnoreCase))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        var text = ReadBounded(path);
                        var values = Cs2SettingsParser.Parse(text);
                        var settings = values.Select(v => new Cs2LocalSetting(
                            Categorize(Path.GetFileName(path), v), v.Section, v.Key, v.Value)).ToArray();
                        files.Add(new(path, relativeFolder + "/" + Path.GetFileName(path),
                            relativeFolder.StartsWith("remote", StringComparison.Ordinal), File.GetLastWriteTimeUtc(path), settings));
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException or DecoderFallbackException)
                    {
                        issues.Add(new(path, ex is FormatException or DecoderFallbackException ? "InvalidFormat" : "ReadFailed"));
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                issues.Add(new(folder, "ReadFailed"));
            }
        }
        return new(files, issues);
    }

    private static string ReadBounded(string path)
    {
        // Sharing permits reading while Steam/CS2 is running; parse failures are reported per file.
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (stream.Length > MaxFileBytes) throw new FormatException("Configuration is too large.");
        using var buffer = new MemoryStream();
        byte[] chunk = new byte[8192];
        int count;
        while ((count = stream.Read(chunk)) != 0)
        {
            if (buffer.Length + count > MaxFileBytes) throw new FormatException("Configuration is too large.");
            buffer.Write(chunk, 0, count);
        }
        return StrictUtf8.GetString(buffer.GetBuffer(), 0, checked((int)buffer.Length));
    }

    internal static bool IsSupportedFile(string path)
    {
        string name = Path.GetFileName(path);
        return name.Equals("cs2_video.txt", StringComparison.OrdinalIgnoreCase) ||
               (name.EndsWith(".vcfg", StringComparison.OrdinalIgnoreCase) &&
                (IsFamily(name, "cs2_user_convars") || IsFamily(name, "cs2_user_keys") || IsFamily(name, "cs2_machine_convars")));
    }

    private static bool IsFamily(string name, string prefix) =>
        name.Equals(prefix + ".vcfg", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith(prefix + "_", StringComparison.OrdinalIgnoreCase);

    internal static Cs2SettingCategory Categorize(string fileName, Cs2ConfigValue value)
    {
        if (fileName.Equals("cs2_video.txt", StringComparison.OrdinalIgnoreCase)) return Cs2SettingCategory.Video;
        var sections = value.Section.Split('/');
        if (sections.Any(s => s.Equals("bindings", StringComparison.OrdinalIgnoreCase) ||
                              s.Equals("keybindings", StringComparison.OrdinalIgnoreCase))) return Cs2SettingCategory.Keys;
        if (!sections.Any(s => s.Equals("convars", StringComparison.OrdinalIgnoreCase))) return Cs2SettingCategory.Other;
        string key = value.Key.TrimEnd('$').ToLowerInvariant();
        if (key.Contains("crosshair", StringComparison.Ordinal)) return Cs2SettingCategory.Crosshair;
        if (key is "sensitivity" or "zoom_sensitivity_ratio" or "zoom_sensitivity_ratio_mouse" ||
            key.StartsWith("m_", StringComparison.Ordinal)) return Cs2SettingCategory.Mouse;
        if (key == "volume" || key.StartsWith("snd_", StringComparison.Ordinal) ||
            key.StartsWith("voice_", StringComparison.Ordinal) || key.StartsWith("audio_", StringComparison.Ordinal))
            return Cs2SettingCategory.Audio;
        return Cs2SettingCategory.Game;
    }
}
