namespace AlzaWatchdog.Api.Admin;

public class AdminOptions
{
    public const string SectionName = "Admin";

    /// <summary>
    /// Account keys granted admin access, as GUIDs in either form. An admin is an
    /// ordinary account that happens to be listed here, so there is no second
    /// credential to manage — and revoking access is a config change plus a restart
    /// rather than a database edit.
    /// </summary>
    public string[] Keys { get; set; } = [];

    /// <summary>Parsed once at startup; malformed entries are ignored rather than crashing the app.</summary>
    public HashSet<Guid> ParsedKeys =>
        _parsed ??= [.. Keys.Select(k => Guid.TryParse(k, out var id) ? id : (Guid?)null)
                            .Where(id => id is not null)
                            .Select(id => id!.Value)];

    private HashSet<Guid>? _parsed;

    public bool IsAdmin(Guid userId) => ParsedKeys.Contains(userId);
}
