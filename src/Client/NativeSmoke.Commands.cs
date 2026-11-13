using Godot;
using C = ClientPackets;
using S = ServerPackets;

namespace Crystal.Native;

public partial class NativeSmoke
{
    private async Task VerifyCommandFeedback()
    {
        stage = "chat command delivery, missing arguments, unknown names and GM denial feedback";
        var chat = Client.FindChildren("*", "LineEdit", true, false).Cast<LineEdit>().Single(edit => edit.MaxLength == Globals.MaxChatLength);
        var messages = (RichTextLabel)Client.FindChild("ChatMessages", true, false);
        for (var line = 0; line < 110; line++) Client.Log($"命令反馈历史 {line}");
        if (messages.GetParagraphCount() > 101 || !messages.GetParsedText().Contains("命令反馈历史 109") || messages.GetParsedText().Contains("命令反馈历史 0\n"))
            throw new Exception("Chat history trimming lost its latest entries or exceeded its bound.");
        foreach (var (command, expected) in new[] { ("@mm", "参数不足"), ("@NoSuchCommand", "未知命令"), ("@mm 0", "无权限"), ("@books Wizard", "无权限") })
        {
            packets.RemoveAll(packet => packet is S.Chat);
            chat.Text = command; chat.GrabFocus(); chat.EmitSignal(LineEdit.SignalName.TextSubmitted, command);
            await Until(() => packets.OfType<S.Chat>().Any(packet => packet.Message.Contains(expected)));
            if (!messages.GetParsedText().Contains(expected)) throw new Exception("Server command result did not reach the chat history.");
        }
        GD.Print("PASS native command feedback: @mm chat submission, parameter errors, unknown commands and original GM permission denial appear in chat.");
    }
}
