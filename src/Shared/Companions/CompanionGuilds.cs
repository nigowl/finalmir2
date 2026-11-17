#nullable enable
namespace Mir2.Companions;

public sealed record CompanionGuildPreferences
{
    public string Mode { get; set; } = "off";
    public string Name { get; set; } = "";
    public bool Siege { get; set; }
}

public sealed record KnownGuildPlan(string Action, string Name, string Member, string Reason,
    bool InvitationsEnabled, KnownGoal? Goal = null, uint[]? Targets = null);
