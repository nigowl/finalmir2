using Godot;
using C = ClientPackets;
using S = ServerPackets;

namespace Crystal.Native;

public partial class NativeSmoke
{
    private async Task VerifyChatDisplay()
    {
        stage = "实际频道切换与独立历史";
        var channels = (Control)Client.FindChild("ChatChannels", true, false);
        var messages = (RichTextLabel)Client.FindChild("ChatMessages", true, false);
        foreach (var (channel, type) in new[] { (ChatChannel.Public, ChatType.Normal), (ChatChannel.Whisper, ChatType.WhisperIn),
            (ChatChannel.Group, ChatType.Group), (ChatChannel.Guild, ChatType.Guild), (ChatChannel.Shout, ChatType.Shout),
            (ChatChannel.System, ChatType.System), (ChatChannel.Relationship, ChatType.Relationship), (ChatChannel.Mentor, ChatType.Mentor) })
            Client.Log($"频道验证{channel}消息", type);
        foreach (var channel in Enum.GetValues<ChatChannel>())
        {
            await ClickSelectionControl(channels.GetNode<Button>("ChatChannel" + channel));
            var text = messages.GetParsedText();
            foreach (var source in Enum.GetValues<ChatChannel>().Where(c => c != ChatChannel.All))
                if (text.Contains($"频道验证{source}消息", StringComparison.Ordinal) != (channel == ChatChannel.All || channel == source))
                    throw new Exception($"频道{channel}仍显示{source}历史或未刷新。");
        }
        await ClickSelectionControl(channels.GetNode<Button>("ChatChannelPublic"));
        stage = "公共聊天原ObjectChat头顶气泡";
        var content = "公共气泡" + new string('测', 65);
        packets.RemoveAll(p => p is S.ObjectChat);
        Client.Send(new C.Chat { Message = content });
        await Until(() => packets.OfType<S.ObjectChat>().Any(p => p.ObjectID == State.User!.ObjectID && p.Type == ChatType.Normal && p.Text.Contains(content)));
        await Until(() => Client.World.ChatBubblesDrawn > 0);
        var bubble = Client.World.ChatBubbles[State.User!.ObjectID];
        if (bubble.Width > 180 || bubble.Lines.Length != 2 || !bubble.Lines[^1].EndsWith('…') || bubble.Lines[0].Contains(State.User.Name))
            throw new Exception("公共气泡没有限宽/两行省略或重复显示角色名。");
        await Screenshot("public-chat-bubble");
        Client.World.SpellPacket(new S.ObjectChat { ObjectID = State.User.ObjectID, Type = ChatType.WhisperIn, Text = "私信不得出现在头顶" });
        Client.World.SpellPacket(new S.ObjectChat { ObjectID = 0x7fffffff, Type = ChatType.Normal, Text = "未知对象" });
        if (Client.World.ChatBubbles.Count != 1 || Client.World.ChatBubbles[State.User.ObjectID] != bubble) throw new Exception("私信或未知对象创建/替换了公共气泡。");
        await Until(() => Client.World.ChatBubblesDrawn == 0);
        Client.World.SpellPacket(new S.MapChanged());
        if (Client.World.ChatBubbles.Count != 0) throw new Exception("切图保留旧聊天气泡。");
        GD.Print("PASS native chat channels: nine actual mouse switches filter history; original TCP public bubble <=180px/two lines/ellipsis, expiry, private/unknown and map cleanup.");
    }
}
