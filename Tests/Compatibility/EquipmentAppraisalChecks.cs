using Crystal.Native;
using Mir2.Companions;
using Server.MirDatabase;
using Server.MirEnvir;
using Server.MirObjects;
using S = ServerPackets;

internal static class EquipmentAppraisalChecks
{
    private sealed class Player : PlayerObject
    {
        public readonly List<Packet> Packets = [];
        public override void Enqueue(Packet packet) => Packets.Add(packet);
    }

    public static void Run(Action<bool, string> check)
    {
        var rolls = Enumerable.Range(0, 10000).Select(EquipmentAppraisal.Roll).GroupBy(value => value).OrderBy(group => group.Key).Select(group => group.Count());
        check(rolls.SequenceEqual(new[] { 6500, 2300, 900, 250, 50 }), "品质概率或边界错误");
        var info = new ItemInfo { Index = 1, Name = "品质检查刀", Type = ItemType.Weapon, Price = 20000, Durability = 10000,
            Stats = new Stats { [Stat.MinDC] = 5, [Stat.MaxDC] = 20, [Stat.HP] = 100 } };
        var drop = Envir.Main.CreateDropItem(info, appraisable: true);
        check(drop.EquipmentQuality is >= EquipmentGrade.Normal and <= EquipmentGrade.Supreme && !drop.Identified && drop.AddedStats.Count == 0 && drop.GetTotal(Stat.MaxDC) == 20,
            "怪物新装备在鉴定前已有品质加成或自动鉴定");
        check(Envir.Main.CreateDropItem(info).EquipmentQuality == EquipmentGrade.Legacy && Envir.Main.CreateDropItem(info).Identified, "非怪物及旧掉落入口被改变");
        check(!EquipmentAppraisal.Supports(new ItemInfo { Type = ItemType.Potion }) && !EquipmentAppraisal.Supports(new ItemInfo { Type = ItemType.Torch }), "非战斗装备被鉴定");
        var player = new Player { Account = new AccountInfo { Gold = 1000000 }, Info = new CharacterInfo { Index = 7, HP = 100 }, NPCObjectID = 10 };
        foreach (var grade in Enum.GetValues<EquipmentGrade>())
        {
            var item = new UserItem(info) { UniqueID = (ulong)(100 + (int)grade), EquipmentQuality = grade, Identified = grade == EquipmentGrade.Legacy, CurrentDura = 9000, MaxDura = 10000 };
            if (grade == EquipmentGrade.Legacy) item.AddedStats[Stat.MaxDC] = 7;
            VerifySave(item, check);
            if (grade == EquipmentGrade.Legacy) continue;
            player.Info.Inventory[6] = item;
            var gold = player.Account.Gold;
            EquipmentAppraiser.Show(player);
            check(item.GetTotal(Stat.MaxDC) == 20 && ItemPresentation.Category(item).Contains("未鉴定") && EquipmentAppraisal.Colour(item) == System.Drawing.Color.Silver,
                "未鉴定属性、提示或颜色泄露真实品质");
            check(ItemRecycling.Rejection(item) != "" && !CompanionInventoryRules.PlainWeapon(item, new HashSet<int>(), 100), "自动减负会出售未鉴定装备");
            EquipmentAppraiser.TryCall(player, 10, $"[@APPRAISECONFIRM_{item.UniqueID}]");
            check(!item.Identified && player.Account.Gold == gold, "无确认报价也能鉴定");
            EquipmentAppraiser.TryCall(player, 10, $"[@APPRAISESELECT_{item.UniqueID}]");
            EquipmentAppraiser.Show(player);
            EquipmentAppraiser.TryCall(player, 10, $"[@APPRAISECONFIRM_{item.UniqueID}]");
            check(!item.Identified && player.Account.Gold == gold, "取消后旧确认仍扣费");
            EquipmentAppraiser.TryCall(player, 10, $"[@APPRAISESELECT_{item.UniqueID}]");
            EquipmentAppraiser.TryCall(player, 10, $"[@APPRAISECONFIRM_{item.UniqueID}]");
            var bonus = new[] { 0, 0, 1, 2, 3, 5 }[(int)grade];
            var minBonus = new[] { 0, 0, 1, 1, 1, 2 }[(int)grade];
            check(item.Identified && item.GetTotal(Stat.MaxDC) == 20 + bonus && item.GetTotal(Stat.MinDC) == 5 + minBonus && player.Account.Gold == gold - 2000,
                "品质上下限加成或实际扣费不正确");
            check(player.Packets.OfType<S.EquipmentAppraisalResult>().Last().Success && !EquipmentAppraisal.Pending(item), "鉴定成功缺少对应物品回执");
            EquipmentAppraiser.TryCall(player, 10, $"[@APPRAISECONFIRM_{item.UniqueID}]");
            check(item.GetTotal(Stat.MaxDC) == 20 + bonus && player.Account.Gold == gold - 2000, "重复鉴定重复加属性或扣费");
            check(item.Clone().EquipmentQuality == grade && item.Clone().AddedStats.Equals(item.AddedStats), "交易/仓库复制丢失品质或加成");
            if (grade > EquipmentGrade.Normal) check(ItemRecycling.Rejection(item) != "", "高品质装备可自动回收");
            VerifySave(item, check);
        }
        Refusals(player, info, check);
        Presentation(check);
        NpcAndProtocol(check);
    }

