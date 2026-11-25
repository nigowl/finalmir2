using Crystal.Native;
using Godot;
using C = ClientPackets;
using S = ServerPackets;

public partial class Main
{
    private OptionButton petCombatMode = null!;
    private Label petCombatLabel = null!;
    private Packet? heroItemPending;
    private long heroItemSent;
    private bool heroItemTimedOut, renderingCompanions, heroWindow;
    private Vector2 characterEquipmentPosition;
    private bool HeroInventoryContext => heroWindow && equipment.Visible;
    private S.UserInformation? EquipmentSubject => HeroInventoryContext ? State.Hero : State.User;
    private List<ClientMagic> SubjectMagics => EquipmentSubject?.Magics ?? [];
    private UserItem[] SubjectEquipment => EquipmentSubject?.Equipment ?? [];
    private MirGridType SubjectGrid => HeroInventoryContext ? MirGridType.HeroInventory : MirGridType.Inventory;

    private void ToggleHeroWindow()
    {
        if (State.User?.HasHero != true) return;
        if (HeroInventoryContext) { CloseEquipment(); return; }
        CloseSocial(); CancelItemContext(); characterEquipmentPosition = equipment.Position; heroWindow = true;
        equipment.Position = new Vector2(Math.Max(0, inventory.Position.X - equipment.Size.X - 8), inventory.Position.Y);
        equipmentTabs[0].Text = "英雄"; paperdoll.Hero = true;
        ShowEquipmentPage(0); RenderInventory();
    }

    private void CloseEquipment()
    {
        equipment.Hide(); if (heroWindow) equipment.Position = characterEquipmentPosition;
        heroWindow = false; paperdoll.Hero = false;
        equipmentTabs[0].Text = "人物"; CancelItemContext(); RenderInventory();
    }

    private void CancelItemContext()
    {
        // 切换对象只撤销未发送的拖拽和确认；已发送的资产锁等待原回执。
        CancelItemDrag(); dropDialog.Hide(); dropCandidate = 0;
        CancelSkillLearning();
        RenderSkills();
    }

    private void CastSubjectSkill(int index)
    {
        if (!HeroInventoryContext) { Cast(index); return; }
        if (State.HeroState != HeroSpawnState.Summoned || State.Hero == null || heroItemPending != null || State.Trading || State.StorageActive ||
            State.Me?.Dead != false || State.Actors.GetValueOrDefault(State.Hero.ObjectID) is not { Dead: false } actor ||
            SubjectMagics.ElementAtOrDefault(index) is not { } magic || ClientSkillRules.IsPassive(magic.Spell)) return;
        var aim = ClientSkillRules.Aim(magic.Spell, actor, State.Actors.GetValueOrDefault(World.Selected), World.CellAt(World.GetGlobalMousePosition()));
        if (aim == null) { Log("请先选择英雄技能的有效目标。", ChatType.Hint); return; }
        Send(new C.Magic { ObjectID = actor.Id, Spell = magic.Spell, Direction = actor.Direction, TargetID = aim.Value.Target, Location = aim.Value.Location });
    }

    private bool SendSubjectItem(Packet request)
    {
        if (heroItemPending != null || inventoryOrganizer.Pending != null || recyclePending != null || automation.PendingItem != 0 ||
            pendingDrop != 0 || npcTransactionPending || publicPurchases.Pending || SkillPreparationBusy)
        { Log("正在等待物品回执，请稍候。", ChatType.Hint); return false; }
        var resurrect = request is C.UseItem use && State.User?.Inventory.Any(item => item?.UniqueID == use.UniqueID && item.Info.Type == ItemType.Scroll && item.Info.Shape == 6) == true;
        if (HeroInventoryContext && (State.HeroState != HeroSpawnState.Summoned && !(State.HeroState == HeroSpawnState.Dead && resurrect) || State.Hero == null || State.Me?.Dead != false ||
            State.Trading || State.StorageActive || GuildStorageOpen || shopMode != null))
        { Log("请先召唤英雄，并结束交易、仓库或商店操作。", ChatType.Hint); return false; }
        heroItemPending = request; heroItemSent = Environment.TickCount64; heroItemTimedOut = false;
        Send(request); return true;
    }

    private void BuildCompanionControls()
    {
        petCombatLabel = Text(social, "召唤物模式", 12, 223, 88); petCombatLabel.Size = new Vector2(88, 24);
        petCombatMode = new OptionButton { Name = "PetCombatMode", Position = new Vector2(108, 220), Size = new Vector2(204, 26), FocusMode = FocusModeEnum.None };
        foreach (var mode in Enum.GetValues<PetMode>()) petCombatMode.AddItem(mode == PetMode.None ? "原地待命" : mode.ToLocalizedString(), (int)mode);
        petCombatMode.TooltipText = "召唤物和英雄共用移动 / 攻击模式；拾取宠物只跟随，不参与战斗。";
        social.AddChild(petCombatMode);
        petCombatMode.ItemSelected += index => { if (!renderingCompanions) ChoosePetMode((PetMode)petCombatMode.GetItemId((int)index)); };
    }

    private void ChoosePetMode(PetMode mode)
    {
        if (State.User != null && Enum.IsDefined(mode) && mode != State.CurrentPetMode) Send(new C.ChangePMode { Mode = mode });
        petCombatMode.Select(petCombatMode.GetItemIndex((int)State.CurrentPetMode));
        hudPetMode.Select(hudPetMode.GetItemIndex((int)State.CurrentPetMode));
    }

    private void ChooseHeroBehaviour(HeroBehaviour mode)
    {
        if (State.User is { } user && State.HeroState == HeroSpawnState.Summoned && mode is >= HeroBehaviour.Attack and <= HeroBehaviour.Follow && mode != user.HeroBehaviour)
            Send(new C.SetHeroBehaviour { Behaviour = mode });
        hudHeroMode.Select(hudHeroMode.GetItemIndex((int)(State.User?.HeroBehaviour ?? HeroBehaviour.Attack)));
    }

    private void RenderCompanionControls()
    {
        renderingCompanions = true;
        petCombatLabel.Visible = petCombatMode.Visible = socialPage == 2;
        petCombatMode.Select(petCombatMode.GetItemIndex((int)State.CurrentPetMode));
        if (socialPage == 2) socialDetail.Size = new Vector2(300, 54);
        renderingCompanions = false;
    }

    private void HeroItemPacket(Packet packet)
    {
        var matches = (heroItemPending, packet) switch
        {
            (C.UseItem a, S.UseItem b) => a.Grid == b.Grid && a.UniqueID == b.UniqueID,
            (C.EquipItem a, S.EquipItem b) => a.Grid == b.Grid && a.UniqueID == b.UniqueID && a.To == b.To,
            (C.RemoveItem a, S.RemoveItem b) => a.Grid == b.Grid && a.UniqueID == b.UniqueID && a.To == b.To,
            _ => false
        };
        if (matches || packet is S.UserInformation and not S.HeroInformation or S.Disconnect or S.LogOutSuccess or S.ReturnToLogin)
        { heroItemPending = null; heroItemTimedOut = false; socialRevision = -1; }
    }

    private bool BlockHeroItemConflict(Packet packet)
    {
        if (heroItemPending == null || ReferenceEquals(packet, heroItemPending)) return false;
        return packet is C.MoveItem or C.MergeItem or C.SplitItem or C.UseItem or C.EquipItem or C.RemoveItem or C.DropItem or
            C.StoreItem or C.TakeBackItem or C.TransferHeroItem or C.TakeBackHeroItem or C.SellItem or C.RecycleItem or C.ChangeHero;
    }
}
