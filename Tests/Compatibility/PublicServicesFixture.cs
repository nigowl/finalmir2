using Server.MirDatabase;
using Server.MirEnvir;

internal static class PublicServicesFixture
{
    public static void Configure(Envir world, ItemInfo potion, ItemInfo sword)
    {
        var character = world.CharacterList.Single(role => role.Name == "NativeGM");
        character.AccountInfo.Gold = 3_000_000; character.HP = 1; character.MP = 0; character.Level = 50;
        character.Equipment[0] = world.CreateFreshItem(sword);
        character.Inventory = new UserItem[46];
        character.Inventory[45] = world.CreateFreshItem(world.ItemInfoList.Single(item => item.Name == "NativeEquipFixture"));
        character.Magics.AddRange([new UserMagic(Spell.FireBall), new UserMagic(Spell.Fencing)]);
        foreach (var id in new[] { 1, 2 }) character.CurrentQuests.Add(new QuestProgressInfo(id) { StartDateTime = DateTime.UtcNow });
        var second = Path.Combine(Server.Settings.QuestPath, "NativeQuest2.txt");
        File.WriteAllText(second, File.ReadAllText(second).Replace("NativeTarget", "NativeDanger"));
        if (!world.ItemInfoList.Any(PublicShopRules.IsGuildHorn))
            world.ItemInfoList.Add(new ItemInfo { Index = ++world.ItemIndex, Name = "沃玛角", Type = ItemType.CraftingMaterial,
                Image = potion.Image, Price = 1_000_000, StackSize = 999, Weight = 1 });
        var shop = Path.Combine(Server.Settings.NPCPath, "NativeShop.txt");
        File.AppendAllLines(shop, world.ItemInfoList.Where(item => item.Index != potion.Index && PublicShopRules.Category(item) >= 0 && item.Price > 0)
            .GroupBy(item => (item.Type, item.Shape)).Select(group => group.First()).Select(item => item.Name + " 1"));
        new InIReader(Path.Combine("Configs", "Setup.ini")).Write("Optional", "ExpMobLevelDifference", false);
        world.SaveAccounts();
    }
}
