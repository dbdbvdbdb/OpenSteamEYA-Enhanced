using System.Text;
using SteamEyaWinUI.Services;

internal static class Cs2SettingsChecks
{
    public static void Run(Action<bool, string> check, string testRoot)
    {
        var values = Cs2SettingsParser.Parse("\uFEFF// CS2 fixture\n\"config\" { \"bindings\" { \"w\" \"+forward\" \"MOUSE1\" \"+attack\" } }");
        check(values.Count == 2 && values[0] == new Cs2ConfigValue("config/bindings", "w", "+forward"), "CS2 nested key bindings and BOM");
        values = Cs2SettingsParser.Parse("/* header */ config { convars { sensitivity 1.25 // note\n volume 0.8 } }");
        check(values.Count == 2 && values[1].Value == "0.8", "CS2 bare tokens and both comment forms");
        values = Cs2SettingsParser.Parse("\"config\" { \"bindings\" { \";\" \"say \\\"{hello}//世界\\\"; +jump\" \"f\" \"\" } }");
        check(values[0].Value == "say \"{hello}//世界\"; +jump" && values[1].Value == "", "CS2 quoted delimiters, Unicode, escaped quotes and empty values");
        values = Cs2SettingsParser.Parse("\"{\" \"}\" \"path\" \"cfg\\example\" \"x\" \"1\" \"x\" \"2\"");
        check(values[0].Key == "{" && values[0].Value == "}" && values[1].Value == "cfg\\example" && values.Count == 4,
            "CS2 quoted braces, unknown escapes and duplicate keys retained");
        check(Cs2SettingsParser.Parse("// empty").Count == 0, "CS2 comment-only files");
        foreach (string invalid in new[] { "config { key value", "config { key }", "key", "}", "{ key value }", "key \"unterminated", "/*", "config {} trailing" })
            check(Rejects(() => Cs2SettingsParser.Parse(invalid)), "CS2 rejects malformed structure without partial values");
        check(Rejects(() => Cs2SettingsParser.Parse(new string(' ', Cs2SettingsParser.MaxCharacters + 1))), "CS2 parser size limit");
        check(Rejects(() => Cs2SettingsParser.Parse(string.Concat(Enumerable.Repeat("n { ", 34)) + new string('}', 34))), "CS2 nesting limit");
        check(Rejects(() => Cs2SettingsParser.Parse(string.Concat(Enumerable.Repeat("a b ", Cs2SettingsParser.MaxEntries + 1)))), "CS2 entry limit");

        var userRoot = Path.Combine(testRoot, "cs2-fixture-userdata");
        var cfg = Path.Combine(userRoot, "123", "730", "local", "cfg");
        var remote = Path.Combine(userRoot, "123", "730", "remote");
        Directory.CreateDirectory(cfg);
        Directory.CreateDirectory(Path.Combine(remote, "cfg"));
        Directory.CreateDirectory(Path.Combine(userRoot, "456", "730", "remote"));
        Directory.CreateDirectory(Path.Combine(userRoot, "789", "730"));
        Directory.CreateDirectory(Path.Combine(userRoot, "0", "730"));
        Directory.CreateDirectory(Path.Combine(userRoot, "not-an-account", "730"));
        Directory.CreateDirectory(Path.Combine(userRoot, "000123", "730"));
        Directory.CreateDirectory(Path.Combine(userRoot, "999", "config"));
        var convarsPath = Path.Combine(cfg, "cs2_user_convars_0_slot0.vcfg");
        File.WriteAllText(convarsPath, """
            "config" {
                "version" "1"
                "convars" {
                    "sensitivity" "1.25"
                    "cl_crosshairsize" "2.5"
                    "volume" "0.4"
                    "cl_radar_scale" "0.7"
                    "future_unknown_setting" "keep-me"
                }
            }
            """, new UTF8Encoding(true));
        File.WriteAllText(Path.Combine(cfg, "cs2_user_keys_0_slot0.vcfg"), "config { bindings { w +forward MOUSE1 +attack } }");
        File.WriteAllText(Path.Combine(cfg, "cs2_user_convars_0_slot1.vcfg"), "config { convars { sensitivity 2.5 } }");
        File.WriteAllText(Path.Combine(cfg, "cs2_machine_convars.vcfg"), "config { convars { fps_max 240 snd_mute_losefocus 1 } }");
        File.WriteAllText(Path.Combine(cfg, "cs2_video.txt"), "\"video.cfg\" { \"setting.defaultres\" \"1920\" \"setting.defaultresheight\" \"1080\" \"setting.fullscreen\" \"1\" }");
        File.WriteAllText(Path.Combine(remote, "cs2_user_convars.vcfg"), "config { convars { sensitivity 9 } }");
        File.WriteAllText(Path.Combine(remote, "cfg", "cs2_user_keys.vcfg"), "config { bindings { w +back } }");
        File.WriteAllText(Path.Combine(userRoot, "456", "730", "remote", "cs2_user_keys.vcfg"), "config { bindings { e +use } }");
        File.WriteAllText(Path.Combine(cfg, "cs2_user_convars.vcfg.bak"), "unrelated backup");
        File.WriteAllText(Path.Combine(cfg, "autoexec.cfg"), "echo must-not-execute");
        File.WriteAllText(Path.Combine(remote, "socache.dt"), "unrelated data");
        File.WriteAllText(Path.Combine(cfg, "cs2_video.txt.bak"), "unrelated video backup");
        File.WriteAllText(Path.Combine(cfg, "cs2_user_convars_bad.vcfg"), "config { convars {");
        File.WriteAllBytes(Path.Combine(cfg, "cs2_user_keys_bad.vcfg"), [0xFF, 0xFE, 0x01]);
        var largePath = Path.Combine(cfg, "cs2_user_convars_large.vcfg");
        using (var stream = File.Create(largePath)) stream.SetLength(Cs2LocalSettingsService.MaxFileBytes + 1);
        var before = Directory.GetFiles(userRoot, "*", SearchOption.AllDirectories).ToDictionary(p => p, File.ReadAllBytes);

        var service = new Cs2LocalSettingsService();
        var scan = service.ScanAccounts(userRoot);
        check(scan.Accounts.Select(a => a.AccountId).SequenceEqual(new uint[] { 123, 456, 789 }) && scan.Issues.Count == 0,
            "CS2 discovers only canonical account folders containing app 730");
        check(scan.Accounts[0].SteamId64 == "76561197960265851", "CS2 account ID to SteamID64 conversion");
        var snapshot = service.ReadAccount(userRoot, 123);
        check(snapshot.Files.Count == 7 && snapshot.Issues.Count == 3, "CS2 local/cloud files discovered, backups ignored, invalid files isolated");
        check(snapshot.Issues.All(i => i.Reason == "InvalidFormat"), "CS2 corrupt, non-UTF8 and oversized files reported");
        check(snapshot.Files.Count(f => f.IsCloudCache) == 2, "CS2 remote and remote/cfg explicitly marked cloud cache");
        var settings = snapshot.Files.SelectMany(f => f.Settings).ToArray();
        check(settings.Where(v => v.Key == "sensitivity").Select(v => v.Value).SequenceEqual(new[] { "1.25", "2.5", "9" }),
            "CS2 retains separate local slots and cloud values without merging");
        check(settings.Single(v => v.Key == "future_unknown_setting").Value == "keep-me", "CS2 unknown convars retained");
        foreach (var (key, category) in new[] {
            ("sensitivity", Cs2SettingCategory.Mouse), ("cl_crosshairsize", Cs2SettingCategory.Crosshair),
            ("volume", Cs2SettingCategory.Audio), ("snd_mute_losefocus", Cs2SettingCategory.Audio),
            ("cl_radar_scale", Cs2SettingCategory.Game), ("fps_max", Cs2SettingCategory.Game),
            ("setting.defaultres", Cs2SettingCategory.Video), ("version", Cs2SettingCategory.Other), ("w", Cs2SettingCategory.Keys) })
            check(settings.Where(v => v.Key == key).All(v => v.Category == category), "CS2 category " + category + ": " + key);
        check(snapshot.Files.All(f => Path.IsPathFullyQualified(f.Path) && f.LastWriteTimeUtc > DateTime.UnixEpoch), "CS2 source path and modification time");
        check(service.ReadAccount(userRoot, 456).Files.Single().Settings.Single().Key == "e", "CS2 account isolation");
        check(service.ReadAccount(userRoot, 789).Files.Count == 0, "CS2 account with no saved settings");
        check(service.ScanAccounts(Path.Combine(userRoot, "missing")).Accounts.Count == 0, "CS2 missing userdata");
        check(service.ReadAccount(userRoot, uint.MaxValue).Files.Count == 0, "CS2 missing account settings");
        check(before.All(pair => File.ReadAllBytes(pair.Key).SequenceEqual(pair.Value)) &&
              before.Count == Directory.GetFiles(userRoot, "*", SearchOption.AllDirectories).Length,
            "CS2 viewing leaves all source files unchanged and creates no files");

        File.WriteAllText(convarsPath, "config { convars { sensitivity 3.75 } }");
        check(service.ReadAccount(userRoot, 123).Files.Single(f => f.Path == convarsPath).Settings.Single().Value == "3.75", "CS2 refresh reads current disk data");
        File.Delete(convarsPath);
        check(service.ReadAccount(userRoot, 123).Files.All(f => f.Path != convarsPath), "CS2 removed files disappear on refresh");
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        bool canceled = false;
        try { service.ReadAccount(userRoot, 123, cts.Token); } catch (OperationCanceledException) { canceled = true; }
        check(canceled, "CS2 reading honors cancellation");
        bool invalidAccount = false;
        try { service.ReadAccount(userRoot, 0); } catch (ArgumentOutOfRangeException) { invalidAccount = true; }
        check(invalidAccount, "CS2 rejects account zero");
    }

    private static bool Rejects(Action action)
    {
        try { action(); return false; } catch (FormatException) { return true; }
    }
}
