using Crystal.Native;
using C = ClientPackets;
using S = ServerPackets;
using Point = System.Drawing.Point;

public partial class Main
{
    private sealed record PreparedSkill(Spell Spell, uint Target, Point Location, bool Automatic, bool Profession,
        int Map, uint User, SkillMaterial[] Materials, long Started);
    private PreparedSkill? preparedSkill;
    private ulong skillEquipmentPending;
    private bool skillEquipmentRemoving, skillPreparationPaused;
    private int skillEquipmentSlot;
    private long skillEquipmentSince;
    private bool SkillPreparationBusy => preparedSkill != null || skillEquipmentPending != 0;

    private void RequestSkillCast(ClientMagic magic, uint target, Point point, bool automatic, bool profession)
    {
        if (SkillPreparationBusy) return;
        var materials = SkillMaterials.Required(State, magic.Spell, State.Actors.GetValueOrDefault(target));
        if (materials.All(material => SkillMaterials.Equipped(State, material))) { CommitSkillCast(magic, target, point, automatic, profession, materials); return; }
        if (skillPreparationPaused) { if (!automatic) Log("毒符换装已暂停，请检查物品后重新保存职业设置。"); return; }
        var missing = SkillMaterials.Missing(State, materials);
        if (missing.Length > 0) { SkillPreparationFailed(missing, automatic); return; }
        if (!preferences.AutoSwapAmulet && !preferences.SmartBattle) { SkillPreparationFailed("技能需要的毒粉/护符未装备；可在职业设置开启自动毒符切换。", automatic); return; }
        preparedSkill = new(magic.Spell, target, point, automatic, profession, State.Map?.MapIndex ?? 0, State.User!.ObjectID, materials, Environment.TickCount64);
    }

    private bool ProcessSkillPreparation(long now)
    {
        if (skillEquipmentPending != 0)
        {
            if (now - skillEquipmentSince >= 3000 && !skillPreparationPaused)
            {
                skillPreparationPaused = true; preparedSkill = null; classSkillsPaused = true;
                Log("毒符换装回执超时，已暂停；未确认的换装不会自动重发。", ChatType.Hint);
            }
            return true;
        }
        var request = preparedSkill;
        if (request == null) return false;
        if (request.User != State.User?.ObjectID || request.Map != State.Map?.MapIndex || State.Me?.Dead != false || now - request.Started > 12000)
        { preparedSkill = null; return false; }
        if (State.Trading || State.StorageActive || npc.Visible || GuildStorageOpen || automation.PendingItem != 0 || moving) return true;
        var magic = State.User!.Magics.FirstOrDefault(magic => magic.Spell == request.Spell);
        if (magic == null || State.DisabledSkills.Contains(request.Spell)) { preparedSkill = null; return false; }
        if (!request.Materials.All(material => SkillMaterials.Equipped(State, material)))
        {
            var change = SkillMaterials.NextChange(State, request.Materials);
            if (change == null) { preparedSkill = null; SkillPreparationFailed("无法安全完成毒符切换，请检查背包空位和耗材。", request.Automatic); return false; }
            if (change is C.EquipItem equip) { skillEquipmentPending = equip.UniqueID; skillEquipmentSlot = equip.To; skillEquipmentRemoving = false; }
            else if (change is C.RemoveItem remove) { skillEquipmentPending = remove.UniqueID; skillEquipmentSlot = remove.To; skillEquipmentRemoving = true; }
            skillEquipmentSince = now; Send(change); return true;
        }
        if (now < spellTime || now < magic.CastTime + Math.Max(0, magic.Delay)) return true;
        var target = State.Actors.GetValueOrDefault(request.Target);
        if (request.Target != 0 && (target == null || target.Dead && request.Spell != Spell.Reincarnation)) { preparedSkill = null; return false; }
        preparedSkill = null;
        CommitSkillCast(magic, request.Target, target?.Location ?? request.Location, request.Automatic, request.Profession, request.Materials);
        return true;
    }

    private void CommitSkillCast(ClientMagic magic, uint target, Point point, bool automatic, bool profession, SkillMaterial[] materials)
    {
        if (State.User == null || State.Me?.Dead != false || State.Trading || State.StorageActive) return;
        var now = Environment.TickCount64;
        destination = null; if (!automatic) actionTarget = 0; pendingAttack = null; MiniMap.Route = []; moveTime = now;
        State.Me.Direction = Direction(State.User.Location, point);
        Send(new C.Magic { ObjectID = State.User.ObjectID, Spell = magic.Spell, Direction = State.Me.Direction, TargetID = target, Location = point });
        spellTime = now + ClientSkillRules.GlobalDelay(magic.Spell);
        if (profession) AwaitClassSkill(magic.Spell, target, materials.FirstOrDefault().Shape, now);
    }

    private void SkillPreparationPacket(Packet packet)
    {
        if (skillEquipmentPending != 0 && packet is S.EquipItem equip && !skillEquipmentRemoving && equip.Grid == MirGridType.Inventory && equip.UniqueID == skillEquipmentPending && equip.To == skillEquipmentSlot)
            SkillEquipmentResult(equip.Success);
        if (skillEquipmentPending != 0 && packet is S.RemoveItem remove && skillEquipmentRemoving && remove.Grid == MirGridType.Inventory && remove.UniqueID == skillEquipmentPending && remove.To == skillEquipmentSlot)
            SkillEquipmentResult(remove.Success);
        if (packet is S.MapChanged or S.Death) preparedSkill = null;
        if (packet is S.LogOutSuccess or S.ReturnToLogin or S.Disconnect or S.UserInformation)
        { preparedSkill = null; skillEquipmentPending = 0; skillPreparationPaused = false; ResetClassSkills(); }
    }

    private void SkillEquipmentResult(bool success)
    {
        skillEquipmentPending = 0;
        if (success) return;
        preparedSkill = null; skillPreparationPaused = true; classSkillsPaused = true;
        Log("服务端拒绝毒符换装，已暂停；请检查职业、等级、负重或装备限制。", ChatType.Hint);
    }
    private void SkillPreparationFailed(string reason, bool automatic)
    {
        if (automatic) { classSkillsPaused = true; skillPreparationPaused = true; }
        Log(reason, ChatType.Hint);
    }
}
