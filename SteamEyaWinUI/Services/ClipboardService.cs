using Windows.ApplicationModel.DataTransfer;

namespace SteamEyaWinUI.Services;

internal static class ClipboardService
{
    public static void Copy(string text)
    {
        var package = new DataPackage(); package.SetText(text); Clipboard.SetContent(package);
        // User-requested persistent clipboard: no timer, encryption, authentication or automatic clearing.
    }
}
