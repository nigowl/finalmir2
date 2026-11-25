using Crystal.Native;
using S = ServerPackets;

internal static class ClientNoticeChecks
{
    public static void Run(Action<bool, string> check)
    {
        var state = new GameState(); var notices = new List<(string Text, ChatType Type)>();
        var chats = new List<(string Text, ChatType Type)>();
        state.ChatMessage += (text, type) => chats.Add((text, type));
        state.ItemMessage += (text, type) => notices.Add((text, type));
        state.Apply(new S.UserInformation { ObjectID = 1, HP = 30, Gold = 100, Inventory = new UserItem[8], Equipment = new UserItem[14], QuestInventory = [] });
        var info = new ItemInfo { Index = 1, Name = "回归药品", Type = ItemType.Potion, StackSize = 99 };
        state.Items[1] = info;
        state.Apply(new S.GainedItem { Item = new UserItem(info) { UniqueID = 10, Count = 4 } });
        check(notices[^1] == ("获得 回归药品 ×4", ChatType.System2), "获得物品提示丢失原名称或数量");
        state.Apply(new S.UseItem { UniqueID = 10, Success = false });
        check(notices.Count == 1 && chats[^1].Type == ChatType.Hint && state.User!.Inventory[0].Count == 4, "使用失败仍扣除物品或显示成功");
        state.Apply(new S.UseItem { UniqueID = 10, Success = true });
        check(notices[^1] == ("使用成功：回归药品。", ChatType.System2) && state.User!.Inventory[0].Count == 3, "使用回执的提示或数量错误");
        state.Apply(new S.DropItem { UniqueID = 10, Count = 1, Success = true });
        check(notices[^1].Text == "丢弃成功：回归药品 ×1。" && state.User!.Inventory[0].Count == 2, "丢弃提示没有在删除前读取物品名称");
        state.Apply(new S.DeleteItem { UniqueID = 10, Count = 1 });
        check(notices[^1].Text == "扣除物品：回归药品 ×1。" && state.User!.Inventory[0].Count == 1, "服务器扣除物品没有真实回执提示");
        var previous = notices.Count; state.Apply(new S.DeleteItem { UniqueID = 99, Count = 1 });
        check(notices.Count == previous, "未知物品扣除伪造了名称提示");
        state.Apply(new S.GainedGold { Gold = 37 });
        check(state.User!.Gold == 137 && notices[^1] == ("获得金币 ×37", ChatType.System2), "金币到账或提示错误");
        state.Apply(new S.LoseGold { Gold = 20 });
        check(state.User.Gold == 117 && notices[^1] == ("消耗金币 ×20", ChatType.System2), "金币支出或提示错误");
        var beforeExperience = notices.Count; var beforeChat = chats.Count;
        state.Apply(new S.GainExperience { Amount = 125 });
        check(state.User.Experience == 125 && notices.Count == beforeExperience && chats.Count == beforeChat + 1 && chats[^1].Text == "获得经验 +125", "经验回执没有进入聊天或错误进入物品提示");
        state.Apply(new S.GainExperience { Amount = 0 });
        check(chats.Count == beforeChat + 1, "零经验仍显示获得提示");
        var itemCount = notices.Count; var chatCount = chats.Count;
        state.Apply(new S.Chat { Message = "购买失败：余额不足", Type = ChatType.System });
        state.Apply(new S.Chat { Message = "命令执行错误：地图不存在", Type = ChatType.Hint });
        state.Apply(new S.Chat { Message = "购买成功：普通聊天文字", Type = ChatType.Normal });
        check(notices.Count == itemCount && chats.Count == chatCount + 3, "错误/命令/普通聊天误入物品消息区域");
        state.Apply(new S.Chat { Message = "购买成功：回归药品 ×1", Type = ChatType.System });
        check(notices.Count == itemCount + 1 && chats.Count == chatCount + 3, "原服购买成功消息没有独立分流");
        check(ChatChannels.Includes(ChatChannel.Whisper, ChatType.WhisperOut) && !ChatChannels.Includes(ChatChannel.Whisper, ChatType.Guild) &&
            ChatChannels.Includes(ChatChannel.Shout, ChatType.Shout3) && !ChatChannels.Includes(ChatChannel.Guild, ChatType.Hint) &&
            ChatChannels.Includes(ChatChannel.System, ChatType.Hint), "原聊天类目过滤不正确");
        check(ChatChannels.Format(ChatChannel.Whisper, "测试", "小明", false, false, out _) == "/小明 测试" &&
            ChatChannels.Format(ChatChannel.Group, "测试", "", true, false, out _) == "!!测试" &&
            ChatChannels.Format(ChatChannel.Guild, "测试", "", false, true, out _) == "!~测试", "频道没有沿用原TCP聊天前缀");
        check(ChatChannels.Format(ChatChannel.Guild, "@mm 0", "", false, false, out _) == "@mm 0" &&
            ChatChannels.Format(ChatChannel.Group, "/小明 测试", "", false, false, out _) == "/小明 测试", "选中频道错误改写显式命令或私信");
        check(ChatChannels.Format(ChatChannel.Whisper, "测试", "", false, false, out var noName) == null && noName != null &&
            ChatChannels.Format(ChatChannel.Guild, "测试", "", false, false, out var noGuild) == null && noGuild != null &&
            ChatChannels.Format(ChatChannel.Guild, new string('测', Globals.MaxChatLength), "", false, true, out var tooLong) == null && tooLong != null,
            "发包前未检查私信对象、行会和包含前缀的长度");
        foreach (var channel in Enum.GetValues<ChatChannel>().Where(c => c is not (ChatChannel.All or ChatChannel.System)))
            check(!ChatChannels.Includes(channel, ChatType.System) && !ChatChannels.Includes(channel, ChatType.Hint), "系统消息混入其他频道导致切换内容不变");
        check(ChatChannels.BubbleText("小明:  公共\n聊天  ", "小明") == "公共 聊天" && ChatChannels.BubbleText("?????:  匿名", "小明") == "匿名", "气泡未去原发送者前缀或未规范换行");
        var emoji = string.Concat(Enumerable.Repeat("😀", 90));
        var bubble = ChatChannels.BubbleText(emoji, "小明");
        check(System.Globalization.StringInfo.ParseCombiningCharacters(bubble).Length == 81 && bubble.EndsWith('…'), "气泡未有界裁剪或切断Unicode字符");
        state.Apply(new S.FriendUpdate { Friends = [new() { Index = 2, Name = "好友" }] });
        state.Apply(new S.ReceiveMail { Mail = [new() { MailID = 7, Message = "来信" }] });
        state.Apply(new S.UpdateIntelligentCreatureList { CreatureSummoned = true, SummonedCreatureType = IntelligentCreatureType.Chick, CreatureList = [new() { PetType = IntelligentCreatureType.Chick }] });
        state.Apply(new S.HeroInformation { ObjectID = 10, Name = "英雄" });
        state.Apply(new S.UpdateHeroSpawnState { State = HeroSpawnState.Summoned });
        check(state.User?.ObjectID == 1 && state.Hero?.ObjectID == 10 && state.HeroState == HeroSpawnState.Summoned && state.User.HasHero &&
            state.Friends.Count == 1 && state.Mail.Count == 1 && state.User.IntelligentCreatures.Count == 1 && state.User.CreatureSummoned, "原社交回执未独立保存，或英雄包覆盖玩家");
        state.Hero!.Equipment = new UserItem[14];
        var heroItem = state.User!.Inventory[0];
        state.Apply(new S.TransferHeroItem { From = 0, To = 0, Success = false });
        check(state.User.Inventory[0] == heroItem && ReferenceEquals(state.Hero.Inventory, state.User.Inventory), "英雄没有使用共用背包");
        state.Apply(new S.TransferHeroItem { From = 0, To = 0, Success = true });
        state.Apply(new S.EquipItem { Grid = MirGridType.HeroInventory, UniqueID = heroItem.UniqueID, To = 0, Success = true });
        check(state.User.Inventory[0] == null && state.Hero.Inventory[0] == null && state.Hero.Equipment[0] == heroItem, "英雄穿戴未保持编号或覆盖玩家装备");
        state.Apply(new S.RemoveItem { Grid = MirGridType.HeroInventory, UniqueID = heroItem.UniqueID, To = 1, Success = true });

        check(state.User.Inventory[1] == heroItem && state.Hero.Inventory[1] == heroItem && state.Hero.Equipment[0] == null, "英雄卸下取回未保持原物品");
        state.Apply(new S.ChangePMode { Mode = PetMode.FocusMasterTarget });
        state.Apply(new S.SetHeroBehaviour { Behaviour = HeroBehaviour.CounterAttack });
        check(state.CurrentPetMode == PetMode.FocusMasterTarget && state.User.HeroBehaviour == HeroBehaviour.CounterAttack, "宠物或英雄战斗模式没有采用服务端回执");
        state.Apply(new S.HeroBaseStatsInfo { Stats = new BaseStats(MirClass.Warrior) { Stats = [new BaseStat(Stat.HP) { Base = 100 }, new BaseStat(Stat.MP) { Base = 50 }] } });
        state.Apply(new S.AddBuff { Buff = new ClientBuff { ObjectID = 10, Type = BuffType.HealthAid, Stats = new Stats { [Stat.HP] = 20 } } });
        state.Apply(new S.HeroHealthChanged { HP = 80, MP = 30 });
        state.Apply(new S.HeroLevelChanged { Level = 12, Experience = 25, MaxExperience = 200 });
        check(state.CalculateHeroStats()[Stat.HP] == 120 && state.CalculateHeroStats()[Stat.MP] == 50 && state.Hero.HP == 80 && state.Hero.MP == 30 &&
            state.Hero.Level == 12 && state.Hero.Experience == 25 && state.Hero.MaxExperience == 200 && state.UserBuffs.Count == 0,
            "英雄生命魔力、增益或升级归约错误，或覆盖本人增益");
        state.Apply(new S.RemoveBuff { ObjectID = 10, Type = BuffType.HealthAid });
        check(state.CalculateHeroStats()[Stat.HP] == 100, "英雄增益移除后仍增加生命上限");
        state.Apply(new S.ManageHeroes { CurrentHero = new ClientHeroInformation { Index = 20, Name = "当前" }, Heroes = [new() { Index = 30, Name = "备用" }], MaximumCount = 2 });
        state.Apply(new S.ChangeHero { FromIndex = 0 });
        check(state.ActiveHero?.Index == 30 && state.StoredHeroes[0].Index == 20 && state.Hero == null && state.User.ObjectID == 1,
            "英雄切换混淆存储编号与场景对象编号，或覆盖本人");
        state.Apply(new S.LogOutSuccess());
        check(state.Friends.Count == 0 && state.Mail.Count == 0 && state.Hero == null && state.HeroState == HeroSpawnState.None, "切换角色未清空社交数据");
    }
}
