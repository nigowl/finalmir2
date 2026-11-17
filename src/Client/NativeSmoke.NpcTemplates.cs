using Godot;
using S = ServerPackets;

namespace Crystal.Native;

public partial class NativeSmoke
{
    private async Task VerifyNpcTemplatesOnly()
    {
        if (!(Environment.GetEnvironmentVariable("MIR2_CLIENT_CACHE") ?? "").Contains("mir2-native.", StringComparison.Ordinal))
            throw new InvalidOperationException("NPC模板检查必须使用隔离原生fixture。");
        stage = "原TCP NPC模板与真实对话显示";
        await EnterUiFixture();
        if (Argument("--npc-location").Split(',') is [var x, var y])
        {
            var point = new System.Drawing.Point(int.Parse(x), int.Parse(y));
            Client.SetDestination(point);
            await Until(() => State.User!.Location == point);
        }
        await Until(() => State.Actors.Values.Any(actor => actor.Kind == ObjectType.Merchant));
        var npc = State.Actors.Values.Single(actor => actor.Kind == ObjectType.Merchant);
        packets.RemoveAll(packet => packet is S.NPCResponse);
        Client.SelectTarget(npc.Id);
        var response = await Packet<S.NPCResponse>();
        if (response.Page.Any(line => line.Contains("<$")) ||
            !response.Page.Contains($"{{{State.User!.Name}/KHAKI}}，来啦！我就是{{{npc.Name.Replace('_', ' ')}/KHAKI}}。") ||
            !response.Page.Any(line => line.Contains($"生命：{{{State.User.HP}/") && line.Contains($"魔力：{{{State.User.MP}/")) ||
            !response.Page.Contains("变量：7||7") ||
            !response.Page.Any(line => line.StartsWith("当前佩戴：{左：") && line.Contains("　右：")))
            throw new Exception("原NPCResponse仍有未替换变量，或姓名/HP/MP/左右装备/变量值错误。");
        var text = (VBoxContainer)Client.FindChild("NpcText", true, false);
        var panel = (Control)Client.FindChild("NpcDialog", true, false);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        var labels = text.FindChildren("*", "Label", true, false).Cast<Label>().ToArray();
        var links = text.FindChildren("*", "LinkButton", true, false).Cast<LinkButton>().ToArray();
        if (!panel.IsVisibleInTree() || labels.Any(label => label.Text.Contains("<$") || label.Text.Contains("/KHAKI}")) ||
            !labels.Any(label => label.Text.Contains(State.User.Name) && label.Text.Contains(npc.Name)) ||
            !links.Any(link => link.Text == "购买"))
            throw new Exception("模板已传输但Godot对话文本/购买按钮显示错误。");
        GetWindow().GrabFocus();
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        if (Argument("--screenshot") is { Length: > 0 } shot) Capture(Path.ChangeExtension(shot, "npc-templates.png"));
        links.Single(link => link.Text == "购买").EmitSignal(BaseButton.SignalName.Pressed);
        await Packet<S.NPCGoods>();
        panel.FindChildren("*", "Button", true, false).Cast<Button>().Single(button => button.Text == "返回").EmitSignal(BaseButton.SignalName.Pressed);
        if ((await Packet<S.NPCResponse>()).Page.Any(line => line.Contains("<$")))
            throw new Exception("商店返回后NPC模板未重新读取当前状态。");
        GD.Print("PASS native NPC templates: original TCP greeting, HP/MP, both rings, multi-digit/empty OUTPUT, Godot labels and buy/back links.");
        await Client.Quit();
    }
}
