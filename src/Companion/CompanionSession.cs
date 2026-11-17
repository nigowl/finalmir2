using System.Diagnostics;
using Crystal.Native;
using Mir2.Companions;
using C = ClientPackets;
using S = ServerPackets;

namespace Mir2.Companion;

public sealed partial class CompanionSession
{
    private readonly WorkerStart start;
    private readonly Func<WorkerEvent, Task> report;
    private readonly GameConnection connection = new();
    private readonly GameState state = new();
    private readonly CancellationTokenSource cancellation = new();
    private Task? run;
    private readonly System.Collections.Concurrent.ConcurrentQueue<WorkerCommand> controls=new();
    private long nextAction, nextReport;
    private string lastState = "", lastReason = "";
    private int deaths;
    private bool serverDisconnect;
    private long lastControl=Environment.TickCount64;
    public CompanionProfile Profile { get; set; }
    private CompanionKnowledge? latestKnowledge;
    public CompanionKnowledge? Knowledge
    {
        // 私有管道与游戏TCP独立到达；保留提前送达的快照，仅在原地图编号匹配后启用。
        get => state.Map!=null && latestKnowledge?.MapId==state.Map.MapIndex ? latestKnowledge : null;
        set => latestKnowledge=value;
    }
    public bool Paused { get; set; }
    private bool retiring;
    public bool Finished=>run?.IsCompleted==true;
    public CompanionSession(WorkerStart start, Func<WorkerEvent, Task> report)
    { this.start = start; this.report = e=>report(e with{Session=start.Run}); Profile = start.Profile; random = new(start.Seed); deaths=start.Deaths; }
    public void Begin() => run = Run();
    public void Control(WorkerCommand command)=>controls.Enqueue(command);
    public async Task Stop()
    {
        try{if(connection.Connected)await connection.Send(new C.LogOut());}
        finally{cancellation.Cancel(); connection.Dispose();}
        if (run != null) await run;
        cancellation.Dispose();
    }
    private async Task Status(string status, string reason, bool force = false)
    {
        if (!force && status == lastState && reason == lastReason) return;
        lastState = status; lastReason = reason;
        await report(new("status", Profile.Id, status, reason));
    }
    private async Task Run()
    {
        try
        {
            await Status("连接中", "等待原协议握手");
            await connection.Connect(start.Host, start.Port);
            await Wait<S.Connected>(); await connection.Send(new C.ClientVersion { VersionHash = [] });
            if ((await Wait<S.ClientVersion>()).Result != 1) throw new InvalidDataException("原游戏协议握手失败。");
            if (!start.Registered)
            {
                await connection.Send(new C.NewAccount { AccountID = start.Account, Password = start.Password });
                var created = await Wait<S.NewAccount>();
                if (created.Result is not (8 or 7)) throw new InvalidOperationException($"原服拒绝注册（{created.Result}），不会自动重试注册。");
            }
            await connection.Send(new C.Login { AccountID = start.Account, Password = start.Password });
            var login = await Wait<S.LoginSuccess>();
            await report(new("registered", Profile.Id));
            var character = login.Characters.SingleOrDefault(x => x.Name == Profile.Name);
            if (character == null)
            {
                if (login.Characters.Count != 0) throw new InvalidDataException("账户已有其他角色，停止自动认领。");
                await connection.Send(new C.NewCharacter { Name = Profile.Name, Class = (MirClass)Profile.Class, Gender = (MirGender)Profile.Gender });
                character = (await Wait<S.NewCharacterSuccess>()).CharInfo;
            }
            if ((int)character.Class != Profile.Class || (int)character.Gender != Profile.Gender) throw new InvalidDataException("角色身份与档案不匹配。");
            await connection.Send(new C.StartGame { CharacterIndex = character.Index });
            await Wait<S.UserInformation>(); await report(new("entered", Profile.Id));
            await connection.Send(new C.SwitchGroup { AllowGroup = Profile.AcceptGroup });
            await Status("运行中", "根据本人状态规划");
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(100));
            while (await timer.WaitForNextTickAsync(cancellation.Token))
            {
                while(controls.TryDequeue(out var control))
                {
                    lastControl=Environment.TickCount64;
                    if(control.Kind=="update"&&control.Profile!=null)
                    {
                        if(Profile.AcceptGroup!=control.Profile.AcceptGroup)await connection.Send(new C.SwitchGroup{AllowGroup=control.Profile.AcceptGroup});
                        if(Profile.LlmMode!=control.Profile.LlmMode||Profile.LlmModelId!=control.Profile.LlmModelId)InvalidateSuggestions();
                        if(Profile.Version!=control.Profile.Version)await report(new("configured",Profile.Id,Text:control.Profile.Version));
                        Profile=control.Profile;
                    }
                    else if(control.Kind=="knowledge")Knowledge=control.Knowledge;
                    else if(control.Kind=="pause")Paused=true;
                    else if(control.Kind=="resume")Paused=false;
                    else if(control.Kind=="retire")retiring=true;
                    else if(control.Kind=="llm")LlmResult(control.Context,control.Text);
                    else if(control.Kind=="goal")GoalResult(control.Context,control.Text);
                }
                while (connection.Received.TryDequeue(out var packet)) await Apply(packet);
                if(logoutConfirmed)return;
                if (connection.Errors.TryDequeue(out var error)) throw new IOException(error);
                if (!connection.Connected) throw new IOException("连接已断开。");
                var now = Environment.TickCount64;
                if(now-lastControl>20000)throw new TimeoutException("宿主监管租约超时，已主动下线。");
                await PendingSpeech(now);
                if (now >= nextReport) { nextReport = now + 5000; await report(new("heartbeat", Profile.Id, lastState, lastReason)); }
                // 保命独立于走路、施法及任务等待；不重置发展动作的原冷却。
                if (await ProtectItems(now)) continue;
                if (now >= nextAction && state.User != null) await Think(now);
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception e) { await report(new("failed", Profile.Id, "已停止", e.Message, Recoverable:CompanionSafety.CanReconnect(state,e,deaths,Profile.DeathLimit,
            pendingAsset!=null||protection.PendingItem!=0||protection.Paused||repairItem!=0||guildNameRequested||logoutSent!=0,serverDisconnect))); }
        finally
        {
            connection.Dispose();cancellation.Cancel();
            if(mapLoad!=null)
            {
                try{await mapLoad;}
                catch(OperationCanceledException)when(cancellation.IsCancellationRequested){}
                catch(Exception error){await report(new("diagnostic",Profile.Id,Reason:"地图读取已终止："+error.Message));}
            }
            await report(new("stopped", Profile.Id, "离线", "客户端会话已结束"));
        }
    }
    private async Task<T> Wait<T>() where T : Packet
    {
        var watch = Stopwatch.StartNew();
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(20));
        do
        {
            cancellation.Token.ThrowIfCancellationRequested();
            while (connection.Received.TryDequeue(out var packet))
            {
                await Apply(packet); if (packet is T match) return match;
                if (packet is S.Login p) throw new InvalidOperationException($"原服拒绝登录（{p.Result}），请核对账号或登录开关。");
                if (packet is S.NewCharacter c) throw new InvalidOperationException($"原服拒绝创建角色（{c.Result}）。");
            }
            if (connection.Errors.TryDequeue(out var error)) throw new IOException(error);
            if (watch.Elapsed > TimeSpan.FromSeconds(15)) throw new TimeoutException("等待原协议回执超时，停止操作并保留创建意图。");
        } while (await timer.WaitForNextTickAsync(cancellation.Token));
        throw new IOException("连接已结束。");
    }
    private async Task Apply(Packet packet)
    {
        if (packet is S.Disconnect d) {serverDisconnect=true;throw new IOException(d.Reason == 1 ? "账户被其他连接占用，已停止自动登录。" : $"原服断开连接（{d.Reason}）。");}
        var townResult=packet is S.UseItem {Grid:MirGridType.Inventory} itemResult && itemResult.UniqueID==protection.PendingItem && protection.PendingScrollShape==1;
        var protectionResult=packet is S.UseItem {Grid:MirGridType.Inventory} used && used.UniqueID==protection.PendingItem && protection.PendingItem!=0;
        var protectedItem=protectionResult?state.User?.Inventory.FirstOrDefault(item=>item?.UniqueID==protection.PendingItem):null;
        state.Apply(packet); protection.Apply(packet, Environment.TickCount64);
        if(protectionResult && packet is S.UseItem {Success:false})
            await report(new("protection-rejected",Profile.Id,Reason:protection.Notice,
                Text:$"物品 {protectedItem?.ItemIndex}/{protectedItem?.Info?.Name}，唯一编号 {protectedItem?.UniqueID}，生命 {state.User?.HP}/{state.CalculateStats()[Stat.HP]}，魔法 {state.User?.MP}，地图 {state.Map?.MapIndex}，坐标 {state.User?.Location}，已死亡 {state.Me?.Dead}，禁药 {Knowledge?.NoDrug}，禁回城 {Knowledge?.NoTownTeleport}"));
        await SkillPacket(packet);
        if(await RetirementPacket(packet))return;
        StoragePacket(packet);
        GuildPacket(packet);
        if(townResult && packet is S.UseItem town)
            await report(new("escape",Profile.Id,Text:town.Success?"原服确认使用回城卷轴；按新地图重新规划":"原服拒绝回城卷轴，已停止自动保护，不会重发"));
        if (packet is S.Magic { Spell:Spell.Healing, Cast:true } healing && healing.TargetID==state.User?.ObjectID)
            await report(new("healing",Profile.Id,Text:$"原服确认自疗，生命 {state.User.HP}，魔法 {state.User.MP}"));
        if(packet is S.GainedItem or S.GainedGold && Knowledge!=null)Knowledge=Knowledge with{LootTargets=[]};
        if(packet is S.ChangeQuest or S.CompleteQuest or S.GainedQuestItem or S.GainedItem)lastProgress=Environment.TickCount64;
        await ApplyAssetResult(packet);
        if (packet is S.Death)
        {
            deaths++; nextAction = Environment.TickCount64 + 3000;InvalidateSuggestions();
            await report(new("death",Profile.Id,Reason:$"本次会话死亡 {deaths}/{Profile.DeathLimit}",
                Text:$"地图 {Knowledge?.MapId}，坐标 {state.User?.Location}，金币 {state.User?.Gold}，红药 {PotionCount(true)}，蓝药 {PotionCount(false)}，目标 {Knowledge?.Goal?.Key}，"+
                    $"近期攻击者 {string.Join('、',monsterAttackers.Where(p=>Environment.TickCount64-p.Value<10000).Select(p=>state.Actors.GetValueOrDefault(p.Key)?.Name??p.Key.ToString()))}，"+
                    $"武器 {state.User?.Equipment[0]?.ItemIndex}，技能 {string.Join(',',state.User!.Magics.Select(m=>m.Spell))}"));
        }
        if (packet is S.MapInformation or S.MapChanged) { CloseStorage();map = null; mapName = ""; moving = false;lastMoved=0; pendingNpc = npcId = 0; goods = null;repairItem=0;repairSlot=-1;repairPage=sellPage=false;page=[];monsterAttackers.Clear();combatTarget=monsterAttacker=0;InvalidateSuggestions(); }
        if (packet is S.AddMember or S.DeleteMember or S.DeleteGroup or S.GroupInvite) InvalidateSuggestions();
        if(packet is S.AddMember or S.DeleteMember or S.DeleteGroup)
            await report(new("party",Profile.Id,Text:"原服确认当前队伍："+string.Join('、',state.GroupMembers)));
        if(packet is S.AddMember joined && joined.Name!=state.User?.Name && state.GroupMembers.FirstOrDefault()==state.User?.Name)
            nextInvite=Math.Min(nextInvite,Environment.TickCount64+5000);
        if (packet is S.UserLocation location)
        {
            lastMoved=moving&&location.Location==moveDestination?moveSent:0;
            if(moving&&lastMoved==0)navigation.Clear();
            moving=false;
        }
        if (packet is S.UserInformation) { moving=false;lastMoved=0; }
        if (packet is S.NPCResponse npc) { page = npc.Page; pendingNpc = 0; }
        if (packet is S.NPCGoods shop) { goods = shop; pendingNpc = 0; foreach (var item in shop.List) state.Bind(item); }
        if (packet is S.NPCRepair repair) { repairPage = true; repairRate=repair.Rate; pendingNpc = 0; }
        if (packet is S.NPCSell) { sellPage = true; pendingNpc = 0; }
        await Social(packet);
        var attacker=packet is S.Struck struck?struck.AttackerID:packet is S.ObjectStruck hit&&hit.ObjectID==state.User?.ObjectID?hit.AttackerID:0;
        if(attacker!=0&&state.Actors.GetValueOrDefault(attacker) is {Kind:ObjectType.Monster,MasterObjectId:0})
        {
            monsterAttacker=attacker;monsterAttackedAt=Environment.TickCount64;
            if(monsterAttackers.Count>=8&&!monsterAttackers.ContainsKey(attacker))monsterAttackers.Remove(monsterAttackers.MinBy(p=>p.Value).Key);
            monsterAttackers[attacker]=monsterAttackedAt;
        }
        if(attacker!=0&&state.Actors.GetValueOrDefault(attacker)?.Kind==ObjectType.Player)
        {aggressor=attacker;aggressorUntil=Environment.TickCount64+15000;}
        if(packet is S.ChangeAMode mode)attackMode=mode.Mode;
    }
}
