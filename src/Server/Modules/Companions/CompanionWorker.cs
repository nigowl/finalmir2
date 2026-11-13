using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Mir2.Companions;

namespace Server.ConsoleHost;

public sealed class CompanionWorker
{
    private Process? process;
    private readonly SemaphoreSlim writer = new(1);
    private Task? reader, errors;
    public ConcurrentQueue<WorkerEvent> Events { get; } = new();
    public bool Running => process is { HasExited: false };
    public static string ExecutableDirectory => Path.Combine(AppContext.BaseDirectory, "companion");
    public static bool Installed => File.Exists(Path.Combine(ExecutableDirectory, "Mir2.Companion.dll"));
    public async Task Start()
    {
        if (Running) return;
        if(process!=null)await Stop();
        while(Events.TryDequeue(out _)){}
        if (!Installed) throw new InvalidOperationException("陪玩程序未构建/发布，请重新构建服务端。");
        var native = Path.Combine(ExecutableDirectory, OperatingSystem.IsWindows() ? "Mir2.Companion.exe" : "Mir2.Companion");
        var info = new ProcessStartInfo(File.Exists(native) ? native : "dotnet")
        { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = ExecutableDirectory };
        if (!File.Exists(native)) info.ArgumentList.Add(Path.Combine(ExecutableDirectory, "Mir2.Companion.dll"));
        process = Process.Start(info) ?? throw new IOException("无法启动陪玩工作进程。");
        reader = Read(process.StandardOutput); errors = ReadErrors(process.StandardError);
    }
    private async Task Read(StreamReader stream)
    {
        try
        {
            while (await ReadLine(stream) is { } line)
            {
                var value = JsonSerializer.Deserialize<WorkerEvent>(line) ?? throw new InvalidDataException("陪玩事件为空。");
                if (Events.Count >= 2000) throw new InvalidDataException("陪玩事件积压超限。");
                Events.Enqueue(value);
            }
        }
        catch (Exception e) when (e is IOException or JsonException or InvalidDataException)
        { Events.Enqueue(new("worker-failed", Guid.Empty, Reason: e.Message)); }
    }
    private static async Task<string?> ReadLine(StreamReader stream)
    {
        var line = new StringBuilder(); var buffer = new char[1];
        while (await stream.ReadAsync(buffer.AsMemory()) > 0)
        {
            if (buffer[0] == '\n') return line.ToString();
            if (line.Length >= CompanionProtocol.MaxMessage) throw new InvalidDataException("陪玩事件过长。");
            if (buffer[0] != '\r') line.Append(buffer[0]);
        }
        return line.Length == 0 ? null : line.ToString();
    }
    private async Task ReadErrors(StreamReader stream)
    {
        try { while (await ReadLine(stream) is { } line)
            {if(Events.Count>=2000)throw new InvalidDataException("陪玩诊断积压超限。");Events.Enqueue(new("diagnostic", Guid.Empty, Reason: line[..Math.Min(line.Length,500)]));} }
        catch (Exception e) when(e is IOException or InvalidDataException) { Events.Enqueue(new("worker-failed", Guid.Empty, Reason: e.Message)); }
    }
    public async Task Send(WorkerCommand command)
    {
        if (!Running) throw new IOException("陪玩工作进程未运行。");
        var line = JsonSerializer.Serialize(command);
        if (line.Length > CompanionProtocol.MaxMessage) throw new InvalidDataException("陪玩控制消息超限。");
        await writer.WaitAsync();
        try
        {
            using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await process!.StandardInput.WriteLineAsync(line.AsMemory(),timeout.Token);await process.StandardInput.FlushAsync(timeout.Token);
        }
        finally { writer.Release(); }
    }
    public async Task Stop()
    {
        var child=process; if(child==null)return;
        try
        {
            if(!child.HasExited)
            {
                try{await Send(new("quit"));}catch(Exception e)when(e is IOException or OperationCanceledException){Events.Enqueue(new("diagnostic",Guid.Empty,Reason:e.Message));}
                child.StandardInput.Close();
                try { await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(8)); }
                catch (TimeoutException) { child.Kill(true); await child.WaitForExitAsync(); }
            }
            if(reader!=null)await reader; if(errors!=null)await errors;
        }
        finally { child.Dispose(); process=null; }
    }
}
