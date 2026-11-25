using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using Crystal.Native;
using Mir2.Companion;
using Mir2.Companions;
using Server.ConsoleHost;
using Server.MirDatabase;
using Server.MirEnvir;
using Server.MirObjects;
using S = ServerPackets;

internal static class CompanionHeroChecks
{
    public static async Task Run(Action<bool, string> check)
    {
        var book = new UserItem(new ItemInfo { Index = 90, Type = ItemType.Book, Shape = (short)Spell.FireBall, RequiredClass = RequiredClass.Wizard });
        check(CompanionHeroRules.Usable(book, MirClass.Wizard, MirGender.Male, 1), "英雄不能使用普通职业书");
        check(!CompanionHeroRules.Usable(book, MirClass.Warrior, MirGender.Male, 1), "英雄书忽略职业");
        check(CompanionHeroRules.ProtectedItems([book], MirClass.Wizard, MirGender.Male, 1, [], new HashSet<Spell>()).Contains(90), "英雄未学书被当成主人无用书存仓");
        check(!CompanionHeroRules.ProtectedItems([book], MirClass.Wizard, MirGender.Male, 1, [], new HashSet<Spell> { Spell.FireBall }).Any(), "英雄已学书永久阻止减负");
        book.Info.Bind = BindMode.NoHero;
        check(!CompanionHeroRules.Usable(book, MirClass.Wizard, MirGender.Male, 1), "英雄忽略NoHero绑定");
        var blade = new UserItem(new ItemInfo { Index = 91, Type = ItemType.Weapon, Durability = 1000 }) { CurrentDura = 1000 };
        check(CompanionHeroRules.ProtectedItems([blade], MirClass.Wizard, MirGender.Male, 1, new UserItem[14], new HashSet<Spell>()).Contains(91), "英雄空武器槽的升级被回收");
        blade.Info.RequiredAmount = 30;
        check(CompanionHeroRules.ProtectedItems([blade], MirClass.Wizard, MirGender.Male, 1, new UserItem[14], new HashSet<Spell>()).Contains(91), "英雄未来装备被回收");
        Planning(check);
        await Session(check, "normal");
        await Session(check, "rejected");
        await Session(check, "unknown");
    }

    private static void Planning(Action<bool, string> check)
    {
        var world = Envir.Main; var scripts = world.Scripts; var scriptIndex = world.ScriptIndex;
        var enabled = Server.Settings.AllowNewHero; var jobs = Server.Settings.Hero_CanCreateClass;
        var minimum = Server.Settings.Hero_RequiredLevel; var sealName = Server.Settings.HeroSealItemName;
        try
        {
            world.Scripts = []; Server.Settings.AllowNewHero = true; Server.Settings.Hero_CanCreateClass = [false, false, false, false, true];
            Server.Settings.Hero_RequiredLevel = 22; Server.Settings.HeroSealItemName = "MissingTestSeal";
            var map = new Map(new MapInfo { Index = 91 });
            var player = new PlayerObject { Info = new CharacterInfo { Level = 22, Heroes = new HeroInfo[1] }, CurrentMap = map };
            var npc = new NPCObject(new NPCInfo { Name = "真实英雄使者", FileName = "CompanionHeroChecksAbsent" }) { CurrentMap = map };
            var script = NPCScript.Get(npc.ScriptID); script.NPCPages = [new(NPCScript.HeroCreateKey)];
            KnownGoal? Goal(ISet<string>? blocked = null) => CompanionHeroes.Choose(player, [npc], new() { [91] = null }, blocked);
            var goal = Goal(); check(goal?.NpcId == npc.ObjectID, "弓箭手单独开放时未规划创建英雄");
            check(Goal(new HashSet<string> { goal!.Key }) == null, "英雄创建忽略不可达排除");
            player.Level = 21; check(Goal() == null, "英雄创建忽略最低等级"); player.Level = 22;
            Server.Settings.AllowNewHero = false; check(Goal() == null, "英雄创建忽略总开关"); Server.Settings.AllowNewHero = true;
            player.Info.Inventory[0] = new(new ItemInfo { Type = ItemType.SealedHero }); check(Goal() == null, "已持有封印仍重复创建"); player.Info.Inventory[0] = null;
            player.Info.Heroes[0] = new(new ClientPackets.NewHero()); check(Goal() == null, "已有英雄仍重复创建"); player.Info.Heroes[0] = null;
            var page = script.NPCPages[0]; page.SegmentList.Add(new NPCSegment(page, [], [], [], [], []) { ActList = [new(ActionType.TakeGold, "100")] });
            check(Goal() == null, "自动创建执行了尚未适配的自定义扣款剧情"); page.SegmentList.Clear();
            script.NPCPages = []; check(Goal() == null, "仅凭NPC名称而绕过真实创建入口");
        }
        finally
        {
            world.Scripts = scripts; world.ScriptIndex = scriptIndex; Server.Settings.AllowNewHero = enabled;
            Server.Settings.Hero_CanCreateClass = jobs; Server.Settings.Hero_RequiredLevel = minimum; Server.Settings.HeroSealItemName = sealName;
        }
    }

