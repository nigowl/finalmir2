namespace Crystal.Native;

public static partial class ClassSkillRules
{
    public static Spell[] Maintained(MirClass @class) => @class switch
    {
        MirClass.Warrior => [Spell.FlamingSword, Spell.TwinDrakeBlade, Spell.ProtectionField, Spell.Rage, Spell.Fury, Spell.ImmortalSkin, Spell.CounterAttack],
        MirClass.Wizard => [Spell.MagicShield, Spell.MagicBooster],
        MirClass.Taoist => [Spell.SoulShield, Spell.BlessedArmour, Spell.UltimateEnhancer, Spell.EnergyShield, Spell.PetEnhancer, Spell.Purification],
        MirClass.Assassin => [Spell.Haste, Spell.LightBody],
        MirClass.Archer => [Spell.ElementalBarrier, Spell.Concentration],
        _ => []
    };
    public static bool CanMaintain(Spell spell) => Enum.GetValues<MirClass>().Any(@class => Maintained(@class).Contains(spell));
    public static Spell[] Summons(MirClass @class) => @class switch
    {
        MirClass.Wizard => [Spell.Mirroring],
        MirClass.Taoist => [Spell.SummonHolyDeva, Spell.SummonShinsu, Spell.SummonSkeleton],
        _ => []
    };
    public static BuffType Buff(Spell spell) => spell switch
    {
        Spell.MagicShield => BuffType.MagicShield,
        Spell.MagicBooster => BuffType.MagicBooster, Spell.SoulShield => BuffType.SoulShield,
        Spell.BlessedArmour => BuffType.BlessedArmour,
        Spell.ProtectionField => BuffType.ProtectionField, Spell.Rage => BuffType.Rage,
        Spell.Fury => BuffType.Fury, Spell.ImmortalSkin => BuffType.ImmortalSkin, Spell.CounterAttack => BuffType.CounterAttack,
        Spell.UltimateEnhancer => BuffType.UltimateEnhancer, Spell.EnergyShield => BuffType.EnergyShield, Spell.PetEnhancer => BuffType.PetEnhancer,
        Spell.Haste => BuffType.Haste, Spell.LightBody => BuffType.LightBody,
        Spell.ElementalBarrier => BuffType.ElementalBarrier, Spell.Concentration => BuffType.Concentration,
        _ => BuffType.None
    };
    public static bool Active(GameState state, Spell spell) => state.ActiveSkills.Contains(spell) || spell switch
    {
        Spell.MagicShield => state.Me?.MagicShield == true,
        Spell.ElementalBarrier => state.Me?.ElementalBarrier == true || state.UserBuffs.TryGetValue(BuffType.ElementalBarrier, out var barrier) && !barrier.Paused,
        _ => Buff(spell) != BuffType.None && state.UserBuffs.TryGetValue(Buff(spell), out var buff) && !buff.Paused
    };
    public static int ManaCost(GameState state, ClientMagic magic)
    {
        if (magic.Spell is Spell.Slaying or Spell.Thrusting) return 0;
        var cost = magic.BaseCost + magic.LevelCost * magic.Level;
        return ClientSkillRules.IsTrigger(magic.Spell) || ClientAutomation.IsAttackSkill(magic.Spell) ? cost : cost + cost * Math.Max(0, state.CalculateStats()[Stat.ManaPenaltyPercent]) / 100;
    }
    public static bool Ready(GameState state, ClientMagic magic, long now) => state.User != null && !state.DisabledSkills.Contains(magic.Spell)
        && now >= magic.CastTime + ClientSkillRules.Cooldown(magic)
        // 双龙斩准备和出刀各扣一次蓝，自动准备前须同时留够两段费用。
        && state.User.MP >= (magic.Spell == Spell.TwinDrakeBlade ? Math.Max(1, ManaCost(state, magic) * 2)
            : ManaCost(state, magic) + (ClientSkillRules.IsTrigger(magic.Spell) ? 1 : 0));

    // shortcut: 原协议仅同步视野内宠物；远处同类宠物由原服召回，有全局宠物清单后再精确判断。
    public static bool HasPet(GameState state) => state.User != null && state.Actors.Values.Any(actor =>
        actor.Kind == ObjectType.Monster && actor.MasterObjectId == state.User.ObjectID && !actor.Dead);

    // 原镜像用同名ObjectPlayer同步，而不是带MasterObjectId的ObjectMonster。
    public static bool HasMirror(GameState state) => state.User != null && state.Actors.Values.Any(actor => actor.Kind == ObjectType.Player &&
        actor.Id != state.User.ObjectID && !actor.Dead && actor.Name == state.User.Name && actor.Class == state.User.Class && actor.Gender == state.User.Gender);

