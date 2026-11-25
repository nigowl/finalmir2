public sealed record PlayerActionTiming(int MoveMilliseconds, int ActionSpeedPercent)
{
    public const int DefaultMoveMilliseconds = 500, MinMoveMilliseconds = 300, MaxMoveMilliseconds = 1000;
    public const int DefaultActionSpeedPercent = 110, MinActionSpeedPercent = 80, MaxActionSpeedPercent = 150;
    public static PlayerActionTiming Default { get; } = new(DefaultMoveMilliseconds, DefaultActionSpeedPercent);
    public static bool Valid(int moveMilliseconds, int actionSpeedPercent) =>
        moveMilliseconds is >= MinMoveMilliseconds and <= MaxMoveMilliseconds &&
        actionSpeedPercent is >= MinActionSpeedPercent and <= MaxActionSpeedPercent;

    public int TurnMilliseconds => Scale(350);
    public int Scale(int milliseconds) => (int)(((long)milliseconds * 100 + ActionSpeedPercent - 1) / ActionSpeedPercent);
    public int AttackDelay(int attackSpeed, int level) => Scale(Math.Max(550, 1400 - attackSpeed * 60 - Math.Min(370, level * 14)));
    public int CastDelay(Spell spell) => Scale(spell == Spell.FlameField ? 2500 : 1800);
}
