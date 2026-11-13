namespace Crystal.Native;

public static class ClassSkillRules
{
    public static Spell[] Maintained(MirClass @class) => @class switch
    {
        MirClass.Warrior => [Spell.FlamingSword, Spell.TwinDrakeBlade],
        MirClass.Wizard => [Spell.MagicShield, Spell.MagicBooster],
        MirClass.Taoist => [Spell.SoulShield, Spell.BlessedArmour],
        _ => []
    };
    public static bool CanMaintain(Spell spell) => Enum.GetValues<MirClass>().Any(@class => Maintained(@class).Contains(spell));
    public static Spell[] Summons(MirClass @class) => @class switch
    {
        MirClass.Taoist => [Spell.SummonHolyDeva, Spell.SummonShinsu, Spell.SummonSkeleton],
        _ => []
    };
    public static BuffType Buff(Spell spell) => spell switch
    {
        Spell.MagicShield => BuffType.MagicShield,
        Spell.MagicBooster => BuffType.MagicBooster, Spell.SoulShield => BuffType.SoulShield,
        Spell.BlessedArmour => BuffType.BlessedArmour,
        _ => BuffType.None
    };
    public static bool Active(GameState state, Spell spell) => state.ActiveSkills.Contains(spell) || spell switch
    {
        Spell.MagicShield => state.Me?.MagicShield == true,
        _ => Buff(spell) != BuffType.None && state.UserBuffs.TryGetValue(Buff(spell), out var buff) && !buff.Paused
    };
    public static int ManaCost(GameState state, ClientMagic magic)
    {
        var cost = magic.BaseCost + magic.LevelCost * magic.Level;
        return ClientSkillRules.IsTrigger(magic.Spell) || ClientAutomation.IsAttackSkill(magic.Spell) ? cost : cost + cost * Math.Max(0, state.CalculateStats()[Stat.ManaPenaltyPercent]) / 100;
    }
    public static bool Ready(GameState state, ClientMagic magic, long now) => state.User != null && !state.DisabledSkills.Contains(magic.Spell)
        && now >= magic.CastTime + Math.Max(magic.Delay, magic.Spell == Spell.FlamingSword ? 10000 : magic.Spell == Spell.CounterAttack ? 7000 : 0)
        && state.User.MP >= ManaCost(state, magic) + (ClientSkillRules.IsTrigger(magic.Spell) ? 1 : 0);

    // shortcut: 原协议仅同步视野内宠物；远处同类宠物由原服召回，有全局宠物清单后再精确判断。
    public static bool HasPet(GameState state) => state.User != null && state.Actors.Values.Any(actor =>
        actor.Kind == ObjectType.Monster && actor.MasterObjectId == state.User.ObjectID && !actor.Dead);

    public static bool Confirmed(GameState state, Spell spell, uint target, short poisonShape, int previousHp) => spell switch
    {
        Spell.Healing => state.User?.HP > previousHp,
        Spell.Poisoning => state.Actors.TryGetValue(target, out var actor) && actor.Poison.HasFlag(poisonShape == 2 ? PoisonType.Red : PoisonType.Green),
        Spell.SummonSkeleton or Spell.SummonShinsu or Spell.SummonHolyDeva => HasPet(state),
        _ => Active(state, spell)
    };

    public static Spell Next(GameState state, ClientPreferences preferences, Actor? target, bool battling, long now)
    {
        var user = state.User;
        if (user == null || state.Me?.Dead != false || state.Trading || state.StorageActive) return Spell.None;
        if (target != null && !ClientAutomation.Targets(state).Any(actor => actor.Id == target.Id)) target = null;
        bool ReadySpell(Spell spell) => user.Magics.FirstOrDefault(magic => magic.Spell == spell) is { } magic && Ready(state, magic, now);
        if (user.Class == MirClass.Taoist && preferences.AutoHeal && user.HP * 100L <= (long)state.CalculateStats()[Stat.HP] * preferences.HealPercent &&
            ReadySpell(Spell.Healing) && user.Magics.First(magic => magic.Spell == Spell.Healing).CastTime + 5000 <= now) return Spell.Healing;
        foreach (var spell in Maintained(user.Class))
        {
            if (!preferences.AutoSkills.Contains(spell) || Active(state, spell) || !ReadySpell(spell)) continue;
            if (ClientSkillRules.IsTrigger(spell) && (target == null || target.Dead || Functions.MaxDistance(user.Location, target.Location) > 1 || ClientAutomation.RequiresPhysicalAttack(target))) continue;
            return spell;
        }
        var summon = preferences.SummonSkill;
        if (user.Class == MirClass.Taoist && preferences.AutoSummon &&
            !HasPet(state))
            foreach (var spell in Summons(user.Class).Where(spell => summon == Spell.None || summon == spell))
                if (ReadySpell(spell)) return spell;
        if (user.Class == MirClass.Taoist && preferences.AutoPoison && battling && target is { Dead: false } && !ClientAutomation.RequiresPhysicalAttack(target) &&
            Functions.MaxDistance(user.Location, target.Location) <= 9 && (target.Poison & (PoisonType.Red | PoisonType.Green)) != (PoisonType.Red | PoisonType.Green) && ReadySpell(Spell.Poisoning))
            return Spell.Poisoning;
        return Spell.None;
    }

    public static Spell SmartMelee(GameState state, MirDirection direction)
    {
        if (state.User == null) return Spell.None;
        if (ClientAutomation.Targets(state).Any(actor => actor.Location == Functions.PointMove(state.User.Location, direction, 1) && ClientAutomation.RequiresPhysicalAttack(actor))) return Spell.None;
        var targets = ClientAutomation.Targets(state).Where(actor => !ClientAutomation.RequiresPhysicalAttack(actor) && Functions.MaxDistance(state.User.Location, actor.Location) == 1).Select(actor => actor.Location).Distinct().ToHashSet();
        foreach (var spell in new[] { Spell.CrossHalfMoon, Spell.HalfMoon })
        {
            var magic = state.User.Magics.FirstOrDefault(magic => magic.Spell == spell);
            if (magic == null || state.DisabledSkills.Contains(spell) || ManaCost(state, magic) > state.User.MP) continue;
            var cells = Enumerable.Range(spell == Spell.HalfMoon ? -1 : 0, spell == Spell.HalfMoon ? 4 : 8)
                .Select(offset => Functions.PointMove(state.User.Location, (MirDirection)(((int)direction + offset + 8) % 8), 1));
            if (cells.Count(targets.Contains) >= 2) return spell;
        }
        return Spell.None;
    }
}
