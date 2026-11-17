using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Mir2.Companions;
using Mir2.Hosting;

namespace Server.ConsoleHost;

public sealed record CompanionRecord
{
    public CompanionProfile Profile { get; set; } = new();
    public string Account { get; set; } = "";
    public string Password { get; set; } = "";
    public int Seed { get; set; }
    public int AccountId { get; set; }
    public int CharacterId { get; set; }
    public bool Registered { get; set; }
    public string Desired { get; set; } = "off";
    public DateTimeOffset? SuppressedUntil { get; set; }
    public DateTimeOffset? RestUntil { get; set; }
    public DateTimeOffset? QueueSince { get; set; }
    public DateTimeOffset? SessionUntil { get; set; }
    public DateTimeOffset? SessionStarted { get; set; }
    public int SessionRestMinutes { get; set; }
    public int SessionNumber { get; set; }
    public string LastSessionReason { get; set; } = "";
    public Dictionary<long, int> OnlineSeconds { get; set; } = [];
    public string Error { get; set; } = "";
    public DateTimeOffset? RecoveryAt { get; set; }
    public int RecoveryAttempts { get; set; }
    public bool RecoveryDisabled { get; set; }
    public int SessionDeaths { get; set; }
}
public sealed record CompanionConfiguration(CompanionSettings Settings, string LlmKey, string Instance, DateTimeOffset LlmDay, int LlmCalls,
    Dictionary<Guid,string>? ModelKeys = null, CompanionArrivalState? Arrivals = null);
public sealed record CompanionEnvelope<T>(int Schema, T Value);

