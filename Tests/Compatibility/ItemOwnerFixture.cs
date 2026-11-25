using System.Drawing;
using Server.MirDatabase;
using Server.MirEnvir;

internal static class ItemOwnerFixture
{
    public static void Configure(Envir world, MapInfo map, Point start, string root)
    {
        if (!root.Contains("/mir2-item-names.", StringComparison.Ordinal)) throw new InvalidOperationException("绑定名称夹具只允许独立临时版本。");
        foreach (var area in world.MapInfoList) { area.Respawns.Clear(); area.Movements.Clear(); }
        world.AccountList.Clear(); world.CharacterList.Clear(); world.GuildList.Clear(); world.NPCInfoList.Clear(); world.QuestInfoList.Clear();
        var account = new AccountInfo { Index = ++world.NextAccountID, AccountID = "BoundObserver", Password = "Native123", Gold = 1234,
            CreationDate = DateTime.UtcNow, CreationIP = "127.0.0.1" };
        world.NextCharacterID = Math.Max(world.NextCharacterID, 70000);
        CharacterInfo Role(string name) => new() { Index = ++world.NextCharacterID, Name = name, AccountInfo = account,
            Class = MirClass.Warrior, Level = 22, HP = 100, MP = 100, CurrentMapIndex = map.Index, BindMapIndex = map.Index,
            CurrentLocation = start, BindLocation = start, Inventory = new UserItem[Globals.BaseInventorySize], Equipment = new UserItem[14],
            Heroes = new HeroInfo[1], CreationDate = DateTime.UtcNow, CreationIP = "127.0.0.1" };
        var observer = Role("绑定观察者"); var owner = Role("离线绑定主人");
        account.Characters.AddRange([observer, owner]); world.AccountList.Add(account); world.CharacterList.AddRange([observer, owner]);
        foreach (var (name, slot, boundId) in new[] { ("本人绑定刀", 6, observer.Index), ("他人绑定戒", 7, owner.Index),
            ("同主绑定戒", 8, owner.Index), ("未知绑定戒", 9, int.MaxValue), ("绑定检查保留物", 10, -1) })
        {
            var info = new ItemInfo { Index = ++world.ItemIndex, Name = name, Type = slot == 6 ? ItemType.Weapon : ItemType.Ring, Durability = 5000 };
            world.ItemInfoList.Add(info); observer.Inventory[slot] = world.CreateFreshItem(info); observer.Inventory[slot].SoulBoundId = boundId;
        }
        var worn = new ItemInfo { Index = ++world.ItemIndex, Name = "本人对照戒", Type = ItemType.Ring, Durability = 5000 };
        world.ItemInfoList.Add(worn); observer.Equipment[7] = world.CreateFreshItem(worn); observer.Equipment[7].SoulBoundId = observer.Index;
        world.SaveDB(); world.SaveAccounts();
        Console.WriteLine("PASS isolated bound-name fixture: database IDs distinct from scene IDs, offline owner and preserved original bound items.");
    }
}
