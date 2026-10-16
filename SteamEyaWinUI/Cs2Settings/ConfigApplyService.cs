using System.Globalization;
using System.Text.Json;

namespace SteamEyaWinUI.Cs2Settings;

/// <summary>Preview/confirm/apply local CS2 files only. No dependency on Steam login or cloud services.</summary>
internal sealed class ConfigApplyService(string steamRoot, string backupRoot, Func<bool>? isRunning = null, Action<string>? beforeWrite = null, Func<IReadOnlyList<DisplayMode>>? displayModes = null)
{
    private static readonly object Gate = new();
    public string SteamRoot { get; } = Path.GetFullPath(steamRoot);
    public string BackupRoot { get; } = Path.GetFullPath(backupRoot);
    private readonly Func<bool> _running = isRunning ?? LocalConfigStore.GameRunning;
    private string DirectoryFor(uint id) => Path.Combine(SteamRoot, "userdata", id.ToString(CultureInfo.InvariantCulture), "730", "local", "cfg");
    private void EnsureStopped() { if (_running()) throw new InvalidOperationException("请先关闭 CS2，再应用或恢复本地配置。"); }

    public ApplyPreview Preview(TemplateSnapshot template)
    {
        EnsureStopped();
        // Validate all template inputs before scanning any account.
        foreach (var (name, bytes) in template.Files) SafeConfigIO.ValidateFile(name, bytes);
        UnifiedConfigStore.ValidateVideoCombination(template.Files);
        var rows = UnifiedConfigStore.GetSettings(template);
        if (template.Manifest.Display is { } display && !(displayModes?.Invoke() ?? DisplayModeService.Enumerate()).Any(m => m.Device == display.Device && m.Width == display.Width && m.Height == display.Height && m.Hz == display.Hz) ||
            template.Manifest.Display is { } savedDisplay && (!template.Files.TryGetValue(LocalConfigStore.VideoFile, out var displayBytes) || DisplayModeService.SavedRefresh(new ConfigText(displayBytes)) != savedDisplay.Hz))
            throw new InvalidDataException("模板分辨率不在当前显示设备的有效模式中；请重新选择分辨率或导入不含显示模式绑定的模板。");
        foreach (var row in rows.Where(r => r.Enabled && r.Original is not null))
        {
            if (row.Spec.Boolean && row.Original is not "0" and not "1" and not "true" and not "false")
                throw new InvalidDataException(row.Spec.Label + "：模板原值不是已验证的开关值，请先明确编辑该项。");
            _ = row.Validated();
        }
        string userdata = Path.Combine(SteamRoot, "userdata"); SafeConfigIO.RejectLinks(userdata);
        var accounts = new List<AccountApplyPlan>();
        if (!Directory.Exists(userdata)) return new(SteamRoot, template.Manifest.Revision, accounts);
        foreach (string folder in Directory.GetDirectories(userdata).Order(StringComparer.Ordinal))
        {
            string rawId = Path.GetFileName(folder);
            if (!uint.TryParse(rawId, NumberStyles.None, CultureInfo.InvariantCulture, out uint id) || id == 0 || id.ToString(CultureInfo.InvariantCulture) != rawId) continue;
            string cfg = DirectoryFor(id);
            // Existing 730 data is required; never provision CS2 under unrelated Steam accounts.
            if (!Directory.Exists(Path.Combine(folder, "730"))) continue;
            var original = new Dictionary<string, byte[]?>(); var after = new Dictionary<string, byte[]?>(); var notes = rows.Where(r => !r.Enabled && r.Original is not null).Select(r => "未应用的模板参数：" + r.Key + "（" + r.Issue + "）").ToList();
            try
            {
                SafeConfigIO.RejectLinks(cfg);
                foreach (string name in template.Files.Keys)
                {
                    if (name == LocalConfigStore.VideoFile && new ConfigText(template.Files[name]).Find("video.cfg", "Version")?.Value != SettingCatalog.VideoVersion)
                    { notes.Add("模板视频格式未验证，跳过视频参数，不改写目标文件。"); continue; }
                    string path = SafeConfigIO.Child(cfg, name);
                    byte[]? bytes = SafeConfigIO.ReadOptional(path);
                    if (bytes is not null) SafeConfigIO.ValidateFile(name, bytes);
                    if (name == LocalConfigStore.VideoFile && (bytes is null || new ConfigText(bytes).Find("video.cfg", "Version")?.Value != SettingCatalog.VideoVersion))
                    { notes.Add("视频文件缺失或不是格式 16，保留原状，需由游戏完成硬件初始化。"); continue; }
                    var doc = new ConfigText(bytes ?? UnifiedConfigStore.EmptyFile(name));
                    string section = name == LocalConfigStore.VideoFile ? "video.cfg" : name == LocalConfigStore.KeysFile ? "config/bindings" : "config/convars";
                    var changes = new Dictionary<string, string>();
                    if (name == LocalConfigStore.KeysFile)
                    {
                        var source = new ConfigText(template.Files[name]);
                        foreach (var bind in source.Entries.Where(e => e.Section == section))
                        {
                            if (!SettingCatalog.BindKeys.Contains(bind.Key, StringComparer.OrdinalIgnoreCase) || !SettingCatalog.VerifiedBindActions.Any(a => a.Value == bind.Value))
                            { notes.Add("自定义或未验证的模板键位仅保留在模板中：" + bind.Key); continue; }
                            var existing = doc.Entries.SingleOrDefault(e => e.Section == section && e.Key.Equals(bind.Key, StringComparison.OrdinalIgnoreCase));
                            changes.Add(existing?.Key ?? bind.Key, bind.Value);
                        }
                    }
                    else foreach (var row in rows.Where(r => r.Enabled && r.Original is not null && r.Spec.FileName == name))
                    {
                        var candidates = doc.Entries.Where(e => e.Section == section && SettingCatalog.BaseKey(e.Key) == row.Spec.Key).ToArray();
                        if (candidates.Length > 1) throw new InvalidDataException("目标参数存在多个版本键：" + row.Spec.Key);
                        var existing = candidates.SingleOrDefault();
                        if (existing is null && !row.Spec.AllowInsert) continue;
                        // Use the target's original suffixed key and boolean representation.
                        string value = row.Spec.Validate(row.Value, existing?.Value ?? row.Spec.DefaultValue ?? "");
                        changes.Add(existing?.Key ?? row.Spec.Key, value);
                    }
                    if (name == LocalConfigStore.VideoFile && template.Manifest.Display is { } selection)
                    {
                        if (doc.Find("video.cfg", "setting.defaultres") is null || doc.Find("video.cfg", "setting.defaultresheight") is null || DisplayModeService.SavedRefresh(doc) != selection.Hz)
                            notes.Add("目标视频文件缺少显示字段或刷新率不同，未修改分辨率。");
                        else
                        {
                            changes["setting.defaultres"] = selection.Width.ToString(CultureInfo.InvariantCulture);
                            changes["setting.defaultresheight"] = selection.Height.ToString(CultureInfo.InvariantCulture);
                        }
                    }
                    if (changes.Count == 0) continue;
                    byte[] result = doc.Patch(section, changes, true);
                    if (SafeConfigIO.Same(bytes, result)) continue;
                    SafeConfigIO.ValidateFile(name, result);
                    original.Add(name, bytes); after.Add(name, result);
                }
                accounts.Add(new(id, cfg, original, after, notes, null));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or System.Text.DecoderFallbackException)
            { accounts.Add(new(id, cfg, [], [], notes, ex.Message)); }
        }
        return new(SteamRoot, template.Manifest.Revision, accounts);
    }

