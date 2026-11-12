using Crystal.Native;
using Godot;
using C = ClientPackets;
using S = ServerPackets;
using Point = System.Drawing.Point;

public partial class Main
{
    private readonly ClientAutomation automation = new();
    private bool autoBattleEnabled;
    private long nextBattleScan;
    private int roamDirection;
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
        if (gameMenu?.Visible == true) return;
        if (input is InputEventMouseButton mouse && gameMenuButton != null && gameMenuButton.GetGlobalRect().HasPoint(mouse.Position)) return;
        if (input is InputEventKey { Pressed: true, Echo: false } or InputEventMouseButton { Pressed: true }) StopAutoBattle();
    }

    private void ToggleAutoBattle()
    {
        if (autoBattleEnabled) { StopAutoBattle(); return; }
        if (State.Me?.Dead != false || World.Loading || State.Trading || State.StorageActive) { Log("当前状态不能开始挂机。"); return; }
        CloseSettings(); if (settingsDialog.Visible) return;
        automation.Resume(); autoBattleEnabled = true; nextBattleScan = noMonsterSince = 0; battleMapIndex = State.Map?.MapIndex ?? 0;
        destination = null; actionTarget = 0; pendingAttack = null; MiniMap.Route = []; gameMenu.SetItemText(7, "停止挂机");
        Log("挂机已开始；手动操作或 Esc 停止。");
    }

    private void StopAutoBattle()
    {
        if (!autoBattleEnabled) return;
        autoBattleEnabled = false; actionTarget = 0; destination = null; pendingAttack = null; MiniMap.Route = [];
        noMonsterSince = 0; gameMenu.SetItemText(7, "开始挂机");
        Log("挂机已停止。");
    }

    private void ProcessAutomation()
    {
        if (!HandshakeReady || World.Loading || State.Me?.Dead != false || settingsDialog.Visible || pendingDrop != 0 || dropDialog.Visible) return;
        var now = Environment.TickCount64;
        var idleRandom = autoBattleEnabled && preferences.NoMonsterRandom && noMonsterSince > 0 && now - noMonsterSince >= 3000;
        if (automation.NextItem(State, preferences, now, idleRandom) is { } item)
        {
            if (item.Info.Type == ItemType.Scroll) { destination = null; actionTarget = 0; pendingAttack = null; MiniMap.Route = []; }
            Send(new C.UseItem { Grid = MirGridType.Inventory, UniqueID = item.UniqueID });
        }
        if (automation.Notice != automationNotice) { automationNotice = automation.Notice; if (automationNotice.Length > 0) Log(automationNotice); }
        if (!autoBattleEnabled || automation.PendingScrollShape != null || now < nextBattleScan) return;
        nextBattleScan = now + 350;
        if (State.Trading || State.StorageActive || npc.Visible || GuildStorageOpen) { StopAutoBattle(); return; }
        var targets = ClientAutomation.Targets(State).Take(16).ToArray();
        if (targets.Length > 0) noMonsterSince = 0; else if (noMonsterSince == 0) noMonsterSince = now;
        if (targets.Any(target => target.Id == actionTarget)) return;
        actionTarget = 0;
        foreach (var target in targets)
        {
            if (Distance(State.User!.Location, target.Location) > 1 && World.Approach(target) == null) continue;
            SelectTarget(target.Id); running = true; return;
        }
        if (!preferences.AutoRoam || moving || destination.HasValue || World.Map == null) return;
        for (var attempt = 0; attempt < 8; attempt++)
        {
            var target = Functions.PointMove(State.User!.Location, (MirDirection)((roamDirection + attempt) % 8), 12);
            target = new Point(Math.Clamp(target.X, 0, World.Map.Width - 1), Math.Clamp(target.Y, 0, World.Map.Height - 1));
            if (target == State.User.Location || World.NextStep(target) == null) continue;
            SetDestination(target, true); roamDirection = Random.Shared.Next(8); return;
        }
        StopAutoBattle(); Log("附近没有可行路线，已停止挂机；请手动选择位置。");
    }

    private void AutomationPacket(Packet packet)
    {
        automation.Apply(packet, Environment.TickCount64);
        if (packet is S.MapChanged map && autoBattleEnabled && automation.PendingScrollShape == 2 && map.MapIndex == battleMapIndex)
        { noMonsterSince = 0; nextBattleScan = Environment.TickCount64 + 500; return; }
        if (packet is S.Death or S.MapChanged) StopAutoBattle();
    }

    private bool BattleSkill(Actor target, long now)
    {
        if (!autoBattleEnabled) return false;
        if (now < spellTime) return true;
        var skill = ClientAutomation.SkillFor(State, preferences, target, now);
        if (skill == null) return false;
        var distance = Distance(State.User!.Location, target.Location);
        if (ClientAutomation.IsAttackSkill(skill.Spell))
        {
            if (distance > 1) return false;
            if (!State.ActiveSkills.Contains(skill.Spell)) Cast(State.User.Magics.IndexOf(skill));
            Attack(Direction(State.User.Location, target.Location), skill.Spell); return true;
        }
        if (distance > 9) return false;
        World.Selected = target.Id; Cast(State.User.Magics.IndexOf(skill), true); return true;
    }
}
