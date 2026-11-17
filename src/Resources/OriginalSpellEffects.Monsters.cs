namespace Client.MirObjects;

public static partial class OriginalSpellEffects
{
    public static SpellMissile? GuardMissile(Monster image) => image switch
    {
        Monster.ArcherGuard or Monster.ArcherGuard2 => new(new("Monster/139", 38, 3, 90, Blend: false), null, DirectionStride: 9),
        Monster.ArcherGuard3 => new(new("Monster/378", 104, 3, 90, Blend: false), null, DirectionStride: 3),
        _ => null
    };
}