// 所有调用都在游戏线程，工作进程不打开 SQLite。
public sealed partial class CompanionStore
{
    private readonly SqliteDatabaseStorage storage;
    private readonly AdminChanges changes;
    private readonly ProjectPaths paths;
    private bool legacyFormat;
    private static readonly JsonSerializerOptions Strict=new(){UnmappedMemberHandling=JsonUnmappedMemberHandling.Disallow};
    public Dictionary<Guid, CompanionRecord> Records { get; } = [];
    public List<PersonalityPreset> CustomPresets { get; } = [];
    public CompanionConfiguration Configuration { get; private set; }
    public string? LoadError { get; private set; }
    public CompanionStore(SqliteDatabaseStorage storage, AdminChanges changes, ProjectPaths paths)
    {
        this.storage = storage; this.changes = changes; this.paths = paths;
        Configuration = new(new(), "", paths.VersionId, DateTimeOffset.UtcNow.Date, 0);
        try
        {
            var configuration=Read("companions/settings.json", Configuration);
            if(configuration?.Settings==null||configuration.LlmKey==null||configuration.Instance==null)throw new InvalidDataException("陪玩设置为空。");
            Configuration=CompanionModels.Upgrade(configuration);
            CompanionValidation.Settings(new(Configuration.Settings));
            var presets=Read("companions/presets.json", Array.Empty<PersonalityPreset>());
            if(presets==null||presets.Any(p=>p==null||p.Key==null||p.Name==null||p.Traits==null))throw new InvalidDataException("人格修订内容无效。");
            CustomPresets.AddRange(presets);
            foreach (var key in storage.List("companions/profiles", "*.json"))
            {
                var record = Read<CompanionRecord>(key, null!);
                if (record?.Profile == null || record.Profile.Id == Guid.Empty || record.OnlineSeconds == null || record.Password == null || record.Account == null || record.Error==null || record.Profile.Name==null || record.Profile.Traits==null || record.Profile.Schedule==null || !Records.TryAdd(record.Profile.Id, record)) throw new InvalidDataException("陪玩档案编号或内容无效。");
                CompanionScheduling.Validate(record.Profile.Schedule);
                CompanionValidation.Guilds(record.Profile.Guilds);
                if(record.RecoveryAttempts is <0 or >1000 || record.SessionDeaths is <0 or >1000)throw new InvalidDataException("陪玩托管恢复或死亡计数无效。");
            }
        }
        catch (Exception e) when (e is JsonException or InvalidDataException or EndOfStreamException)
        { Records.Clear();CustomPresets.Clear();Configuration=new(new(),"",paths.VersionId,default,0);LoadError = "陪玩数据不能读取，已保留原记录并禁止写入：" + e.Message; }
    }
    public PersonalityPreset[] Presets => PersonalityPresets.All.Concat(CustomPresets).ToArray();
    public CompanionSettings Settings => Configuration.Settings with { HasLlmKey = false,
        Models=Configuration.Settings.Models.Select(m=>m with{HasKey=Configuration.ModelKeys?.GetValueOrDefault(m.Id)?.Length>0}).ToArray(),
        Enabled = Configuration.Settings.Enabled && Configuration.Instance == paths.VersionId,
        Version = AdminChanges.Version(Configuration with { LlmDay = default, LlmCalls = 0, Arrivals = null }) };
    public void Ready() { if (LoadError != null) throw new InvalidOperationException(LoadError); }
    private T Read<T>(string key, T fallback)
    {
        if (!storage.Contains(key)) return fallback;
        using var stream = storage.Read(key);
        var value = JsonSerializer.Deserialize<CompanionEnvelope<T>>(stream,Strict) ?? throw new InvalidDataException("陪玩快照为空。");
        if (value.Schema is not (1 or 2)) throw new InvalidDataException("陪玩快照来自不支持的格式版本。");
        legacyFormat|=value.Schema==1;
        return value.Value;
    }
    private void PrepareWrite()
    {
        Ready();if(!legacyFormat)return;
        storage.Backup(Path.Combine(paths.Runtime,"backups","companions-upgrade-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff"),"mir2.sqlite3"));
        legacyFormat=false;
    }
    private void Write<T>(string key, T value) => storage.Write(key, JsonSerializer.SerializeToUtf8Bytes(new CompanionEnvelope<T>(2, value)));
    private static string Key(Guid id) => $"companions/profiles/{id:N}.json";
    public CompanionRecord Get(Guid id) => Records.GetValueOrDefault(id) ?? throw new FileNotFoundException("未找到陪玩。");
    public CompanionProfile Public(CompanionRecord record) => record.Profile with { Version = AdminChanges.Version(record.Profile with { Version = "" }) };
    public void SaveProfile(CompanionProfile profile, string account = "", string password = "")
    {
        PrepareWrite(); var existing = Records.GetValueOrDefault(profile.Id);
        if (existing != null) AdminChanges.Match(Public(existing).Version, profile.Version);
        var value = existing == null ? new CompanionRecord { Seed = RandomNumberGenerator.GetInt32(int.MaxValue), Account = account, Password = password } : existing with { };
        value.Profile = profile with { Version = "" };
        value.RecoveryDisabled=!profile.Enabled;
        if (!profile.Enabled) { value.Desired = "off"; value.RecoveryDisabled = true; value.RecoveryAt = null; }
        changes.Commit(() => { Write(Key(profile.Id), value); Records[profile.Id] = value; }, () =>
        { if (existing == null) Records.Remove(profile.Id); else Records[profile.Id] = existing; });
    }
    public void SaveSettings(CompanionSettingsEdit edit)
    {
        PrepareWrite(); AdminChanges.Match(Settings.Version, edit.Settings.Version);
        if(Records.Values.Any(r=>r.Profile.LlmModelId!=Guid.Empty&&r.Profile.LlmMode>0&&!edit.Settings.Models.Any(m=>m.Id==r.Profile.LlmModelId)))
            throw new InvalidOperationException("模型仍被开启增强的陪玩引用，请先为这些角色更换模型或关闭增强。");
        var old = Configuration;
        var next=CompanionModels.Apply(old,edit,paths.VersionId);
        var a=next.Settings.Arrivals;
        var reset=old.Instance!=paths.VersionId||!old.Settings.Enabled||!old.Settings.Arrivals.Enabled||
            a.MinMinutes!=old.Settings.Arrivals.MinMinutes||a.MaxMinutes!=old.Settings.Arrivals.MaxMinutes;
        next=next with{Arrivals=(old.Arrivals??new()) with{NextAt=next.Settings.Enabled&&a.Enabled?
            reset?CompanionScheduling.NextArrival(a,DateTimeOffset.UtcNow):old.Arrivals?.NextAt:null}};
        changes.Commit(() => { Write("companions/settings.json", next); Configuration = next; }, () => Configuration = old);
    }
    public string StartAllVersion => AdminChanges.Version(new { Settings.Version, Profiles=Records.Values.OrderBy(r=>r.Profile.Id).Select(r=>Public(r).Version).ToArray() });
    public int StartAll(string version)
    {
        PrepareWrite();AdminChanges.Match(StartAllVersion,version);
        var old=Records.Values.Where(r=>r.Profile.Enabled).ToArray();
        if(old.Length==0)throw new InvalidOperationException("没有已启用的陪玩，请先新增或启用角色。");
        var configuration=Configuration;
        var next=configuration with{Settings=configuration.Settings with{Enabled=true},Instance=paths.VersionId};
        if(next.Settings.Arrivals.Enabled&&(!Settings.Enabled||configuration.Arrivals?.NextAt==null))
            next=next with{Arrivals=(configuration.Arrivals??new()) with{NextAt=CompanionScheduling.NextArrival(next.Settings.Arrivals,DateTimeOffset.UtcNow)}};
        // 档案意愿与总开关一次提交；保留人格、作息、用量、凭据和模型密钥。
        changes.Commit(()=>
        {
            foreach(var r in old)
            {
                var value=r with{Desired="run",Error="",SuppressedUntil=null,RestUntil=null,QueueSince=null,RecoveryAt=null,RecoveryAttempts=0,RecoveryDisabled=false,SessionDeaths=0};
                Write(Key(value.Profile.Id),value);Records[value.Profile.Id]=value;
            }
            Write("companions/settings.json",next);Configuration=next;
        },()=>{foreach(var r in old)Records[r.Profile.Id]=r;Configuration=configuration;});
        return old.Length;
    }
    public void SavePreset(PersonalityPreset value)
    {
        PrepareWrite(); var old = CustomPresets.ToArray();
        changes.Commit(() => { CustomPresets.Add(value); Write("companions/presets.json", CustomPresets); }, () => { CustomPresets.Clear(); CustomPresets.AddRange(old); });
    }
    public void ArchivePreset(string key,int revision,bool archived)
    {
        PrepareWrite();var index=CustomPresets.FindIndex(p=>p.Key==key&&p.Revision==revision);
        if(index<0)throw new FileNotFoundException("未找到自定义人格修订。");
        var old=CustomPresets[index];var value=old with{Archived=archived};
        changes.Commit(()=>{CustomPresets[index]=value;Write("companions/presets.json",CustomPresets);},()=>CustomPresets[index]=old);
    }
    public void Mutate(CompanionRecord value, Action<CompanionRecord> action, bool backup = false, bool saveAccounts = false)
    {
        PrepareWrite(); var next = value with { OnlineSeconds = new(value.OnlineSeconds) }; action(next);
        void Change() { Write(Key(next.Profile.Id), next); Records[next.Profile.Id] = next;if(saveAccounts)Server.MirEnvir.Envir.Main.SaveAccounts(); }
        void Undo() => Records[value.Profile.Id] = value;
        if (backup) changes.Commit(Change, Undo);
        else { try { storage.Transaction(Change); } catch { Undo(); throw; } }
    }
    public void Remove(Guid id)
    {
        PrepareWrite(); var old = Get(id);
        changes.Commit(() => { storage.Remove(Key(id)); Records.Remove(id); }, () => Records[id] = old);
    }
    public void SaveBatch(CompanionProfile[] profiles,string[] nicknames,CompanionArrivalState? arrivals=null)
    {
        PrepareWrite();var old=Configuration;
        var records=profiles.Select(p=>new CompanionRecord{Profile=p with{Version=""},Seed=RandomNumberGenerator.GetInt32(int.MaxValue),
            Account="c"+Convert.ToHexString(RandomNumberGenerator.GetBytes(6)).ToLowerInvariant(),Password=Convert.ToHexString(RandomNumberGenerator.GetBytes(7))}).ToArray();
        var next=old with{Settings=old.Settings with{Nicknames=nicknames.Select(n=>n.Trim()).Where(n=>n.Length>0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()},Arrivals=arrivals??old.Arrivals};
        changes.Commit(()=>
        {
            foreach(var r in records){Write(Key(r.Profile.Id),r);Records.Add(r.Profile.Id,r);}
            Write("companions/settings.json",next);Configuration=next;
        },()=>{foreach(var r in records)Records.Remove(r.Profile.Id);Configuration=old;});
    }
    public int TodaySeconds(CompanionRecord r, DateTimeOffset now)
    {
        var zone = CompanionScheduling.Zone(r.Profile.Schedule.TimeZone);
        var day = TimeZoneInfo.ConvertTime(now, zone).Date;
        return r.OnlineSeconds.Where(x => TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeSeconds(x.Key * 60), zone).Date == day).Sum(x => x.Value);
    }
    public int RollingSeconds(CompanionRecord r, DateTimeOffset now) => r.OnlineSeconds.Where(x => x.Key >= now.AddHours(-24).ToUnixTimeSeconds() / 60).Sum(x => x.Value);
    public void ReserveOnline(CompanionRecord r, DateTimeOffset now, int seconds)
    {
        seconds=Math.Max(0,Math.Min(seconds,r.Profile.Schedule.DailyMinutes*60-Math.Max(TodaySeconds(r,now),RollingSeconds(r,now))));
        Mutate(r, next =>
        {
            for (var i = 0; i < seconds; i++)
            {
                var minute = now.AddSeconds(i).ToUnixTimeSeconds() / 60;
                next.OnlineSeconds[minute] = Math.Min(60, next.OnlineSeconds.GetValueOrDefault(minute) + 1);
            }
            foreach (var key in next.OnlineSeconds.Keys.Where(k => k < now.AddHours(-48).ToUnixTimeSeconds() / 60).ToArray()) next.OnlineSeconds.Remove(key);
        });
    }
    public bool ReserveLlm()
    {
        PrepareWrite();var now = DateTimeOffset.UtcNow; var count = Configuration.LlmDay.Date == now.Date ? Configuration.LlmCalls : 0;
        if (count >= Settings.DailyLlmCalls) return false;
        var next = Configuration with { LlmDay = now.Date, LlmCalls = count + 1 };
        storage.Transaction(() => Write("companions/settings.json", next)); Configuration = next; return true;
    }
}
