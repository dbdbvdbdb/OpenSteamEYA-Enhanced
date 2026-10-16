namespace SteamEyaWinUI.Models;

// Compiled x:Bind keeps the settings list compatible with Native AOT.
public sealed partial class Cs2SettingRow
{
    public string Title { get; init; } = "";
    public string Detail { get; init; } = "";
    public string Value { get; init; } = "";
    public string Source { get; init; } = "";
}
