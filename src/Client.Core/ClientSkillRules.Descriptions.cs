using System.Globalization;

namespace Crystal.Native;

public static partial class ClientSkillRules
{
    public static long Cooldown(ClientMagic magic) => Math.Max(Math.Max(0, magic.Delay),
        magic.Spell == Spell.FlamingSword ? 10000 : magic.Spell == Spell.CounterAttack ? 7000 : 0);

    public static string Tooltip(ClientMagic magic, bool enabled = true)
    {
        var passive = IsPassive(magic.Spell); var toggle = IsToggle(magic.Spell);
        var kind = passive ? "被动技能" : toggle ? $"攻击开关 · {(enabled ? "已开启" : "已关闭")}" : IsTrigger(magic.Spell) ? "蓄力技能" : "主动技能";
        var cooldown = passive ? "无（被动生效）" : toggle ? "无（随普通攻击生效）" : Seconds(Cooldown(magic)) + "秒";
        var text = $"{magic.Name ?? magic.Spell.ToLocalizedString()} · {magic.Level}级\n{kind}\n效果：{Description(magic.Spell)}\n冷却：{cooldown}";
        if (!passive) text += $"\n基础耗蓝：{magic.BaseCost + magic.LevelCost * magic.Level}";
        if (!passive && !toggle && !IsTrigger(magic.Spell)) text += $"\n施放间隔：{Seconds(GlobalDelay(magic.Spell))}秒";
        if (magic.Range > 0 && !passive) text += $"\n施法距离：{magic.Range}格";
        if (magic.Spell == Spell.Slaying) text += $"\n触发概率：{(Math.Clamp((int)magic.Level, 0, 3) + 1) * 100d / 12:0.#}%（原服概率准备）";
        text += !IsImplemented(magic.Spell) ? "\n当前原引擎尚未实现，无法施放。" : passive ? "\n学习后自动生效，无需设置快捷键。" : toggle
            ? "\n点击图标或按快捷键切换。" : "\n点击图标或按快捷键施放。";
        return text;
    }

    private static string Seconds(long milliseconds) => (milliseconds / 1000d).ToString("0.###", CultureInfo.InvariantCulture);

