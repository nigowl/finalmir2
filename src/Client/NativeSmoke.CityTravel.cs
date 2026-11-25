using Godot;
using C = ClientPackets;
using S = ServerPackets;

namespace Crystal.Native;

public partial class NativeSmoke
{
    private async Task VerifyCityTravel()
    {
        if (!(Environment.GetEnvironmentVariable("MIR2_CLIENT_CACHE") ?? "").Contains("mir2-native.", StringComparison.Ordinal))
            throw new InvalidOperationException("城镇传送界面只允许独立测试缓存。");
        stage = "传送菜单自动换行与原服费用回执";
        await Until(() => Client.HandshakeReady);
        Client.Login("TravelNative", "Travel123");
        await Packet<S.LoginSuccess>();
        Client.Send(new C.StartGame { CharacterIndex = State.Characters.Single(c => c.Name == "TravelNative").Index });
        await Until(() => State.User != null && !Client.World.Loading);
        var panel = (Control)Client.FindChild("NpcDialog", true, false);
        var text = (VBoxContainer)Client.FindChild("NpcText", true, false);
        var scroll = (ScrollContainer)Client.FindChild("NpcScroll", true, false);
        async Task Open(string name)
        {
            var guide = State.Actors.Values.Single(a => a.Kind == ObjectType.Merchant && a.Name == name);
            packets.Clear(); Client.SelectTarget(guide.Id); await Packet<S.NPCResponse>();
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        }
        LinkButton Link(string label) => text.FindChildren("*", "LinkButton", true, false).Cast<LinkButton>().Single(b => b.Text.StartsWith(label, StringComparison.Ordinal));
        await Open("边境驿卒");
        var flows = text.GetChildren().OfType<HFlowContainer>().ToArray();
        var cityLinks = flows.Single(f => f.GetChildCount() == 9).GetChildren().Cast<LinkButton>().ToArray();
        if (!panel.Visible || !cityLinks.GroupBy(link => link.GetGlobalRect().Position.Y).Any(row => row.Count() > 1) ||
            cityLinks.Any(link => link.GetGlobalRect().End.X > scroll.GetGlobalRect().End.X) ||
            !flows.Any(f => f.GetChildCount() == 3) || Link("天然洞穴").Text != "天然洞穴·2,000" ||
            text.FindChildren("*", "LinkButton", true, false).Cast<LinkButton>().Any(b => b.Text.Contains("下一页") || b.Text.Contains("更多打怪")))
            throw new Exception("城镇/危险区未并排自适应、链接越界或仍使用分页。");
        await CombatScreenshot("city-travel-menu");
        var gold = State.User!.Gold;
        packets.Clear(); Link("比奇城").EmitSignal(BaseButton.SignalName.Pressed);
        await Until(() => packets.OfType<S.MapChanged>().Any() && packets.OfType<S.NPCResponse>().Any(p => p.Page.Count == 0));
        if (panel.Visible || State.User.Gold != gold) throw new Exception("免费城镇传送未关闭对话或错误扣费。");
        await Open("比奇驿官");
        packets.Clear(); Link("天然洞穴").EmitSignal(BaseButton.SignalName.Pressed);
        var confirmation = await Packet<S.NPCResponse>();
        if (!panel.Visible || !confirmation.Page.Any(line => line.Contains("2,000")) || State.User.Gold != gold)
            throw new Exception("危险区确认费用未显示或提前收款。");
        await CombatScreenshot("city-travel-confirm");
        packets.Clear(); Link("确认前往").EmitSignal(BaseButton.SignalName.Pressed);
        await Until(() => State.Map?.MapIndex == 34 && packets.OfType<S.NPCResponse>().Any(p => p.Page.Count == 0));
        if (panel.Visible || State.User.Gold != gold - 2000 ||
            State.User.Inventory.Where(i => i?.Info is { Type: ItemType.Scroll, Shape: 1 }).Sum(i => i!.Count) != 1)
            throw new Exception("危险区实扣、赠卷数量或成功关闭对话不符。");
        await CombatScreenshot("city-travel-arrived");
        GD.Print("PASS native city travel: responsive inline links, local destinations, visible fee, free city, one scroll, paid field and closed dialogue.");
        await Client.Quit();
    }
}