    public static bool Confirmed(GameState state, Spell spell, uint target, short poisonShape, int previousHp, int previousElements = -1) => spell switch
    {
        // 原元素屏障第一次无元素时仅聚集元素，确认这一阶段后才允许第二次施放护盾。
        Spell.ElementalBarrier => Active(state, spell) || previousElements == 0 && state.Me?.Elements.Count > 0,
        Spell.Healing => target==state.User?.ObjectID ? state.User.HP > previousHp :
            state.Actors.TryGetValue(target,out var ally)&&ally.Health>previousHp,
        Spell.Poisoning => state.Actors.TryGetValue(target, out var actor) && actor.Poison.HasFlag(poisonShape == 2 ? PoisonType.Red : PoisonType.Green),
        Spell.SummonSkeleton or Spell.SummonShinsu or Spell.SummonHolyDeva => HasPet(state),
        Spell.Mirroring => HasMirror(state),
        Spell.PetEnhancer => state.Actors.TryGetValue(target, out var pet) && pet.Buffs.Contains(BuffType.PetEnhancer),
        Spell.Purification => state.Me?.Poison == PoisonType.None,
        _ => Active(state, spell)
    };

    public static Spell Next(GameState state, ClientPreferences preferences, Actor? target, bool battling, long now)
    {
        var user = state.User;
        if (user == null || state.Me?.Dead != false || state.Trading || state.StorageActive) return Spell.None;
        if (target != null && !ClientAutomation.Targets(state).Any(actor => actor.Id == target.Id)) target = null;
        var smart = preferences.SmartBattle && (battling || target != null);
        bool ReadySpell(Spell spell) => user.Magics.FirstOrDefault(magic => magic.Spell == spell) is { } magic && Ready(state, magic, now)
            && (spell != Spell.ElementalBarrier || state.Me.Elements.Count > 0 || user.Magics.Any(m => m.Spell == Spell.Meditation))
            && (!smart || MaterialsAvailable(state, preferences, spell, target, true));
        if (user.Class == MirClass.Taoist && (preferences.AutoHeal || smart) && user.HP * 100L <= (long)state.CalculateStats()[Stat.HP] * preferences.HealPercent &&
            ReadySpell(Spell.Healing) && user.Magics.First(magic => magic.Spell == Spell.Healing).CastTime + 5000 <= now) return Spell.Healing;
        if (user.Class == MirClass.Taoist && (smart || preferences.AutoSkills.Contains(Spell.Purification)) && state.Me.Poison != PoisonType.None && ReadySpell(Spell.Purification)) return Spell.Purification;
        Spell Summon()
        {
            if (!(user.Class == MirClass.Wizard ? preferences.AutoMirroring || smart : preferences.AutoSummon || smart) ||
                (user.Class == MirClass.Wizard ? HasMirror(state) : HasPet(state))) return Spell.None;
            foreach (var spell in Summons(user.Class))
                if ((smart || user.Class == MirClass.Wizard || preferences.SummonSkill == Spell.None || preferences.SummonSkill == spell) && ReadySpell(spell)) return spell;
            return Spell.None;
        }
        if (user.Class == MirClass.Taoist)
        {
            if (Summon() is not Spell.None and var summoned) return summoned;
            if ((preferences.AutoPoison || smart) && target is { Dead: false } && !ClientAutomation.RequiresPhysicalAttack(target) &&
                Functions.MaxDistance(user.Location, target.Location) <= 9 && (target.Poison & (PoisonType.Red | PoisonType.Green)) != (PoisonType.Red | PoisonType.Green) && ReadySpell(Spell.Poisoning)) return Spell.Poisoning;
        }
        foreach (var spell in Maintained(user.Class))
        {
            if ((!preferences.AutoSkills.Contains(spell) && !smart) || Active(state, spell) || !ReadySpell(spell)) continue;
            if (spell == Spell.PetEnhancer && PetToEnhance(state) == null) continue;
            if (spell == Spell.Purification) continue;
            if (spell == Spell.ImmortalSkin && smart && user.HP * 2L > state.CalculateStats()[Stat.HP] &&
                ClientAutomation.Targets(state).Count(actor => Functions.MaxDistance(user.Location, actor.Location) <= 1) < 3) continue;
            if (ClientSkillRules.IsTrigger(spell) && (target == null || target.Dead || Functions.MaxDistance(user.Location, target.Location) > 1 || ClientAutomation.RequiresPhysicalAttack(target))) continue;
            return spell;
        }
        return user.Class == MirClass.Wizard ? Summon() : Spell.None;
    }

    public static Actor? PetToEnhance(GameState state) => state.Actors.Values.FirstOrDefault(actor => actor.Kind == ObjectType.Monster &&
        actor.MasterObjectId == state.User?.ObjectID && !actor.Dead && !actor.Buffs.Contains(BuffType.PetEnhancer) &&
        state.User != null && Functions.MaxDistance(actor.Location, state.User.Location) <= 9);

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
