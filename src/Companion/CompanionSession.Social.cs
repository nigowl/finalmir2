using C = ClientPackets;
using S = ServerPackets;

namespace Mir2.Companion;

public sealed partial class CompanionSession
{
    private long nextChat;
    private string llmContext="",llmFallback="",llmReply="",llmPrefix="";
    private long llmDeadline;
    private bool llmAnswered;
    private string lastLlmChat="";
    private long sameLlmChatUntil;
    public void LlmResult(string context,string text)
    {
        if(context==start.Run+"/"+llmContext&&Environment.TickCount64<llmDeadline&&text.Length<=120&&!text.Any(char.IsControl)&&!text.TrimStart().StartsWith('@')&&!text.TrimStart().StartsWith('/')&&!text.TrimStart().StartsWith('!')){llmReply=text;llmAnswered=true;}
    }
    private async Task PendingSpeech(long now)
    {
        if(llmContext.Length==0)return;
        if(!llmAnswered&&llmReply.Length==0&&now<llmDeadline)return;
        var generated=llmReply.Length>0;var text=generated?llmReply:llmFallback;llmContext="";llmReply="";
        var sent=state.Me?.Dead==false&&Profile.Chat&&await Say(llmPrefix+text);
        if(generated)await report(new("llm-outcome",Profile.Id,sent?"已采用":"未采用","聊天回复",sent?"已通过原TCP发送模型回复。":"角色状态或发言间隔改变，未发送模型回复。"));
    }
    private async Task Social(Packet packet)
    {
        if(packet is S.GroupInvite && state.GroupInvitation.Length>0)
        {
            var accept=Profile.AcceptGroup && (Knowledge?.Party?.Peers.Contains(state.GroupInvitation)==true || Profile.Traits.Social+Profile.Traits.Helpfulness>=70);
            await connection.Send(new C.GroupInvite{AcceptInvite=accept});
            if(accept)await Say("!!一起走，我会先准备好补给。");
        }
        var prefix="";var sender="";
        var chat=packet as S.ObjectChat;
        if(packet is S.Chat {Type:ChatType.WhisperIn} whisper)
        {
            var separator=whisper.Message.IndexOf("=>",StringComparison.Ordinal);
            if(separator<=0)return;
            sender=whisper.Message[..separator];
            if(sender.Any(c=>char.IsWhiteSpace(c)||char.IsControl(c)||c is '/'or'!'or'@'))return;
            prefix="/"+sender+" ";chat=new(){Text=whisper.Message[(separator+2)..],Type=ChatType.WhisperIn};
        }
        if(!Profile.Chat || chat==null || chat.ObjectID==state.User?.ObjectID || chat.Type is not (ChatType.Normal or ChatType.Group or ChatType.WhisperIn))return;
        if(chat.Type==ChatType.Group)prefix="!!";
        if(sender.Length==0)sender=state.Actors.GetValueOrDefault(chat.ObjectID)?.Name??"";
        if(Profile.PkMode=="duel"&&Knowledge?.AllowPk==true&&!Knowledge.InSafeZone&&!Knowledge.NoFight&&chat.Type==ChatType.Normal&&
            (chat.Text.EndsWith(Profile.Name+"，切磋",StringComparison.Ordinal)||chat.Text.EndsWith(Profile.Name+" 切磋",StringComparison.Ordinal))&&
            state.Actors.GetValueOrDefault(chat.ObjectID) is {Kind:ObjectType.Player,Dead:false,Hidden:false} challenger&&
            !state.GroupMembers.Contains(challenger.Name))
        {await ConsiderDuel(challenger,Environment.TickCount64);return;}
        if(Environment.TickCount64<nextChat || llmContext.Length>0 || chat.Type!=ChatType.WhisperIn&&!chat.Text.Contains(Profile.Name,StringComparison.Ordinal))return;
        var text=Profile.Traits.Talkative<30?"你好。":Profile.Traits.Helpfulness>75?"你好，需要一起打怪吗？我会量力而行。":"你好，我在附近发展，补好药再出发。";
        var repeated=sender+"/"+chat.Text.Trim();
        if(Profile.LlmMode>0&&(repeated!=lastLlmChat||Environment.TickCount64>=sameLlmChatUntil))
        {
            lastLlmChat=repeated;sameLlmChatUntil=Environment.TickCount64+600000;
            llmContext=Guid.NewGuid().ToString("N");llmFallback=text;llmReply="";llmAnswered=false;llmPrefix=prefix;llmDeadline=Environment.TickCount64+6500;
            await report(new("llm",Profile.Id,State:sender,Reason:llmContext,Text:chat.Text));
        }
        else await Say(prefix+text);
    }
    private async Task<bool> Say(string text)
    {
        if(Environment.TickCount64<nextChat)return false;
        nextChat=Environment.TickCount64+60000+(100-Profile.Traits.Talkative)*1000;
        await connection.Send(new C.Chat{Message=text});
        await report(new("social",Profile.Id,Text:text));
        return true;
    }
}