    public BatchApplyResult Apply(ApplyPreview preview, CancellationToken token = default, IProgress<string>? progress = null)
    {
        if (!string.Equals(Path.GetFullPath(preview.SteamRoot), SteamRoot, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("预览不属于此 Steam 目录。");
        if (preview.Accounts.Select(p => p.AccountId).Distinct().Count() != preview.Accounts.Count) throw new InvalidDataException("预览含重复账号。");
        lock (Gate)
        {
            EnsureStopped();
            SafeConfigIO.RejectLinks(BackupRoot); Directory.CreateDirectory(BackupRoot);
            // Cross-process writer lease, in addition to the in-process gate.
            using var lease = new FileStream(SafeConfigIO.Child(BackupRoot, ".apply.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            string batch = SafeConfigIO.Child(BackupRoot, "apply-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N"));
            var results = new List<AccountApplyResult>();
            foreach (var plan in preview.Accounts)
            {
                progress?.Report($"正在处理 {results.Count + 1}/{preview.Accounts.Count}：{plan.AccountId}");
                if (token.IsCancellationRequested) { results.Add(new(plan.AccountId, "跳过", 0, null, "操作已取消。", plan.Notes)); continue; }
                if (plan.Error is not null) { results.Add(new(plan.AccountId, "失败", 0, null, plan.Error, plan.Notes)); continue; }
                if (plan.After.Count == 0) { results.Add(new(plan.AccountId, plan.Notes.Count == 0 ? "无需更改" : "跳过", 0, null, null, plan.Notes)); continue; }
                string backup = SafeConfigIO.Child(batch, plan.AccountId.ToString(CultureInfo.InvariantCulture));
                try
                {
                    Commit(plan, backup, token);
                    results.Add(new(plan.AccountId, "成功", plan.After.Count, backup, null, plan.Notes));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or InvalidDataException or OperationCanceledException)
                { results.Add(new(plan.AccountId, "失败", 0, Directory.Exists(backup) ? backup : null, ex.Message, plan.Notes)); }
            }
            var result = new BatchApplyResult(results);
            try { SafeConfigIO.AtomicWrite(SafeConfigIO.Child(batch, "results.json"), JsonSerializer.SerializeToUtf8Bytes(result, TemplateJsonContext.Default.BatchApplyResult)); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { return result with { LogError = "账号处理已完成，但结果日志无法保存：" + ex.Message }; }
            return result;
        }
    }
    private void ValidatePlan(AccountApplyPlan plan)
    {
        if (plan.AccountId == 0 || !string.Equals(Path.GetFullPath(plan.Directory), DirectoryFor(plan.AccountId), StringComparison.OrdinalIgnoreCase) ||
            !Directory.Exists(Path.Combine(SteamRoot, "userdata", plan.AccountId.ToString(CultureInfo.InvariantCulture), "730")) ||
            plan.After.Count is < 1 or > 4 || !plan.Before.Keys.Order().SequenceEqual(plan.After.Keys.Order())) throw new InvalidDataException("目标账号、文件清单或路径无效。");
        SafeConfigIO.RejectLinks(plan.Directory);
        foreach (var (name, bytes) in plan.After)
        {
            if (!LocalConfigStore.FileNames.Contains(name)) throw new InvalidDataException("不允许写入非 CS2 配置文件。");
            if (bytes is not null) SafeConfigIO.ValidateFile(name, bytes);
            if (plan.Before[name] is { } old) SafeConfigIO.ValidateFile(name, old);
        }
    }
    private static void EnsureUnchanged(string path, byte[]? expected)
    {
        if (!SafeConfigIO.Same(SafeConfigIO.ReadOptional(path), expected)) throw new IOException("目标配置已被外部修改，请重新预览：" + Path.GetFileName(path));
        if (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReadOnly) != 0) throw new IOException("目标配置文件只读：" + Path.GetFileName(path));
    }
    private void Commit(AccountApplyPlan plan, string backup, CancellationToken token)
    {
        EnsureStopped(); ValidatePlan(plan);
        foreach (var (name, bytes) in plan.Before) EnsureUnchanged(SafeConfigIO.Child(plan.Directory, name), bytes);
        var manifest = new AccountBackup { AccountId = plan.AccountId };
        foreach (var (name, bytes) in plan.Before)
        {
            if (bytes is not null) SafeConfigIO.AtomicWrite(SafeConfigIO.Child(backup, name), bytes);
            manifest.Before.Add(name, bytes is null ? null : SafeConfigIO.Hash(bytes));
            manifest.After.Add(name, plan.After[name] is { } next ? SafeConfigIO.Hash(next) : null);
        }
        SafeConfigIO.AtomicWrite(SafeConfigIO.Child(backup, "manifest.json"), JsonSerializer.SerializeToUtf8Bytes(manifest, TemplateJsonContext.Default.AccountBackup));
        var committed = new List<string>();
        try
        {
            foreach (var (name, bytes) in plan.After)
            {
                token.ThrowIfCancellationRequested(); EnsureStopped();
                var path = SafeConfigIO.Child(plan.Directory, name);
                EnsureUnchanged(path, plan.Before[name]);
                beforeWrite?.Invoke(path);
                // Recheck after hooks/expensive operations, before touching the actual target.
                EnsureStopped(); EnsureUnchanged(path, plan.Before[name]);
                if (bytes is null) File.Delete(path); else SafeConfigIO.AtomicWrite(path, bytes);
                committed.Add(name);
            }
        }
        catch (Exception ex)
        {
            bool restored = true;
            foreach (string name in committed.AsEnumerable().Reverse())
            {
                try
                {
                    string path = SafeConfigIO.Child(plan.Directory, name);
                    if (!SafeConfigIO.Same(SafeConfigIO.ReadOptional(path), plan.After[name])) { restored = false; continue; }
                    if (plan.Before[name] is { } original) SafeConfigIO.AtomicWrite(path, original); else File.Delete(path);
                }
                catch { restored = false; }
            }
            throw new IOException((restored ? "写入失败，已回滚此账号本次修改。" : "部分文件无法回滚，请从备份恢复。") + " 备份：" + backup + "\n" + ex.Message, ex);
        }
    }
    public IReadOnlyList<TemplateBackupInfo> AccountBackups()
    {
        SafeConfigIO.RejectLinks(BackupRoot);
        if (!Directory.Exists(BackupRoot)) return [];
        var result = new List<TemplateBackupInfo>();
        foreach (var batch in Directory.GetDirectories(BackupRoot, "apply-*").OrderDescending(StringComparer.Ordinal))
        {
            SafeConfigIO.RejectLinks(batch);
            foreach (string directory in Directory.GetDirectories(batch))
            {
                SafeConfigIO.RejectLinks(directory);
                if (File.Exists(SafeConfigIO.Child(directory, "manifest.json"))) result.Add(new(directory, "账号 " + Path.GetFileName(directory) + " · " + Path.GetFileName(batch)));
            }
        }
        return result;
    }
    public ApplyPreview PreviewRestore(string backupDirectory)
    {
        EnsureStopped();
        string directory = Path.GetFullPath(backupDirectory);
        string? batch = Path.GetDirectoryName(directory);
        if (batch is null || !string.Equals(Path.GetDirectoryName(batch), BackupRoot, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(batch).StartsWith("apply-", StringComparison.Ordinal))
            throw new InvalidDataException("备份目录不在配置备份范围内。");
        var manifest = JsonSerializer.Deserialize(SafeConfigIO.Read(SafeConfigIO.Child(directory, "manifest.json"), 65536), TemplateJsonContext.Default.AccountBackup)
            ?? throw new InvalidDataException("备份清单为空。");
        if (manifest.Version != 1 || manifest.AccountId == 0 || manifest.Before is null || manifest.After is null ||
            !manifest.Before.Keys.Order().SequenceEqual(manifest.After.Keys.Order()) || manifest.Before.Count is < 1 or > 4 ||
            manifest.AccountId.ToString(CultureInfo.InvariantCulture) != Path.GetFileName(directory)) throw new InvalidDataException("备份清单无效。");
        string target = DirectoryFor(manifest.AccountId);
        var before = new Dictionary<string, byte[]?>(); var after = new Dictionary<string, byte[]?>();
        foreach (var (name, hash) in manifest.Before)
        {
            if (!LocalConfigStore.FileNames.Contains(name)) throw new InvalidDataException("备份文件名无效。");
            var bytes = hash is null ? null : SafeConfigIO.Read(SafeConfigIO.Child(directory, name));
            if (bytes is not null && SafeConfigIO.Hash(bytes) != hash) throw new InvalidDataException("备份文件校验失败。");
            byte[]? current = SafeConfigIO.ReadOptional(SafeConfigIO.Child(target, name));
            string? currentHash = current is null ? null : SafeConfigIO.Hash(current);
            if (currentHash == hash) continue; // already original, e.g. a partially completed rollback
            if (currentHash != manifest.After[name]) throw new IOException("备份之后此账号配置已发生变化，拒绝覆盖新内容。");
            before.Add(name, current); after.Add(name, bytes);
        }
        if (after.Count == 0) throw new InvalidOperationException("这些文件已是备份中的原始状态，无需再次恢复。");
        var plan = new AccountApplyPlan(manifest.AccountId, target, before, after, [], null);
        ValidatePlan(plan);
        return new(SteamRoot, "restore", [plan]);
    }
}
