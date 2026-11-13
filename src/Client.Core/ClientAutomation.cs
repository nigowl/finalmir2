using S = ServerPackets;

namespace Crystal.Native;

public sealed class ClientAutomation
{
    public ulong PendingItem { get; private set; }
    public short? PendingScrollShape { get; private set; }
    public bool Paused { get; private set; }
    public string Notice { get; private set; } = "";
    private long pendingSince, nextItem, nextRandom;
    private bool randomTriggered, townTriggered;

    public void Reset()
    {
        PendingItem = 0; PendingScrollShape = null; Paused = false; Notice = "";
        pendingSince = nextItem = nextRandom = 0; randomTriggered = townTriggered = false;
    }

    public void Resume()
    {
        if (PendingItem != 0) return;
        Paused = false; Notice = "";
    }

    public UserItem? NextItem(GameState state, ClientPreferences preferences, long now, bool idleRandom = false)
    {
        if (PendingItem != 0)
        {
            if (now - pendingSince >= 3000) { Paused = true; Notice = "自动保护回执超时，已暂停；不会自动重发，请检查连接。"; }
            return null;
        }
        if (Paused || now < nextItem || state.User == null || state.Me?.Dead != false || state.Trading || state.StorageActive) return null;
        var user = state.User; var stats = state.CalculateStats();
        bool Low(Stat stat, int value, int threshold) => value * 100L <= (long)Math.Max(value, stats[stat]) * threshold;
        var lowTown = preferences.AutoTown && Low(Stat.HP, user.HP, preferences.TownPercent);
        var lowRandom = preferences.AutoRandom && Low(Stat.HP, user.HP, preferences.RandomPercent);
        // 地图暂时屏蔽回城不能重置同一次低血保护，实际恢复生命后才重新布防。
        if (!Low(Stat.HP, user.HP, preferences.TownPercent)) townTriggered = false;
        if (!lowRandom) randomTriggered = false;
        // 回城优先于随机和喝药；同一次低血量只触发一张保护卷，恢复后才重新布防。
        UserItem? item = null;
        if (lowTown && !townTriggered) { item = Scroll(user.Inventory, 1); if (item != null) townTriggered = true; else Notice = "自动回城：背包没有回城卷。"; }
        if (item == null && lowRandom && !randomTriggered && !(lowTown && townTriggered)) { item = Scroll(user.Inventory, 2); if (item != null) randomTriggered = true; else Notice = "自动随机：背包没有随机卷。"; }
        if (item == null && preferences.AutoHp && user.HP > 0 && Low(Stat.HP, user.HP, preferences.HpPercent)) item = Potion(user.Inventory, Stat.HP);
        if (item == null && preferences.AutoMp && stats[Stat.MP] > 0 && Low(Stat.MP, user.MP, preferences.MpPercent)) item = Potion(user.Inventory, Stat.MP);
        if (item == null && idleRandom && now >= nextRandom)
        {
            nextRandom = now + 5000; item = Scroll(user.Inventory, 2);
            if (item == null) Notice = "无怪随机：背包没有随机卷，将按跑图设置继续。";
        }
        if (item == null) return null;
        PendingItem = item.UniqueID; pendingSince = now;
        PendingScrollShape = item.Info.Type == ItemType.Scroll ? item.Info.Shape : null;
        if (PendingScrollShape == 2) nextRandom = Math.Max(nextRandom, now + 5000);
        return item;
    }

    private static UserItem? Potion(IEnumerable<UserItem> items, Stat stat) => items.FirstOrDefault(item =>
        item?.Info is { Type: ItemType.Potion, Shape: 0 or 1 } info && item.Count > 0 && info.Stats[stat] > 0);
    private static UserItem? Scroll(IEnumerable<UserItem> items, short shape) => items.FirstOrDefault(item =>
        item?.Info is { Type: ItemType.Scroll } info && info.Shape == shape && item.Count > 0);

    public void Apply(Packet packet, long now)
    {
        if (packet is not S.UseItem { Grid: MirGridType.Inventory } result || result.UniqueID != PendingItem || PendingItem == 0) return;
        PendingItem = 0; PendingScrollShape = null; nextItem = now + 1000;
        if (!result.Success) { Paused = true; Notice = "服务端拒绝自动保护，已暂停；请检查物品和地图限制后重新开启对应保护。"; }
    }

    public static bool IsAttackSkill(Spell spell) => spell is Spell.Thrusting or Spell.HalfMoon or Spell.CrossHalfMoon or Spell.DoubleSlash or Spell.TwinDrakeBlade or Spell.FlamingSword;
    public static bool IsSingleSkill(Spell spell) => spell is Spell.None or Spell.Thrusting or Spell.DoubleSlash or Spell.TwinDrakeBlade or Spell.FlamingSword
        or Spell.FireBall or Spell.GreatFireBall or Spell.ThunderBolt or Spell.FrostCrunch or Spell.Vampirism or Spell.FlameDisruptor or Spell.SoulFireBall;
    public static bool IsAreaSkill(Spell spell) => spell is Spell.None or Spell.HalfMoon or Spell.CrossHalfMoon or Spell.HellFire or Spell.FireBang
        or Spell.Lightning or Spell.ThunderStorm or Spell.IceStorm or Spell.FlameField or Spell.Blizzard or Spell.MeteorStrike or Spell.IceThrust or Spell.PoisonCloud;

    // 原Tree仅接受ACAgility普攻；AI来自原ObjectMonster包，不依赖版本内名称/物品编号。
    public static bool RequiresPhysicalAttack(Actor target) => target.Kind==ObjectType.Monster && target.AI==3;

    public static ClientMagic? SkillFor(GameState state, ClientPreferences preferences, Actor target, long now)
    {
        if (state.User == null || RequiresPhysicalAttack(target)) return null;
        var crowded = Targets(state).Count(actor => Functions.MaxDistance(actor.Location, target.Location) <= 2) >= 2;
        foreach (var spell in crowded ? new[] { preferences.AreaSkill, preferences.SingleSkill } : new[] { preferences.SingleSkill })
        {
            if (spell == Spell.None || state.DisabledSkills.Contains(spell)) continue;
            var skill = state.User.Magics.FirstOrDefault(magic => magic.Spell == spell);
            if (skill != null && skill.BaseCost + skill.LevelCost * skill.Level <= state.User.MP && now >= skill.CastTime + skill.Delay) return skill;
        }
        return null;
    }

    public static IEnumerable<Actor> Targets(GameState state) => state.User == null ? [] : state.Actors.Values
        .Where(actor => actor.Kind == ObjectType.Monster && !actor.Dead && !actor.Hidden && actor.MasterObjectId == 0 && actor.AI is not (6 or 56 or 57 or 58 or 64 or 113))
        .OrderBy(actor => Math.Max(Math.Abs(actor.Location.X - state.User.Location.X), Math.Abs(actor.Location.Y - state.User.Location.Y)));
}
