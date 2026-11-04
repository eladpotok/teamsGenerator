using TeamsGenerator.API;

namespace TeamsGeneratorWebAPI.GroupJoins;

public sealed record GroupJoinSubmission(string SubmissionId, string Name, bool IsGoalKeeper,
    string? Background, Dictionary<string, double> Ratings, int? AlgorithmKey = null,
    List<int>? Positions = null, List<SkillDefinition>? Skills = null,
    bool IsRecoveringFromInjury = false, double? GoalkeeperSkill = null);
public sealed record ProfileGroupJoinSubmission(string SubmissionId, int ProfileRevision, Dictionary<string, double> Ratings,
    int? AlgorithmKey = null, List<int>? Positions = null, string? Background = null, List<SkillDefinition>? Skills = null,
    string? Name = null, bool? IsGoalKeeper = null, bool? IsRecoveringFromInjury = null, double? GoalkeeperSkill = null);
public sealed record GroupJoinDecision(int Revision, Dictionary<string, double> Ratings);
public sealed record GroupJoinReviewVersion(int Revision);
public sealed record GroupJoinSuggestion(string SkillId, double? SuggestedRating, string Explanation, bool InsufficientEvidence);
public sealed record GroupJoinLink(string? Token, bool CanApprove = true);
public sealed record GroupJoinCapability(string StateKey);

public sealed class GroupJoinRequest
{
    public string Id { get; set; } = "";
    public string? ProfileId { get; set; }
    public string Name { get; set; } = "";
    public bool IsGoalKeeper { get; set; }
    public bool IsRecoveringFromInjury { get; set; }
    public double? GoalkeeperSkill { get; set; }
    public string Background { get; set; } = "";
    public List<SkillDefinition> Skills { get; set; } = new();
    public Dictionary<string, double> SelfRatings { get; set; } = new();
    public Dictionary<string, double>? FinalRatings { get; set; }
    public List<GroupJoinSuggestion>? Suggestions { get; set; }
    public string Status { get; set; } = "pending";
    public int Revision { get; set; } = 1;
    public int? ApprovedAlgorithm { get; set; }
    public int? SubmittedAlgorithm { get; set; }
    public List<int> Positions { get; set; } = new();
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }
    public string? ReviewedBy { get; set; }
}

public sealed class GroupJoinState
{
    public string OwnerId { get; set; } = "";
    public string GroupId { get; set; } = "";
    public string? Token { get; set; }
    public List<GroupJoinRequest> Requests { get; set; } = new();
}

public sealed record PublicGroupJoin(string GroupName, string MatchName, string Location,
    string? GroupLogoDataUrl, string Language, string ScheduleType, string? RepeatDay, string? RepeatTime,
    string? EventDate, string? EventTime, int EventDurationMinutes, MatchdayRules? Rules,
    IReadOnlyList<SkillDefinition> Skills, int AlgorithmKey);