    private static void Presentation(Action<bool, string> check)
    {
        var item = new UserItem(new ItemInfo { Type = ItemType.Weapon, Stats = new Stats { [Stat.MinDC] = 3, [Stat.MaxDC] = 5, [Stat.HP] = 10 } })
            { EquipmentQuality = EquipmentGrade.Supreme };
        check(ItemPresentation.Stats(item).Contains("攻击：3 - 5") && !ItemPresentation.StatLines(item).SelectMany(line => line).Any(part => part.Quality),
            "未鉴定装备提前显示品质加成");
        item.Identified = true; item.AddedStats.Add(EquipmentAppraisal.Bonuses(item));
        check(ItemPresentation.Stats(item).Contains("攻击：3(+1) - 5(+2)") && ItemPresentation.Stats(item).Contains("生命：10(+3)"),
            "品质加成没有单独追加到基础属性之后");
        check(ItemPresentation.StatLines(item).First().Where(part => part.Quality).Select(part => part.Text).SequenceEqual(new[] { "(+1)", "(+2)" }),
            "品质颜色范围包含基础属性或缺少括号");
        item.AddedStats[Stat.MaxDC] += 7;
        check(ItemPresentation.Stats(item).Contains("攻击：3(+1) - 12(+2)"), "旧强化被错误归入品质加成");
        item.EquipmentQuality = EquipmentGrade.Legacy;
        check(!ItemPresentation.StatLines(item).SelectMany(line => line).Any(part => part.Quality), "旧装备显示虚构品质加成");
    }

    private static void VerifySave(UserItem item, Action<bool, string> check)
    {
        using var memory = new MemoryStream(); using (var writer = new BinaryWriter(memory, System.Text.Encoding.UTF8, true)) item.Save(writer);
        var bytes = memory.ToArray(); memory.Position = 0; using var reader = new BinaryReader(memory);
        var restored = new UserItem(reader, Envir.Version, Envir.CustomVersion) { Info = item.Info };
        check(memory.Position == memory.Length && restored.UniqueID == item.UniqueID && restored.EquipmentQuality == item.EquipmentQuality &&
            restored.Identified == item.Identified && restored.AddedStats.Equals(item.AddedStats), "品质/旧装备/属性存档往返丢失");
        check((bytes[22] & 3) == ((item.Identified ? 1 : 0) | (item.Cursed ? 2 : 0)) && ((bytes[22] >> 2) & 7) == (byte)item.EquipmentQuality,
            "品质没有复用原标志字节或改变了原标志布局");
    }

    private static void Refusals(Player player, ItemInfo info, Action<bool, string> check)
    {
        var item = new UserItem(info) { UniqueID = 200, EquipmentQuality = EquipmentGrade.Supreme };
        player.Info.Inventory[6] = item;
        void Confirm() => EquipmentAppraiser.TryCall(player, 10, "[@APPRAISECONFIRM_200]");
        void Select() { EquipmentAppraiser.Show(player); EquipmentAppraiser.TryCall(player, 10, "[@APPRAISESELECT_200]"); }
        Select(); player.Account.Gold = 1; Confirm(); check(!item.Identified && player.Account.Gold == 1, "金币不足仍鉴定或扣款");
        player.Account.Gold = 10000; Select(); player.Info.Inventory[6] = null; Confirm();
        check(!item.Identified && player.Account.Gold == 10000, "装备离开背包仍可鉴定");
        player.Info.Inventory[6] = item; item.SoulBoundId = 8; Select(); Confirm();
        check(!item.Identified && player.Account.Gold == 10000, "他人灵魂绑定装备可鉴定");
        item.SoulBoundId = -1; Select(); player.Dead = true; Confirm(); player.Dead = false;
        check(!item.Identified && player.Account.Gold == 10000, "死亡时仍鉴定");
        Select(); EquipmentAppraiser.TryCall(player, 11, "[@APPRAISECONFIRM_200]");
        check(!item.Identified && player.Account.Gold == 10000, "更换NPC仍接受旧报价");
        item.AddedStats[Stat.MaxDC] = int.MaxValue; Select(); Confirm();
        check(!item.Identified && player.Account.Gold == 10000 && item.AddedStats[Stat.MaxDC] == int.MaxValue, "鉴定属性溢出或拒绝后资产改变");
        item.AddedStats.Clear(); info.Stats[Stat.MaxDC] = int.MaxValue; Select(); Confirm();
        check(!item.Identified && player.Account.Gold == 10000 && item.AddedStats.Count == 0, "基础与品质合计溢出仍可鉴定或扣费");
        info.Stats[Stat.MaxDC] = 20;
        info.Price = 0; check(EquipmentAppraisal.Price(item) == 100, "鉴定最低费用错误");
        info.Price = uint.MaxValue; check(EquipmentAppraisal.Price(item) == 50000, "鉴定最高费用错误");
    }

