// Added for ASP Assistant Arknights on 2026-11-13; see NOTICE.
namespace ASPAssistant.Core.Models;

public sealed class TrackingState
{
    public List<TrackingEntry> Entries { get; set; } = [];
    public List<TrackedLineup> Lineups { get; set; } = [];
}

public sealed record TrackedLineup(string Id, string Name, bool Highlighted = true);

/// <summary>An empty LineupId records an independent target; older unassociated targets remain independent.</summary>
public sealed record TrackingOrigin
{
    public string LineupId { get; init; } = "";
    public bool IsRoute { get; init; }
    public bool IsElite { get; init; }
    public TrackingRole Role { get; init; }
    public string Stages { get; init; } = "";
    public int ExpiresAt { get; init; }
    public string? TransformedCovenant { get; init; }
    public List<string> Equipments { get; init; } = [];

    public static TrackingOrigin From(TrackingEntry entry, string lineupId) => new()
    {
        LineupId = lineupId, IsElite = entry.IsElite, Role = entry.Role, Stages = entry.Stages,
        ExpiresAt = entry.ExpiresAt, TransformedCovenant = entry.TransformedCovenant, Equipments = [.. entry.Equipments]
    };
}
