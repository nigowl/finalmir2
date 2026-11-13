#nullable enable
using Server.MirDatabase;
using Server.MirObjects;

namespace Server.Helpers;

public static class AttackModeProtection
{
    public static bool Protects(MapObject owner, MapObject target)
    {
        if (owner is HeroObject heroOwner) owner = heroOwner.Owner;
        static CharacterInfo? Character(MapObject actor) => actor is HeroObject hero ? hero.Owner?.Info : (actor as HumanObject)?.Info;
        var source = Character(owner); var other = Character(target);
        if (source == null || other == null) return false;
        return owner.AMode switch
        {
            AttackMode.Couple => source.Married > 0 && source.Married == other.Index,
            AttackMode.Mentor => source.Mentor > 0 && source.Mentor == other.Index || other.Mentor > 0 && other.Mentor == source.Index,
            _ => false
        };
    }
}
