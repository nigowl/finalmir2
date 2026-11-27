using System.Diagnostics;
using Mir2.Hosting;
using Mir2.Management;
using Server.MirEnvir;

namespace Server.ConsoleHost;

public sealed class GameRuntime(ProjectPaths paths, AssetCatalog assets) : IHostedService
{
    private readonly SemaphoreSlim lifecycle = new(1);
    private readonly Stopwatch uptime = Stopwatch.StartNew();
    private int generation;
    public SemaphoreSlim Changes { get; } = new(1);
    public int GamePort { get; private set; } = 7000;
    public bool Ready => Envir.Main.Running && Envir.Main.NetworkStarted;
    public bool Stopping { get; private set; }
    public DateTimeOffset? LastSave { get; private set; }
    public Func<Task>? BeforeStop { get; set; }
    public string AssetVersion => assets.Version;
    public Task StartAsync(CancellationToken cancellationToken) => Start(cancellationToken);
    public Task StopAsync(CancellationToken cancellationToken) => Stop(cancellationToken);
    public async Task Start(CancellationToken cancellation)
    {
        await lifecycle.WaitAsync(cancellation);
        try { await StartCore(); }
        finally { lifecycle.Release(); }
    }
    private async Task StartCore()
    {
        if (Ready) return;
        Stopping = false;
        Settings.MapPath = Path.Combine(paths.Assets, "maps");
        Settings.Load();
        Settings.IPAddress = Environment.GetEnvironmentVariable("MIR2_BIND_ADDRESS") ?? "127.0.0.1";
        Settings.Port = ushort.Parse(Environment.GetEnvironmentVariable("MIR2_GAME_PORT") ?? Settings.Port.ToString());
        Settings.CheckVersion = Settings.StartHTTPService = Settings.Multithreaded = false;
        Settings.AllowStartGame = true; GamePort = Settings.Port;
        var environment = Envir.Main; environment.StatusPortEnabled = false; environment.Start();
        var deadline = Stopwatch.StartNew();
        while (!Ready && environment.Running && deadline.Elapsed < TimeSpan.FromMinutes(2)) await Task.Delay(100);
        if (!Ready)
        {
            await Task.Run(environment.Stop);
            throw new InvalidOperationException("游戏引擎启动失败：" + string.Join(Environment.NewLine,
                MessageQueue.Instance.MessageLog.Concat(MessageQueue.Instance.DebugLog).TakeLast(8)), environment.LastError);
        }
        Console.WriteLine($"Game server ready on {Settings.IPAddress}:{GamePort}.");
    }
    public async Task Stop(CancellationToken cancellation)
    {
        await lifecycle.WaitAsync(cancellation);
        try { await StopCore(); }
        finally { lifecycle.Release(); }
    }
    private async Task StopCore()
    {
        Stopping = true;
        try
        {
            if (Ready)
            {
                await Invoke(() => { Envir.Main.SaveAll(); return true; }, allowStopping: true);
                LastSave = DateTimeOffset.UtcNow;
            }
            if (BeforeStop != null) await BeforeStop();
            Interlocked.Increment(ref generation);
            await Task.Run(() => Envir.Main.Stop());
            if (Envir.Main.LastError != null) throw new InvalidOperationException("游戏引擎退出失败，请检查引擎日志；未执行重新启动。", Envir.Main.LastError);
        }
        catch { if (Ready) Stopping = false; throw; }
    }
    public async Task<T> Invoke<T>(Func<T> action, CancellationToken cancellation = default, bool allowStopping = false)
    {
        if (!Ready || Stopping && !allowStopping) throw new InvalidOperationException("游戏引擎未运行或正在停止，请稍后重试。");
        var queuedGeneration = Volatile.Read(ref generation);
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation); timeout.CancelAfter(TimeSpan.FromSeconds(20));
        var token = timeout.Token; var state = 0;
        using var registration = token.Register(() =>
        {
            if (Interlocked.CompareExchange(ref state, 2, 0) == 0) completion.TrySetCanceled(token);
        });
        Envir.Main.HostActions.Enqueue(() =>
        {
            // Cancellation can remove queued work, but must not detach an active save.
            if (Interlocked.CompareExchange(ref state, 1, 0) != 0) return;
            try
            {
                if (!Ready || queuedGeneration != Volatile.Read(ref generation) || Stopping && !allowStopping)
                    throw new InvalidOperationException("游戏引擎正在切换，请刷新后重试。");
                completion.TrySetResult(action());
            }
            catch (Exception error) { completion.TrySetException(error); }
        });
        return await completion.Task;
    }
    public async Task Save(CancellationToken cancellation)
    {
        await lifecycle.WaitAsync(cancellation);
        try { await Invoke(() => { Envir.Main.SaveAll(); return true; }); LastSave = DateTimeOffset.UtcNow; }
        finally { lifecycle.Release(); }
    }
    public async Task Restart(CancellationToken cancellation)
    {
        await lifecycle.WaitAsync(cancellation);
        try { await StopCore(); await StartCore(); }
        finally { lifecycle.Release(); }
    }
    public async Task<ServerStatus> Status(CancellationToken cancellation)
    {
        var data = Ready && !Stopping ? await Invoke(() => new
        {
            Counts = new WorldCounts(Envir.Main.MapList.Count, Envir.Main.ItemInfoList.Count, Envir.Main.MonsterInfoList.Count, Envir.Main.NPCInfoList.Count, Envir.Main.QuestInfoList.Count, Envir.Main.AccountList.Count),
            Players = Envir.Main.Players.OrderBy(p => p.Info.Index).Select(p => new PlayerStatus(p.Name, p.Class.ToString(), p.Level, p.CurrentMap?.Info.Title ?? "", p.CurrentLocation.X, p.CurrentLocation.Y,
                p.GroupMembers?.FirstOrDefault()?.Name ?? "", p.GroupMembers?.Count ?? 0, p.TradePartner?.Name ?? "", p.TradeLocked, p.MyGuild?.Name ?? "", p.MyGuildRank?.Name ?? "")).ToArray(),
            Objects = Envir.Main.Objects.Count
        }, cancellation) : null;
        using var process = Process.GetCurrentProcess();
        return new(Envir.Main.Running, Ready && !Stopping, Environment.ProcessId, uptime.Elapsed.TotalSeconds, process.WorkingSet64 / 1048576d,
            process.TotalProcessorTime.TotalSeconds, Envir.LastRunTime, data?.Objects ?? 0, data?.Counts ?? new(0, 0, 0, 0, 0, 0), data?.Players ?? [],
            assets.Version, assets.Files.Length, assets.Files.Sum(f => f.Size), paths.Database, LastSave, DateTimeOffset.UtcNow);
    }
}

