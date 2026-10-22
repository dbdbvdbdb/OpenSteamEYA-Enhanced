using System.Diagnostics;
using System.Text;

namespace OSEUpdater;

internal sealed record UpdateOptions(
    int ParentPid,
    string? Source,
    string? Target,
    string? Restart,
    string? Backup,
    string? LogPath,
    string? Version,
    bool DryRun,
    bool VerifyOnly,
    bool SelfTest,
    int FailAfter,
    int WaitSeconds)
{
    public static UpdateOptions Parse(string[] args)
    {
        var pid = 0;
        string? source = null, target = null, restart = null, backup = null, log = null, version = null;
        var dryRun = false;
        var verifyOnly = false;
        var selfTest = false;
        var failAfter = 0;
        var wait = 120;

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException("missing value for " + arg);
            switch (arg)
            {
                case "--pid": pid = int.Parse(Next()); break;
                case "--source": source = Next(); break;
                case "--target": target = Next(); break;
                case "--restart": restart = Next(); break;
                case "--backup": backup = Next(); break;
                case "--log": log = Next(); break;
                case "--version": version = Next(); break;
                case "--wait-seconds": wait = int.Parse(Next()); break;
                case "--simulate-failure-at": failAfter = int.Parse(Next()); break;
                case "--dry-run": dryRun = true; break;
                case "--verify-only": verifyOnly = true; break;
                case "--self-test": selfTest = true; break;
                default: throw new ArgumentException("unknown argument " + arg);
            }
        }

        if (!selfTest)
        {
            if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(target) || string.IsNullOrWhiteSpace(backup))
                throw new ArgumentException("--source, --target and --backup are required");
        }

        return new UpdateOptions(pid, source, target, restart, backup, log, version, dryRun, verifyOnly, selfTest, failAfter, wait);
    }
}

internal static class Program
{
    private const int ExitOk = 0;
    private const int ExitFailed = 2;
    private const int ExitArguments = 3;
    private const int ExitTargetNotWritable = 4;
    private const int ExitSelfTestFailed = 5;
    private const string StagingSuffix = ".ose-new";
    private static readonly StringBuilder Transcript = new();

    private static int Main(string[] args)
    {
        UpdateOptions options;
        try
        {
            options = UpdateOptions.Parse(args);
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException)
        {
            Console.Error.WriteLine("argument error: " + ex.Message);
            return ExitArguments;
        }

        if (options.SelfTest)
        {
            var selfTestExit = SelfTest.Run();
            Flush(options.LogPath);
            return selfTestExit;
        }

        try
        {
            var exit = Run(options);
            Flush(options.LogPath);
            return exit;
        }
        catch (Exception ex)
        {
            Write("fatal: " + ex);
            Flush(options.LogPath);
            return ExitFailed;
        }
    }

    private static int Run(UpdateOptions options)
    {
        var source = Path.GetFullPath(options.Source!);
        var target = Path.GetFullPath(options.Target!);
        var backup = Path.GetFullPath(options.Backup!);

        Write("updater start: source=" + source);
        Write("updater start: target=" + target);

        if (options.ParentPid > 0)
        {
            if (!WaitForExit(options.ParentPid, TimeSpan.FromSeconds(options.WaitSeconds)))
            {
                Write("parent process " + options.ParentPid + " still running after timeout");
                return ExitFailed;
            }

            Write("parent process exited");
        }

        if (!Directory.Exists(source))
        {
            Write("source directory missing");
            return ExitFailed;
        }

        if (!Directory.Exists(target))
        {
            Write("target directory missing");
            return ExitFailed;
        }

        var files = CollectSourceFiles(source);
        Write("package files: " + files.Count + " (" + files.Sum(f => f.Size) + " bytes)");

        if (options.VerifyOnly)
        {
            foreach (var file in files)
            {
                if (!File.Exists(file.FullPath))
                {
                    Write("verify failed: " + file.Relative);
                    return ExitFailed;
                }
            }

            Write("verify ok");
            return ExitOk;
        }

        if (options.DryRun)
        {
            foreach (var file in files)
            {
                Write("would replace: " + file.Relative + " (" + file.Size + " bytes)");
            }

            return ExitOk;
        }

        if (!ProbeWritable(target))
        {
            Write("target directory is not writable");
            return ExitTargetNotWritable;
        }

        if (!Apply(files, target, backup, options.FailAfter, options.Version, out var applied, out var created, out var error))
        {
            Write("apply failed: " + error);
            Rollback(target, backup, applied, created);
            Write("rollback finished");
            return ExitFailed;
        }

        Write("applied " + applied.Count + " files");
        TryCleanupStaging(target, files);
        TryStart(options.Restart, target);
        return ExitOk;
    }

