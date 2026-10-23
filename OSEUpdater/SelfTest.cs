using System.Text;

namespace OSEUpdater;

internal static class SelfTest
{
    private static int _passed;
    private static int _failed;

    internal static int Run()
    {
        var root = Path.Combine(Path.GetTempPath(), "ose-updater-selftest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            CheckReplacePreservesExtras(root);
            CheckRollbackRestoresPrevious(root);
            CheckSkipsRunningUpdater(root);
            CheckPathGuards();
        }
        catch (Exception ex)
        {
            Fail("self-test crashed: " + ex);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch (Exception) { }
        }

        Program.Write("self-test summary: PASS=" + _passed + " FAIL=" + _failed);
        return _failed == 0 ? 0 : 5;
    }

    private static void CheckReplacePreservesExtras(string root)
    {
        var (source, target, backup) = BuildTrees(root, "replace");
        var files = new List<Program.SourceFile>();
        foreach (var path in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, path);
            files.Add(new Program.SourceFile(relative, path, new FileInfo(path).Length));
        }

        var ok = Program.Apply(files, target, backup, 0, "1.2.3", out var applied, out var created, out var error);
        Check(ok, "apply succeeds: " + error);
        Check(Read(Path.Combine(target, "app.txt")) == "new-app", "replaced file has new content");
        Check(Read(Path.Combine(target, "sub", "core.txt")) == "new-core", "nested file replaced");
        Check(Read(Path.Combine(target, "added.txt")) == "added", "new file created");
        Check(Read(Path.Combine(target, "steam_appid.txt")) == "480", "unrelated target file preserved");
        Check(File.Exists(Path.Combine(target, "last-update.json")), "last-update.json written");
        Check(applied.Count == 3 && created.Count == 1, "applied/created counters match");
        Check(!Directory.EnumerateFiles(target, "*.ose-new", SearchOption.AllDirectories).Any(), "no staging leftovers");
    }

    private static void CheckRollbackRestoresPrevious(string root)    {
        var (source, target, backup) = BuildTrees(root, "rollback");
        var files = new List<Program.SourceFile>();
        foreach (var path in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, path);
            files.Add(new Program.SourceFile(relative, path, new FileInfo(path).Length));
        }

        var ok = Program.Apply(files, target, backup, 2, "1.2.3", out var applied, out var created, out var error);
        Check(!ok && error is not null, "apply reports failure when simulated: " + error);
        Program.Rollback(target, backup, applied, created);
        Check(Read(Path.Combine(target, "app.txt")) == "old-app", "rollback restores replaced file");
        Check(Read(Path.Combine(target, "sub", "core.txt")) == "old-core", "rollback restores nested file");
        Check(!File.Exists(Path.Combine(target, "added.txt")), "rollback removes newly created file");
    }

    private static void CheckSkipsRunningUpdater(string root)
    {
        var (source, target, backup) = BuildTrees(root, "self");
        Write(Path.Combine(source, "OSEUpdater.exe"), "new-updater");
        Write(Path.Combine(target, "OSEUpdater.exe"), "running-updater");
        var files = new List<Program.SourceFile>
        {
            new("OSEUpdater.exe", Path.Combine(source, "OSEUpdater.exe"), new FileInfo(Path.Combine(source, "OSEUpdater.exe")).Length),
        };

        var selfPath = Path.Combine(target, "OSEUpdater.exe");
        Program.SelfPathOverride = selfPath;
        try
        {
            var ok = Program.Apply(files, target, backup, 0, "1.2.3", out var applied, out var created, out var error);
            Check(ok, "apply succeeds when the running updater is part of the package: " + error);
            Check(Read(selfPath) == "running-updater", "running updater file is left untouched");
            Check(applied.Count == 0 && created.Count == 0, "skipped updater is not counted as applied");
            Check(!File.Exists(selfPath + ".ose-new"), "no staging leftover for the running updater");
        }
        finally
        {
            Program.SelfPathOverride = null;
        }
    }

    private static void CheckPathGuards()
    {
        Check(!Program.IsSafeRelative("..\\evil.txt"), "rejects parent traversal");
        Check(!Program.IsSafeRelative("C:\\evil.txt"), "rejects rooted path");
        Check(!Program.IsSafeRelative("sub/../evil.txt"), "rejects embedded traversal");
        Check(Program.IsSafeRelative("sub/core.txt"), "accepts normal relative path");
        Check(!Program.IsUnder("C:\\apps\\ose", "C:\\apps\\ose-evil\\x.txt"), "rejects sibling prefix escape");
        Check(Program.IsUnder("C:\\apps\\ose", "C:\\apps\\ose\\x.txt"), "accepts inner path");
    }

    private static (string Source, string Target, string Backup) BuildTrees(string root, string name)
    {
        var baseDir = Path.Combine(root, name);
        var source = Path.Combine(baseDir, "source");
        var target = Path.Combine(baseDir, "target");
        var backup = Path.Combine(baseDir, "backup");
        Directory.CreateDirectory(Path.Combine(source, "sub"));
        Directory.CreateDirectory(Path.Combine(target, "sub"));
        Write(Path.Combine(source, "app.txt"), "new-app");
        Write(Path.Combine(source, "sub", "core.txt"), "new-core");
        Write(Path.Combine(source, "added.txt"), "added");
        Write(Path.Combine(target, "app.txt"), "old-app");
        Write(Path.Combine(target, "sub", "core.txt"), "old-core");
        Write(Path.Combine(target, "steam_appid.txt"), "480");
        return (source, target, backup);
    }

    private static void Write(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content, new UTF8Encoding(false));
    }

    private static string Read(string path) => File.Exists(path) ? File.ReadAllText(path) : "<missing>";

    private static void Check(bool condition, string name)
    {
        if (condition)
        {
            _passed++;
            Program.Write("PASS " + name);
        }
        else
        {
            Fail(name);
        }
    }

    private static void Fail(string name)
    {
        _failed++;
        Program.Write("FAIL " + name);
    }
}
