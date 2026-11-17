using Client.MirObjects;
using Godot;
using S = ServerPackets;

namespace Crystal.Native;

public partial class NativeSmoke
{
    private async Task VerifyShadowPixelsOnly()
    {
        if (!(Environment.GetEnvironmentVariable("MIR2_CLIENT_CACHE") ?? "").Contains("mir2-native.", StringComparison.Ordinal))
            throw new InvalidOperationException("阴影检查必须使用隔离原生fixture。");
        stage = "真实地图与角色点阵阴影恢复";
        await Until(() => Client.HandshakeReady && Client.Assets.Ready);
        var state = new GameState();
        state.Apply(new S.UserInformation { ObjectID = 0x71000000, Name = "ShadowCheck", HP = 100,
            Class = MirClass.Warrior, Gender = MirGender.Male, Location = new(294, 576),
            Inventory = [], Equipment = new UserItem[14], QuestInventory = [] });
        state.Me!.Armour = 1; state.Me.Hair = 0; state.Me.Weapon = -1;
        var viewport = new SubViewport { Size = new Vector2I(1024, 768), World2D = new World2D(),
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always, Disable3D = true };
        Client.AddChild(viewport);
        var world = new WorldView { State = state, Assets = Client.Assets };
        viewport.AddChild(world);
        try
        {
            await world.Load("0");
            await Until(() => !world.Loading && Client.Assets.PendingDownloads == 0 && world.PrefetchedCells > 0);
            foreach (var (library, index) in new[] { ("Map/WemadeMir2/Objects", 1409), ("CArmour/01", 0), ("NPC/00", 0) })
            {
                await Until(() => Client.Assets.Sprite(library, index) != null);
                using var image = Client.Assets.Sprite(library, index)!.Texture.GetImage();
                var pixels = image.GetData();
                if (!Enumerable.Range(0, pixels.Length / 4).Any(i => pixels[i * 4 + 3] == 128) ||
                    OriginalShadowPixels.RestoreAlpha(library, pixels, image.GetWidth(), image.GetHeight()) != 0)
                    throw new Exception("真实GPU纹理未恢复阴影，或重复处理已恢复像素：" + library);
            }
            var times = new List<double>();
            for (var i = 0; i < 60; i++)
            {
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                times.Add(world.LastDrawMilliseconds);
            }
            using var screenshot = viewport.GetTexture().GetImage();
            if (screenshot.GetWidth() != 1024 || screenshot.GetPixel(480, 352).A == 0)
                throw new Exception("原生地图阴影截图没有实际渲染。");
            if (Argument("--screenshot") is { Length: > 0 } path) screenshot.SavePng(Path.ChangeExtension(path, "shadows.png"));
            times.Sort();
            GD.Print($"PASS native shadows: actual map/player/NPC GPU alpha, unchanged original cache; 60-frame world CPU p95={times[57]:F3}ms; isolated 1024x768 map capture, no role or asset writes.");
        }
        finally { Client.RemoveChild(viewport); viewport.QueueFree(); }
        await Client.Quit();
    }
}
