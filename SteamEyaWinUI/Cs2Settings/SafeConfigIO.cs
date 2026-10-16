using System.Security.Cryptography;

namespace SteamEyaWinUI.Cs2Settings;

internal static class SafeConfigIO
{
    internal static string Hash(byte[] data) => Convert.ToHexString(SHA256.HashData(data));
    internal static void RejectLinks(string path)
    {
        for (string? p = Path.GetFullPath(path); p is not null; p = Path.GetDirectoryName(p))
        {
            // GetAttributes also detects dangling symlinks; Exists alone does not.
            try
            {
                if ((File.GetAttributes(p) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("拒绝符号链接或重解析目录：" + p);
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
    }
    internal static string Child(string parent, string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name != Path.GetFileName(name) || name is "." or ".." || name.Contains(':') || name.Contains('\\') || name.Contains('/'))
            throw new InvalidDataException("配置路径无效。");
        string path = Path.Combine(Path.GetFullPath(parent), name);
        RejectLinks(path);
        return path;
    }
    internal static byte[] Read(string path, int limit = 2 * 1024 * 1024)
    {
        RejectLinks(path);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > limit) throw new InvalidDataException("配置文件超出读取上限。");
        var bytes = new byte[checked((int)stream.Length)];
        stream.ReadExactly(bytes);
        return bytes;
    }
    internal static void AtomicWrite(string path, byte[] bytes)
    {
        RejectLinks(path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        RejectLinks(path);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { stream.Write(bytes); stream.Flush(true); }
            RejectLinks(path);
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    internal static bool Same(byte[]? a, byte[]? b) => a is null ? b is null : b is not null && a.AsSpan().SequenceEqual(b);
    internal static byte[]? ReadOptional(string path) => File.Exists(path) ? Read(path) : Missing(path);
    private static byte[]? Missing(string path) { RejectLinks(path); return null; }
    internal static void ValidateFile(string name, byte[] data)
    {
        if (!LocalConfigStore.FileNames.Contains(name)) throw new InvalidDataException("文件不在 CS2 配置允许列表中。");
        var document = new ConfigText(data);
        string section = name == LocalConfigStore.VideoFile ? "video.cfg" : name == LocalConfigStore.KeysFile ? "config/bindings" : "config/convars";
        if (!document.HasSection(section)) throw new InvalidDataException(name + " 缺少正确的配置节点。");
        document.EnsureUnique();
    }
}
