using Crystal.Native;
using S = ServerPackets;

public partial class Main
{
    private bool classSkillsPaused;
    private Spell classPendingSpell;
    private uint classPendingTarget;
    private short classPendingPoison;
    private int classPreviousHp, classPreviousElements;
    private long classPendingSince, nextClassAction;
    private bool SmartCombatActive => preferences.SmartBattle && (autoBattleEnabled || ClientAutomation.Targets(State).Any(actor => actor.Id == actionTarget));

    private void AwaitClassSkill(Spell spell, uint target, short poison, long now)
    {
        classPendingSpell = spell; classPendingTarget = target; classPendingPoison = poison;
        classPreviousHp = State.User?.HP ?? 0; classPendingSince = now;
        classPreviousElements = State.Me?.Elements.Count ?? 0;
    }

    private void ResetClassSkills()
    {
        classSkillsPaused = false; classPendingSpell = Spell.None; nextClassAction = 0;
    }

    private bool ProcessClassSkills(long now)
    {
        if (classPendingSpell != Spell.None)
        {
            if (ClassSkillRules.Confirmed(State, classPendingSpell, classPendingTarget, classPendingPoison, classPreviousHp, classPreviousElements))
            { classPendingSpell = Spell.None; nextClassAction = now + 200; }
            else if (classPendingSpell == Spell.Poisoning && (!State.Actors.TryGetValue(classPendingTarget, out var poisonedTarget) || poisonedTarget.Dead))
                classPendingSpell = Spell.None;
            else if (now - classPendingSince >= 6000)
                PauseClassSkills($"{classPendingSpell.ToLocalizedString()}未确认生效（可能受地图、目标或耗材限制）");
            return true;
        }
        if (classSkillsPaused || now < nextClassAction || now < spellTime || now < castPendingUntil || moving || destination.HasValue && actionTarget == 0 || State.User == null ||
            State.Trading || State.StorageActive || npc.Visible || GuildStorageOpen || itemDragIcon.Visible || SkillPreparationBusy) return false;
        if (State.Me == null || ActionInProgress(State.Me, now) || ClientSkillRules.CastBlocked(State.Me.Poison)) return false;
        var target = State.Actors.GetValueOrDefault(actionTarget);
        if (target == null && preferences.SmartBattle && now < manualComboUntil &&
            State.Actors.GetValueOrDefault(manualComboTarget) is { Kind: ObjectType.Monster, Dead: false, Hidden: false } manual)
            target = manual;
        if (target == null && autoBattleEnabled) target = ClientAutomation.Targets(State).FirstOrDefault(actor => State.User != null && Functions.MaxDistance(State.User.Location, actor.Location) <= 9);
        var spell = ClassSkillRules.Next(State, preferences, target, autoBattleEnabled, now);
        if (spell == Spell.None) return false;
        var selection = World.Selected;
        World.Selected = spell == Spell.Poisoning ? target!.Id : spell == Spell.PetEnhancer ? ClassSkillRules.PetToEnhance(State)!.Id : State.User.ObjectID;
        Cast(State.User.Magics.FindIndex(magic => magic.Spell == spell), true, true);
        World.Selected = selection;
        return SkillPreparationBusy || classPendingSpell != Spell.None;
    }

    private void PauseClassSkills(string reason)
    {
        classPendingSpell = Spell.None; classSkillsPaused = true;
        Log($"职业辅助已暂停：{reason}。检查后重新保存职业设置或重新开始挂机可恢复。", ChatType.Hint);
    }

    private void ClassSkillPacket(Packet packet)
    {
        if (packet is S.RemoveMagic && State.User?.Magics.Any(magic => magic.Spell == classPendingSpell) != true)
            classPendingSpell = Spell.None;
        if (packet is S.Magic { Cast: false } result && result.Spell == classPendingSpell)
            PauseClassSkills($"服务端拒绝{result.Spell.ToLocalizedString()}");
        if (packet is S.MapChanged or S.Death) classPendingSpell = Spell.None;
    }
}
