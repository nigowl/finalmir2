using Crystal.Native;
using Godot;
using C = ClientPackets;
using S = ServerPackets;
using Point = System.Drawing.Point;

public partial class Main
{
    private readonly ClientAutomation automation = new();
    private readonly ClientPickup pickup = new();
    private uint autoPickupTarget;
    private bool pickupNotice;
    private bool autoBattleEnabled;
    private long nextBattleScan;
    private readonly ClientRoaming roaming = new();
    private Point? roamDestination;
    private string automationNotice = "";
    private long noMonsterSince;
    private int battleMapIndex;

    public override void _Input(InputEvent input)
    {
        StopBattleForInput(input);
        ChatResizeInput(input);
        InventoryDragInput(input);
        if (input is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } mouse) itemDragStart = mouse.Position;
        if (input is InputEventKey { Pressed: true, PhysicalKeycode: Key.Escape }) CancelItemDrag();
    }

    private void StopBattleForInput(InputEvent input)
    {
        if (GetViewport().GuiGetFocusOwner() is LineEdit or TextEdit) return;
        if (input is InputEventKey { Pressed: true, Echo: false, PhysicalKeycode: Key.Escape }) { preparedSkill = null; StopAutoBattle(); }
    }

    private void ToggleAutoBattle()
    {
        if (autoBattleEnabled) { StopAutoBattle(); return; }
        if (State.Me?.Dead != false || World.Loading || State.Trading || State.StorageActive) { Log("当前状态不能开始挂机。"); return; }
        CloseSettings(); if (settingsDialog.Visible) return;
        automation.Resume();
        if (skillEquipmentPending == 0) { skillPreparationPaused = false; classSkillsPaused = false; }
        autoBattleEnabled = true; nextBattleScan = noMonsterSince = 0; battleMapIndex = State.Map?.MapIndex ?? 0;
        roaming.Reset(); roamDestination = null;
        destination = null; actionTarget = 0; pendingAttack = null; MiniMap.Route = []; RefreshBattleMenu();
        Log("挂机已开始；Ctrl+Z、Esc或菜单可停止，点击地面时确认停止。");
    }

    private void StopAutoBattle()
    {
        if (!autoBattleEnabled) return;
        autoBattleEnabled = false; actionTarget = 0; autoPickupTarget = 0; destination = null; pendingAttack = null; MiniMap.Route = [];
        roamDestination = null;
        noMonsterSince = 0; RefreshBattleMenu();
        Log("挂机已停止。");
    }

    private void ProcessAutomation()
    {
        if (!HandshakeReady || World.Loading || State.Me?.Dead != false || settingsDialog.Visible || pendingDrop != 0 || dropDialog.Visible) return;
        var now = Environment.TickCount64;
        pickup.Tick(now);
        if (!pickup.Paused) pickupNotice = false;
        if (pickup.Paused && !pickupNotice) { pickupNotice = true; Log("自动拣取未收到物品移除回执，已暂停且不会重发；可手动核对，切图或重连后恢复。", ChatType.Hint); }
        if (autoBattleEnabled && State.User != null) roaming.Observe(State.Map?.MapIndex ?? 0, State.User.Location);
        if (skillEquipmentPending != 0) { ProcessSkillPreparation(now); return; }
        var idleRandom = autoBattleEnabled && preferences.NoMonsterRandom && noMonsterSince > 0 && now - noMonsterSince >= 3000;
        if (!npc.Visible && !GuildStorageOpen && automation.NextItem(State, preferences, now, idleRandom) is { } item)
        {
            if (item.Info.Type == ItemType.Scroll) { destination = null; actionTarget = 0; pendingAttack = null; MiniMap.Route = []; }
            Send(new C.UseItem { Grid = MirGridType.Inventory, UniqueID = item.UniqueID });
        }
        if (automation.Notice != automationNotice) { automationNotice = automation.Notice; if (automationNotice.Length > 0) Log(automationNotice); }
        if (automation.PendingItem != 0 || ProcessSkillPreparation(now) || ProcessClassSkills(now)) return;
        if (!autoBattleEnabled || automation.PendingScrollShape != null || now < nextBattleScan) return;
        nextBattleScan = now + 350;
        if (State.Trading || State.StorageActive || npc.Visible || GuildStorageOpen) return;
        var targets = ClientAutomation.Targets(State).Take(16).ToArray();
        if (targets.Length > 0) noMonsterSince = 0; else if (noMonsterSince == 0) noMonsterSince = now;
        if (targets.Any(target => target.Id == actionTarget)) return;
        // 当前战斗收尾后先捡允许的附近掉落，再接新怪，避免不断刷怪饿死拣取。
        if (pickup.Next(State, preferences, now, item => item.Location == State.User!.Location || World.NextStep(item.Location) != null) is { } nearby)
        {
            if (actionTarget != nearby.Id) { SelectTarget(nearby.Id); autoPickupTarget = nearby.Id; running = true; }
            return;
        }
        foreach (var target in targets)
        {
            if (Distance(State.User!.Location, target.Location) > 1 && World.Approach(target) == null) continue;
            SelectTarget(target.Id); running = true; return;
        }
        if (autoPickupTarget != 0 && actionTarget == autoPickupTarget) { destination = null; MiniMap.Route = []; }
        autoPickupTarget = 0; actionTarget = 0;
        if (!preferences.AutoRoam || moving || destination.HasValue || World.Map == null) return;
        if (roamDestination is { } previous)
        {
            if (State.User!.Location != previous && World.NextStep(previous) != null) { SetDestination(previous, true); return; }
            if (State.User.Location != previous) roaming.Block(previous, now);
            roamDestination = null;
        }
        var next = roaming.Next(State.Map?.MapIndex ?? 0, State.User!.Location, World.Map.Width, World.Map.Height, now,
            World.Walkable, point => World.NextStep(point) != null);
        if (next is { } goal) { roamDestination = goal; SetDestination(goal, true); return; }
        nextBattleScan = now + 5000;
    }

    private void AutomationPacket(Packet packet)
    {
        pickup.Apply(packet);
        if (packet is S.UserInformation or S.MapChanged or S.Death or S.Disconnect or S.LogOutSuccess or S.ReturnToLogin) { autoPickupTarget = 0; pickupNotice = false; }
        automation.Apply(packet, Environment.TickCount64);
        if (packet is S.Death or S.MapChanged) CloseGroundMove();
        if (packet is S.MapChanged map && autoBattleEnabled && automation.PendingScrollShape == 2 && map.MapIndex == battleMapIndex)
        { noMonsterSince = 0; nextBattleScan = Environment.TickCount64 + 500; return; }
        if (packet is S.Death) StopAutoBattle();
        if (packet is S.MapChanged changed) { battleMapIndex = changed.MapIndex; noMonsterSince = 0; roamDestination = null; roaming.Reset(); }
    }

    private void PickUpTarget(Actor target)
    {
        if (autoPickupTarget != target.Id || pickup.Begin(State, preferences, target, Environment.TickCount64)) Send(new C.PickUp());
        autoPickupTarget = 0; actionTarget = 0;
    }

    private bool BattleSkill(Actor target, long now)
    {
        if (!SmartCombatActive) return false;
        if (now < spellTime) return true;
        var skill = ClientAutomation.SkillFor(State, preferences, target, now);
        if (skill == null) return ClassSkillRules.WaitForRanged(State, preferences, target, now);
        if (skillPreparationPaused && SkillMaterials.Required(State, skill.Spell, target).Length > 0) return false;
        var distance = Distance(State.User!.Location, target.Location);
        if (distance > 9) return false;
        World.Selected = target.Id; Cast(State.User.Magics.IndexOf(skill), true); return true;
    }
}