    private static bool WaitForExit(int pid, TimeSpan timeout)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return process.WaitForExit((int)timeout.TotalMilliseconds);
        }
        catch (ArgumentException)
        {
            return true;
        }
        catch (InvalidOperationException)
        {
            return true;
        }
    }

    private static bool ProbeWritable(string target)
    {
        var probe = Path.Combine(target, ".ose-write-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var stream = new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.None))
            {
                stream.WriteByte(0);
            }

            File.Delete(probe);
            return true;
        }
        catch (Exception ex)
        {
            Write("write probe failed: " + ex.Message);
            return false;
        }
    }

    private static List<SourceFile> CollectSourceFiles(string source)
    {
        var files = new List<SourceFile>();
        foreach (var path in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, path);
            if (!IsSafeRelative(relative))
            {
                throw new InvalidOperationException("unsafe path rejected: " + relative);
            }

            var info = new FileInfo(path);
            if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidOperationException("reparse point rejected: " + relative);
            }

            files.Add(new SourceFile(relative, path, info.Length));
        }

        return files;
    }

    internal static bool IsSafeRelative(string relative) =>
        !string.IsNullOrWhiteSpace(relative) &&
        !Path.IsPathRooted(relative) &&
        !relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Contains("..") &&
        !relative.Contains(':');

    internal static bool Apply(
        List<SourceFile> files,
        string target,
        string backup,
        int failAfter,
        string? version,
        out List<string> applied,
        out List<string> created,
        out string? error)
    {
        applied = new List<string>();
        created = new List<string>();
        error = null;
        Directory.CreateDirectory(backup);

        try
        {
            for (var i = 0; i < files.Count; i++)
            {
                var file = files[i];
                var destination = Path.Combine(target, file.Relative);
                if (!IsUnder(target, destination))
                {
                    throw new InvalidOperationException("unsafe destination: " + file.Relative);
                }

                var destinationDirectory = Path.GetDirectoryName(destination)!;
                Directory.CreateDirectory(destinationDirectory);

                var backupPath = Path.Combine(backup, file.Relative);
                if (File.Exists(destination))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(backupPath)!);
                    File.Copy(destination, backupPath, overwrite: true);
                }
                else
                {
                    created.Add(file.Relative);
                }

                var staging = destination + StagingSuffix;
                File.Copy(file.FullPath, staging, overwrite: true);
                File.Move(staging, destination, overwrite: true);
                applied.Add(file.Relative);

                if (failAfter > 0 && applied.Count >= failAfter)
                {
                    throw new InvalidOperationException("simulated failure after " + failAfter + " files");
                }
            }

            WriteLastUpdate(target, version, files.Count);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    internal static void Rollback(string target, string backup, List<string> applied, List<string> created)
    {
        created.Reverse();
        applied.Reverse();
        foreach (var relative in applied)
        {
            var destination = Path.Combine(target, relative);
            var backupPath = Path.Combine(backup, relative);
            try
            {
                if (File.Exists(backupPath))
                {
                    File.Copy(backupPath, destination, overwrite: true);
                }
            }
            catch (Exception ex)
            {
                Write("rollback copy failed: " + relative + ": " + ex.Message);
            }
        }

        foreach (var relative in created)
        {
            try
            {
                var destination = Path.Combine(target, relative);
                if (File.Exists(destination))
                {
                    File.Delete(destination);
                }
            }
            catch (Exception ex)
            {
                Write("rollback delete failed: " + relative + ": " + ex.Message);
            }
        }
    }

    private static void WriteLastUpdate(string target, string? version, int fileCount)
    {
        var payload = "{\"to\":\"" + Escape(version ?? "unknown") + "\",\"files\":" + fileCount +
                      ",\"at\":\"" + DateTimeOffset.Now.ToString("O") + "\"}\n";
        var path = Path.Combine(target, "last-update.json");
        File.WriteAllText(path, payload, new UTF8Encoding(false));
    }

    private static void TryCleanupStaging(string target, List<SourceFile> files)
    {
        foreach (var file in files)
        {
            var staging = Path.Combine(target, file.Relative) + StagingSuffix;
            try
            {
                if (File.Exists(staging))
                {
                    File.Delete(staging);
                }
            }
            catch (Exception)
            {
                // A leftover staging file is harmless; the next update overwrites it.
            }
        }
    }

    private static void TryStart(string? restart, string target)
    {
        if (string.IsNullOrWhiteSpace(restart))
        {
            return;
        }

        var path = Path.Combine(target, restart);
        if (!File.Exists(path))
        {
            Write("restart target missing: " + path);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                WorkingDirectory = target,
                UseShellExecute = true,
            });
            Write("restart requested: " + restart);
        }
        catch (Exception ex)
        {
            Write("restart failed: " + ex.Message);
        }
    }

    internal static bool IsUnder(string root, string candidate)
    {
        var normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var normalized = Path.GetFullPath(candidate);
        return normalized.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
    }

    internal static string Escape(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);

    internal static void Write(string message)
    {
        var line = DateTimeOffset.Now.ToString("HH:mm:ss.fff") + " " + message;
        Console.WriteLine(line);
        Transcript.AppendLine(line);
    }

    private static void Flush(string? logPath)
    {
        if (string.IsNullOrWhiteSpace(logPath))
        {
            return;
        }

        try
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(logPath));
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.AppendAllText(logPath, Transcript.ToString(), new UTF8Encoding(false));
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("log write failed: " + ex.Message);
        }
    }

    internal sealed record SourceFile(string Relative, string FullPath, long Size);
}
