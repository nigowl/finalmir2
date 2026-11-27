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
            await Send(new S.UserInformation { ObjectID = 1, Name = profile.Name, Level = 30, HP = 100, MP = 100, Inventory = new UserItem[80], Equipment = new UserItem[14], QuestInventory = [] });
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
            await Send(new S.NewItemInfo { Info = potion }); await Send(new S.GainedItem { Item = new(potion) { UniqueID = 904, Count = 2 } }); await Barrier();
            session.Knowledge = session.Knowledge! with { NoDrug = false };
            check(await Maintain(), "低血且无法安全分药时英雄没有转为跟随");await Expect(ClientPacketIds.SetHeroBehaviour);
            await Send(new S.SetHeroBehaviour{Behaviour=HeroBehaviour.Follow});await Barrier();
            check(!await Maintain(), "陪玩不应绕过引擎自动喝药再次发药品请求");
            await Send(new S.GainedItem { Item = new(potion) { UniqueID = 905, Count = 2 } }); await Barrier();
            check(!await Maintain(), "陪玩对引擎管理的英雄药品重复发请求");
            await Send(new S.UseItem { Grid = MirGridType.HeroInventory, UniqueID = 904, Success = true }); await Barrier();
            check(state.User!.Inventory.Where(item => item?.ItemIndex == 904).Sum(item => item.Count) == 3, "英雄药品回执没有从主人共用包扣除一次");
            await Send(new S.HeroHealthChanged{HP=80});await Barrier();
            check(await Maintain(),"英雄恢复后没有重新参与战斗");await Expect(ClientPacketIds.SetHeroBehaviour);
            await Send(new S.SetHeroBehaviour{Behaviour=HeroBehaviour.Attack});await Barrier();
            var appraisalSlot=Array.IndexOf(state.User!.Inventory,null);
            var pending=state.Hero.Equipment[0]!;pending.Identified=false;pending.EquipmentQuality=EquipmentGrade.Fine;
            session.Knowledge=session.Knowledge! with{Goal=new("service",1,0,1,"鉴定英雄装备",NpcId:2,Key:"appraise/902",AppraisalItemId:902)};
            await Send(new S.GainedGold{Gold=10000});
            await Send(new S.NPCResponse{Page=["<鉴定装备/@APPRAISE>"]});await Barrier();
            async Task<bool> Appraise()=>await (Task<bool>)typeof(CompanionSession).GetMethod("AppraiseEquipment",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(session,[])!;
            check(await Appraise(),"已穿英雄装备没有先卸下进行鉴定");await Expect(ClientPacketIds.RemoveItem);
            await Send(new S.RemoveItem{Grid=MirGridType.Inventory,UniqueID=902,To=appraisalSlot,Success=false});await Barrier();
            check((string?)Field("pendingAsset").GetValue(session)=="hero-remove","主人卸装回执解锁了英雄卸装");
            await Send(new S.RemoveItem{Grid=MirGridType.HeroInventory,UniqueID=902,To=appraisalSlot,Success=true});await Barrier();
            check(state.Hero.Equipment[0]==null&&state.User!.Inventory[appraisalSlot]?.UniqueID==902,"英雄卸装没有保留原编号进入主人包");
            check(await Appraise(),"没有使用当前NPC实际鉴定入口");await Expect(ClientPacketIds.CallNPC);
            await Send(new S.NPCResponse{Page=["<装备/@APPRAISESELECT_902>"]});await Barrier();
            check(await Appraise(),"没有使用原装备的实际报价选项");await Expect(ClientPacketIds.CallNPC);
            await Send(new S.NPCResponse{Page=["<确认鉴定/@APPRAISECONFIRM_902>","<取消/@APPRAISE>"]});await Barrier();
            check(await Appraise(),"没有提交原NPC二次确认");await Expect(ClientPacketIds.CallNPC);
            await Send(new S.EquipmentAppraisalResult{UniqueID=999,Success=true});await Barrier();
            check((string?)Field("pendingAsset").GetValue(session)=="appraise","其他装备鉴定回执误解锁请求");
            var identified=state.User!.Inventory[appraisalSlot]!.Clone();identified.Identified=true;identified.AddedStats[Stat.MaxMC]=1;
            await Send(new S.RefreshItem{Item=identified});await Send(new S.EquipmentAppraisalResult{UniqueID=902,Success=true});await Barrier();
            check(Field("pendingAsset").GetValue(session)==null&&state.User.Inventory[appraisalSlot]!.Identified,"鉴定回执没有确认原装备状态");
            check(await Maintain(),"英雄鉴定后没有重新穿回原装备");await Expect(ClientPacketIds.EquipItem);
            await Send(new S.EquipItem{Grid=MirGridType.HeroInventory,UniqueID=902,To=0,Success=true});await Barrier();
            check(state.Hero.Equipment[0]?.UniqueID==902&&state.Hero.Equipment[0].Identified,"鉴定后重穿改变了英雄装备编号");
            state.Hero.Equipment[0].MaxDura=1000;state.Hero.Equipment[0].CurrentDura=100;
            session.Knowledge=session.Knowledge! with{Npcs=[new(1,2,1,0,1,"修理师",[],[(int)ItemType.Weapon])],
                Goal=new("service",1,0,1,"修理英雄装备",NpcId:2,Key:"repair/2")};
            Field("repairOnly").SetValue(session,true);
            await Send(new S.NPCRepair{Rate=1});await Barrier();
            async Task<bool> Repair()=>await (Task<bool>)typeof(CompanionSession).GetMethod("TasksAndSupplies",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(session,[Environment.TickCount64])!;
            check(await Repair(),"低耐久英雄装备没有从原英雄槽卸下修理");await Expect(ClientPacketIds.RemoveItem);
            await Send(new S.RemoveItem{Grid=MirGridType.HeroInventory,UniqueID=902,To=appraisalSlot,Success=true});await Barrier();
            check(await Repair(),"英雄装备卸下后没有继续原NPC修理");await Expect(ClientPacketIds.RepairItem);
            await Send(new S.ItemRepaired{UniqueID=902,CurrentDura=1000,MaxDura=1000});await Expect(ClientPacketIds.EquipItem);
            check((string?)Field("pendingAsset").GetValue(session)=="hero-equip","英雄修理后误穿到主人槽位");
            await Send(new S.EquipItem{Grid=MirGridType.HeroInventory,UniqueID=902,To=0,Success=true});await Barrier();
            check(state.Hero.Equipment[0]?.UniqueID==902&&state.Hero.Equipment[0].CurrentDura==1000,"英雄修理没有保留原编号和耐久");
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
                var id = BitConverter.ToInt16(header, 2);
                if (id != (short)ClientPacketIds.KeepAlive && id != (short)ClientPacketIds.ItemPricesRequest) return (id, body);
            }
        }
    }
}
