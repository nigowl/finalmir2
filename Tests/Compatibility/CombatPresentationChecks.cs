using Crystal.Native;
using Client.MirObjects;
using S = ServerPackets;

internal static class CombatPresentationChecks
{
    public static void Run(Action<bool, string> check)
    {
        var previous = 0;
        foreach (var damage in new[] { 1, 5, 10, 50, 100, 500, 1000, 10000, int.MaxValue })
        {
            var size = CombatPresentation.DamageFontSize(-damage, DamageType.Hit);
            check(size >= previous && size is >= 15 and <= 36, "伤害字号没有递增或失去上限"); previous = size;
        }
        check(CombatPresentation.DamageFontSize(-100, DamageType.Critical) > CombatPresentation.DamageFontSize(-100, DamageType.Hit), "真实暴击未突出显示");
        check(CombatPresentation.DamageText(int.MinValue, DamageType.Hit) == "2147483648", "伤害绝对值溢出");
        check(CombatPresentation.DamageText(30, DamageType.Hit) == "+30" && CombatPresentation.DamageText(-30, DamageType.Critical) == "暴击 30", "治疗或真实暴击文字丢失");
        check(CombatPresentation.DamageText(-30, DamageType.Miss) == "未命中" && CombatPresentation.DamageFontSize(0, DamageType.Hit) == 13, "闪避被伪装成命中");
        foreach (var type in Enum.GetValues<BuffType>())
            check(type.ToLocalizedString().Any(character => character is >= '\u4e00' and <= '\u9fff'), $"Buff飘字缺少中文名称：{type}");
        check(BuffType.Rage.ToLocalizedString() == "狂暴" && BuffType.MagicShield.ToLocalizedString() == "魔法盾", "技能Buff名称与原中文名称不一致");
        var state = new GameState(); state.Apply(new S.UserInformation { ObjectID = 1, Class = MirClass.Warrior, HP = 100, MP = 30,
            Inventory = [], Equipment = new UserItem[14], QuestInventory = [] });
        check(!CombatPresentation.HasBasicSword(state, state.Me), "未学习基础剑术已有剑气");
        state.User!.Magics.Add(new ClientMagic { Spell = Spell.Fencing });
        check(CombatPresentation.HasBasicSword(state, state.Me), "已学战士基础剑术未显示剑气");
        check(!CombatPresentation.HasBasicSword(state, new Actor { Id = 2, Kind = ObjectType.Player, Class = MirClass.Warrior }), "未获远端被动信息却显示剑气");
        state.Me!.Class = MirClass.Wizard;
        check(!CombatPresentation.HasBasicSword(state, state.Me), "法师误用基础剑气"); state.Me.Class = MirClass.Warrior;
        state.DisabledSkills.Add(Spell.Fencing);
        check(!CombatPresentation.HasBasicSword(state, state.Me), "已禁用的基础剑术仍显示剑气");
        foreach (var direction in Enum.GetValues<MirDirection>())
        {
            var clip = OriginalSpellEffects.BasicSword(direction);
            check(clip.Count == 6 && clip.Duration == 600 && clip.Opacity < OriginalSpellEffects.Melee(Spell.Slaying, 0, direction)!.Value.Opacity &&
                clip.FrameAt(0) == clip.Index && clip.FrameAt(599) == clip.Index + 5 && clip.FrameAt(600) == -1, "基础剑气原方向帧不完整或覆盖主动技能辨识");
        }
        check(state.Me.ActionSpell == Spell.None && state.User.HP == 100 && state.User.MP == 30 && state.ActiveSkills.Count == 0, "表现计算修改了权威战斗状态");
    }
}
