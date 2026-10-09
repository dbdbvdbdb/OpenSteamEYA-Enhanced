namespace OpenSteamEya.AccountManagement;

public sealed record AccountQueryBatchResult(int Succeeded, int Failed, int Skipped, bool Cancelled)
{
    public int ProcessedCount { get; init; }
    public IReadOnlyDictionary<string, bool> Outcomes { get; init; } = new Dictionary<string, bool>();
}

/// <summary>Sequential orchestration only. The caller supplies the established Steam query operation.</summary>
public static class AccountQueryBatch
{
    public static async Task<AccountQueryBatchResult> RunAsync(IReadOnlyList<string?> keys,
        Func<string, CancellationToken, Task> query, CancellationToken cancellationToken = default,
        Action<int, int>? progress = null)
    {
        int succeeded = 0, failed = 0, skipped = 0;
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var outcomes = new Dictionary<string, bool>(StringComparer.Ordinal);
        AccountQueryBatchResult Result(bool cancelled, int processed) => new(succeeded, failed, skipped, cancelled)
        { ProcessedCount = processed, Outcomes = outcomes };
        for (int i = 0; i < keys.Count; i++)
        {
            if (cancellationToken.IsCancellationRequested) return Result(true, i);
            var key = keys[i];
            if (string.IsNullOrEmpty(key) || !visited.Add(key)) { skipped++; continue; }
            progress?.Invoke(i + 1, keys.Count);
            try { await query(key, cancellationToken); succeeded++; outcomes[key] = true; }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            { return Result(true, i); }
            catch { failed++; outcomes[key] = false; } // No credential or input contents in summaries.
        }
        return Result(false, keys.Count);
    }
}
