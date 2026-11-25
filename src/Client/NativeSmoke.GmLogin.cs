using Godot;
using C = ClientPackets;
using S = ServerPackets;

namespace Crystal.Native;

public partial class NativeSmoke
{
    private async Task VerifyGmLogin()
    {
        if (!(Environment.GetEnvironmentVariable("MIR2_CLIENT_CACHE") ?? "").Contains("mir2-heroes.", StringComparison.Ordinal) || AssetsPortIsDefault())
            throw new InvalidOperationException("GM登录检查仅允许隔离版本。");
        stage = "GM original saved location and login dialogue";
        var action = Argument("--gm-login-action");
        int initialHeroHP = -1;
        void CaptureHeroHP(Packet p) { if (p is S.HeroInformation hero && initialHeroHP < 0) initialHeroHP = hero.HP; }
        Client.PacketReceived += CaptureHeroHP;
        await Until(() => Client.HandshakeReady); Client.Login(action == "normal" ? "TradeLeft" : "TradeRight", "Trade123");
        Client.Send(new C.StartGame { CharacterIndex = (await Packet<S.LoginSuccess>()).Characters.Single().Index });
        await Until(() => State.User != null && !Client.World.Loading);
        var dialog = (Control)Client.FindChild("NpcDialog", true, false);
        await ToSignal(GetTree().CreateTimer(1), SceneTreeTimer.SignalName.Timeout);
        if (State.Map!.FileName == "R001") throw new Exception("GM未经确认已自动进入管理员地图。");
        var map = State.Map.MapIndex; var location = State.User!.Location;
        if (action == "normal")
        {
            if (initialHeroHP != State.CalculateHeroStats()[Stat.HP] || initialHeroHP <= 0) throw new Exception("新英雄的首次原资料仍未初始化生命。");
            if (packets.OfType<S.NPCResponse>().Any(p => p.Page.Any(line => line.Contains("是否进入管理员")))) throw new Exception("普通角色收到GM登录提示。");
            Client.Send(new C.CallNPC { ObjectID = packets.OfType<S.NPCUpdate>().Last().NPCID, Key = "[@ENTERADMINMAP]" });
        }
        else
        {
            GD.Print("GM login pages: " + System.Text.Json.JsonSerializer.Serialize(packets.OfType<S.NPCResponse>().Select(p => p.Page)));
            await Until(() => dialog.Visible && Client.FindChild("NpcText", true, false).FindChildren("*", "LinkButton", true, false).Cast<LinkButton>().Any(b => b.Text == "进入管理员地图"));
            await Screenshot("gm-login-" + action);
            if (action == "close") await ClickSelectionControl(GameArt("关闭 NPC"));
            else
            {
                var text = action == "enter" ? "进入管理员地图" : "留在当前位置";
                var link = Client.FindChild("NpcText", true, false).FindChildren("*", "LinkButton", true, false).Cast<LinkButton>().Single(b => b.Text == text);
                await ClickSelectionControl(link);
            }
            if (action == "enter")
            {
                await Until(() => State.Map!.FileName == "R001" && !Client.World.Loading);
                if (State.User.Location != new System.Drawing.Point(100, 100)) throw new Exception("确认后未沿原脚本到达管理员落点。");
            }
        }
        var time = Environment.TickCount64; Client.Send(new C.KeepAlive { Time = time });
        await Until(() => packets.OfType<S.KeepAlive>().Any(p => p.Time == time));
        if (action != "enter" && (State.Map!.MapIndex != map || State.User.Location != location)) throw new Exception("取消/关闭或普通角色越权改变存档位置。");
        if (action != "normal" && dialog.Visible) throw new Exception("选择后登录询问未关闭。");
        GD.Print($"PASS native GM login {action}: saved map, original script confirmation and ISADMIN authority.");
        await Client.Quit();
    }
}
