using Godot;
using S = ServerPackets;
using Point = System.Drawing.Point;

namespace Crystal.Native;

public partial class NativeSmoke
{
    private async Task VerifyTerrainCache()
    {
        stage = "switched-map warmup and stationary cache stability at screenshot coordinates";
        var probeState = new GameState();
        probeState.Apply(new S.UserInformation { ObjectID = uint.MaxValue, Name = "缓存回归", Location = new Point(331, 261), HP = 1,
            Inventory = [], Equipment = new UserItem[14], QuestInventory = [] });
        var probe = new WorldView { State = probeState, Assets = Client.Assets };
        Client.AddChild(probe);
        try
        {
            await probe.Load("0");
            await Until(() => !probe.Loading && probe.PrefetchedCells > 0 && probe.PrefetchQueueCount == 0 && Client.Assets.PendingDownloads == 0);
            var cached = Client.Assets.CachedFiles; var downloads = Client.Assets.Downloads; var evictions = Client.Assets.TextureEvictions;
            long maxFrame = 0; var previous = Environment.TickCount64;
            for (var sample = 0; sample < 120; sample++)
            {
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                var now = Environment.TickCount64; maxFrame = Math.Max(maxFrame, now - previous); previous = now;
            }
            if (Client.Assets.TextureCount > 8192 || Client.Assets.TextureBytes > 256L * 1024 * 1024 || Client.Assets.TextureEvictions != evictions ||
                Client.Assets.CachedFiles - cached > 2 || Client.Assets.Downloads != downloads)
                throw new Exception($"Stationary terrain keeps re-reading/evicting cached tiles: disk={Client.Assets.CachedFiles - cached}, downloads={Client.Assets.Downloads - downloads}, evictions={Client.Assets.TextureEvictions - evictions}.");
            await Screenshot("cache-map-331-261");
            GD.Print($"PASS native terrain cache: screenshot coordinate 331,261, initial visible/animated warmup, six-cell margin, textures={Client.Assets.TextureCount}, RGBA={Client.Assets.TextureBytes / 1048576d:F1}MiB, stationary disk reads={Client.Assets.CachedFiles - cached}, downloads={Client.Assets.Downloads - downloads}, max sampled frame={maxFrame}ms.");
        }
        finally { Client.RemoveChild(probe); probe.QueueFree(); }
    }
}
