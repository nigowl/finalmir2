using Mir2.Companions;

internal static class CompanionPublicServicesChecks
{
    public static void Run(Action<bool, string> check)
    {
        var red = new ItemInfo { Index = 1, Type = ItemType.Potion, StackSize = 100, Stats = new Stats { [Stat.HP] = 100 } };
        var blue = new ItemInfo { Index = 2, Type = ItemType.Potion, StackSize = 100, Stats = new Stats { [Stat.MP] = 100 } };
        var charm = new ItemInfo { Index = 3, Name = "护身符（小）", Type = ItemType.Amulet, StackSize = 500 };
        var book = new ItemInfo { Index = 4, Type = ItemType.Book, Shape = (short)Spell.SoulFireBall, RequiredClass = RequiredClass.Taoist };
        var infos = new[] { red, blue, charm, book }.ToDictionary(i => i.Index);
        var offers = infos.Values.Select(info => new PublicShopEntry { ItemIndex = info.Index, Price = info == charm ? 500u : 40u, MaxCount = 999 }).ToArray();
        var bag = new UserItem?[46]; var equipment = new UserItem?[14]; var learned = new HashSet<Spell>();
        (PublicShopEntry Offer, ushort Count)? Choose(MirClass role = MirClass.Taoist, uint gold = 10000, decimal room = 100, bool development = false) =>
            CompanionPublicShopRules.Choose(offers, id => infos.GetValueOrDefault(id), bag, equipment, role, MirGender.Male, 40,
                learned, gold, room, 50, true, development);
        foreach (var role in Enum.GetValues<MirClass>().Where(role => (int)role < 5))
            check(Choose(role) is { Offer.ItemIndex: 1, Count: 15 }, $"{role}没有优先原地补红药");
        check(Choose(gold: 40) is { Offer.ItemIndex: 1, Count: 1 } && Choose(gold: 39) == null, "无红保命采购预算被绕过或最低一瓶不可买");
        check(Choose(room: 0) == null, "商城采购绕过真实负重");
        bag[0] = new(red) { Count = 15 };
        check(Choose() is { Offer.ItemIndex: 2 } && Choose(MirClass.Warrior) == null, "蓝药短缺未采购或战士多买蓝药");
        bag[1] = new(blue) { Count = 20 };
        check(Choose() == null && Choose(development: true) is { Offer.ItemIndex: 4, Count: 1 }, "补满后反复买药或未买未学职业书");
        learned.Add(Spell.SoulFireBall);
        check(Choose(development: true) is { Offer.ItemIndex: 3, Count: 1 }, "500次护符按次数误买多包");
        equipment[9] = new(charm) { Count = 25 };
        check(Choose(development: true) == null, "25次护符尚未到补给线就采购");
        equipment[9]!.Count = 24;
        check(Choose(development: true) is { Count: 1 } && Choose(gold: 499, development: true) == null, "护符整包报价/预算错误");
        bag[0] = null;
        check(Choose(development: true) == null, "书籍护符采购抢占红药");
        check(PublicShopRules.Category(book) == 4, "公共商店缺少普通职业书籍分类");
        book.Bind = BindMode.DontSell;
        check(PublicShopRules.Category(book) < 0, "特殊绑定技能书被加入商城");
    }
}
