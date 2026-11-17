using System.Security.Cryptography;
using Mir2.Companions;
using Mir2.Hosting;
using Server.MirEnvir;

namespace Server.ConsoleHost;

public sealed partial class CompanionService(GameRuntime runtime, CompanionStore store, ProjectPaths paths) : BackgroundService
{
    private readonly CompanionWorker worker=new();
    private readonly Dictionary<Guid,LiveCompanion> live=[];
    private readonly SemaphoreSlim transport=new(1);
    private DateTimeOffset nextConnect;
    private bool handshaken;
    private string queueReason="等待名额";
    private string? runtimeError;
    private string runId=Guid.NewGuid().ToString("N");
    private readonly CompanionLlm llm=new();
    private readonly System.Collections.Concurrent.ConcurrentQueue<WorkerCommand> llmResults=new();
    private readonly HashSet<Guid> llmPending=[];
    private readonly Dictionary<Guid,DateTimeOffset> llmCooldown=[];
    private sealed class LiveCompanion
    {
        public string State="排队中",Reason="等待登录",Error="",Knowledge="";
        public KnownGuildPlan? Guild;
        public string Session=Guid.NewGuid().ToString("N");
        public DateTimeOffset Started=DateTimeOffset.UtcNow,Heartbeat=DateTimeOffset.UtcNow,ReservedUntil,NextKnowledge;
        public DateTimeOffset? StoppingAt;
        public bool WorkerStopped,Recovering,Entered;
        public Queue<CompanionEvent> Events=[];
        public KnownGoal? Goal;
        public Dictionary<string,DateTimeOffset> BlockedGoals=[];
    }
    public CompanionOverview Overview() => new(paths.VersionName,paths.VersionId,CompanionProtocol.Fingerprint,store.Settings,store.Presets,Capabilities(),live.Count,Limits(),store.Records.Values.Count(r=>r.Profile.Enabled),store.StartAllVersion,CompanionValidation.AvailableClasses,
        ArrivalStatus(),new(store.Records.Values.Count(r=>!live.ContainsKey(r.Profile.Id)&&Eligibility(r,DateTimeOffset.UtcNow).Allowed),
            store.Records.Values.Count(r=>r.Profile.Enabled&&r.RestUntil>DateTimeOffset.UtcNow),live.Values.Count(l=>l.StoppingAt!=null)));
    public int StartAll(CompanionCommand command)
    {
        if(command.Action!="start")throw new InvalidDataException("未知批量陪玩操作。");
        var count=store.StartAll(command.Version);runtimeError=null;return count;
    }
    private static string LocalHost()
    {
        var address=System.Net.IPAddress.Parse(Settings.IPAddress);
        return address.Equals(System.Net.IPAddress.Any)?"127.0.0.1":address.Equals(System.Net.IPAddress.IPv6Any)?"::1":address.ToString();
    }
    private CompanionLimits Limits()=>new(Settings.MaxUser,Settings.MaxIP,Envir.Main.Connections.Count,Envir.Main.Connections.Count(c=>c.IPAddress==LocalHost()),
        Math.Max(0,Math.Min(store.Settings.MaxOnline,Math.Min(Settings.MaxUser-store.Settings.ReservePlayers,Settings.MaxIP-1))));
    public CompanionPage List(int page=0,string? search=null)
    {
        var values=store.Records.Values.Where(r=>string.IsNullOrWhiteSpace(search)||$"{r.Profile.Name} {r.Profile.Alias} {r.Profile.Tags} {r.Profile.PresetKey}".Contains(search,StringComparison.OrdinalIgnoreCase)).OrderBy(r=>r.Profile.Name).ToArray();
        var queue=QueuePositions(DateTimeOffset.UtcNow);
        return new(values.Skip(Math.Clamp(page,0,10000)*20).Take(20).Select(r=>Row(r,queue)).ToArray(),values.Length);
    }
    private Dictionary<Guid,int> QueuePositions(DateTimeOffset now)=>CompanionStore.Waiting(store.Records.Values
        .Where(r=>!live.ContainsKey(r.Profile.Id)&&Eligibility(r,now).Allowed)).Select((r,i)=>(r.Profile.Id,Position:i+1)).ToDictionary(p=>p.Id,p=>p.Position);
    private CompanionRow Row(CompanionRecord r,IReadOnlyDictionary<Guid,int>? positions=null)
    {
        var now=DateTimeOffset.UtcNow; var c=Envir.Main.CharacterList.FirstOrDefault(c=>c.Index==r.CharacterId);
        var active=live.GetValueOrDefault(r.Profile.Id);
        var preview=CompanionScheduling.Preview(r.Profile.Schedule,r.Seed,now);
        var next=preview.FirstOrDefault(w=>w.Attending && w.End>now)?.Start;
        if(r.RestUntil>now)next=r.RestUntil;
        if(r.RecoveryAt!=null)next=r.RecoveryAt;
        var queue=(positions??QueuePositions(now)).TryGetValue(r.Profile.Id,out var position)?position:(int?)null;
        var reason=active?.Reason??Eligibility(r,now).Reason;
        if(active==null&&Eligibility(r,now).Allowed)reason=queueReason;
        var recoveryEnabled=store.Settings.Enabled&&store.Settings.SmartRecovery;
        if(active==null&&r.RecoveryAt!=null)reason=recoveryEnabled?$"智能托管第{r.RecoveryAttempts}次恢复，等待旧连接退出及重试时间；{r.Error}":$"智能托管已暂停，保留故障记录：{r.Error}";
        return new(store.Public(r),r.AccountId,r.CharacterId,c?.Level??0,c?.Player?.CurrentMap?.Info.Title??Envir.Main.GetMapInfo(c?.CurrentMapIndex??0)?.Title??"",
            active?.State??(r.RecoveryAt!=null?recoveryEnabled?"托管恢复中":"托管已暂停":r.Error.Length>0?"需处理":"离线"),reason,store.TodaySeconds(r,now),next,active?.Error??r.Error,
            c==null?null:c.Player?.CurrentLocation.X??c.CurrentLocation.X,c==null?null:c.Player?.CurrentLocation.Y??c.CurrentLocation.Y,
            r.SessionUntil,r.RestUntil,queue,r.LastSessionReason);
    }
    public CompanionDetail Detail(Guid id)
    {
        var r=store.Get(id);var active=live.GetValueOrDefault(id);var player=Envir.Main.CharacterList.FirstOrDefault(c=>c.Index==r.CharacterId)?.Player;
        var state=player==null?null:new CompanionRuntimeState(player.Account.Gold,player.HP,player.Stats[Stat.HP],player.MP,player.Stats[Stat.MP],player.Experience,player.MaxExperience,active?.Goal,
            player.CurrentQuests.SelectMany(q=>new[]{q.Info.Name+(q.Completed?" · 可交付":"")}.Concat(q.TaskList)).Take(100).ToArray(),
            player.CurrentBagWeight,player.Stats[Stat.BagWeight],player.Info.Inventory.Count(item=>item==null),player.MyGuild?.Name??"",active?.Guild?.Reason??"",CompanionCombatReader.Read(player));
        return new(Row(r),CompanionScheduling.Preview(r.Profile.Schedule,r.Seed,DateTimeOffset.UtcNow),active?.Events.ToArray()??[],state);
    }
    public CompanionProfile Save(CompanionProfile p)
    {
        store.Ready();if(p.Id==Guid.Empty)p=p with{Id=Guid.NewGuid()};
        CompanionValidation.Profile(p,store);
        if(store.Records.Count>=500&&!store.Records.ContainsKey(p.Id))throw new InvalidOperationException("本版本最多保存 500 个陪玩档案。");
        if(store.Records.TryGetValue(p.Id,out var old)&&live.ContainsKey(p.Id))
        {
            if(old.Profile.Class!=p.Class||old.Profile.Gender!=p.Gender||old.Profile.Name!=p.Name)throw new InvalidOperationException("角色创建期间不能修改身份，请先下线。");
        }
        store.SaveProfile(p,"c"+Convert.ToHexString(RandomNumberGenerator.GetBytes(6)).ToLowerInvariant(),Convert.ToHexString(RandomNumberGenerator.GetBytes(7)));
        return store.Public(store.Get(p.Id));
    }
    public CompanionProfile Create(CompanionProfile p)
    {
        if(store.Records.TryGetValue(p.Id,out var r))
        {
            if(AdminChanges.Version(p with{Version=""})!=AdminChanges.Version(r.Profile))throw new InvalidOperationException("创建编号已被不同的陪玩资料使用。");
            return store.Public(r);
        }
        return Save(p);
    }
    public void Command(Guid id,CompanionCommand command)
    {
        var r=store.Get(id);AdminChanges.Match(store.Public(r).Version,command.Version);
        if(command.Action is not("start"or"pause"or"resume"or"stop"or"release"))throw new InvalidDataException("未知陪玩操作。");
        if(command.Action=="start"&&r.CharacterId==0&&!CompanionValidation.CanCreateClass(r.Profile.Class))throw new InvalidOperationException("当前版本未开放此职业创建，请先修改陪玩职业。");
        if(command.Action=="release")
        {
            if(live.ContainsKey(id)||Envir.Main.GetAccount(r.Account)?.Connection!=null)throw new InvalidOperationException("先下线并等待会话结束，再解除托管。");
            store.Remove(id);return;
        }
        store.Mutate(r,n=>
        {
            n.Desired=command.Action switch{"start"or"resume"=>"run","pause"=>"pause",_=>"off"};
            if(command.Action=="start"){n.Profile=n.Profile with{Enabled=true};n.Error="";n.RecoveryAt=null;n.RecoveryAttempts=0;n.RecoveryDisabled=false;n.SessionDeaths=0;n.SuppressedUntil=null;n.RestUntil=null;if(!live.ContainsKey(id)){n.SessionUntil=null;n.SessionStarted=null;n.QueueSince=null;}}
            if(command.Action=="stop"){n.RecoveryDisabled=true;n.RecoveryAt=null;}
            if(command.Action=="resume"&&n.Profile.Enabled)n.RecoveryDisabled=false;
            if(command.Action=="stop")n.SuppressedUntil=CompanionScheduling.Preview(n.Profile.Schedule,n.Seed,DateTimeOffset.UtcNow)
                .FirstOrDefault(w=>w.Start<=DateTimeOffset.UtcNow&&w.End>DateTimeOffset.UtcNow)?.End??DateTimeOffset.UtcNow.AddMinutes(n.Profile.Schedule.RestMinutes);
        },true);
        if(command.Action=="start")runtimeError=null;
    }
    private CompanionCapability[] Capabilities() =>
    [
        new("运行监管",runtimeError==null?"可用":"阻断",runtimeError??(store.Settings.SmartRecovery?"独立进程、20秒控制租约、30秒心跳；技术故障自动清理退避，资产/身份等保护阻断仍需人工处理。":"独立进程、20秒控制租约、30秒心跳；故障停止陪玩，保留游戏宿主。")),
        new("智能托管",store.Settings.SmartRecovery?"已启用":"未启用",$"每30秒检查，{store.Records.Values.Count(r=>r.RecoveryAt!=null&&!CompanionSupervision.Death(r))}名等待技术恢复，{store.Records.Values.Count(CompanionSupervision.Death)}名等待死亡恢复；名额不足时先技术后死亡，核对旧连接和安全复活点，记录历史后重开死亡预算，保留人工意愿和资产未知等保护。"),
        new("版本隔离/人格/日程",store.LoadError==null?"可用":"阻断",store.LoadError??"全部资料与用量保存在当前版本 SQLite；人格修订固定。"),
        new("独立 TCP 客户端",CompanionWorker.Installed?"可用":"阻断","正常注册、创建角色与登录。"),
        new("真实成长","可用","服务器提供NPC/任务/刷新点与实际掉落来源，规则规划接取、寻怪收集和返回交付；支持战斗、拾取、换装、学书、补给与修理。"),
        new("背包仓库","受限","普通余料及同定义重复防具/首饰经NPC和密码校验暂存；留最好1件备装，戒指/手镯留2件，材料可安全取回至5份；任务/特殊物不动，人工密码不重置。"),
        new("剧情与跨图","受限","按当前版本编号规划公开入口步行路线；支持击杀/掉落收集，隐藏剧情和特殊入口仍需适配。无进度保持在线重新规划。"),
        new("组队/聊天","可用","陪玩自主邀请，优先同行会和互补职业；原队伍回执确认后跟随公开跨图路线、协助合法怪物和治疗队友，补给/筹资及任务交付优先。"),
        new("行会发展/攻城","受限","可选主动入会/自主建会，按NPC、等级、费用、材料与权限执行；开启PK且有资格时参与已开战城池、攻击合法目标和王宫/据点驻守，不自动宣战或操作行会资产。"),
        new("PK","可用","总开关默认关闭；开启后仅短时红褐模式自卫或收到点名切磋的30秒约战，低血撤退。"),
        new("玩家交易/行会资产","受限","尚未接入自动报价/确认和行会资产操作；陪玩不自动发起。"),
        new("LLM 增强",store.Settings.Models.Any(m=>m.Enabled)?"可用":"未配置，使用规则","多模型独立密钥/角色选择与连接测试；5秒超时、每日请求额度、无效输出回落规则。")
    ];
}
