using Crystal.Native;
using S = ServerPackets;

public partial class Main
{
    private bool classSkillsPaused;
    private Spell classPendingSpell;
    private uint classPendingTarget;
    private short classPendingPoison;
    private int classPreviousHp;
    private long classPendingSince, nextClassAction;

    private void AwaitClassSkill(Spell spell, uint target, short poison, long now)
    {
        classPendingSpell = spell; classPendingTarget = target; classPendingPoison = poison;
        classPreviousHp = State.User?.HP ?? 0; classPendingSince = now;
    }

    private void ResetClassSkills()
    {
        classSkillsPaused = false; classPendingSpell = Spell.None; nextClassAction = 0;
    }

    private bool ProcessClassSkills(long now)
    {
        if (classPendingSpell != Spell.None)
        {
            if (ClassSkillRules.Confirmed(State, classPendingSpell, classPendingTarget, classPendingPoison, classPreviousHp))
            { classPendingSpell = Spell.None; nextClassAction = now + 200; }
            else if (classPendingSpell == Spell.Poisoning && (!State.Actors.TryGetValue(classPendingTarget, out var poisonedTarget) || poisonedTarget.Dead))
                classPendingSpell = Spell.None;
            else if (now - classPendingSince >= 6000)
                PauseClassSkills($"{classPendingSpell.ToLocalizedString()}未确认生效（可能受地图、目标或耗材限制）");
            return true;
        }
        if (classSkillsPaused || now < nextClassAction || now < spellTime || moving || destination.HasValue && actionTarget == 0 || State.User == null ||
            State.Trading || State.StorageActive || npc.Visible || GuildStorageOpen || itemDragIcon.Visible || SkillPreparationBusy) return false;
        if (State.Me == null || now - State.Me.ActionTime < World.ActionDuration(State.Me) && State.Me.Action != MirAction.Standing) return false;
        var target = State.Actors.GetValueOrDefault(actionTarget);
        var spell = ClassSkillRules.Next(State, preferences, target, autoBattleEnabled, now);
        if (spell == Spell.None) return false;
        var selection = World.Selected;
        World.Selected = spell == Spell.Poisoning ? target!.Id : State.User.ObjectID;
        Cast(State.User.Magics.FindIndex(magic => magic.Spell == spell), true, true);
        World.Selected = selection;
        return SkillPreparationBusy || classPendingSpell != Spell.None;
    }

    private void PauseClassSkills(string reason)
    {
        classPendingSpell = Spell.None; classSkillsPaused = true;
        Log($"职业辅助已暂停：{reason}。检查后重新保存职业设置可恢复。", ChatType.Hint);
    }

    private void ClassSkillPacket(Packet packet)
    {
        if (packet is S.Magic { Cast: false } result && result.Spell == classPendingSpell)
            PauseClassSkills($"服务端拒绝{result.Spell.ToLocalizedString()}");
        if (packet is S.MapChanged or S.Death) classPendingSpell = Spell.None;
    }
}