    private static void NpcAndProtocol(Action<bool, string> check)
    {
        var state = new GameState(); state.Apply(new S.ObjectNPC { ObjectID = 42, Name = NpcLabels.Encode("玄鉴先生", "Appraisal/MengchongAppraiser", false) });
        check(state.Actors[42].Name == "玄鉴先生" && state.Actors[42].NpcRole == "鉴定大师", "NPC姓名或分类串入协议名字");
        check(NpcLabels.Role("沈药师", "Town/Potion1") == "药品商人" && NpcLabels.Role("甲", "GuildTerritory\\GA0\\GTTeleporter-GA0") == "驿站传送" &&
            NpcLabels.Role("乙", "Unknown", true) == "任务指引" && NpcLabels.Read("旧NPC").Name == "旧NPC", "NPC分类或旧包兼容错误");
        var wasServer = Packet.IsServer;
        try
        {
            Packet.IsServer = false;
            var packet = new S.EquipmentAppraisalResult { UniqueID = 200, Success = true, Message = "鉴定成功" };
            var reply = (S.EquipmentAppraisalResult)Packet.ReceivePacket(packet.GetPacketBytes().ToArray(), out var extra);
            check(reply.UniqueID == 200 && reply.Success && reply.Message == packet.Message && extra.Length == 0 &&
                (short)ServerPacketIds.EquipmentAppraisalResult == (short)ServerPacketIds.DeleteHeroResult + 1, "鉴定追加回执或包编号不正确");
        }
        finally { Packet.IsServer = wasServer; }
    }

    public static void Upgrade(Mir2.Hosting.ProjectPaths paths)
    {
        if (!paths.Data.Contains("/mir2-appraisal-upgrade.", StringComparison.Ordinal)) throw new InvalidOperationException("品质升级验证仅允许隔离版本。");
        using var ownership = new FileStream(Path.Combine(paths.Data, ".server.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        using var storage = new Server.ConsoleHost.SqliteDatabaseStorage(paths.Database); Server.DatabaseStorage.Current = storage;
        Directory.SetCurrentDirectory(paths.World);
        try
        {
            var world = Envir.Main;
            if (!world.LoadDB()) throw new InvalidDataException("旧世界读取失败。");
            world.BuffInfoList.AddRange(BuffInfo.Load()); world.LoadAccounts();
            string Hash(Action<BinaryWriter> save)
            {
                using var stream = new MemoryStream(); using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true)) save(writer);
                return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream.ToArray()));
            }
            Dictionary<string, string> State() => world.AccountList.ToDictionary(account => "account/" + account.Index, account => Hash(account.Save))
                .Concat(world.HeroList.ToDictionary(hero => "hero/" + hero.Index, hero => Hash(hero.Save))).ToDictionary(pair => pair.Key, pair => pair.Value);
            var before = State(); world.SaveDB(); world.SaveAccounts();
            if (!world.LoadDB()) throw new InvalidDataException("升级世界读取失败。");
            world.LoadAccounts(); var after = State();
            if (before.Count != after.Count || before.Any(pair => !after.TryGetValue(pair.Key, out var hash) || hash != pair.Value))
                throw new InvalidDataException("品质升级改变了账户、角色、英雄或原物品字节。");
            Console.WriteLine($"PASS legacy quality v4 migration: {before.Count} account/hero serialized snapshots unchanged; old IDs, stats, quests and assets retained.");
        }
        finally { Server.DatabaseStorage.Current = null; }
    }
}