    private static async Task Session(Action<bool, string> check, string mode)
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var events = new ConcurrentQueue<WorkerEvent>();
        var profile = new CompanionProfile { Id = Guid.NewGuid(), Name = "HeroAck", AcceptGroup = false, Chat = false };
        var session = new CompanionSession(new(profile, "HeroAckAccount", "HeroAckPassword", true, "127.0.0.1", ((IPEndPoint)listener.LocalEndpoint).Port,
            "test", "hero-check", CompanionProtocol.Fingerprint, "", "", "", 1), e => { events.Enqueue(e); return Task.CompletedTask; }) { Paused = true };
        session.Begin(); using var peer = await listener.AcceptTcpClientAsync(timeout.Token); var stream = peer.GetStream();
        FieldInfo Field(string name) => typeof(CompanionSession).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!;
        var state = (GameState)Field("state").GetValue(session)!;
        async Task<bool> Maintain() => await (Task<bool>)typeof(CompanionSession).GetMethod("MaintainHero", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(session, [Environment.TickCount64])!;
        try
        {
            await Send(new S.Connected()); await Expect(ClientPacketIds.ClientVersion);
            await Send(new S.ClientVersion { Result = 1 }); await Expect(ClientPacketIds.Login);
            await Send(new S.LoginSuccess { Characters = [new SelectInfo { Index = 1, Name = profile.Name }] }); await Expect(ClientPacketIds.StartGame);
            await Send(new S.UserInformation { ObjectID = 1, Name = profile.Name, HP = 100, MP = 100, Inventory = new UserItem[80], Equipment = new UserItem[14], QuestInventory = [] });
            await Expect(ClientPacketIds.SwitchGroup);
            await Send(new S.MapInformation { MapIndex = 1, FileName = "Unused" });
            await Send(new S.ObjectNPC { ObjectID = 2, Name = "英雄使者", Location = new(0, 1) }); await Barrier();
            session.Knowledge = new("hero", 1, false, true, true, [], [], [], [], NoHero: false,
                Goal: new("service", 1, 0, 1, "创建英雄", NpcId: 2, Key: "hero-create/2")); Field("npcId").SetValue(session, 2u);
            var visited = (Dictionary<uint, (long Until, uint Gold)>)Field("visited").GetValue(session)!;
            visited[2] = (Environment.TickCount64 + 60000, 0);
            await Send(new S.NPCResponse { Page = ["<<创建英雄/@CreateHero/Green>> <<复活英雄/@ReviveHero/Red>>"] }); await Barrier();
            var interact = (Task<bool>)typeof(CompanionSession).GetMethod("TasksAndSupplies", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(session, [Environment.TickCount64])!;
            check(await interact, "实际彩色NPC对白没有触发创建英雄链接");
            using (var reader = new BinaryReader(new MemoryStream(await Expect(ClientPacketIds.CallNPC))))
            { check(reader.ReadUInt32() == 2 && reader.ReadString() == "[@CREATEHERO]", "彩色NPC链接把颜色后缀当成原页面名"); }
            await Send(new S.HeroCreateRequest { CanCreateClass = [false, false, false, false, true] }); await Barrier();
            check(await Maintain(), "原创建许可到达后未提交英雄创建");
            var bytes = await Expect(ClientPacketIds.NewHero);
            using (var reader = new BinaryReader(new MemoryStream(bytes)))
            { check(reader.ReadString().Length <= Globals.MaxCharacterNameLength && reader.ReadByte() == (byte)MirGender.Male && reader.ReadByte() == (byte)MirClass.Archer, "英雄名称过长或未遵守原职业开关"); }
            if (mode == "unknown")
            {
                while (!session.Finished) await Task.Delay(20, timeout.Token);
                check(events.Any(e => e.Kind == "failed" && !e.Recoverable && e.Reason.Contains("创建英雄回执未知")), "未知创建没有阻断自动重连/重建");
                check(await Read() == null, "未知英雄创建被重复发送"); return;
            }
            if (mode == "rejected")
            {
                await Send(new S.NewHero { Result = 7 }); await Barrier();
                check(!await Maintain() && !session.Finished && events.Any(e => e.Kind == "goal-blocked"), "明确拒绝仍重复创建，或无故停掉正常角色");
                return;
            }
            await Send(new S.NewHero { Result = 5 }); await Barrier();
            check(await Maintain(), "明确重名后没有进行有界换名"); await Expect(ClientPacketIds.NewHero);
            var seal = new ItemInfo { Index = 901, Type = ItemType.SealedHero };
            await Send(new S.NewItemInfo { Info = seal });
            await Send(new S.GainedItem { Item = new(seal) { UniqueID = 901, AddedStats = new Stats { [Stat.Hero] = 10 } } });
            await Send(new S.NewHero { Result = 10 }); await Barrier();
            check(await Maintain(), "创建成功后没有使用封印英雄"); await Expect(ClientPacketIds.UseItem);
            await Send(new S.UseItem { UniqueID = 901, Grid = MirGridType.HeroInventory, Success = true }); await Barrier();
            check((string?)Field("pendingAsset").GetValue(session) == "hero-seal", "英雄格回执误解锁主人封印激活");
            await Send(new S.UseItem { UniqueID = 901, Grid = MirGridType.Inventory, Success = true });
            await Send(new S.ManageHeroes { CurrentHero = new() { Index = 10, Name = "英雄回执", Class = MirClass.Archer, Level = 1 }, Heroes = [] });
            Field("moving").SetValue(session, true);
            await Send(new S.HeroInformation { ObjectID = 10, Name = "英雄回执", Class = MirClass.Archer, Level = 1, HP = 100, Inventory = [], Equipment = new UserItem[14], Magics = [] });
            await Send(new S.HeroRecovery { HeroIndex = 10, Milliseconds = 0 });
            await Send(new S.UpdateHeroSpawnState { State = HeroSpawnState.Summoned }); await Barrier();
            check((bool)Field("moving").GetValue(session)! && state.User!.ObjectID == 1, "英雄资料覆盖主人移动/身份"); Field("moving").SetValue(session, false);
            var weapon = new ItemInfo { Index = 902, Type = ItemType.Weapon, Durability = 1000 };
            await Send(new S.NewItemInfo { Info = weapon }); await Send(new S.GainedItem { Item = new(weapon) { UniqueID = 902, CurrentDura = 1000 } }); await Barrier();
            check(await Maintain(), "英雄空槽未从共用包换装"); await Expect(ClientPacketIds.EquipItem);
            await Send(new S.EquipItem { Grid = MirGridType.Inventory, UniqueID = 902, To = 0, Success = false }); await Barrier();
            check((string?)Field("pendingAsset").GetValue(session) == "hero-equip", "主人装备回执解锁了英雄资产请求");
            await Send(new S.EquipItem { Grid = MirGridType.HeroInventory, UniqueID = 902, To = 0, Success = true }); await Barrier();
            check(state.Hero!.Equipment[0]?.UniqueID == 902 && state.User!.Equipment[0] == null, "英雄穿戴没有归约到原英雄槽位");
            var skillBook = new ItemInfo { Index = 903, Type = ItemType.Book, Shape = (short)Spell.StraightShot };
            await Send(new S.NewItemInfo { Info = skillBook }); await Send(new S.GainedItem { Item = new(skillBook) { UniqueID = 903 } }); await Barrier();
            check(await Maintain(), "英雄没有学习共用包普通技能书"); await Expect(ClientPacketIds.UseItem);
            await Send(new S.UseItem { Grid = MirGridType.Inventory, UniqueID = 903, Success = false }); await Barrier();
            check((string?)Field("pendingAsset").GetValue(session) == "hero-use", "主人学书回执解锁英雄请求");
            await Send(new S.NewMagic { Hero = true, Magic = new ClientMagic { Name = "基本射术", Spell = Spell.StraightShot } });
            await Send(new S.UseItem { Grid = MirGridType.HeroInventory, UniqueID = 903, Success = true }); await Barrier();
            check(state.Hero.Magics.Any(magic => magic.Spell == Spell.StraightShot) && state.User!.Magics.Count == 0, "英雄学书覆盖了主人技能");
            await Send(new S.HeroBaseStatsInfo { Stats = new BaseStats(MirClass.Archer) { Stats = [new(Stat.HP) { Base = 100 }] } });
            await Send(new S.HeroHealthChanged { HP = 10 });
            var potion = new ItemInfo { Index = 904, Type = ItemType.Potion, Shape = 0, StackSize = 20, Stats = new Stats { [Stat.HP] = 30 } };
            await Send(new S.NewItemInfo { Info = potion }); await Send(new S.GainedItem { Item = new(potion) { UniqueID = 904, Count = 8 } }); await Barrier();
            session.Knowledge = session.Knowledge! with { NoDrug = false };
            check(!await Maintain(), "英雄吃掉了主人红药储备线以内的药品");
            await Send(new S.GainedItem { Item = new(potion) { UniqueID = 905, Count = 2 } }); await Barrier();
            check(await Maintain(), "英雄恢复10%血量后未使用富余红药"); await Expect(ClientPacketIds.UseItem);
            await Send(new S.UseItem { Grid = MirGridType.HeroInventory, UniqueID = 904, Success = true }); await Barrier();
            check(state.User!.Inventory.Where(item => item?.ItemIndex == 904).Sum(item => item.Count) == 9, "英雄药品回执没有从主人共用包扣除一次");
            await Send(new S.HeroHealthChanged { HP = 0 }); await Send(new S.UpdateHeroSpawnState { State = HeroSpawnState.Dead });
            await Send(new S.HeroRecovery { HeroIndex = 10, Milliseconds = 60000 }); await Barrier();
            check(!await Maintain(), "英雄死亡60秒内提前召唤");
            await Send(new S.HeroRecovery { HeroIndex = 10, Milliseconds = 0 }); await Barrier();
            session.Knowledge = session.Knowledge! with { NoHero = true }; check(!await Maintain(), "禁止英雄地图仍自动召唤");
            session.Knowledge = session.Knowledge! with { NoHero = false }; check(await Maintain(), "英雄冷却结束未召唤"); await Expect(ClientPacketIds.Chat);
            await Send(new S.UpdateHeroSpawnState { State = HeroSpawnState.Summoned }); await Barrier();
            check(Field("pendingAsset").GetValue(session) == null, "原英雄状态没有确认召唤结果");
        }
        finally { await session.Stop(); }
        Task Send(Packet packet) => stream.WriteAsync(packet.GetPacketBytes().ToArray(), timeout.Token).AsTask();
        async Task Barrier() { await Send(new S.GroupInvite { Name = "英雄回执屏障" }); await Expect(ClientPacketIds.GroupInvite); }
        async Task<byte[]> Expect(ClientPacketIds expected)
        { var packet = await Read(); check(packet?.Id == (short)expected, $"英雄检查期待{expected}，实际{packet?.Id}"); return packet!.Value.Body; }
        async Task<(short Id, byte[] Body)?> Read()
        {
            while (true)
            {
                var header = new byte[4]; try { await stream.ReadExactlyAsync(header, timeout.Token); } catch (EndOfStreamException) { return null; }
                var body = new byte[BitConverter.ToUInt16(header) - 4]; await stream.ReadExactlyAsync(body, timeout.Token);
                var id = BitConverter.ToInt16(header, 2); if (id != (short)ClientPacketIds.KeepAlive) return (id, body);
            }
        }
    }
}
