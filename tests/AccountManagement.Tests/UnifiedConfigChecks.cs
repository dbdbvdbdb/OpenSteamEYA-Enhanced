using System.Text;
using SteamEyaWinUI.Cs2Settings;

internal static class UnifiedConfigChecks
{
    public static void Run(Action<bool, string> check, string testRoot)
    {
        void Pass(bool value, string name) => check(value, "Unified CS2 " + name);
        void Reject(Action action, string name)
        {
            bool rejected = false;
            try { action(); } catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException or ArgumentException or System.Text.Json.JsonException) { rejected = true; }
            Pass(rejected, name);
        }
        string root = Path.Combine(testRoot, "unified-template");
        var store = new UnifiedConfigStore(root);
        var template = store.LoadOrCreate();
        Pass(template.Files.Count == 4 && template.Manifest.Revision.Length == 32, "default native files created without Steam or accounts");
        Pass(new UnifiedConfigStore(root).LoadOrCreate().ManifestHash == template.ManifestHash, "template persists across store instances");
        var rows = UnifiedConfigStore.GetSettings(template);
        Pass(rows.Count > 60 && rows.Any(r => r.Spec.Key == "setting.videocfg_fsr_detail" && r.Enabled), "catalog-driven editor includes advanced video options");
        Pass(rows.Single(r => r.Spec.Key == "setting.r_low_latency").Enabled == false, "Reflex disabled without verified driver capability");
        Pass(SettingCatalog.Visible.Single(s => s.Key == "setting.videocfg_ao_detail").Choices!.Select(c => c.Value).SequenceEqual(new[] { "0", "2", "3" }), "AO uses current menu values 0/2/3");
        Pass(SettingCatalog.Visible.Single(s => s.Key == "setting.videocfg_hdr_detail").Choices!.Select(c => c.Value).SequenceEqual(new[] { "3", "-1" }), "HDR uses current performance/quality mapping");
        Pass(SettingCatalog.Visible.Single(s => s.Key == "setting.videocfg_fsr_detail").Choices!.Length == 5, "FSR includes ultra quality and all five verified modes");
        Pass(SettingCatalog.Visible.Single(s => s.Key == "cl_crosshairgap").Availability == SettingAvailability.Unsupported, "absent legacy crosshair alias not invented");
        Pass(SettingCatalog.Visible.Single(s => s.Key == "hud_scaling").Min == .9 && SettingCatalog.Visible.Single(s => s.Key == "sensitivity").Max == 8, "bounds follow pinned current game declarations");
        Pass(SettingCatalog.Editable.All(s => s.AllowInsert && s.DefaultValue is not null && s.Evidence.Length > 0), "editable parameters have defaults, mappings and evidence");
        var sensitivity = rows.Single(r => r.Spec.Key == "sensitivity"); sensitivity.Value = "2.25"; sensitivity.Dirty = true;
        template = store.Save(template, rows, new Dictionary<string, string> { ["SPACE"] = "+jump" }, "我的测试模板");
        Pass(new ConfigText(template.Files[LocalConfigStore.UserFile]).Find("config/convars", "sensitivity")?.Value == "2.25", "save edits template native parameter");
        Pass(new ConfigText(template.Files[LocalConfigStore.KeysFile]).Find("config/bindings", "SPACE")?.Value == "+jump", "save inserts binding into exact native node");
        Pass(store.TemplateBackups().Count == 1, "saving automatically backs up previous template");
        Reject(() => store.Save(store.DefaultForTest(), rows, new Dictionary<string, string>(), "stale"), "stale template snapshot cannot overwrite saved state");
        rows = UnifiedConfigStore.GetSettings(template); sensitivity = rows.Single(r => r.Spec.Key == "sensitivity"); sensitivity.Value = "NaN"; sensitivity.Dirty = true;
        Reject(() => store.Save(template, rows, new Dictionary<string, string>(), template.Manifest.Name), "invalid value cannot be saved");
        rows = UnifiedConfigStore.GetSettings(template); var ao = rows.Single(r => r.Spec.Key == "setting.videocfg_ao_detail"); ao.Value = "1"; ao.Dirty = true;
        Reject(() => store.Save(template, rows, new Dictionary<string, string>(), template.Manifest.Name), "invalid AO enum cannot be saved");
        string export = Path.Combine(testRoot, "unified-export"); store.Export(template, export);
        var imported = store.ReadFolder(export);
        Pass(imported.Files.All(p => p.Value.SequenceEqual(template.Files[p.Key])), "export/import preserves full native files and metadata");
        Reject(() => store.Export(template, export), "export cannot overwrite a nonempty folder");
        Reject(() => store.Export(template, Path.Combine(root, "template-files", "bad")), "export cannot overwrite internal generations");
        File.AppendAllText(Path.Combine(export, LocalConfigStore.UserFile), "// tampered");
        Reject(() => store.ReadFolder(export), "tampered exported manifest rejected");
        var restored = store.RestoreTemplate(template, store.TemplateBackups().Single().Directory);
        Pass(new ConfigText(restored.Files[LocalConfigStore.UserFile]).Find("config/convars", "sensitivity")?.Value == "1.25", "template restore restores old generation");
        Reject(() => store.RestoreTemplate(restored, Path.Combine(testRoot, "outside")), "template restore path escape rejected");

