using Mir2.Companions;
using Server.MirObjects;

namespace Server.ConsoleHost;

public static class CompanionCombatReader
{
    // 只复制游戏线程当前值，不能在管理端重算或让响应继续持有可变的引擎属性集合。
    public static CompanionCombatState Read(PlayerObject player) => new(new Stats(player.Stats),
        player.Info.Magics.Select(magic => new CompanionSkillState(magic.Spell,
            ChineseText.Name(magic.Info?.Name ?? magic.Spell.ToString()), magic.Level, magic.Experience,
            magic.Level switch { 0 => magic.Info?.Need1 ?? 0, 1 => magic.Info?.Need2 ?? 0, 2 => magic.Info?.Need3 ?? 0, _ => 0 },
            magic.IsTempSpell)).ToArray());
}
