using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OpenSteamEya.AccountManagement;
using SteamEyaWinUI.Services;

namespace SteamEyaWinUI.Pages;

public sealed partial class AccountManagerPage
{
    private async void BatchAdd_Click(object sender, RoutedEventArgs e) => await SafeAsync(BatchAddAsync);

    internal async Task BatchAddAsync()
    {
        if (privacy) return;
        var input = new TextBox { Header = "每行一个账号：steam账号----steam密码", AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap, MinHeight = 220, MaxHeight = 360, MaxLength = 32 * 1024 * 1024,
            PlaceholderText = "steam账号----steam密码\n第二个账号----第二个密码" };
        ScrollViewer.SetVerticalScrollBarVisibility(input, ScrollBarVisibility.Auto);
        ScrollViewer.SetHorizontalScrollBarVisibility(input, ScrollBarVisibility.Auto);
        var policy = Choice("重复账号处理", "跳过重复账号，保留已有密码", "覆盖已有密码", "只补充未保存的密码");
        var feedback = new TextBlock { TextWrapping = TextWrapping.Wrap };
        var form = Form();
        form.Children.Add(Text("无需双引号，使用四个半角连字符分隔账号和密码。密码中的引号、空格、反斜杠及后续 ---- 均原样保存；空密码可用于添加未保存密码的账号。"));
        form.Children.Add(input); form.Children.Add(policy); form.Children.Add(feedback);
        var dialog = Dialog("批量添加账号", Scroll(form), "添加账号");
        dialog.IsPrimaryButtonEnabled = false;
        input.TextChanged += (_, _) => { dialog.IsPrimaryButtonEnabled = !string.IsNullOrWhiteSpace(input.Text); feedback.Text = ""; };
        ImportPreview? accepted = null;
        DuplicatePolicy acceptedPolicy = DuplicatePolicy.Skip;
        // A validation failure leaves the pasted input available for correction. Never echo password lines.
        dialog.PrimaryButtonClick += async (_, args) =>
        {
            var deferral = args.GetDeferral();
            string text = input.Text; var duplicatePolicy = Policy(policy);
            input.IsEnabled = policy.IsEnabled = false;
            try
            {
                var preview = await Task.Run(() => BatchAccountInput.Parse(text));
                if (preview.Rows.Count == 0 || preview.Issues.Count > 0)
                {
                    args.Cancel = true;
                    feedback.Text = preview.Issues.Count > 0 ? string.Join("\n", preview.Issues.Take(8).Select(x => $"第 {x.Line} 行：{x.Reason}")) : "请输入至少一个账号。";
                    return;
                }
                accepted = preview; acceptedPolicy = duplicatePolicy;
            }
            catch
            {
                args.Cancel = true; feedback.Text = "添加未完成，请检查文件权限和数据文件后重试。";
            }
            finally { input.IsEnabled = policy.IsEnabled = true; deferral.Complete(); }
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary || accepted is null) return;
        var result = await AccountClassification.ImportWhiteAsync(this, accepted, acceptedPolicy);
        await ReloadAsync();
        AppState.ShowStatus($"批量添加完成：新增 {result.Added} · 更新 {result.Updated} · 跳过 {result.Skipped}", InfoBarSeverity.Success);
    }
}