public sealed class ServerLogs : BackgroundService
{
    private readonly object gate = new();
    private readonly Queue<LogEntry> entries = new();
    private long sequence;
    public LogEntry[] Read(long after = 0) { lock (gate) return entries.Where(e => e.Id > after).ToArray(); }
    public void Add(string category, string message)
    {
        lock (gate)
        {
            entries.Enqueue(new(++sequence, DateTimeOffset.UtcNow, category, message.TrimEnd()));
            while (entries.Count > 500) entries.Dequeue();
        }
        Console.Write(message);
    }
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(250));
        try
        {
            do
            {
                var queue = MessageQueue.Instance;
                foreach (var (category, log) in new[] { ("server", queue.MessageLog), ("debug", queue.DebugLog), ("chat", queue.ChatLog) })
                    while (log.TryDequeue(out var message)) Add(category, message);
            } while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
}

public static class OperationsModule
{
    public static void MapOperations(this RouteGroupBuilder group)
    {
        group.MapGet("/settings", (GameRuntime runtime, SystemSettings settings, CancellationToken token) => runtime.Invoke(settings.Read, token));
        group.MapPut("/settings", async (GameRuntime runtime, SystemSettings settings, ServerSettingsEdit edit, CancellationToken token) =>
        {
            var saved = await runtime.Invoke(() => settings.Save(edit), token);
            // 保存退出后更新地图效果和动作间隔，旧连接随引擎退出。
            if (saved.RequiresRestart) await runtime.Restart(CancellationToken.None);
            return await runtime.Invoke(settings.Read, CancellationToken.None);
        });
        group.MapGet("/status", (GameRuntime runtime, CancellationToken token) => runtime.Status(token));
        group.MapGet("/logs", (ServerLogs logs, long? after) => logs.Read(after ?? 0));
        group.MapPost("/save", async (GameRuntime runtime, CancellationToken token) => { await runtime.Save(token); return new ActionResult("世界和玩家数据已保存到 SQLite。"); });
        group.MapPost("/start", async (GameRuntime runtime, CancellationToken token) => { await runtime.Start(token); return new ActionResult("游戏引擎已启动。"); });
        group.MapPost("/stop", async (GameRuntime runtime, CancellationToken token) => { await runtime.Stop(token); return new ActionResult("游戏引擎已停止并保存。"); });
        group.MapPost("/restart", async (GameRuntime runtime, CancellationToken token) => { await runtime.Restart(token); return new ActionResult("游戏引擎已重启。"); });
        group.MapPost("/reload-scripts", async (GameRuntime runtime, CancellationToken token) =>
        {
            await runtime.Invoke(() => { Envir.Main.ReloadNPCs(); Envir.Main.ReloadDrops(); return true; }, token);
            return new ActionResult("人物脚本与掉落规则已重新加载。任务和配置修改需要重启引擎。");
        });
    }
}
