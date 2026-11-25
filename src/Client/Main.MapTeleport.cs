using Crystal.Native;
using C = ClientPackets;
using Point = System.Drawing.Point;

public partial class Main
{
    private void SendMapTeleport(Point point)
    {
        if (!HandshakeReady || World.Loading || State.Me?.Dead != false) { Log("当前状态无法进行坐标传送。"); return; }
        CancelQuestNavigation(); CloseGroundMove(); destination = null; actionTarget = 0; pendingAttack = null; moving = false; MiniMap.Route = [];
        // 原MOVE命令由服务器校验GM/传送装备、禁传地图、冷却及落点，不预改客户端坐标。
        Send(new C.Chat { Message = $"@mv {point.X} {point.Y}" }); MiniMap.SetExpanded(false);
        Log($"请求传送至 {point.X},{point.Y}，等待服务端确认。");
    }
}
