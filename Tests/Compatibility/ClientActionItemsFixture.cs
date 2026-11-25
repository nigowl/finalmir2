using Server.MirDatabase;
using Server.MirEnvir;

internal static class ClientActionItemsFixture
{
    public static void Configure(Envir world)
    {
        var target = world.MonsterInfoList.Single(info => info.Name == "技能测试靶怪");
        target.Image = Monster.Scarecrow; target.Name = "稻草人检查";
        var role = world.CharacterList.Single(character => character.AccountInfo.AccountID == "NativeWizard");
        var source = world.MapInfoList.Single(map => map.Index == role.CurrentMapIndex);
        var restricted = new MapInfo { Index = ++world.MapIndex, FileName = source.FileName, Title = "组队入口回执检查", RequiredGroup = true, RequiredGroupSize = 2 };
        world.MapInfoList.Add(restricted);
        source.Movements.Add(new MovementInfo { MapIndex = restricted.Index, Source = Functions.PointMove(role.CurrentLocation, MirDirection.UpLeft, 3), Destination = role.CurrentLocation });
        var junk = new ItemInfo { Index = ++world.ItemIndex, Name = "检查余装", Type = ItemType.Weapon, Price = 100, Durability = 1000 };
        var kept = new ItemInfo { Index = ++world.ItemIndex, Name = "检查保留", Type = ItemType.Weapon, Price = 100, Durability = 1000, Bind = BindMode.DontSell };
        world.ItemInfoList.AddRange([junk, kept]);
        var empty = Array.FindIndex(role.Inventory, item => item == null);
        role.Inventory[empty] = world.CreateFreshItem(junk);
        for (var index = 0; index < 12; index++)
            if (role.Inventory[index] == null) role.Inventory[index] = world.CreateFreshItem(kept);
        world.SaveAccounts();
    }
}