        string folder = Path.Combine(testRoot, "unified-import"); Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, LocalConfigStore.UserFile), "// original\r\nconfig { convars { unknown custom sensitivity 1.5 } }\r\n");
        var importedNative = store.ReadFolder(folder);
        var importedRows = UnifiedConfigStore.GetSettings(importedNative);
        Pass(importedRows.Single(r => r.Spec.Key == "zoom_sensitivity_ratio").Enabled && importedRows.Single(r => r.Spec.Key == "zoom_sensitivity_ratio").Original is null, "verified missing parameter displayed and editable");
        var zoom = importedRows.Single(r => r.Spec.Key == "zoom_sensitivity_ratio"); zoom.Value = "1.5"; zoom.Dirty = true;
        template = store.Replace(restored, importedNative, "导入模板");
        template = store.Save(template, importedRows, new Dictionary<string, string>(), "导入模板");
        var document = new ConfigText(template.Files[LocalConfigStore.UserFile]);
        Pass(document.Find("config/convars", "zoom_sensitivity_ratio")?.Value == "1.5" && document.Find("config/convars", "unknown")?.Value == "custom", "insertion preserves unknown fields and original comments");
        Pass(Encoding.UTF8.GetString(template.Files[LocalConfigStore.UserFile]).StartsWith("// original\r\n"), "insertion preserves CRLF comments");
        File.WriteAllText(Path.Combine(folder, LocalConfigStore.UserFile), "config { convars { sensitivity 1 sensitivity 2 } }");
        Reject(() => store.ReadFolder(folder), "duplicate imported keys rejected");
        foreach (var encoding in new Encoding[] { new UTF8Encoding(true), new UnicodeEncoding(false, true), new UnicodeEncoding(true, true) })
        {
            const string text = "/* keep block */\r\nconfig { convars { unknown preserve } }\r\n";
            byte[] bytes = [.. encoding.GetPreamble(), .. encoding.GetBytes(text)];
            var doc = new ConfigText(bytes);
            var patched = new ConfigText(doc.Patch("config/convars", new Dictionary<string, string> { ["sensitivity"] = "2" }, true));
            Pass(patched.Find("config/convars", "unknown")?.Value == "preserve" && patched.Find("config/convars", "sensitivity")?.Value == "2", "lossless insertion into " + encoding.WebName);
        }
        Reject(() => SafeConfigIO.ValidateFile(LocalConfigStore.UserFile, Encoding.UTF8.GetBytes("config { convars { W 1 w 2 } }")), "case-insensitive duplicate keys blocked");
        Reject(() => SafeConfigIO.ValidateFile(LocalConfigStore.UserFile, Encoding.UTF8.GetBytes("\"video.cfg\" {}")), "wrong native file root rejected");
        Reject(() => SafeConfigIO.Child(root, "../outside"), "path traversal rejected");
        Reject(() => SafeConfigIO.Child(root, "file:stream"), "alternate file streams rejected");

        // Full multi-account transaction with independent baselines and a remote mirror sentinel.
        template = store.Replace(template, UnifiedConfigStore.DefaultTemplate(), "全账号模板");
        var templaterows = UnifiedConfigStore.GetSettings(template); var row = templaterows.Single(r => r.Spec.Key == "sensitivity"); row.Value = "2.75"; row.Dirty = true;
        template = store.Save(template, templaterows, new Dictionary<string, string> { ["w"] = "+back" }, "全账号模板");
        string steam = Path.Combine(testRoot, "unified-steam"), backups = Path.Combine(root, "backups", "accounts");
        string Cfg(uint id) => Path.Combine(steam, "userdata", id.ToString(), "730", "local", "cfg");
        void Fixture(uint id)
        {
            Directory.CreateDirectory(Cfg(id));
            File.WriteAllText(Path.Combine(Cfg(id), LocalConfigStore.UserFile), "// different account\nconfig { convars { sensitivity 1 unknown preserve } }");
            File.WriteAllText(Path.Combine(Cfg(id), LocalConfigStore.MachineFile), "config { convars { volume 0.1 custom_machine keep } }");
            File.WriteAllText(Path.Combine(Cfg(id), LocalConfigStore.VideoFile), "\"video.cfg\" { Version 16 VendorID 999 \"setting.defaultres\" 1920 \"setting.defaultresheight\" 1080 custom_video preserve }");
            File.WriteAllText(Path.Combine(Cfg(id), LocalConfigStore.KeysFile), "config { bindings { w +forward x \"custom;command\" } }");
        }
        Fixture(101); Fixture(202);
        Directory.CreateDirectory(Path.Combine(steam, "userdata", "303", "730"));
        Directory.CreateDirectory(Cfg(404)); foreach (var (name, bytes) in template.Files) File.WriteAllBytes(Path.Combine(Cfg(404), name), bytes);
        Fixture(505); File.WriteAllText(Path.Combine(Cfg(505), LocalConfigStore.UserFile), "config { convars {");
        Directory.CreateDirectory(Path.Combine(steam, "userdata", "999", "999"));
        string remote = Path.Combine(steam, "userdata", "101", "730", "remote"); Directory.CreateDirectory(remote);
        File.WriteAllText(Path.Combine(remote, "cs2_user_convars.vcfg"), "mirror sentinel");
        File.WriteAllText(Path.Combine(steam, "userdata", "101", "identity-sentinel"), "unrelated identity data");
        var before = Directory.GetFiles(steam, "*", SearchOption.AllDirectories).ToDictionary(p => p, File.ReadAllBytes);
        bool running = false;
        var service = new ConfigApplyService(steam, backups, () => running);
        running = true; Reject(() => service.Preview(template), "running game blocks preview"); running = false;
        var preview = service.Preview(template);
        Pass(preview.Accounts.Count == 5 && preview.Accounts.Single(p => p.AccountId == 505).Error is not null, "scans applicable accounts and isolates damaged configuration");
        Pass(before.All(p => File.ReadAllBytes(p.Key).SequenceEqual(p.Value)), "preview changes no account files");
        File.SetAttributes(Path.Combine(Cfg(202), LocalConfigStore.UserFile), FileAttributes.ReadOnly);
        BatchApplyResult applied;
        try { applied = service.Apply(preview); }
        finally { File.SetAttributes(Path.Combine(Cfg(202), LocalConfigStore.UserFile), FileAttributes.Normal); }
        Pass(applied.Succeeded == 2 && applied.Unchanged == 1 && applied.Failed == 2, "multi-account success unchanged and failure totals");
        var saved = new ConfigText(File.ReadAllBytes(Path.Combine(Cfg(101), LocalConfigStore.UserFile)));
        Pass(saved.Find("config/convars", "sensitivity")?.Value == "2.75" && saved.Find("config/convars", "unknown")?.Value == "preserve", "apply changes only recognized values and preserves unknown user keys");
        var video = new ConfigText(File.ReadAllBytes(Path.Combine(Cfg(101), LocalConfigStore.VideoFile)));
        Pass(video.Find("video.cfg", "VendorID")?.Value == "999" && video.Find("video.cfg", "custom_video")?.Value == "preserve", "apply preserves hardware IDs and unknown video parameters");
        var keys = new ConfigText(File.ReadAllBytes(Path.Combine(Cfg(101), LocalConfigStore.KeysFile)));
        Pass(keys.Find("config/bindings", "w")?.Value == "+back" && keys.Find("config/bindings", "x")?.Value == "custom;command", "apply standard binding while preserving custom target commands");
        Pass(File.ReadAllText(Path.Combine(remote, "cs2_user_convars.vcfg")) == "mirror sentinel" && File.ReadAllText(Path.Combine(steam, "userdata", "101", "identity-sentinel")) == "unrelated identity data", "cloud mirror and identity files remain byte-for-byte unchanged");
        Pass(File.Exists(Path.Combine(Cfg(303), LocalConfigStore.UserFile)) && !File.Exists(Path.Combine(Cfg(303), LocalConfigStore.VideoFile)), "safe missing convars created but no invented video hardware config");
        var backup101 = applied.Accounts.Single(a => a.AccountId == 101).Backup!;
        Pass(File.ReadAllBytes(Path.Combine(backup101, LocalConfigStore.UserFile)).SequenceEqual(before[Path.Combine(Cfg(101), LocalConfigStore.UserFile)]), "per-account backup exactly matches original bytes");
        Pass(File.ReadAllBytes(Path.Combine(Cfg(202), LocalConfigStore.UserFile)).SequenceEqual(before[Path.Combine(Cfg(202), LocalConfigStore.UserFile)]), "read-only account remains unchanged");
        var restoredResult = service.Apply(service.PreviewRestore(backup101));
        Pass(restoredResult.Succeeded == 1 && File.ReadAllBytes(Path.Combine(Cfg(101), LocalConfigStore.UserFile)).SequenceEqual(before[Path.Combine(Cfg(101), LocalConfigStore.UserFile)]), "account restore creates own backup and restores exact data");
        var backup303 = applied.Accounts.Single(a => a.AccountId == 303).Backup!;
        service.Apply(service.PreviewRestore(backup303));
        Pass(!File.Exists(Path.Combine(Cfg(303), LocalConfigStore.UserFile)) && !File.Exists(Path.Combine(Cfg(303), LocalConfigStore.MachineFile)), "restore removes only files created by this transaction");
        Reject(() => service.PreviewRestore(backup101), "restore detects newer external or restored content");
        Reject(() => service.PreviewRestore(Path.Combine(testRoot, "outside")), "account restore rejects escaped backup path");

        Fixture(606);
        var conflict = service.Preview(template);
        File.AppendAllText(Path.Combine(Cfg(606), LocalConfigStore.UserFile), "// changed externally");
        var conflictResult = service.Apply(conflict);
        Pass(conflictResult.Accounts.Single(a => a.AccountId == 606).Status == "失败" && File.ReadAllText(Path.Combine(Cfg(606), LocalConfigStore.UserFile)).EndsWith("// changed externally"), "preview-to-apply conflict preserves external changes");
        Fixture(707); Fixture(808);
        var rollbackBefore = Directory.GetFiles(Cfg(707)).ToDictionary(p => p, File.ReadAllBytes);
        int writes = 0;
        var failing = new ConfigApplyService(steam, backups, () => false, path => { if (path.StartsWith(Cfg(707)) && ++writes == 2) throw new IOException("Synthetic disk failure"); });
        var failure = failing.Apply(failing.Preview(template));
        Pass(failure.Accounts.Single(a => a.AccountId == 707).Status == "失败" && rollbackBefore.All(p => File.ReadAllBytes(p.Key).SequenceEqual(p.Value)), "mid-transaction failure rolls back every committed file for that account");
        Pass(failure.Accounts.Single(a => a.AccountId == 808).Status == "成功" && service.AccountBackups().Count > 0, "failed account does not invalidate other account backups or writes");
        using (var cts = new CancellationTokenSource())
        {
            var plan = service.Preview(template); cts.Cancel();
            Pass(service.Apply(plan, cts.Token).Accounts.All(a => a.Status == "跳过"), "cancellation records all unprocessed accounts as skipped");
        }
        var wrongVersion = template.Files.ToDictionary(p => p.Key, p => p.Value.ToArray());
        wrongVersion[LocalConfigStore.VideoFile] = Encoding.UTF8.GetBytes("\"video.cfg\" { Version 999 }");
        Pass(UnifiedConfigStore.GetSettings(template with { Files = wrongVersion }).Where(r => r.Spec.FileName == LocalConfigStore.VideoFile).All(r => !r.Enabled), "unknown video version cannot expose editable video options");
        var modes = new[] { new DisplayMode("fixture-device", "Synthetic monitor", 1920, 1080, 144, true) };
        var withVideo = template.Files.ToDictionary(p => p.Key, p => p.Value.ToArray());
        withVideo[LocalConfigStore.VideoFile] = Encoding.UTF8.GetBytes("\"video.cfg\" { Version 16 \"setting.defaultres\" 1280 \"setting.defaultresheight\" 720 \"setting.refreshrate_numerator\" 144 \"setting.refreshrate_denominator\" 1 }");
        var selection = DisplayModeService.ChangeResolution(template with { Files = withVideo }, "fixture-device", 1920, 1080, modes);
        Pass(selection.Manifest.Display?.Width == 1920, "resolution selection records validated device and dimensions");
        Reject(() => DisplayModeService.ChangeResolution(selection, "fixture-device", 8000, 8000, modes), "unsupported hardware mode rejected");
        Reject(() => DisplayModeService.ChangeResolution(selection, "fixture-device", 1920, 1080, [new("fixture-device", "Synthetic monitor", 1920, 1080, 60, true)]), "resolution unsupported at saved refresh rate rejected");
        Reject(() => service.Preview(selection), "stale or foreign display binding blocks application");
        Pass(new ConfigApplyService(steam, backups, () => false, displayModes: () => modes).Preview(selection).Accounts.Count > 0, "hardware mode is revalidated before apply preview");
        if (!OperatingSystem.IsWindows())
        {
            string link = Path.Combine(testRoot, "unified-linked"); Directory.CreateSymbolicLink(link, root);
            Reject(() => new UnifiedConfigStore(link).LoadOrCreate(), "symbolic template root rejected");
            string accountLink = Path.Combine(steam, "userdata", "909"); Directory.CreateSymbolicLink(accountLink, Path.Combine(steam, "userdata", "101"));
            Pass(service.Preview(template).Accounts.Single(a => a.AccountId == 909).Error is not null, "linked account recorded as failure without touching target");
            string dangling = Path.Combine(testRoot, "dangling.vcfg"); File.CreateSymbolicLink(dangling, Path.Combine(testRoot, "nonexistent-target"));
            Reject(() => SafeConfigIO.ReadOptional(dangling), "dangling file symlink rejected");
        }
        var incompatible = selection.Files.ToDictionary(p => p.Key, p => p.Value.ToArray());
        incompatible[LocalConfigStore.VideoFile] = Encoding.UTF8.GetBytes("\"video.cfg\" { Version 16 \"setting.videocfg_fsr_detail\" 2 \"setting.msaa_samples\" 4 \"setting.r_csgo_cmaa_enable\" 0 }");
        Reject(() => UnifiedConfigStore.ValidateVideoCombination(incompatible), "conservative FSR/AA interlock blocks unverified combinations");
        var unknownBoolean = template.Files.ToDictionary(p => p.Key, p => p.Value.ToArray());
        unknownBoolean[LocalConfigStore.UserFile] = new ConfigText(unknownBoolean[LocalConfigStore.UserFile]).Patch("config/convars", new Dictionary<string, string> { ["cl_sniper_auto_rezoom"] = "future-value" });
        Reject(() => service.Preview(template with { Files = unknownBoolean }), "unknown original boolean is never silently treated as false");
        string manySteam = Path.Combine(testRoot, "unified-many-steam");
        for (int i = 1; i <= 100; i++) Directory.CreateDirectory(Path.Combine(manySteam, "userdata", i.ToString(), "730"));
        var many = new ConfigApplyService(manySteam, Path.Combine(root, "backups", "many"), () => false);
        var manyResult = many.Apply(many.Preview(template));
        Pass(manyResult.Accounts.Count == 100 && manyResult.Succeeded == 100, "one operation handles 100 accounts with safe missing-file creation");
        Pass(many.Apply(many.Preview(template)).Accounts.All(a => a.Status == "跳过" && a.Files == 0), "repeat batch performs no unnecessary writes and reports skipped uninitialized video");
        Fixture(1001);
        int interruptedWrites = 0; bool startsRunning = false;
        var interrupt = new ConfigApplyService(steam, backups, () => startsRunning, path => { if (path.StartsWith(Cfg(1001)) && ++interruptedWrites == 2) startsRunning = true; });
        var interruptedBefore = Directory.GetFiles(Cfg(1001)).ToDictionary(p => p, File.ReadAllBytes);
        var interrupted = interrupt.Apply(interrupt.Preview(template));
        Pass(interrupted.Accounts.Single(a => a.AccountId == 1001).Status == "失败" && interruptedBefore.All(p => File.ReadAllBytes(p.Key).SequenceEqual(p.Value)), "game starting mid-transaction rolls back prior writes");
        // External generation tampering is never silently replaced by a later save.
        var live = store.Load();
        string generation = Path.Combine(root, "template-files", live.Manifest.Revision, LocalConfigStore.UserFile);
        File.AppendAllText(generation, "// tamper");
        Reject(() => store.Load(), "generation hash mismatch rejected on read");
        Reject(() => store.Save(live, [], new Dictionary<string, string>(), "new"), "corrupt generation cannot be silently replaced");
    }
    private static TemplateSnapshot DefaultForTest(this UnifiedConfigStore _) => UnifiedConfigStore.DefaultTemplate();
}
