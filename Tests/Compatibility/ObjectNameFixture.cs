using System.Drawing;
using Server;
using Server.MirDatabase;
using Server.MirEnvir;
using Server.MirObjects;

internal static class ObjectNameFixture
{
    public static void Configure(Envir world, MapInfo map, Point start, string root)
    {
        if (!root.Contains("/mir2-object-name.", StringComparison.Ordinal)) throw new InvalidOperationException("名称增量夹具只允许独立版本。");
        foreach (var area in world.MapInfoList) { area.Respawns.Clear(); area.Movements.Clear(); }
        world.AccountList.Clear(); world.CharacterList.Clear(); world.GuildList.Clear(); world.NPCInfoList.Clear(); world.QuestInfoList.Clear();
        var monster = new MonsterInfo { Index = ++world.MonsterIndex, Name = "到期召唤兽", Image = Monster.Hen, AI = 13, Level = 1,
            ViewRange = 0, MoveSpeed = ushort.MaxValue, AttackSpeed = ushort.MaxValue, Stats = new Stats { [Stat.HP] = 500 } };
        world.MonsterInfoList.Add(monster);
        var account = new AccountInfo { Index = ++world.NextAccountID, AccountID = "NameObserver", Password = "Native123", Gold = 1234,
            CreationDate = DateTime.UtcNow, CreationIP = "127.0.0.1" };
        var character = new CharacterInfo { Index = ++world.NextCharacterID, Name = "名字同步验证", AccountInfo = account, Class = MirClass.Wizard,
            Gender = MirGender.Male, Level = 22, HP = 200, MP = 100, CurrentMapIndex = map.Index, BindMapIndex = map.Index,
            CurrentLocation = start, BindLocation = start, Inventory = new UserItem[Globals.BaseInventorySize], Equipment = new UserItem[14], Heroes = new HeroInfo[1],
            CreationDate = DateTime.UtcNow, CreationIP = "127.0.0.1" };
        // 原PetInfo不存计时；明确准备到期状态，验证原登录恢复后的脱离主人回执。
        character.Pets.Add(new PetInfo(MonsterObject.GetMonster(monster)) { HP = 500, MaxPetLevel = 7 });
        var marker = new ItemInfo { Index = ++world.ItemIndex, Name = "名称检查保留物", Type = ItemType.CraftingMaterial, StackSize = 10 };
        world.ItemInfoList.Add(marker); character.Inventory[0] = world.CreateFreshItem(marker); character.Inventory[0].Count = 3;
        account.Characters.Add(character); world.AccountList.Add(account); world.CharacterList.Add(character);
        var settings = new InIReader(Path.Combine("Configs", "Setup.ini")); settings.Write("Game", "PetSave", false); settings.Save();
        world.SaveDB(); world.SaveAccounts();
        Console.WriteLine("PASS isolated object-name fixture: original saved wizard pet expires after login; no new timer or protocol layout.");
    }
}
