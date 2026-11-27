using Client.MirObjects;
using Crystal.Native;
using S = ServerPackets;

public partial class Main
{
    // 必须在状态归约前取物品类型，最后一件使用、出售、丢弃后已不在背包。
    internal void AudioItemPacket(Packet packet)
    {
        UserItem? Find(ulong id) => State.User?.Inventory.Concat(State.User.Equipment).Concat(State.User.QuestInventory)
            .Concat(State.Storage).FirstOrDefault(item => item?.UniqueID == id);
        int Item(ulong id) => OriginalSoundRules.Item(Find(id)?.Info?.Type);
        int Slot(UserItem[]? items, int index) => OriginalSoundRules.Item(items?.ElementAtOrDefault(index)?.Info?.Type);
        var sound = packet switch
        {
            S.GainedGold { Gold: > 0 } or S.LoseGold { Gold: > 0 } => 10106,
            S.GainedItem p => OriginalSoundRules.Item(State.Items.GetValueOrDefault(p.Item.ItemIndex)?.Type),
            S.GainedQuestItem => 10118,
            S.UseItem { Success: true } p => Find(p.UniqueID)?.Info?.Type == ItemType.Potion ? 10107 : 10118,
            S.DropItem { Success: true, HeroItem: false } p => Item(p.UniqueID),
            S.SellItem { Success: true } p => Item(p.UniqueID),
            S.EquipItem { Success: true } p => Item(p.UniqueID),
            S.RemoveItem { Success: true } p => Item(p.UniqueID),
            S.EquipSlotItem { Success: true } p => Item(p.UniqueID),
            S.RemoveSlotItem { Success: true } p => Item(p.UniqueID),
            S.MergeItem { Success: true } p => Item(p.IDFrom),
            S.SplitItem1 { Success: true } p => Item(p.UniqueID),
            S.MoveItem { Success: true } p => Slot(p.Grid == MirGridType.Storage ? State.Storage : State.User?.Inventory, p.From),
            S.StoreItem { Success: true } p => Slot(p.From < 0 ? State.User?.QuestInventory : State.User?.Inventory, p.From < 0 ? ~p.From : p.From),
            S.TakeBackItem { Success: true } p => Slot(State.Storage, p.From),
            S.DepositTradeItem { Success: true } p => Slot(State.User?.Inventory, p.From),
            S.RetrieveTradeItem { Success: true } p => Slot(State.TradeItems, p.From),
            S.TradeConfirm => 10104,
            S.GuildStorageItemChange { Type: 0 } p when p.User == State.User?.RealId && p.Item?.Item != null => OriginalSoundRules.Item(p.Item.Item.Info?.Type ?? State.Items.GetValueOrDefault(p.Item.Item.ItemIndex)?.Type),
            S.GuildStorageItemChange { Type: 1 } p when p.User == State.User?.RealId => OriginalSoundRules.Item(State.GuildStorage.ElementAtOrDefault(p.From)?.Item.Info?.Type),
            S.ItemRepaired p => Item(p.UniqueID),
            S.NewMagic { Hero: false } => 10104,
            _ => -1
        };
        if (sound >= 0) _ = PlaySound(sound);
        if (packet is S.ObjectMonster { Dead: false } monster && !State.Actors.ContainsKey(monster.ObjectID))
        {
            var summon = OriginalSoundRules.Summon(monster.Image);
            ActorSound(new Actor { Id = monster.ObjectID, Location = monster.Location, Hidden = monster.Hidden },
                !monster.Extra && summon >= 0 ? summon : OriginalSoundRules.Appear(monster.Image, monster.Extra));
        }
        if (packet is S.ObjectTeleportOut teleport && State.Actors.TryGetValue(teleport.ObjectID, out var actor)) ActorSound(actor, 10110);
    }
}
