namespace IceCrow.Battlegrounds.Memory;

/// <summary>
/// One minion observed on both boards whose visible values differ. Deltas are
/// raw observation arithmetic; a negative delta is recorded as-is because it
/// may come from a transform or effect rather than a real loss.
/// </summary>
public sealed record MinionChange(
    MinionSnapshot Previous,
    MinionSnapshot Current,
    MinionIdentity Identity)
{
    public int AttackDelta => Current.Attack - Previous.Attack;

    public int HealthDelta => Current.Health - Previous.Health;

    public int PositionDelta => Current.ZonePosition - Previous.ZonePosition;

    public bool HasStatChange => AttackDelta != 0 || HealthDelta != 0;

    public bool HasPositionChange => PositionDelta != 0;
}
