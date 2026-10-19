using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using SteamEyaWinUI.Cs2Settings;

return Verifier.Run(args);

internal static class Verifier
{
    internal const string Pending = "待实机验证";
    internal const string Baseline = "453ddafad244b0cc6af7a3a92f3013abc2451375";
    private static readonly Dictionary<string,string> Sections = LocalConfigStore.FileNames.ToDictionary(n=>n,n=>n==LocalConfigStore.VideoFile?"video.cfg":n==LocalConfigStore.KeysFile?"config/bindings":"config/convars");
    internal static int Run(string[] args)
    {
        try
        {
            if(args.Length==0) { Console.WriteLine("catalog | self-test | snapshot | compare | observe. See tools/Cs2ConfigVerifier/README.zh-CN.md. Real configuration is never written.");return 0; }
            string Option(string name,string? fallback=null)
            { int i=Array.IndexOf(args,"--"+name);return i>=0&&i+1<args.Length?args[i+1]:fallback??throw new ArgumentException("Missing --"+name); }
            string output=Path.GetFullPath(Option("out","artifacts/cs2-feasibility/tool-output"));
            Directory.CreateDirectory(output);
            switch(args[0])
            {
                case "catalog": Catalog(output);break;
                case "self-test": SelfTest(output);break;
                case "snapshot": Snapshot(Option("cfg"),Option("stage"),output);break;
                case "compare": Compare(Option("before"),Option("after"),output);break;
                case "observe": Observe(Option("input"),output);break;
                default:throw new ArgumentException("Unknown command. No real configuration was changed.");
            }
            return 0;
        }
        catch(Exception ex) { Console.Error.WriteLine(ex.GetType().Name+": "+ex.Message);return 1; }
    }
    private static JsonTypeInfo<T> TypeInfo<T>()=>(JsonTypeInfo<T>)(VerifierJsonContext.Default.GetTypeInfo(typeof(T))??throw new InvalidOperationException("Missing generated JSON metadata."));
    internal static void Json<T>(string path,T value)=>File.WriteAllText(path,JsonSerializer.Serialize(value,TypeInfo<T>()),new UTF8Encoding(false));
    internal static T Read<T>(string path)=>JsonSerializer.Deserialize(SafeConfigIO.Read(path,16*1024*1024),TypeInfo<T>())??throw new InvalidDataException("Empty JSON.");
    private static string Csv(string? s)=>"\""+(s??"").Replace("\"","\"\"")+"\"";
    private static string Range(SettingSpec s)=>s.Boolean?"true/false；按原值保留 0/1 或布尔文本":s.Choices is {} c?string.Join("; ",c.Select(x=>x.Value+"="+x.Label)):$"{s.Min.ToString(CultureInfo.InvariantCulture)}..{s.Max.ToString(CultureInfo.InvariantCulture)}; step={s.Step.ToString(CultureInfo.InvariantCulture)}";
    internal static List<ParameterRow> Parameters()
    {
        var rows=SettingCatalog.Visible.Select(s=>new ParameterRow(s.Key,s.Label,s.Category,s.Key,s.FileName,s.Section,s.DataType,Range(s),s.DefaultValue,
            SettingCatalog.Editable.Any(e=>e.Key==s.Key),s.Key is "setting.defaultres" or "setting.defaultresheight",s.Availability.ToString(),s.AllowInsert,s.RestartRequired,s.Evidence,Pending)).ToList();
        rows.AddRange(SettingCatalog.BindKeys.Select(k=>new ParameterRow("bind:"+k,"按键 "+k,"键位绑定",k,LocalConfigStore.KeysFile,"config/bindings","binding",string.Join("; ",SettingCatalog.VerifiedBindActions.Select(a=>a.Value+"="+a.Label)),null,true,false,"AppWhiteList",true,true,"SettingCatalog.VerifiedBindActions",Pending)));
        return rows;
    }
    private static void Catalog(string output)
    {
        var rows=Parameters();var checks=new List<string>();
        void Check(bool ok,string name) { if(!ok)throw new InvalidDataException("Catalog: "+name);checks.Add(name); }
        Check(rows.Select(r=>r.Id).Distinct().Count()==rows.Count,"visible IDs unique");
        Check(SettingCatalog.Editable.All(s=>LocalConfigStore.FileNames.Contains(s.FileName)&&s.Section==Sections[s.FileName]),"all editable mappings use allowlisted files and correct nodes");
        foreach(var s in SettingCatalog.Editable)
        {
            Check(!s.Key.StartsWith("ui:")&&!string.IsNullOrWhiteSpace(s.Evidence),"editable evidence and native key: "+s.Key);
            if(s.DefaultValue is {} def)_=s.Validate(s.Boolean?def is "1" or "true"?"true":"false":def,def);
            if(s.Choices is {} choices)Check(choices.Select(c=>c.Value).Distinct().Count()==choices.Length,"enum values unique: "+s.Key);
        }
        Json(Path.Combine(output,"catalog.json"),new CatalogReport(Baseline,SettingCatalog.GameCompatibility,SettingCatalog.SourceRevision,DateTime.UtcNow,rows,checks));
        var csv=new List<string>{"ID,名称,分类,原始键,文件,节点,类型,范围或枚举,模板参考值,应用允许编辑,条件编辑,源码标记,缺失可插入,声明需重启,依据,游戏验证状态"};
        csv.AddRange(rows.Select(r=>string.Join(",",new[]{r.Id,r.Name,r.Category,r.Key,r.File,r.Section,r.Type,r.Range,r.Default,r.Editable.ToString(),r.Conditional.ToString(),r.SourceAvailability,r.AllowInsert.ToString(),r.Restart.ToString(),r.Evidence,r.Status}.Select(Csv))));
        File.WriteAllLines(Path.Combine(output,"parameter-mapping.csv"),csv,new UTF8Encoding(true));
        var md=new List<string>{"# CS2 参数映射与验证状态","","由 v0.2.9 生产参数目录直接生成。应用源码中的 Verified 仅为应用可写白名单，以下游戏状态独立记录；隔离写入成功不会升级为游戏验证通过。","","| ID / 原始键 | 名称 | 文件 / 节点 | 类型与应用范围 | 模板参考 | 可编辑 | 游戏状态 |","| --- | --- | --- | --- | --- | --- | --- |"};
        md.AddRange(rows.Select(r=>$"| `{r.Id}` | {r.Name} | `{r.File}` / `{r.Section}` | {r.Type}；{r.Range.Replace("|","/")} | {r.Default??"保持现有"} | {(r.Editable?"是":r.Conditional?"分辨率组合控件条件允许":"否，保留/显示")} | {r.Status} |"));
        File.WriteAllLines(Path.Combine(output,"parameter-mapping.md"),md,new UTF8Encoding(false));
        Console.WriteLine($"Catalog: {SettingCatalog.Visible.Length} visible settings, {SettingCatalog.Editable.Length} editable settings, {SettingCatalog.BindKeys.Length} binding keys. All game statuses remain {Pending}.");
    }
    private static void SelfTest(string output)
    {
        var root=Path.Combine(output,"isolated-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        var results=new List<CheckResult>();
        void Check(bool condition,string name) { results.Add(new(name,condition,"隔离文件测试，不代表游戏实测"));if(!condition)throw new InvalidDataException("FAIL "+name); }
        try
        {
            Catalog(output);
            foreach(var spec in SettingCatalog.Editable)
            {
                string value=spec.Boolean?(spec.DefaultValue is "1" or "true"?"false":"true"):spec.Choices is {} choices?choices.Last().Value:spec.Max.ToString(CultureInfo.InvariantCulture);
                string caseRoot=Path.Combine(root,"case-"+results.Count);
                var templates=new UnifiedConfigStore(Path.Combine(caseRoot,"template"));var basis=templates.LoadOrCreate();var rows=UnifiedConfigStore.GetSettings(basis);
                var row=rows.Single(r=>r.Spec.Key==spec.Key);row.Value=value;row.Dirty=true;
                if(spec.Key=="setting.videocfg_fsr_detail"&&value!="0")foreach(var aa in rows.Where(r=>r.Spec.Key is "setting.msaa_samples" or "setting.r_csgo_cmaa_enable")){aa.Value="0";aa.Dirty=true;}
                var saved=templates.Save(basis,rows,new Dictionary<string,string>(),"isolated validation");
                var reloaded=templates.Load();Check(saved.Files.All(p=>p.Value.SequenceEqual(reloaded.Files[p.Key])),"template persistence: "+spec.Key);
                string steam=Path.Combine(caseRoot,"steam");
                foreach(uint id in new uint[]{101,202})
                {
                    string cfg=Path.Combine(steam,"userdata",id.ToString(),"730","local","cfg");Directory.CreateDirectory(cfg);
                    foreach(string name in LocalConfigStore.FileNames)
                    {
                        byte[] data=UnifiedConfigStore.EmptyFile(name);
                        string section=Sections[name];
                        var fields=new Dictionary<string,string>{{"unrelated_sentinel",id.ToString()}};
                        if(name!=LocalConfigStore.KeysFile&&spec.FileName==name)fields[spec.Key+(name==LocalConfigStore.VideoFile?"":"$9")]=spec.Boolean?"0":spec.Choices?.First().Value??spec.Min.ToString(CultureInfo.InvariantCulture);
                        data=new ConfigText(data).Patch(section,fields,true);File.WriteAllBytes(Path.Combine(cfg,name),data);
                    }
                    string remote=Path.Combine(steam,"userdata",id.ToString(),"730","remote");Directory.CreateDirectory(remote);File.WriteAllText(Path.Combine(remote,"sentinel"),"never write cloud");
                }
                var service=new ConfigApplyService(steam,Path.Combine(caseRoot,"backups"),()=>false);var preview=service.Preview(saved);var applied=service.Apply(preview);
                Check(applied.Succeeded==2&&applied.Failed==0,"two-account deployment: "+spec.Key);
                foreach(var plan in preview.Accounts)
                {
                    var doc=new ConfigText(File.ReadAllBytes(Path.Combine(plan.Directory,spec.FileName)));
                    string targetKey=spec.Key+(spec.FileName==LocalConfigStore.VideoFile?"":"$9");
                    string expected=spec.Validate(value,spec.Boolean?"0":spec.DefaultValue??"");
                    Check(doc.Find(spec.Section,targetKey)?.Value==expected,"target key/value: "+spec.Key+" / "+plan.AccountId);
                    Check(doc.Find(spec.Section,"unrelated_sentinel")?.Value==plan.AccountId.ToString(),"preserve target-specific value: "+spec.Key+" / "+plan.AccountId);
                    Check(File.ReadAllText(Path.Combine(steam,"userdata",plan.AccountId.ToString(),"730","remote","sentinel"))=="never write cloud","cloud untouched: "+spec.Key+" / "+plan.AccountId);
                }
                var backup=applied.Accounts[0].Backup!;var restored=service.Apply(service.PreviewRestore(backup));Check(restored.Succeeded==1,"transaction restore: "+spec.Key);
            }
            ProbeMergeCombination(root,output,Check);
            ProbeDisplayMetadata(root,output,Check);
            ProbeBindings(root,Check);
            ProbeEvidenceGate(root,Check);
            Json(Path.Combine(output,"isolated-results.json"),new IsolationReport(Baseline,results.Count,results,DateTime.UtcNow));
            File.WriteAllLines(Path.Combine(output,"isolated-results.txt"),results.Select(r=>"PASS "+r.Name).Append($"{results.Count} isolated checks passed; no real Steam configuration or game behavior was tested."));
            Console.WriteLine($"PASS {results.Count} isolated checks. All directories are under the requested output. No real account was written.");
        }
        catch { Json(Path.Combine(output,"isolated-results.json"),new IsolationReport(Baseline,results.Count,results,DateTime.UtcNow));throw; }
    }
    private static void ProbeBindings(string root,Action<bool,string> check)
    {
        var actions=SettingCatalog.VerifiedBindActions.Where(a=>a.Value!="<unbound>").ToArray();
        int index=0;
        foreach(string key in SettingCatalog.BindKeys)
        {
            string folder=Path.Combine(root,"binding-"+index);
            var store=new UnifiedConfigStore(Path.Combine(folder,"template"));
            var basis=store.LoadOrCreate();
            string action=actions[index++%actions.Length].Value;
            var saved=store.Save(basis,[],new Dictionary<string,string>{{key,action}},"synthetic binding");
            string steam=Path.Combine(folder,"steam"),cfg=Path.Combine(steam,"userdata","505","730","local","cfg");
            Directory.CreateDirectory(cfg);
            File.WriteAllBytes(Path.Combine(cfg,LocalConfigStore.KeysFile),new ConfigText(UnifiedConfigStore.EmptyFile(LocalConfigStore.KeysFile)).Patch("config/bindings",new Dictionary<string,string>{{"unrelated_custom_key","echo synthetic-sentinel"}},true));
            var service=new ConfigApplyService(steam,Path.Combine(folder,"backups"),()=>false);
            var result=service.Apply(service.Preview(saved));
            var deployed=new ConfigText(File.ReadAllBytes(Path.Combine(cfg,LocalConfigStore.KeysFile)));
            check(result.Succeeded==1&&deployed.Find("config/bindings",key)?.Value==action,"binding deployment: "+key);
            check(deployed.Find("config/bindings","unrelated_custom_key")?.Value=="echo synthetic-sentinel","preserve custom binding: "+key);
        }
        foreach(var action in SettingCatalog.VerifiedBindActions)
        {
            var store=new UnifiedConfigStore(Path.Combine(root,"action-"+index++));
            var basis=store.LoadOrCreate();
            var saved=store.Save(basis,[],new Dictionary<string,string>{{"w",action.Value}},"synthetic action");
            check(new ConfigText(saved.Files[LocalConfigStore.KeysFile]).Find("config/bindings","w")?.Value==action.Value,"binding action template round-trip: "+action.Value);
        }
    }
    private static void ProbeEvidenceGate(string root,Action<bool,string> check)
    {
        string folder=Path.Combine(root,"evidence-gate");Directory.CreateDirectory(folder);
        string input=Path.Combine(folder,"observations.json");
        void Rejected(GameObservation observation,string description)
        {
            Json(input,new List<GameObservation>{observation});
            bool rejected=false;try{Observe(input,folder);}catch(InvalidDataException){rejected=true;}
            check(rejected,description);
        }
        var candidate=new GameObservation("sensitivity","验证通过",false,"2000933",true,"1.37",true,"1.37",["only-file-evidence.json"],"synthetic assertion");
        File.WriteAllText(Path.Combine(folder,"only-file-evidence.json"),"{}");
        Rejected(candidate,"file-only evidence cannot claim a game pass");
        Rejected(candidate with {GameActuallyStarted=true,RestartChecked=false},"no restart observation cannot claim a game pass");
        Rejected(candidate with {GameActuallyStarted=true,Evidence=["missing-evidence.png"]},"missing evidence cannot claim a game pass");
    }
    private static void ProbeMergeCombination(string root,string output,Action<bool,string> check)
    {
        string steam=Path.Combine(root,"partial-combination-steam"),cfg=Path.Combine(steam,"userdata","303","730","local","cfg");Directory.CreateDirectory(cfg);
        File.WriteAllText(Path.Combine(cfg,LocalConfigStore.VideoFile),"\"video.cfg\" { Version 16 \"setting.msaa_samples\" 0 \"setting.r_csgo_cmaa_enable\" 0 \"setting.videocfg_fsr_detail\" 2 }");
        var partial=new TemplateSnapshot(new(),new(){{LocalConfigStore.VideoFile,Encoding.UTF8.GetBytes("\"video.cfg\" { Version 16 \"setting.msaa_samples\" 4 }")}},null);
        var service=new ConfigApplyService(steam,Path.Combine(root,"partial-combination-backups"),()=>false);var preview=service.Preview(partial);
        bool rejected=false;try{UnifiedConfigStore.ValidateVideoCombination(new Dictionary<string,byte[]>{{LocalConfigStore.VideoFile,preview.Accounts.Single().After[LocalConfigStore.VideoFile]!}});}catch(InvalidDataException){rejected=true;}
        Json(Path.Combine(output,"partial-video-merge-probe.json"),new ProbeResult("MERGE-01",preview.Accounts.Single().Error is null&&rejected,"Partial template AA merged with target FSR violates the application's own conservative rule; this does not prove an engine incompatibility.","视频模板 MSAA=4，目标 FSR=2 / MSAA=0 / CMAA=0",Encoding.UTF8.GetString(preview.Accounts.Single().After[LocalConfigStore.VideoFile]!)));
        check(rejected&&preview.Accounts.Single().Error is null,"reproduce merged-video policy gap on unmodified v0.2.9");
    }
    private static void ProbeDisplayMetadata(string root,string output,Action<bool,string> check)
    {
        string steam=Path.Combine(root,"display-steam"),cfg=Path.Combine(steam,"userdata","404","730","local","cfg");Directory.CreateDirectory(cfg);
        byte[] video=Encoding.UTF8.GetBytes("\"video.cfg\" { Version 16 \"setting.defaultres\" 1280 \"setting.defaultresheight\" 720 \"setting.refreshrate_numerator\" 60 \"setting.refreshrate_denominator\" 1 }");
        File.WriteAllBytes(Path.Combine(cfg,LocalConfigStore.VideoFile),video);
        var snapshot=new TemplateSnapshot(new(){Display=new("synthetic-display",1920,1080,60)},new(){{LocalConfigStore.VideoFile,video}},null);
        var service=new ConfigApplyService(steam,Path.Combine(root,"display-backups"),()=>false,displayModes:()=>[new("synthetic-display","synthetic",1920,1080,60,true)]);
        var preview=service.Preview(snapshot);var doc=new ConfigText(preview.Accounts.Single().After[LocalConfigStore.VideoFile]!);
        bool differs=doc.Find("video.cfg","setting.defaultres")?.Value=="1920";
        Json(Path.Combine(output,"display-manifest-probe.json"),new ProbeResult("DISPLAY-01",differs,"A manifest/file resolution mismatch is accepted; deployment follows metadata rather than the native template's width/height.",Encoding.UTF8.GetString(video),Encoding.UTF8.GetString(preview.Accounts.Single().After[LocalConfigStore.VideoFile]!)));
        check(differs,"reproduce display metadata/native-file mismatch on v0.2.9");
    }
    private static void Snapshot(string cfg,string stage,string output)
    {
        if(!new[]{"before","applied","started","exited","restarted","restored"}.Contains(stage))throw new ArgumentException("Unknown stage.");
        string directory=Path.GetFullPath(cfg);SafeConfigIO.RejectLinks(directory);
        var files=new List<FileSnapshot>();
        foreach(string name in LocalConfigStore.FileNames)
        {
            string path=SafeConfigIO.Child(directory,name);if(!File.Exists(path)){files.Add(new(name,false,null,[]));continue;}
            byte[] bytes=SafeConfigIO.Read(path);SafeConfigIO.ValidateFile(name,bytes);var doc=new ConfigText(bytes);
            var keys=SettingCatalog.Visible.Where(s=>s.FileName==name).Select(s=>s.Key).ToHashSet(StringComparer.Ordinal);
            var values=doc.Entries.Where(e=>e.Section==Sections[name]&&(keys.Contains(SettingCatalog.BaseKey(e.Key))||name==LocalConfigStore.VideoFile&&e.Key is "Version" or "setting.refreshrate_denominator" or "setting.nowindowborder" or "setting.coop_fullscreen")).Select(e=>new NativeValue(e.Key,e.Value)).ToList();
            // Binding commands are deliberately excluded: custom commands may contain personal data.
            files.Add(new(name,true,SafeConfigIO.Hash(bytes),values));
        }
        Json(Path.Combine(output,stage+".json"),new PhaseSnapshot(stage,DateTime.UtcNow,LocalConfigStore.GameRunning(),files));
        Console.WriteLine("Read-only snapshot: "+stage+". No game launch or in-game effectiveness is inferred.");
    }
    private static void Compare(string before,string after,string output)
    {
        var a=Read<PhaseSnapshot>(before);var b=Read<PhaseSnapshot>(after);var changes=new List<ValueDiff>();
        foreach(var name in LocalConfigStore.FileNames)
        {
            var f=a.Files.SingleOrDefault(f=>f.Name==name);var g=b.Files.SingleOrDefault(f=>f.Name==name);
            var left=f?.Values.ToDictionary(v=>v.Key,v=>v.Value)??[];var right=g?.Values.ToDictionary(v=>v.Key,v=>v.Value)??[];
            foreach(var key in left.Keys.Union(right.Keys).Order())if(left.GetValueOrDefault(key)!=right.GetValueOrDefault(key))changes.Add(new(name,key,left.GetValueOrDefault(key),right.GetValueOrDefault(key),Pending));
        }
        Json(Path.Combine(output,"diff.json"),new DiffReport(a.Stage,b.Stage,changes));
        Console.WriteLine($"{changes.Count} persisted-value differences. File differences alone do not establish game effectiveness.");
    }
    private static void Observe(string input,string output)
    {
        var observations=Read<List<GameObservation>>(input);var parameters=Parameters();
        if(observations.Select(o=>o.Id).Distinct().Count()!=observations.Count)throw new InvalidDataException("Duplicate observation ID.");
        var allowed=new[]{"验证通过","部分通过","验证失败",Pending,"不适用"};
        foreach(var o in observations)
        {
            if(!parameters.Any(p=>p.Id==o.Id)||!allowed.Contains(o.Status))throw new InvalidDataException("Unknown parameter or status.");
            if(o.Status!=Pending&&(!o.GameActuallyStarted||string.IsNullOrWhiteSpace(o.ClientVersion)||o.Evidence.Count==0||o.Evidence.Any(p=>!File.Exists(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(input))!,p))))))
                throw new InvalidDataException("Conclusive status requires an actual game session, client version and existing evidence files.");
            if(o.Status=="验证通过"&&(!o.ObservedInGame||!o.RestartChecked||string.IsNullOrWhiteSpace(o.InGameValue)||string.IsNullOrWhiteSpace(o.AfterRestartValue)))
                throw new InvalidDataException("验证通过 requires direct in-game observation and restart evidence.");
        }
        Json(Path.Combine(output,"reviewed-observations.json"),observations);
        Console.WriteLine("Observation evidence prerequisites checked. This validates report completeness, not authenticity of a human assertion.");
    }
}
internal sealed record ParameterRow(string Id,string Name,string Category,string Key,string File,string Section,string Type,string Range,string? Default,bool Editable,bool Conditional,string SourceAvailability,bool AllowInsert,bool Restart,string Evidence,string Status);
internal sealed record CatalogReport(string Baseline,string Compatibility,string SourceRevision,DateTime GeneratedUtc,List<ParameterRow> Parameters,List<string> StaticChecks);
internal sealed record CheckResult(string Name,bool Passed,string Scope);
internal sealed record IsolationReport(string Baseline,int Count,List<CheckResult> Checks,DateTime GeneratedUtc);
internal sealed record ProbeResult(string Id,bool Reproduced,string Meaning,string Before,string After);
internal sealed record NativeValue(string Key,string Value);
internal sealed record FileSnapshot(string Name,bool Exists,string? Sha256,List<NativeValue> Values);
internal sealed record PhaseSnapshot(string Stage,DateTime Utc,bool Cs2ProcessPresent,List<FileSnapshot> Files);
internal sealed record ValueDiff(string File,string Key,string? Before,string? After,string GameStatus);
internal sealed record DiffReport(string BeforeStage,string AfterStage,List<ValueDiff> Changes);
internal sealed record GameObservation(string Id,string Status,bool GameActuallyStarted,string ClientVersion,bool ObservedInGame,string? InGameValue,bool RestartChecked,string? AfterRestartValue,List<string> Evidence,string Notes);
[JsonSourceGenerationOptions(WriteIndented=true)]
[JsonSerializable(typeof(CatalogReport))][JsonSerializable(typeof(IsolationReport))][JsonSerializable(typeof(ProbeResult))]
[JsonSerializable(typeof(PhaseSnapshot))][JsonSerializable(typeof(DiffReport))][JsonSerializable(typeof(List<GameObservation>))]
internal sealed partial class VerifierJsonContext:JsonSerializerContext;
