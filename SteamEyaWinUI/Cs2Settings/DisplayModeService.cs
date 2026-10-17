using System.Runtime.InteropServices;

namespace SteamEyaWinUI.Cs2Settings;

internal sealed record DisplayMode(string Device, string DisplayName, int Width, int Height, int Hz, bool Primary);

/// <summary>Enumerates driver-advertised Windows modes. Device names are used for validation only,
/// never converted to unverified CS2 monitor_index values.</summary>
internal static class DisplayModeService
{
    public static IReadOnlyList<DisplayMode> Enumerate()
    {
        if (!OperatingSystem.IsWindows()) return [];
        var result = new List<DisplayMode>();
        for (uint i = 0; i < 64; i++)
        {
            var device = new DisplayDevice { Size = Marshal.SizeOf<DisplayDevice>() };
            if (!EnumDisplayDevices(null, i, ref device, 0)) break;
            if ((device.Flags & 1) == 0 || (device.Flags & 8) != 0) continue;
            for (int index = 0; index < 10000; index++)
            {
                var mode = new DeviceMode { Size = (ushort)Marshal.SizeOf<DeviceMode>() };
                if (!EnumDisplaySettings(device.Name, index, ref mode, 0)) break;
                if (mode.Width > 0 && mode.Height > 0 && mode.Frequency > 1 && mode.BitsPerPixel >= 24)
                    result.Add(new(device.Name, device.Description, (int)mode.Width, (int)mode.Height, (int)mode.Frequency, (device.Flags & 4) != 0));
            }
        }
        return result.Distinct().OrderByDescending(m => m.Primary).ThenBy(m => m.Device).ThenBy(m => m.Width).ThenBy(m => m.Height).ThenBy(m => m.Hz).ToArray();
    }
    public static TemplateSnapshot ChangeResolution(TemplateSnapshot snapshot, string device, int width, int height, IReadOnlyList<DisplayMode> liveModes)
    {
        if (!snapshot.Files.TryGetValue(LocalConfigStore.VideoFile, out var bytes)) throw new InvalidDataException("请先导入游戏生成的视频配置。");
        var doc = new ConfigText(bytes); doc.EnsureUnique();
        int? refresh = SavedRefresh(doc);
        if (refresh is null) throw new InvalidDataException("当前刷新率不是已验证的整数 Hz，不能安全修改分辨率；请先在游戏内选择显示模式。");
        if (!liveModes.Any(m => m.Device == device && m.Width == width && m.Height == height && m.Hz == refresh))
            throw new InvalidDataException("显示设备不支持此分辨率与当前刷新率组合。");
        if (doc.Find("video.cfg", "Version")?.Value != SettingCatalog.VideoVersion || doc.Find("video.cfg", "setting.defaultres") is null || doc.Find("video.cfg", "setting.defaultresheight") is null)
            throw new InvalidDataException("视频版本或分辨率字段未验证，拒绝生成未知显示配置。");
        var files = snapshot.Files.ToDictionary(p => p.Key, p => p.Value.ToArray());
        files[LocalConfigStore.VideoFile] = doc.Patch("video.cfg", new Dictionary<string, string> {
            ["setting.defaultres"] = width.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["setting.defaultresheight"] = height.ToString(System.Globalization.CultureInfo.InvariantCulture) });
        return snapshot with { Files = files, Manifest = snapshot.Manifest with { Display = new(device, width, height, refresh.Value) } };
    }
    internal static int? SavedRefresh(ConfigText document)
    {
        if (!long.TryParse(document.Find("video.cfg", "setting.refreshrate_numerator")?.Value,
            System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out long numerator) ||
            !long.TryParse(document.Find("video.cfg", "setting.refreshrate_denominator")?.Value,
            System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out long denominator) ||
            denominator <= 0 || numerator <= 0 || numerator % denominator != 0 || numerator / denominator > 1000) return null;
        return (int)(numerator / denominator);
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DisplayDevice
    {
        internal int Size;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] internal string Name;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] internal string Description;
        internal uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] internal string Id;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] internal string Key;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DeviceMode
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] internal string Name;
        internal ushort SpecVersion, DriverVersion, Size, DriverExtra;
        internal uint Fields;
        internal int PositionX, PositionY;
        internal uint Orientation, FixedOutput;
        internal short Color, Duplex, YResolution, TTOption, Collate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] internal string Form;
        internal ushort LogPixels;
        internal uint BitsPerPixel, Width, Height, Flags, Frequency, IcmMethod, IcmIntent, MediaType, DitherType, Reserved1, Reserved2, PanningWidth, PanningHeight;
    }
    [DllImport("user32.dll", EntryPoint = "EnumDisplayDevicesW", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayDevices(string? device, uint index, ref DisplayDevice data, uint flags);
    [DllImport("user32.dll", EntryPoint = "EnumDisplaySettingsExW", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplaySettings(string device, int index, ref DeviceMode mode, uint flags);
}
