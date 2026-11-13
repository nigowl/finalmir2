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
        public string Session=Guid.NewGuid().ToString("N");
        public DateTimeOffset Started=DateTimeOffset.UtcNow,Heartbeat=DateTimeOffset.UtcNow,ReservedUntil,NextKnowledge;
        public Queue<CompanionEvent> Events=[];
        public KnownGoal? Goal;
        public Dictionary<string,DateTimeOffset> BlockedGoals=[];
    }
    public CompanionOverview Overview() => new(paths.VersionName,paths.VersionId,CompanionProtocol.Fingerprint,store.Settings,store.Presets,Capabilities(),live.Count,Limits(),store.Records.Values.Count(r=>r.Profile.Enabled),store.StartAllVersion,CompanionValidation.AvailableClasses);
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
        return new(values.Skip(Math.Clamp(page,0,10000)*20).Take(20).Select(Row).ToArray(),values.Length);
    }
    private CompanionRow Row(CompanionRecord r)
    {
        var now=DateTimeOffset.UtcNow; var c=Envir.Main.CharacterList.FirstOrDefault(c=>c.Index==r.CharacterId);
        var active=live.GetValueOrDefault(r.Profile.Id);
        var preview=CompanionScheduling.Preview(r.Profile.Schedule,r.Seed,now);
        var next=preview.FirstOrDefault(w=>w.Attending && w.End>now)?.Start;
        var reason=active?.Reason??Eligibility(r,now).Reason;
        if(active==null&&Eligibility(r,now).Allowed)reason=queueReason;
        return new(store.Public(r),r.AccountId,r.CharacterId,c?.Level??0,c?.Player?.CurrentMap?.Info.Title??Envir.Main.GetMapInfo(c?.CurrentMapIndex??0)?.Title??"",
            active?.State??(r.Error.Length>0?"需处理":"离线"),reason,store.TodaySeconds(r,now),next,active?.Error??r.Error,
            c==null?null:c.Player?.CurrentLocation.X??c.CurrentLocation.X,c==null?null:c.Player?.CurrentLocation.Y??c.CurrentLocation.Y);
    }
    public CompanionDetail Detail(Guid id)
    {
        var r=store.Get(id);var active=live.GetValueOrDefault(id);var player=Envir.Main.CharacterList.FirstOrDefault(c=>c.Index==r.CharacterId)?.Player;
        var state=player==null?null:new CompanionRuntimeState(player.Account.Gold,player.HP,player.Stats[Stat.HP],player.MP,player.Stats[Stat.MP],player.Experience,player.MaxExperience,active?.Goal,
            player.CurrentQuests.SelectMany(q=>new[]{q.Info.Name+(q.Completed?" · 可交付":"")}.Concat(q.TaskList)).Take(100).ToArray(),
            player.CurrentBagWeight,player.Stats[Stat.BagWeight],player.Info.Inventory.Count(item=>item==null));
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
            if(command.Action=="start"){n.Profile=n.Profile with{Enabled=true};n.Error="";n.SuppressedUntil=null;n.RestUntil=null;}
            if(command.Action=="stop")n.SuppressedUntil=CompanionScheduling.Preview(n.Profile.Schedule,n.Seed,DateTimeOffset.UtcNow)
                .FirstOrDefault(w=>w.Start<=DateTimeOffset.UtcNow&&w.End>DateTimeOffset.UtcNow)?.End??DateTimeOffset.UtcNow.AddMinutes(n.Profile.Schedule.RestMinutes);
        },true);
        if(command.Action=="start")runtimeError=null;
    }
    private CompanionCapability[] Capabilities() =>
    [
        new("运行监管",runtimeError==null?"可用":"阻断",runtimeError??"独立进程、20秒控制租约、30秒心跳；故障停止陪玩，保留游戏宿主。"),
        new("版本隔离/人格/日程",store.LoadError==null?"可用":"阻断",store.LoadError??"全部资料与用量保存在当前版本 SQLite；人格修订固定。"),
        new("独立 TCP 客户端",CompanionWorker.Installed?"可用":"阻断","与原 Shared 协议同包构建；正常注册、创建与登录。"),
        new("真实成长","可用","服务器提供NPC/任务/刷新点与实际掉落来源，规则规划接取、寻怪收集和返回交付；原TCP战斗/拾取、换装/学书、补给与修理。"),
        new("剧情与跨图","受限","按当前版本编号规划公开入口步行路线；支持击杀/掉落收集，隐藏剧情和特殊入口仍需适配。无进度保持在线重新规划。"),
        new("组队/聊天","可用","按人格邀请/接受组队、跟随可见队长、已学治疗辅助与有限模板回应。"),
        new("PK","可用","总开关默认关闭；开启后仅短时红褐模式自卫或收到点名切磋的30秒约战，低血撤退。"),
        new("玩家交易/行会资产","受限","尚未接入自动报价/确认和行会资产操作；陪玩不自动发起。"),
        new("LLM 增强",store.Settings.Models.Any(m=>m.Enabled)?"可用":"未配置，使用规则","多模型独立密钥/角色选择与连接测试；5秒超时、每日请求额度、无效输出回落规则。")
    ];
}
