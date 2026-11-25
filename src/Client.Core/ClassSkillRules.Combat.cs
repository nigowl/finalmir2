using Point = System.Drawing.Point;

namespace Crystal.Native;

public static partial class ClassSkillRules
{
    public static bool Thrusting(GameState state, ClientPreferences preferences, Actor target, bool battling, long now) => state.User?.Class == MirClass.Warrior &&
        (preferences.SmartThrusting || state.ActiveSkills.Contains(Spell.Thrusting) || battling && preferences.SmartBattle &&
            !state.User.Magics.Any(magic => magic.Spell is Spell.FlamingSword or Spell.TwinDrakeBlade && (state.ActiveSkills.Contains(magic.Spell) || Ready(state, magic, now)))) &&
        !state.DisabledSkills.Contains(Spell.Thrusting) && state.User.Magics.Any(magic => magic.Spell == Spell.Thrusting) &&
        target.Kind == ObjectType.Monster && !target.Dead && !target.Hidden && !ClientAutomation.RequiresPhysicalAttack(target) &&
        target.Location == Functions.PointMove(state.User.Location, Functions.DirectionFromPoint(state.User.Location, target.Location), 2);

    public static bool MaterialsAvailable(GameState state, ClientPreferences preferences, Spell spell, Actor? target, bool smart)
    {
        var materials = SkillMaterials.Required(state, spell, target);
        return materials.All(material => SkillMaterials.Equipped(state, material)) ||
            (preferences.AutoSwapAmulet || smart) && SkillMaterials.Missing(state, materials).Length == 0;
    }

    public static Spell Melee(GameState state, MirDirection direction, Spell? requested, bool smartHalfMoon)
    {
        if (state.User == null) return Spell.None;
        var front = Functions.PointMove(state.User.Location, direction, 1);
        if (ClientAutomation.Targets(state).Any(actor => actor.Location == front && ClientAutomation.RequiresPhysicalAttack(actor))) return Spell.None;
        bool Available(Spell spell) => !state.DisabledSkills.Contains(spell) && state.User.Magics.FirstOrDefault(magic => magic.Spell == spell) is { } magic &&
            (spell is Spell.FlamingSword or Spell.Slaying || ManaCost(state, magic) <= state.User.MP);
        foreach (var spell in new[] { Spell.FlamingSword, Spell.TwinDrakeBlade })
            if ((requested != Spell.Thrusting || TargetsAt(state, front)) && state.ActiveSkills.Contains(spell) && Available(spell)) return spell;
        if (requested is { } selected && selected != Spell.None && Available(selected)) return selected;
        if (smartHalfMoon && state.User.Class == MirClass.Assassin && Available(Spell.DoubleSlash)) return Spell.DoubleSlash;
        if (smartHalfMoon && state.User.Class==MirClass.Warrior && SmartMelee(state, direction) is not Spell.None and var area) return area;
        if (requested == null)
            foreach (var spell in new[] { Spell.DoubleSlash, Spell.CrossHalfMoon, Spell.HalfMoon, Spell.Thrusting })
            {
                if (smartHalfMoon && spell is Spell.CrossHalfMoon or Spell.HalfMoon) continue;
                if (spell == Spell.Thrusting && !TargetsAt(state, Functions.PointMove(state.User.Location, direction, 2))) continue;
                if (state.ActiveSkills.Contains(spell) && Available(spell)) return spell;
            }
        return state.ActiveSkills.Contains(Spell.Slaying) && Available(Spell.Slaying) ? Spell.Slaying : Spell.None;
    }

    private static bool TargetsAt(GameState state, Point point) => ClientAutomation.Targets(state).Any(actor => actor.Location == point);

    private static Spell[] Singles(MirClass job) => job switch
    {
        MirClass.Wizard => [Spell.FlameDisruptor, Spell.ThunderBolt, Spell.GreatFireBall, Spell.FireBall],
        MirClass.Taoist => [Spell.SoulFireBall],
        MirClass.Archer => [Spell.DoubleShot, Spell.StraightShot],
        _ => []
    };

    private static Spell[] Areas(MirClass job) => job switch
    {
        MirClass.Warrior => [Spell.BladeAvalanche],
        MirClass.Wizard => [Spell.FireWall, Spell.FlameField, Spell.IceStorm, Spell.MeteorStrike, Spell.Blizzard, Spell.ThunderStorm, Spell.FireBang, Spell.Lightning, Spell.HellFire, Spell.IceThrust],
        MirClass.Taoist => [Spell.PoisonCloud],
        MirClass.Assassin => [Spell.CrescentSlash, Spell.HeavenlySword],
        MirClass.Archer => [Spell.NapalmShot],
        _ => []
    };

