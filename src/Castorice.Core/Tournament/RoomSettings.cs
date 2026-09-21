namespace Castorice.Core.Tournament;

/// <summary>The <c>&lt;teammode&gt;</c> argument of <c>!mp set</c>.</summary>
public enum TeamMode
{
    HeadToHead = 0,
    TagCoop = 1,
    TeamVs = 2,
    TagTeamVs = 3,
}

/// <summary>The <c>&lt;scoremode&gt;</c> argument of <c>!mp set</c>.</summary>
public enum ScoreMode
{
    Score = 0,
    Accuracy = 1,
    Combo = 2,
    ScoreV2 = 3,
}

public enum TeamColour
{
    Red,
    Blue,
}
