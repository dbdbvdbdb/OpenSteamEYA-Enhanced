using System.IO;
using System.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OpenSteamEya.AccountManagement;
using SteamEyaWinUI.Services;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace SteamEyaWinUI.Pages;

public sealed partial class AccountManagerPage
{
    private ContentDialog Dialog(string title, object content, string? primary = null) => new()
    { Title = title, Content = content, PrimaryButtonText = primary ?? "", CloseButtonText = "取消",
        DefaultButton = primary is null ? ContentDialogButton.Close : ContentDialogButton.Primary,
        XamlRoot = XamlRoot, RequestedTheme = ActualTheme };
    private static StackPanel Form() => new() { Spacing = 12, MinWidth = 380, MaxWidth = 520 };
    private static TextBlock Text(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap };
    private static ScrollViewer Scroll(UIElement content) => new() { Content = content, MaxHeight = 520, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private static ComboBox Choice(string header, params string[] values)
    { var combo = new ComboBox { Header = header, HorizontalAlignment = HorizontalAlignment.Stretch }; foreach (var value in values) combo.Items.Add(value); combo.SelectedIndex = 0; return combo; }
    private static DuplicatePolicy Policy(ComboBox combo) => (DuplicatePolicy)Math.Max(0, combo.SelectedIndex);
    private ListView GroupChoice(ManagedAccount account)
    {
        var list = new ListView { Header = "所属分组（可多选，始终包含默认分组）", MaxHeight = 200,
            SelectionMode = ListViewSelectionMode.Multiple, HorizontalAlignment = HorizontalAlignment.Stretch };
        var memberships = AccountGroups.Ids(account).ToHashSet();
        foreach (var group in document.Groups.OrderBy(x => x.Order))
        {
            var item = new ListViewItem { Content = group.Name, Tag = group.Id };
            list.Items.Add(item); if (memberships.Contains(group.Id)) list.SelectedItems.Add(item);
        }
        return list;
    }
    private static IEnumerable<Guid> SelectedGroups(ListView list) => list.SelectedItems.OfType<ListViewItem>().Select(x => (Guid)x.Tag);
    private async Task<string?> PickOpenAsync(params string[] formats)
    {
        var picker = new FileOpenPicker(); WinRT.Interop.InitializeWithWindow.Initialize(picker, MainWindow.Hwnd);
        foreach (var format in formats) picker.FileTypeFilter.Add(format);
        return (await picker.PickSingleFileAsync())?.Path;
    }
    private async Task<string?> PickSaveAsync(string name, string extension)
    {
        var picker = new FileSavePicker { SuggestedFileName = name };
        WinRT.Interop.InitializeWithWindow.Initialize(picker, MainWindow.Hwnd);
        picker.FileTypeChoices.Add(extension.ToUpperInvariant() + " 文件", new List<string> { "." + extension });
        return (await picker.PickSaveFileAsync())?.Path;
    }

    internal async Task EditAsync(ManagedAccount original)
    {
        if (privacy) return;
        var account = AccountStore.Clone(original); var form = Form();
        var login = new TextBox { Header = "Steam 登录账号", Text = account.LoginName };
        var pass = new PasswordBox { Header = "Steam 登录密码（明文保存）", Password = account.Password, PasswordRevealMode = PasswordRevealMode.Peek };
        var id = new TextBox { Header = "已确认的 SteamID64（可选，17 位数字）", Text = account.SteamId };
        var nickname = new TextBox { Header = "Steam 昵称（可选，不用于匹配）", Text = account.Nickname };
        var note = new TextBox { Header = "备注", Text = account.Note, AcceptsReturn = true, MaxHeight = 100 };
        var group = GroupChoice(account);
        foreach (var element in new UIElement[] { Text("密码以明文保存于当前用户的本地应用数据目录，文件可直接读取密码。密码不会用于替代原登录凭证。"), login, pass, id, nickname, note, group }) form.Children.Add(element);
        if (await Dialog("账号信息", Scroll(form), "保存").ShowAsync() != ContentDialogResult.Primary) return;
        account.LoginName = login.Text.Trim(); account.Password = pass.Password; account.SteamId = id.Text.Trim();
        account.Nickname = nickname.Text.Trim(); account.Note = note.Text; AccountGroups.Set(account, SelectedGroups(group));
        if (account.LoginName.Length == 0) throw new InvalidDataException("请输入登录账号。");
        if (original.LoginName != account.LoginName || original.SteamId != account.SteamId) account.LinkedKey = null;
        var result = AccountMapping.Resolve(account, ManagedAccounts.Identities());
        if (result.Conflict) account.LinkedKey = await ChooseMappingAsync(result.Candidates);
        else account.LinkedKey = result.Key;
        await ManagedAccounts.Store.SaveAccountAsync(account); await ReloadAsync();
        AppState.ShowStatus(account.LinkedKey is null ? "已保存独立记录，可稍后关联原账号。" : "已保存并关联原账号。", InfoBarSeverity.Success);
    }

    private async Task<string?> ChooseMappingAsync(IEnumerable<ExistingIdentity> candidates)
    {
        var combo = new ComboBox { Header = "选择原有账号", HorizontalAlignment = HorizontalAlignment.Stretch, DisplayMemberPath = "LoginName" };
        // Avoid DisplayMemberPath reflection in Native AOT: store only ComboBoxItem content and key.
        combo.DisplayMemberPath = "";
        foreach (var candidate in candidates) combo.Items.Add(new ComboBoxItem
        { Content = candidate.LoginName + " · " + candidate.SteamId + (candidate.Key.StartsWith("history:") ? " · EYA" : " · 缓存"), Tag = candidate.Key });
        var form = Form(); form.Children.Add(Text("存在身份冲突或多个候选，不会自动分配密码。请选择确认属于同一人的原账号，或取消以保持独立。")); form.Children.Add(combo);
        if (await Dialog("确认账号关联", form, "确认关联").ShowAsync() != ContentDialogResult.Primary) return null;
        return (combo.SelectedItem as ComboBoxItem)?.Tag?.ToString();
    }
    internal async Task LinkAsync(ManagedAccount account)
    {
        if (privacy) return;
        var key = await ChooseMappingAsync(ManagedAccounts.Identities());
        if (key is null) return;
        var identity = ManagedAccounts.Identities().First(x => x.Key == key);
        if (account.SteamId.Length > 0 && identity.SteamId.Length > 0 && account.SteamId != identity.SteamId)
            throw new InvalidDataException("SteamID 与所选账号不一致，请先核实并编辑 SteamID。");
        await ChangeAsync(account, x => { x.LinkedKey = key; if (x.SteamId.Length == 0) x.SteamId = identity.SteamId; });
    }
    private async Task AddExistingAsync()
    {
        if (privacy) return;
        var key = await ChooseMappingAsync(ManagedAccounts.Identities()); if (key is null) return;
        var identity = ManagedAccounts.Identities().First(x => x.Key == key);
        var existing = document.Accounts.FirstOrDefault(x => x.LoginName.Equals(identity.LoginName, StringComparison.OrdinalIgnoreCase));
        if (existing is not null) { await ChangeAsync(existing, x => x.LinkedKey = key); return; }
        var history = ManagedAccounts.History(key); var cached = ManagedAccounts.Cached(key);
        await ManagedAccounts.Store.SaveAccountAsync(new() { LoginName = identity.LoginName, SteamId = identity.SteamId,
            Nickname = history?.PersonaName ?? cached?.PersonaName ?? "", LinkedKey = key });
        await ReloadAsync();
    }
    internal async Task DeleteAsync(ManagedAccount account)
    {
        if (await Dialog("删除密码管理记录", Text("仅删除增强版的密码、备注、收藏与背景配置。原有 Steam/EYA 登录凭证和历史记录保持不变。已有登录凭证的账号仍会显示为未保存密码。"), "删除记录").ShowAsync() != ContentDialogResult.Primary) return;
        await ManagedAccounts.Store.DeleteAccountAsync(account.Id); selected.Remove(account.Id); await ReloadAsync();
    }
    internal async Task MoveGroupAsync(ManagedAccount account)
    {
        if (privacy) return;
        var groups = GroupChoice(account); var form = Form();
        form.Children.Add(Text("勾选多个分组；取消勾选仅移除对应归属。默认分组始终包含全部账号。")); form.Children.Add(groups);
        if (await Dialog("设置账号分组", Scroll(form), "保存").ShowAsync() != ContentDialogResult.Primary) return;
        var ids = SelectedGroups(groups).ToList(); await ChangeAsync(account, x => AccountGroups.Set(x, ids));
    }
    internal async Task DetailsAsync(ManagedAccount account, string? key)
    {
        var history = ManagedAccounts.History(key); var form = Form();
        if (privacy) form.Children.Add(Text("隐私模式已隐藏账号资料、密码、头像与备注。"));
        else
        {
            form.Children.Add(Text("昵称：" + DisplayName(account)));
            form.Children.Add(Text("登录账号：" + account.LoginName)); form.Children.Add(Text("SteamID：" + (history?.SteamId ?? account.SteamId)));
            form.Children.Add(Text("分组：" + GroupName(account))); form.Children.Add(Text("备注：" + account.Note));
            form.Children.Add(Text("密码：" + (account.Password.Length == 0 ? "未保存密码" : "已保存（在卡片中显示或复制）")));
        }
        form.Children.Add(Text("CS2 等级：" + (history?.CsPlayerLevel?.ToString() ?? "未获取")));
        form.Children.Add(Text("优先分：" + (history?.PremierScore?.ToString() ?? "未获取")));
        form.Children.Add(Text("优先状态：未获取（原查询快照没有可靠的优先资格字段）"));
        form.Children.Add(Text("冷却状态：" + (history?.RemainingCooldownStatusText ?? "未获取")));
        form.Children.Add(Text("关联状态：" + (key is null ? "尚未关联或需要确认" : "已关联原有登录凭证")));
        await Dialog("账号详情", Scroll(form)).ShowAsync();
    }

    private async Task ImportAsync()
    {
        if (privacy) return;
        var path = await PickOpenAsync(".txt", ".csv", ".json"); if (path is null) return;
        if (new FileInfo(path).Length > 32 * 1024 * 1024) throw new InvalidDataException("导入文件超过 32MB 限制。");
        var preview = await Task.Run(async () => ImportExport.Parse(await File.ReadAllTextAsync(path, Encoding.UTF8), Path.GetExtension(path)));
        var current = document.Accounts.Select(x => x.LoginName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var duplicate = preview.Rows.Count(x => !current.Add(x.Account.LoginName));
        var form = Form(); form.Children.Add(Text($"可导入 {preview.Rows.Count} 个 · 重复 {duplicate} 个 · 错误 {preview.Issues.Count} 行"));
        form.Children.Add(Text("预览中的密码已遮挡；空密码会按所选重复策略处理。"));
        var policy = Choice("重复登录账号处理", "跳过重复账号", "覆盖已有密码", "仅补充未保存密码"); form.Children.Add(policy);
        var previewList = new ListView { MaxHeight = 260, SelectionMode = ListViewSelectionMode.None };
        foreach (var row in preview.Rows.Take(200)) previewList.Items.Add($"第 {row.Line} 行 · {row.Account.LoginName} · " + (row.Account.Password.Length == 0 ? "空密码" : "••••••••"));
        foreach (var issue in preview.Issues.Take(200)) previewList.Items.Add($"错误第 {issue.Line} 行：{issue.Reason}");
        form.Children.Add(previewList);
        var dialog = Dialog("导入预览", Scroll(form), "开始导入"); dialog.IsPrimaryButtonEnabled = preview.Rows.Count > 0;
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        var result = await ManagedAccounts.Store.ImportAsync(preview, Policy(policy));
        await ReloadAsync();
        AppState.ShowStatus($"新增 {result.Added} · 更新 {result.Updated} · 跳过 {result.Skipped} · 失败 {result.Failed}", InfoBarSeverity.Success);
    }

    private async Task ExportAsync()
    {
        var form = Form(); var scope = Choice("导出范围", "全部账号", "选择分组", "选中账号");
        var format = Choice("文件格式", "txt", "csv", "json");
        var groups = new ListView { SelectionMode = ListViewSelectionMode.Multiple, MaxHeight = 140 };
        groups.Items.Add(new ListViewItem { Content = "默认分组", Tag = "default" });
        foreach (var group in document.Groups.OrderBy(x => x.Order)) groups.Items.Add(new ListViewItem { Content = privacy ? "已隐藏分组" : group.Name, Tag = group.Id.ToString() });
        var includeNote = new CheckBox { Content = "包含备注", IsChecked = true };
        var includeId = new CheckBox { Content = "包含 SteamID", IsChecked = true };
        var count = Text("");
        List<ManagedAccount> Range()
        {
            if (scope.SelectedIndex == 2) return document.Accounts.Where(x => selected.Contains(x.Id)).ToList();
            if (scope.SelectedIndex == 1)
            {
                var tags = groups.SelectedItems.OfType<ListViewItem>().Select(x => x.Tag.ToString() ?? "").ToHashSet();
                return document.Accounts.Where(x => AccountGroups.InExportRange(x, tags)).ToList();
            }
            return document.Accounts.ToList();
        }
        void UpdateCount() { groups.Visibility = scope.SelectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
            count.Text = $"将导出 {Range().Count} 个账号。TXT 使用带版本号的转义格式，保证特殊字符及可选资料完整。"; }
        scope.SelectionChanged += (_, _) => UpdateCount(); groups.SelectionChanged += (_, _) => UpdateCount();
        foreach (var element in new UIElement[] { Text("导出文件包含明文密码，可直接读取。请选择合适的保存位置。"), scope, groups, format, count, includeNote, includeId }) form.Children.Add(element);
        UpdateCount();
        if (await Dialog("导出账号密码", Scroll(form), "选择保存路径").ShowAsync() != ContentDialogResult.Primary) return;
        var accounts = Range(); if (accounts.Count == 0) throw new InvalidDataException("导出范围内没有账号。");
        var extension = format.SelectedItem.ToString()!; var path = await PickSaveAsync("Steam账号密码", extension); if (path is null) return;
        var confirm = Form(); confirm.Children.Add(Text($"范围：{scope.SelectedItem}\n数量：{accounts.Count}\n格式：{extension}\n保存路径：{path}\n包含明文密码"));
        if (await Dialog("确认明文导出", confirm, "导出").ShowAsync() != ContentDialogResult.Primary) return;
        var data = ImportExport.Export(accounts, extension, includeNote.IsChecked == true, includeId.IsChecked == true);
        await File.WriteAllTextAsync(path, data, new UTF8Encoding(false));
        AppState.ShowStatus($"已导出 {accounts.Count} 个账号。", InfoBarSeverity.Success);
    }

    private async Task GroupsAsync()
    {
        if (privacy) return;
        var form = Form(); var list = new ListView { MaxHeight = 210, SelectionMode = ListViewSelectionMode.Single };
        foreach (var group in document.Groups.OrderBy(x => x.Order)) list.Items.Add(new ListViewItem { Content = group.Name, Tag = group.Id });
        var name = new TextBox { Header = "分组名称" };
        list.SelectionChanged += (_, _) => { if (list.SelectedItem is ListViewItem item) name.Text = item.Content.ToString(); };
        var operation = Choice("操作", "创建新分组", "修改选中分组名称", "删除选中分组（保留账号及其他分组）", "选中分组上移", "选中分组下移");
        form.Children.Add(list); form.Children.Add(name); form.Children.Add(operation);
        if (await Dialog("分组管理", form, "执行").ShowAsync() != ContentDialogResult.Primary) return;
        Guid? id = list.SelectedItem is ListViewItem selectedItem ? (Guid)selectedItem.Tag : null;
        if (operation.SelectedIndex != 0 && id is null) throw new InvalidDataException("请选择分组。");
        if (operation.SelectedIndex == 2) await ManagedAccounts.Store.DeleteGroupAsync(id!.Value);
        else await ManagedAccounts.Store.UpdateAsync(doc =>
        {
            if (operation.SelectedIndex < 2)
            {
                var label = name.Text.Trim(); if (label.Length == 0) throw new InvalidDataException("分组名称不能为空。");
                if (label == "默认分组") throw new InvalidDataException("默认分组始终包含全部账号，无需创建或修改。");
                if (doc.Groups.Any(x => x.Id != id && x.Name.Equals(label, StringComparison.OrdinalIgnoreCase))) throw new InvalidDataException("分组名称重复。");
                if (operation.SelectedIndex == 0) doc.Groups.Add(new() { Name = label, Order = doc.Groups.Count });
                else doc.Groups.First(x => x.Id == id).Name = label;
            }
            else
            {
                var ordered = doc.Groups.OrderBy(x => x.Order).ToList(); int index = ordered.FindIndex(x => x.Id == id);
                int next = index + (operation.SelectedIndex == 3 ? -1 : 1);
                if (next < 0 || next >= ordered.Count) return;
                (ordered[index], ordered[next]) = (ordered[next], ordered[index]);
                for (int i = 0; i < ordered.Count; i++) ordered[i].Order = i;
            }
        });
        await ReloadAsync();
    }

    internal async Task BackgroundAsync(ManagedAccount account)
    {
        if (privacy) return;
        var form = Form(); var mode = Choice("背景模式", "跟随默认设置", "头像莫奈取色", "Steam 个人资料背景", "本地自定义图片");
        string[] modes = ["default", "monet", "steam", "local"]; mode.SelectedIndex = Math.Max(0, Array.IndexOf(modes, account.Background.Mode));
        var stretch = Choice("图片显示方式", "裁切填满", "完整显示", "拉伸填满");
        string[] stretches = ["UniformToFill", "Uniform", "Fill"]; stretch.SelectedIndex = Math.Max(0, Array.IndexOf(stretches, account.Background.Stretch));
        var pathText = Text(account.Background.ImageFile is null ? "未设置本地背景" : "已设置本地背景");
        string? newPath = null;
        var choose = new Button { Content = "选择 PNG/JPG/JPEG/WEBP" };
        choose.Click += async (_, _) => await SafeAsync(async () => { newPath = await PickOpenAsync(".png", ".jpg", ".jpeg", ".webp"); if (newPath is not null) pathText.Text = newPath; });
        var remove = new CheckBox { Content = "删除自定义背景并恢复默认" };
        foreach (var element in new UIElement[] { Text("资料背景或本地图片无法获取时，自动回退到头像取色背景。"), mode, stretch, choose, pathText, remove }) form.Children.Add(element);
        if (await Dialog("卡片背景", form, "保存").ShowAsync() != ContentDialogResult.Primary) return;
        string? image = account.Background.ImageFile;
        if (newPath is not null)
        {
            if (new FileInfo(newPath).Length > 20 * 1024 * 1024) throw new InvalidDataException("背景图片超过 20MB 限制。");
            var file = await StorageFile.GetFileFromPathAsync(newPath); using var stream = await file.OpenReadAsync();
            var decoder = await BitmapDecoder.CreateAsync(stream); // verifies the installed decoder supports this image
            if ((long)decoder.PixelWidth * decoder.PixelHeight > 64000000) throw new InvalidDataException("背景图片尺寸超过限制。");
            image = Guid.NewGuid().ToString("N") + Path.GetExtension(newPath).ToLowerInvariant();
            Directory.CreateDirectory(ManagedAccounts.Store.ImageDirectory);
            await Task.Run(() => File.Copy(newPath, Path.Combine(ManagedAccounts.Store.ImageDirectory, image)));
            mode.SelectedIndex = 3;
        }
        if (remove.IsChecked == true) { image = null; mode.SelectedIndex = 0; }
        var oldImage = account.Background.ImageFile;
        await ChangeAsync(account, x => x.Background = new() { Mode = modes[mode.SelectedIndex], ImageFile = image, Stretch = stretches[stretch.SelectedIndex] });
        if (oldImage is not null && oldImage != image && !document.Accounts.Any(x => x.Background.ImageFile == oldImage))
        {
            var oldPath = Path.Combine(ManagedAccounts.Store.ImageDirectory, oldImage); if (File.Exists(oldPath)) File.Delete(oldPath);
        }
    }

    private async Task BackupDialogAsync()
    {
        var choice = Choice("操作", "创建完整备份", "从备份恢复", "打开备份目录");
        var form = Form(); form.Children.Add(Text("备份包含新增管理数据、明文密码与背景图片，不包含 EYA Token 或 Steam 登录会话凭证。ZIP 文件未加密。")); form.Children.Add(choice);
        if (await Dialog("备份与恢复", form, "继续").ShowAsync() != ContentDialogResult.Primary) return;
        var backupDirectory = Path.Combine(ManagedAccounts.Store.Root, "backups"); Directory.CreateDirectory(backupDirectory);
        if (choice.SelectedIndex == 2)
        { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", backupDirectory) { UseShellExecute = true }); return; }
        if (choice.SelectedIndex == 0)
        {
            var path = await PickSaveAsync("Steam账号备份-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"), "zip"); if (path is null) return;
            await ManagedAccounts.Backup.CreateAsync(path);
            // Store a location hint without including any password data in settings or logs.
            await File.WriteAllTextAsync(Path.Combine(backupDirectory, "last-backup-location.txt"), path, Encoding.UTF8);
            AppState.ShowStatus("备份已创建（包含明文密码）。", InfoBarSeverity.Success); return;
        }
        var source = await PickOpenAsync(".zip"); if (source is null) return;
        var preview = await ManagedAccounts.Backup.PreviewAsync(source);
        var restore = Choice("恢复方式", "合并恢复", "覆盖新增管理数据"); var policy = Choice("重复账号", "跳过", "覆盖", "仅补缺失密码");
        if (preview.CurrentUnreadable) restore.SelectedIndex = 1;
        var previewForm = Form(); previewForm.Children.Add(Text($"备份版本：{preview.Document.Version}\n账号：{preview.Document.Accounts.Count}\n分组：{preview.Document.Groups.Count}\n重复账号：{preview.Duplicates}\n背景资源：{preview.Images.Count}\n仅影响增强版管理数据，原登录凭证不受影响。"));
        previewForm.Children.Add(restore); previewForm.Children.Add(policy);
        if (preview.CurrentUnreadable) previewForm.Children.Add(Text("当前密码管理文件损坏，无法统计重复账号；请选择覆盖恢复。原登录凭证不受影响。"));
        if (await Dialog("备份预览", previewForm, "恢复").ShowAsync() != ContentDialogResult.Primary) return;
        await ManagedAccounts.Backup.RestoreAsync(preview, restore.SelectedIndex == 0 ? RestoreMode.Merge : RestoreMode.Replace, Policy(policy));
        await ReloadAsync(); AppState.ShowStatus("备份恢复完成。", InfoBarSeverity.Success);
    }

    private async Task ManagerSettingsAsync()
    {
        var prefs = document.Preferences; var form = Form();
        var view = Choice("默认展示方式", "精美卡片", "紧凑列表"); view.SelectedIndex = prefs.View == "list" ? 1 : 0;
        var sort = Choice("默认排序", "自定义", "昵称", "登录账号", "最近使用", "CS2 等级", "收藏优先");
        string[] sorts = ["custom", "nickname", "login", "recent", "level", "favorite"]; sort.SelectedIndex = Math.Max(0, Array.IndexOf(sorts, prefs.Sort));
        var groupDisplay = Choice("分组显示方式", "筛选显示", "按分组排列"); groupDisplay.SelectedIndex = prefs.GroupDisplay == "sections" ? 1 : 0;
        var favorite = new CheckBox { Content = "优先显示收藏账号", IsChecked = prefs.FavoritesFirst };
        var mode = Choice("默认背景模式", "头像莫奈取色", "Steam 个人资料背景"); mode.SelectedIndex = prefs.BackgroundMode == "steam" ? 1 : 0;
        var strength = new Slider { Header = "头像取色效果", Minimum = 0, Maximum = 1, Value = prefs.MonetStrength };
        var brightness = new Slider { Header = "背景亮度", Minimum = 0.1, Maximum = 0.9, Value = prefs.Brightness };
        var blur = new Slider { Header = "背景柔化强度", Minimum = 0, Maximum = 32, Value = prefs.Blur };
        var full = new CheckBox { Content = "显示完整 SteamID", IsChecked = prefs.FullSteamId };
        var hide = new CheckBox { Content = "默认隐藏密码", IsChecked = prefs.HidePasswords };
        var privateMode = new CheckBox { Content = "隐私模式（保留为启动设置）", IsChecked = prefs.Privacy };
        var folder = new Button { Content = "打开密码数据目录" }; folder.Click += async (_, _) => await SafeAsync(() =>
        { Directory.CreateDirectory(ManagedAccounts.Store.Root); System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", ManagedAccounts.Store.Root) { UseShellExecute = true }); return Task.CompletedTask; });
        foreach (var element in new UIElement[] { view, sort, groupDisplay, favorite, mode, strength, brightness, blur, full, hide, privateMode,
            Text("密码数据位置：" + ManagedAccounts.Store.Root), Text("本地文件、导出和备份包含明文密码。目录固定在当前用户 LocalAppData，避免漫游同步。"), folder }) form.Children.Add(element);
        if (await Dialog("账号管理设置", Scroll(form), "保存").ShowAsync() != ContentDialogResult.Primary) return;
        await ManagedAccounts.Store.UpdateAsync(doc => doc.Preferences = new()
        { View = view.SelectedIndex == 1 ? "list" : "cards", Sort = sorts[sort.SelectedIndex], FavoritesFirst = favorite.IsChecked == true,
            GroupDisplay = groupDisplay.SelectedIndex == 1 ? "sections" : "filter", BackgroundMode = mode.SelectedIndex == 1 ? "steam" : "monet",
            MonetStrength = strength.Value, Brightness = brightness.Value, Blur = blur.Value, FullSteamId = full.IsChecked == true, HidePasswords = hide.IsChecked == true, Privacy = privateMode.IsChecked == true });
        await ReloadAsync();
    }
}