    public static ClientMagic? AttackMagic(GameState state, ClientPreferences preferences, Actor target, long now)
    {
        if (!preferences.SmartBattle || state.User == null || ClientAutomation.RequiresPhysicalAttack(target)) return null;
        if (state.User.Class == MirClass.Warrior && (state.ActiveSkills.Contains(Spell.FlamingSword) || state.ActiveSkills.Contains(Spell.TwinDrakeBlade))) return null;
        ClientMagic? Select(Spell spell)
        {
            var magic = state.User.Magics.FirstOrDefault(magic => magic.Spell == spell);
            return magic != null && Ready(state, magic, now) && InRange(state, target, spell) &&
                (magic.Range == 0 || Functions.MaxDistance(state.User.Location, target.Location) <= magic.Range) &&
                MaterialsAvailable(state, preferences, spell, target, true) ? magic : null;
        }
        var healingShot = state.User.Class == MirClass.Archer ? Spell.VampireShot : Spell.Vampirism;
        if (state.User.Class is MirClass.Wizard or MirClass.Archer && state.User.HP * 2L <= state.CalculateStats()[Stat.HP] && Select(healingShot) is { } drain) return drain;
        if (state.User.Class == MirClass.Assassin && !target.Poison.HasFlag(PoisonType.Green) && Select(Spell.PoisonSword) is { } poisonSword) return poisonSword;
        if (state.User.Class == MirClass.Archer)
        {
            if ((ActiveBuff(state, BuffType.PoisonShot) || ActiveBuff(state, BuffType.VampireShot)) &&
                ClientAutomation.Targets(state).Count(actor => !ClientAutomation.RequiresPhysicalAttack(actor) && Functions.MaxDistance(actor.Location, target.Location) <= 1) >= 2 &&
                Select(Spell.CrippleShot) is { } cripple) return cripple;
            if (!target.Poison.HasFlag(PoisonType.Green) && Select(Spell.PoisonShot) is { } poisonShot) return poisonShot;
        }
        foreach (var spell in Areas(state.User.Class))
        {
            var nearby = ClientAutomation.Targets(state).Count(actor => !ClientAutomation.RequiresPhysicalAttack(actor) && InArea(state.User.Location, target.Location, actor.Location, spell));
            if (nearby < (spell is Spell.FireWall or Spell.PoisonCloud ? 3 : 2)) continue;
            if (spell is Spell.FireWall or Spell.PoisonCloud && state.Actors.Values.Any(actor => actor.Kind == ObjectType.Spell && actor.ActionSpell == spell && Functions.MaxDistance(actor.Location, target.Location) <= 1)) continue;
            if (Select(spell) is { } area) return area;
        }
        foreach (var spell in Singles(state.User.Class)) if (Select(spell) is { } single) return single;
        return null;
    }

    public static bool WaitForRanged(GameState state, ClientPreferences preferences, Actor target, long now) => preferences.SmartBattle &&
        state.User != null && !ClientAutomation.RequiresPhysicalAttack(target) && Functions.MaxDistance(state.User.Location, target.Location) > 1 &&
        state.User.Magics.Any(magic => Singles(state.User.Class).Contains(magic.Spell) && !state.DisabledSkills.Contains(magic.Spell) &&
            state.User.MP >= ManaCost(state, magic) && now < magic.CastTime + ClientSkillRules.Cooldown(magic) && InRange(state, target, magic.Spell) &&
            (magic.Range == 0 || Functions.MaxDistance(state.User.Location, target.Location) <= magic.Range) && MaterialsAvailable(state, preferences, magic.Spell, target, true));

    private static bool ActiveBuff(GameState state, BuffType buff) => state.UserBuffs.TryGetValue(buff, out var status) && !status.Paused;

    private static bool InRange(GameState state, Actor target, Spell spell)
    {
        var distance = Functions.MaxDistance(state.User!.Location, target.Location);
        return spell switch
        {
            Spell.FlameField or Spell.ThunderStorm => distance <= 2,
            Spell.BladeAvalanche => distance <= 3,
            Spell.HellFire => distance <= 4,
            Spell.Lightning => distance <= 5,
            Spell.IceThrust => distance <= 3,
            Spell.CrescentSlash or Spell.PoisonSword => distance <= 1,
            Spell.HeavenlySword => distance <= 3 && target.Location == Functions.PointMove(state.User.Location, Functions.DirectionFromPoint(state.User.Location, target.Location), distance),
            _ => distance <= 9
        };
    }

    private static bool InArea(Point user, Point target, Point other, Spell spell)
    {
        if (spell == Spell.CrescentSlash)
        {
            var facing = Functions.DirectionFromPoint(user, target);
            return Enumerable.Range(-2, 5).Any(offset => other == Functions.PointMove(user, (MirDirection)(((int)facing + offset + 8) % 8), 1));
        }
        if (spell is Spell.FlameField or Spell.ThunderStorm) return Functions.MaxDistance(user, other) <= 2;
        if (spell is not (Spell.HellFire or Spell.Lightning or Spell.BladeAvalanche or Spell.IceThrust or Spell.HeavenlySword)) return Functions.MaxDistance(target, other) <= 2;
        var direction = Functions.DirectionFromPoint(user, target);
        var length = spell == Spell.HellFire ? 4 : spell == Spell.Lightning ? 5 : 3;
        for (var step = 1; step <= length; step++)
        {
            var centre = Functions.PointMove(user, direction, step);
            if (other == centre || spell is Spell.BladeAvalanche or Spell.IceThrust && Functions.MaxDistance(centre, other) <= 1) return true;
        }
        return false;
    }
}
