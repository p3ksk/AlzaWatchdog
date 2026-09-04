using System.Collections.Concurrent;

namespace AlzaWatchdog.Api.Workers;

/// <summary>
/// One line of a worker's status. <paramref name="Hint"/> is the explanation shown
/// on hover: it lives next to the value it describes so renaming a label cannot
/// leave a stale explanation behind somewhere else.
/// </summary>
public record WorkerSetting(string Label, string Value, string? Hint = null);

/// <summary>What a background worker last did, and when it will act again.</summary>
public record WorkerStatus(
    string Name,
    string Description,
    bool Enabled,
    DateTimeOffset? LastRunAt,
    DateTimeOffset? NextRunAt,
    string? LastOutcome,
    int Runs,
    IReadOnlyList<WorkerSetting> Settings);

/// <summary>
/// Where the background workers say what they are doing, so the admin section can
/// show it.
///
/// Each update replaces the whole record rather than mutating one in place: the
/// workers write from their own threads while requests read, and swapping an
/// immutable value avoids a half-updated status being served.
/// </summary>
public class WorkerStatusRegistry
{
    private readonly ConcurrentDictionary<string, WorkerStatus> _statuses = new();

    public void Set(WorkerStatus status) => _statuses[status.Name] = status;

    public WorkerStatus? Get(string name) => _statuses.GetValueOrDefault(name);

    public IReadOnlyList<WorkerStatus> All() => [.. _statuses.Values.OrderBy(s => s.Name)];
}
