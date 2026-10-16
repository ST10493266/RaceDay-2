namespace RaceDay.Api.Models;

/// <summary>The two roles supported by RaceDay. Used in [Authorize(Roles = ...)] and in the Roles table.</summary>
public static class RoleNames
{
    public const string Organiser = "Organiser";
    public const string Participant = "Participant";

    public static readonly string[] All = { Organiser, Participant };
}

/// <summary>Allowed values for Event.EventType (also enforced by a CHECK constraint in the database).</summary>
public static class EventTypes
{
    public const string Run = "Run";
    public const string Walk = "Walk";
    public const string Cycle = "Cycle";

    public static readonly string[] All = { Run, Walk, Cycle };

    /// <summary>Case-insensitive match that returns the canonical spelling.</summary>
    public static bool TryNormalise(string? input, out string normalised)
    {
        var match = All.FirstOrDefault(t => string.Equals(t, input?.Trim(), StringComparison.OrdinalIgnoreCase));
        normalised = match ?? string.Empty;
        return match is not null;
    }
}
