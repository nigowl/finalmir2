using System.Drawing;
using System.Security.Cryptography;
using Client.MirObjects;
using C = ClientPackets;

namespace Mir2.Companion;

public sealed partial class CompanionSession
{
    private MapReader? map;
    private Task? mapLoad;
    private string mapName = "";
    private bool moving;
    private long moveSent;
    private readonly Random random;
    private readonly Dictionary<Point, long> unreachable = [];
    private readonly CompanionPath navigation = new();
    private readonly Dictionary<int,long> exploredMaps=[];
    private int explorationMap;
    private async Task<bool> ExploreExit(long now)
    {
        if(Knowledge==null||Profile.Traits.Exploration<60&&Profile.Goal!="探索优先"&&goalHint!="explore")return false;
        if(explorationMap!=Knowledge.MapId){explorationMap=Knowledge.MapId;exploredMaps[explorationMap]=now+90000;}
        if(now-lastProgress<20000&&Profile.Goal!="探索优先")return false;
        var exit=Knowledge.Exits.Where(e=>exploredMaps.GetValueOrDefault(e.DestinationMapId)<now)
            .OrderBy(e=>Distance(state.User!.Location,new(e.X,e.Y))).FirstOrDefault();
        if(exit==null)return false;
        await Status("跨图中","步行前往当前版本公开入口");
        return await MoveTo(new(exit.X,exit.Y));
    }
    private static int Distance(Point a, Point b) => Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));
    private static MirDirection Direction(Point a, Point b) => (Math.Sign(b.X - a.X), Math.Sign(b.Y - a.Y)) switch
    { (0,-1) => MirDirection.Up, (1,-1) => MirDirection.UpRight, (1,0) => MirDirection.Right, (1,1) => MirDirection.DownRight,
      (0,1) => MirDirection.Down, (-1,1) => MirDirection.DownLeft, (-1,0) => MirDirection.Left, _ => MirDirection.UpLeft };
    private async Task LoadMap()
    {
        if (state.Map == null || mapName == state.Map.FileName && map != null) return;
        var name = state.Map.FileName;
        if (name.Length > 200 || name.Contains("..") || name.Contains('\\') || name.StartsWith('/')) throw new InvalidDataException("地图路径无效。");
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        var url = start.ResourceUrl + "/assets/" + start.AssetVersion + "/map/" + string.Join('/', name.Split('/').Select(Uri.EscapeDataString));
        using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellation.Token);
        response.EnsureSuccessStatusCode();
        using var bytes = new MemoryStream(); await using var stream = await response.Content.ReadAsStreamAsync(cancellation.Token);
        var buffer = new byte[65536]; int read;
        while ((read = await stream.ReadAsync(buffer, cancellation.Token)) > 0)
        { if (bytes.Length + read > 128 * 1024 * 1024) throw new InvalidDataException("地图超出尺寸限制。"); bytes.Write(buffer, 0, read); }
        var content = bytes.ToArray(); var hash = Convert.ToHexString(SHA256.HashData(content));
        if (!response.Headers.TryGetValues("X-Content-SHA256", out var hashes) || !hash.Equals(hashes.Single(), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("地图 SHA-256 校验失败。");
        Directory.CreateDirectory(start.CacheDirectory);
        var file = Path.Combine(start.CacheDirectory, Profile.Id.ToString("N") + ".map");
        var temporary = file + "." + Profile.Id.ToString("N") + ".tmp";
        MapReader decoded;
        try
        {
            await File.WriteAllBytesAsync(temporary, content, cancellation.Token);File.Move(temporary, file, true);
            decoded=await Task.Run(()=>new MapReader(file),cancellation.Token);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary);if(File.Exists(file))File.Delete(file); }
        if(state.Map?.FileName!=name)return;
        map=decoded;mapName=name;unreachable.Clear();navigation.Clear();
    }
    private bool Walkable(Point p) => map != null && p.X >= 0 && p.Y >= 0 && p.X < map.Width && p.Y < map.Height &&
        (map.MapCells[p.X,p.Y].BackImage & 0x20000000) == 0 && (map.MapCells[p.X,p.Y].FrontImage & 0x8000) == 0;
    private Point? Step(Point destination, int near)
    {
        if (state.User == null) return null;
        var origin = state.User.Location;
        var occupied = state.Actors.Values.Where(a => a.Id != state.User.ObjectID && !a.Dead && !a.Hidden && a.Kind is not (ObjectType.Item or ObjectType.Spell)).Select(a => a.Location).ToHashSet();
        foreach(var exit in Knowledge?.Exits ?? [])
            if(near > 0 || new Point(exit.X,exit.Y)!=destination)occupied.Add(new(exit.X,exit.Y));
        return navigation.Next(origin, destination, near, Walkable, occupied);
    }
    private async Task<bool> MoveTo(Point destination, int near = 0)
    {
        if (state.User == null || Distance(state.User.Location, destination) <= near) return false;
        var now = Environment.TickCount64;
        if (unreachable.GetValueOrDefault(destination) > now) return false;
        var step = Step(destination, near);
        if (step != null)
        {
            var door=map!.MapCells[step.Value.X,step.Value.Y].DoorIndex;
            if(door!=0)await connection.Send(new C.Opendoor{DoorIndex=door});
            await connection.Send(new C.Walk { Direction = Direction(state.User.Location, step.Value) });
            moving = true; moveSent = now; nextAction = now + 650; return true;
        }
        unreachable[destination] = now + 15000;
        if (unreachable.Count > 128) unreachable.Clear();
        return false;
    }
}