    // 介绍沿用原客户端含义；不把可调的伤害、持续时间或成功率写成固定数值。
    public static string Description(Spell spell) => spell switch
    {
        Spell.Fencing => "提高近战攻击的命中能力。",
        Spell.Slaying => "普通攻击有概率准备攻杀，下一次近战增强威力；\n未触发时没有攻杀特效，也不增加该技能熟练度。",
        Spell.Thrusting => "开启后以剑气攻击前方直线第二格目标；\n不消耗魔力，命中后按原规则增长熟练度。",
        Spell.HalfMoon => "挥出半月剑气，攻击面前多个目标。",
        Spell.ShoulderDash => "向前冲撞较弱目标，将其推开。",
        Spell.TwinDrakeBlade => "蓄积力量，下一次攻击连续重击目标。",
        Spell.Entrapment => "以力量牵引怪物，将其拉到身边。",
        Spell.FlamingSword => "蓄积烈火，下一次近战附加火焰重击。",
        Spell.LionRoar => "以怒吼震慑周围符合条件的怪物。",
        Spell.CrossHalfMoon => "挥出交叉剑气，攻击身边多个目标。",
        Spell.BladeAvalanche => "向前方多个方向释放刀刃攻击。",
        Spell.ProtectionField => "以护体气场暂时增强自身防御。",
        Spell.Rage => "激发力量，暂时增强攻击能力。",
        Spell.CounterAttack => "进入反击准备，抵御攻击并反击敌人。",
        Spell.SlashingBurst => "向前突进，越过面前的障碍或目标。",
        Spell.Fury => "暂时提高自身的攻击速度。",
        Spell.ImmortalSkin => "暂时提高物理防御，同时降低部分物理攻击。",
        Spell.FireBall => "发射火球，对单个目标造成魔法伤害。",
        Spell.Repulsion => "释放环形冲击，推开周围较弱目标。",
        Spell.ElectricShock => "电击怪物，有机会使其麻痹或被驯服。",
        Spell.GreatFireBall => "发射更强的火球攻击单个目标。",
        Spell.HellFire => "向前喷出火焰，攻击直线上的目标。",
        Spell.ThunderBolt => "召唤雷电，打击单个目标。",
        Spell.Teleport => "使用瞬间移动，尝试转移到其他位置。",
        Spell.FireBang => "引爆指定区域，攻击范围内的目标。",
        Spell.FireWall => "在地面留下火墙，持续灼伤经过的敌人。",
        Spell.Lightning => "释放直线电光，攻击沿途的目标。",
        Spell.FrostCrunch => "以寒气攻击目标，并尝试使其减速。",
        Spell.ThunderStorm => "以周身雷光攻击附近的敌人。",
        Spell.MagicShield => "形成魔法护盾，减轻自身受到的伤害。",
        Spell.TurnUndead => "尝试直接消灭符合等级条件的亡灵。",
        Spell.Vampirism => "攻击目标，并将部分伤害转化为生命。",
        Spell.IceStorm => "在指定区域释放冰雪，攻击范围目标。",
        Spell.FlameDisruptor => "引出地底火焰，重击单个目标。",
        Spell.Mirroring => "制造分身，与自己一同攻击敌人。",
        Spell.FlameField => "在身边释放烈焰，攻击周围目标。",
        Spell.Blizzard => "持续引导冰雪，攻击指定区域的敌人。",
        Spell.MagicBooster => "增强魔法威力，同时增加魔法消耗。",
        Spell.MeteorStrike => "引导陨石火雨，持续攻击指定区域。",
        Spell.IceThrust => "以冰柱攻击前方或指定位置的目标。",
        Spell.FastMove => "原版预留的移动技能，当前引擎未实现。",
        Spell.StormEscape => "震慑附近敌人，并向指定位置转移。",
        Spell.Healing => "为目标施加治疗，逐渐恢复生命。",
        Spell.SpiritSword => "提高道士近战攻击的命中能力。",
        Spell.Poisoning => "消耗毒粉施毒；绿毒持续损血，\n红毒削弱目标防御。",
        Spell.SoulFireBall => "消耗护身符，发射灵魂火符攻击目标。",
        Spell.SummonSkeleton => "消耗护身符，召唤骷髅协助战斗。",
        Spell.Hiding => "消耗护身符，暂时隐藏自身行踪；\n移动后会解除隐身。",
        Spell.MassHiding => "使指定区域的友方目标暂时隐身。",
        Spell.SoulShield => "消耗护身符，暂时增强友方魔法防御。",
        Spell.Revelation => "观察目标，使其生命状态可见。",
        Spell.BlessedArmour => "消耗护身符，暂时增强友方物理防御。",
        Spell.EnergyRepulsor => "释放气功波，将周围较弱目标推开。",
        Spell.TrapHexagon => "以符阵限制符合条件的怪物移动；\n受到攻击后可能解除。",
        Spell.Purification => "尝试清除目标身上的中毒、麻痹等状态。",
        Spell.MassHealing => "治疗指定区域内的友方目标。",
        Spell.Hallucination => "使符合条件的怪物混乱并攻击其他目标。",
        Spell.UltimateEnhancer => "消耗护身符，暂时增强目标属性。",
        Spell.SummonShinsu => "消耗护身符，召唤神兽协助战斗。",
        Spell.Reincarnation => "尝试复活符合条件的死亡玩家。",
        Spell.SummonHolyDeva => "消耗护身符，召唤圣兽以雷电协助攻击。",
        Spell.Curse => "消耗毒符，削弱目标攻击和攻击速度。",
        Spell.Plague => "消耗毒符，干扰目标魔力并施加异常状态。",
        Spell.PoisonCloud => "消耗绿毒，在指定区域生成持续毒雾。",
        Spell.EnergyShield => "为友方施加护盾，反弹部分受到的伤害。",
        Spell.PetEnhancer => "增强召唤物的攻击和防御能力。",
        Spell.HealingCircle => "生成治疗区域，治疗友方并伤害敌人。",
        Spell.Blink => "向附近位置进行短距离瞬间移动。",
        Spell.Portal => "建立传送入口，按当前地图规则通行。",
        Spell.BattleCry => "以战吼影响附近目标的战斗状态。",
        Spell.FireBounce => "释放会在目标之间跳跃的火焰攻击。",
        Spell.MeteorShower => "以陨石攻击指定区域的目标。",
        _ => "具体效果由当前版本原引擎决定。"
    };
}
