namespace Crystal.Native;

public static class CombatPresentation
{
    public static int DamageFontSize(int amount, DamageType type)
    {
        if (amount == 0 || type == DamageType.Miss) return 13;
        var size = 15 + (int)(Math.Log10(1 + Math.Abs((long)amount)) * 5);
        return Math.Clamp(size + (type == DamageType.Critical && amount < 0 ? 3 : 0), 15, 36);
    }

    public static string DamageText(int amount, DamageType type) => amount == 0 || type == DamageType.Miss ? "未命中"
        : amount > 0 ? "+" + amount : (type == DamageType.Critical ? "暴击 " : "") + Math.Abs((long)amount);

    // shortcut: 原协议未同步他人已学被动；有合法远端技能元数据后再展示他人的基础剑气。
    public static bool HasBasicSword(GameState state, Actor? actor) => actor is { Kind: ObjectType.Player, Class: MirClass.Warrior } &&
        state.User is { } user && actor.Id == user.ObjectID && !state.DisabledSkills.Contains(Spell.Fencing) && user.Magics.Any(magic => magic.Spell == Spell.Fencing);
}
