using System.Reflection;
using Godot;
using C = ClientPackets;
using S = ServerPackets;

namespace Crystal.Native;

public partial class NativeSmoke
{
    private async Task VerifyNpcInput()
    {
        if (!(Environment.GetEnvironmentVariable("MIR2_CLIENT_CACHE") ?? "").Contains("mir2-npc-input.", StringComparison.Ordinal) || AssetsPortIsDefault())
            throw new InvalidOperationException("NPC输入检查仅允许隔离版本。");
        stage = "NPC input login";
        await Until(() => Client.HandshakeReady); Client.Login("NativeGM", "Native123");
        Client.Send(new C.StartGame { CharacterIndex = (await Packet<S.LoginSuccess>()).Characters.Single().Index });
        await Until(() => State.User != null && !Client.World.Loading);
        var merchant = State.Actors.Values.Single(actor => actor.Kind == ObjectType.Merchant);
        var dialog = (ConfirmationDialog)Client.FindChild("NpcInputDialog", true, false);
        var input = (LineEdit)dialog.FindChild("NpcInputValue", true, false);
        long barrier = 0;
        async Task Drain()
        {
            var token = --barrier; Client.Send(new C.KeepAlive { Time = token });
            await Until(() => packets.OfType<S.KeepAlive>().Any(packet => packet.Time == token));
        }
        async Task MainPage()
        {
            packets.RemoveAll(packet => packet is S.NPCResponse or S.NPCRequestInput);
            Client.Send(new C.CallNPC { ObjectID = merchant.Id, Key = "[@MAIN]" }); await Packet<S.NPCResponse>();
        }
        async Task Open()
        {
            await MainPage();
            if (((Control)Client.FindChild("NpcText", true, false)).FindChildren("*", "Label", true, false)
                .Cast<Label>().Any(label => label.Text.Length > 0 && label.Text.Trim('<', '>').Length == 0))
                throw new Exception("彩色双尖括号链接仍显示多余括号。");
            var link = ((Control)Client.FindChild("NpcText", true, false)).FindChildren("*", "LinkButton", true, false)
                .Cast<LinkButton>().Single(button => button.Text == "输入检查");
            link.EmitSignal(BaseButton.SignalName.Pressed);
            var request = await Packet<S.NPCRequestInput>();
            await Until(() => dialog.Visible);
            if (request.NPCID != merchant.Id || request.PageName != "[@@INPUT]" || input.Text.Length != 0 || input.MaxLength != 50)
                throw new Exception("原输入链接/编号/页面或50字限制错误。");
        }
        var before = Snapshot();
        stage = "original NPC input cancel, Chinese text, empty value and single submission";
        await Open(); input.Text = "不应执行"; dialog.EmitSignal(ConfirmationDialog.SignalName.Canceled);
        await Drain();
        if (dialog.Visible || packets.OfType<S.NPCResponse>().Any() || Snapshot() != before) throw new Exception("取消输入执行了脚本或改变资产。");
        await Open(); input.Text = " 中文 输入42 "; await Screenshot("npc-input");
        input.EmitSignal(LineEdit.SignalName.TextSubmitted, input.Text); dialog.EmitSignal(ConfirmationDialog.SignalName.Confirmed);
        var response = await Packet<S.NPCResponse>(); await Drain();
        if (!response.Page.Contains("输入结果：| 中文 输入42 |") || dialog.Visible || packets.OfType<S.NPCResponse>().Any())
            throw new Exception("输入未按原文进入原脚本，或回车/确定重复提交。");
        await Open(); dialog.EmitSignal(ConfirmationDialog.SignalName.Confirmed);
        if (!(await Packet<S.NPCResponse>()).Page.Contains("输入结果：||")) throw new Exception("原脚本合法空输入被阻止。");

        stage = "wrong NPC, long input and unoffered page rejected without stale input";
        foreach (var bad in new[]
        {
            new C.NPCConfirmInput { NPCID = merchant.Id + 1, PageName = "[@@INPUT]", Value = "错误会话" },
            new C.NPCConfirmInput { NPCID = merchant.Id, PageName = "[@@INPUT]", Value = new string('长', 51) },
            new C.NPCConfirmInput { NPCID = merchant.Id, PageName = "[@@HIDDEN]", Value = "未开放页面" }
        })
        {
            await MainPage(); Client.Send(bad); await Drain();
            if (packets.OfType<S.NPCResponse>().Any()) throw new Exception("服务端执行了无效NPC输入：" + bad.Value);
            Client.Send(new C.CallNPC { ObjectID = merchant.Id, Key = "[@@INPUT]" }); await Packet<S.NPCRequestInput>();
            if (packets.OfType<S.NPCResponse>().Any()) throw new Exception("拒绝输入后残留旧值绕过了新输入请求。");
            dialog.EmitSignal(ConfirmationDialog.SignalName.Canceled);
        }
        stage = "input invalidation on new conversation, lifecycle and default NPC update";
        await Open(); input.Text = "旧输入"; Client.Send(new C.CallNPC { ObjectID = merchant.Id, Key = "[@MAIN]" });
        await Packet<S.NPCResponse>(); dialog.EmitSignal(ConfirmationDialog.SignalName.Confirmed); await Drain();
        if (dialog.Visible || packets.OfType<S.NPCResponse>().Any()) throw new Exception("切换对话后旧输入仍可确认。");
        var handle = typeof(Main).GetMethod("NpcInputPacket", BindingFlags.Instance | BindingFlags.NonPublic)!;
        void Inject(Packet packet) => handle.Invoke(Client, [packet]);
        foreach (Packet reset in new Packet[] { new S.NPCUpdate { NPCID = merchant.Id + 100 }, new S.MapChanged(), new S.Death() })
        {
            await Open(); Inject(reset); dialog.EmitSignal(ConfirmationDialog.SignalName.Confirmed); await Drain();
            if (dialog.Visible || packets.OfType<S.NPCResponse>().Any()) throw new Exception("生命周期变化后仍可提交输入：" + reset.GetType().Name);
        }
        await Open(); Client.Send(new C.LogOut()); await Packet<S.LogOutSuccess>();
        if (dialog.Visible) throw new Exception("正常退出未清理输入弹窗。");
        GD.Print("PASS native NPC input: real original TCP request/script echo, Chinese/space/empty values, cancel and single submission; wrong NPC/length/link rejection and clean retry; injected map/death/session invalidation and real logout.");
        await Client.Quit();
    }
}
