using System.Text;
using SteamEyaWinUI.Cs2Settings;

internal static class Cs2ConfigChecks
{
    public static void Run(Action<bool,string> check, string root)
    {
        void Reject(Action action, string name)
        {
            bool rejected = false;
            try { action(); } catch (Exception e) when (e is IOException or InvalidDataException or InvalidOperationException or ArgumentException) { rejected = true; }
            check(rejected, "CS2 " + name);
        }
        const string original = "// keep comment\r\n\"config\"\r\n{\r\n\t\"convars\" { \"sensitivity\" \"1.5\" \"unknown\" \"中文\\\\path\" }\r\n}\r\n";
        foreach (var encoding in new Encoding[] { new UTF8Encoding(false), new UTF8Encoding(true), new UnicodeEncoding(false,true), new UnicodeEncoding(true,true) })
        {
            byte[] bytes = [..encoding.GetPreamble(), ..encoding.GetBytes(original)];
            var doc = new ConfigText(bytes);
            check(doc.Patch("config/convars",new Dictionary<string,string>()).SequenceEqual(bytes), "CS2 exact encoding and BOM roundtrip " + encoding.WebName);
            var patched = doc.Patch("config/convars",new Dictionary<string,string>{{"sensitivity","2"}});
            byte[] expected = [..encoding.GetPreamble(), ..encoding.GetBytes(original.Replace("\"1.5\"","\"2\""))];
            check(patched.SequenceEqual(expected), "CS2 edit preserves all unrelated bytes " + encoding.WebName);
        }
        Reject(()=>new ConfigText(Encoding.UTF8.GetBytes("\"config\" {")),"malformed config blocked");
        Reject(()=>new ConfigText(Encoding.UTF8.GetBytes("#base x")),"directives blocked");
        Reject(()=>new ConfigText(Encoding.UTF8.GetBytes("config { convars { a 1 a 2 } }")).Find("config/convars","a"),"duplicate keys blocked");
        check(SettingCatalog.BaseKey("snd_musicvolume$4")=="snd_musicvolume", "CS2 version suffix matched without rewriting key");
        var sensitivity = SettingCatalog.All.Single(s=>s.Key=="sensitivity");
        Reject(()=>sensitivity.Validate("NaN","1"),"NaN blocked");
        Reject(()=>sensitivity.Validate("99","1"),"out of range blocked");
        var steam = Path.Combine(root,"cs2-steam"); var backup = Path.Combine(root,"cs2-backups");
        var cfg = Path.Combine(steam,"userdata","123","730","local","cfg");
        var remote = Path.Combine(steam,"userdata","123","730","remote");
        Directory.CreateDirectory(cfg); Directory.CreateDirectory(remote);
        File.WriteAllText(Path.Combine(cfg,LocalConfigStore.UserFile),original);
        File.WriteAllText(Path.Combine(cfg,LocalConfigStore.MachineFile),"config { convars { \"volume\" \"0.8\" \"snd_musicvolume$4\" \"0.3\" } }");
        File.WriteAllText(Path.Combine(cfg,LocalConfigStore.VideoFile),"\"video.cfg\" { \"setting.defaultres\" \"1920\" \"setting.defaultresheight\" \"1080\" }");
        File.WriteAllText(Path.Combine(cfg,LocalConfigStore.KeysFile),"config { bindings { \"W\" \"+forward\" \"x\" \"custom;command\" } }");
        File.WriteAllText(Path.Combine(remote,"cs2_user_convars.vcfg"),"config { convars { sensitivity 1.5 remote_only preserved } }");
        File.WriteAllText(Path.Combine(remote,"cs2_user_keys.vcfg"),"config { bindings { w +forward remote_only preserved } }");
        bool running = false;
        var store = new LocalConfigStore(steam,backup,()=>running);
        var profiles = store.GetProfiles(); check(profiles.Count==1 && profiles[0].SteamId=="76561197960265851", "CS2 discovers existing account configs");
        var snapshot = store.Load(profiles[0]);
        var rows = LocalConfigStore.GetSettings(snapshot);
        check(rows.Any(r=>r.Key=="snd_musicvolume$4"),"CS2 retains actual suffixed key");
        var row = rows.Single(r=>r.Key=="sensitivity"); row.Value="2"; row.Dirty=true;
        running=true; Reject(()=>store.Save(snapshot,rows,new Dictionary<string,string>()),"running game blocks save"); running=false;
        check(File.ReadAllText(Path.Combine(cfg,LocalConfigStore.UserFile))==original,"CS2 blocked save leaves file intact");
        var result = store.Save(snapshot,rows,new Dictionary<string,string>{{"w","+back"},{"SPACE","+jump"}});
        check(result.FileCount==2 && result.CloudFiles.Count==2,"CS2 saves user and bindings with two cloud payloads");
        check(File.ReadAllBytes(Path.Combine(result.BackupDirectory,LocalConfigStore.UserFile)).SequenceEqual(snapshot.Files[LocalConfigStore.UserFile].Bytes),"CS2 backup is exact pre-edit copy");
        var keydoc = new ConfigText(File.ReadAllBytes(Path.Combine(cfg,LocalConfigStore.KeysFile)));
        check(keydoc.Find("config/bindings","W")?.Value=="+back" && keydoc.Find("config/bindings","w") is null && keydoc.Find("config/bindings","SPACE")?.Value=="+jump","CS2 binding insertion preserves key casing");
        check(keydoc.Find("config/bindings","x")?.Value=="custom;command","CS2 custom commands preserved");
        var cloud = new ConfigText(result.CloudFiles[0].Data);
        check(cloud.Find("config/convars","remote_only")?.Value=="preserved" && cloud.Find("config/convars","sensitivity")?.Value=="2","CS2 cloud merge preserves remote-only settings");
        var cloudKeys=new ConfigText(result.CloudFiles.Single(f=>f.Name=="cs2_user_keys.vcfg").Data);
        check(cloudKeys.Find("config/bindings","w")?.Value=="+back" && cloudKeys.Find("config/bindings","W") is null,"CS2 cloud key casing cannot create competing bindings");
        check(File.ReadAllText(Path.Combine(remote,"cs2_user_convars.vcfg")).Contains("sensitivity 1.5"),"CS2 preparing cloud does not mutate mirror");
        Reject(()=>store.Save(snapshot,rows,new Dictionary<string,string>()),"stale snapshot blocked");
        var current = store.Load(profiles[0]);
        store.Restore(current,result.BackupDirectory);
        check(File.ReadAllText(Path.Combine(cfg,LocalConfigStore.UserFile))==original,"CS2 restore recovers original config");
        Reject(()=>store.Restore(store.Load(profiles[0]),Path.Combine(root,"outside")),"backup path traversal blocked");
        File.AppendAllText(Path.Combine(result.BackupDirectory,LocalConfigStore.UserFile),"// tamper");
        Reject(()=>store.Restore(store.Load(profiles[0]),result.BackupDirectory),"tampered backup blocked");
        snapshot=store.Load(profiles[0]); rows=LocalConfigStore.GetSettings(snapshot);
        row=rows.Single(r=>r.Key=="volume");row.Value="0.5";row.Dirty=true;
        result=store.Save(snapshot,rows,new Dictionary<string,string>());
        check(result.CloudFiles.Count==0 && result.FileCount==1,"CS2 machine settings stay local");
        check(File.ReadAllBytes(Path.Combine(cfg,LocalConfigStore.VideoFile)).SequenceEqual(snapshot.Files[LocalConfigStore.VideoFile].Bytes),"CS2 untouched video file remains exact");
        snapshot=store.Load(profiles[0]); rows=LocalConfigStore.GetSettings(snapshot);row=rows.Single(r=>r.Key=="sensitivity");row.Value="3";row.Dirty=true;
        File.Delete(Path.Combine(remote,"cs2_user_convars.vcfg"));result=store.Save(snapshot,rows,new Dictionary<string,string>());
        check(result.CloudFiles.Count==0 && result.CloudNote is not null,"CS2 missing mirror reports skipped cloud without losing local save");
        check(store.LatestBackup(profiles[0])==result.BackupDirectory,"CS2 most recent backup is deterministic for rapid saves");
        File.WriteAllText(Path.Combine(remote,"cs2_user_convars.vcfg"),"config { broken");
        snapshot=store.Load(profiles[0]); rows=LocalConfigStore.GetSettings(snapshot);row=rows.Single(r=>r.Key=="sensitivity");row.Value="3.5";row.Dirty=true;
        result=store.Save(snapshot,rows,new Dictionary<string,string>());
        check(result.CloudFiles.Count==0 && result.CloudNote is not null,"CS2 malformed mirror does not undo successful local save");
        snapshot=store.Load(profiles[0]);rows=LocalConfigStore.GetSettings(snapshot);row=rows.Single(r=>r.Key=="sensitivity");row.Value="4";row.Dirty=true;
        File.SetAttributes(Path.Combine(cfg,LocalConfigStore.UserFile),FileAttributes.ReadOnly);
        try { Reject(()=>store.Save(snapshot,rows,new Dictionary<string,string>()),"read-only config blocked"); }
        finally { File.SetAttributes(Path.Combine(cfg,LocalConfigStore.UserFile),FileAttributes.Normal); }
        Reject(()=>store.Load(new LocalProfile(123,root)),"profile directory mismatch blocked");
        int checks=0;
        var interrupted=new LocalConfigStore(steam,backup,()=>++checks>=3);
        snapshot=interrupted.Load(profiles[0]);rows=LocalConfigStore.GetSettings(snapshot);
        foreach(var item in rows.Where(r=>r.Key is "sensitivity" or "volume")) { item.Value="0.6";item.Dirty=true; }
        Reject(()=>interrupted.Save(snapshot,rows,new Dictionary<string,string>()),"game starting during multi-file save triggers rollback");
        check(snapshot.Files.All(pair=>File.ReadAllBytes(pair.Value.Path).SequenceEqual(pair.Value.Bytes)),"CS2 rollback preserves all original files");
    }
}
