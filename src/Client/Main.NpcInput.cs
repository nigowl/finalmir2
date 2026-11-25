using Crystal.Native;
using Godot;
using C = ClientPackets;
using S = ServerPackets;

public partial class Main
{
    private ConfirmationDialog npcInput = null!;
    private LineEdit npcInputValue = null!;
    private Label npcInputPrompt = null!;
    private S.NPCRequestInput? npcInputRequest;
    private uint guildWarInputNpc, guildCreateInputNpc;

    private void BuildNpcInput()
    {
        npcInput = new ConfirmationDialog { Name = "NpcInputDialog", Title = "NPC输入", Exclusive = true,
            Transient = true, Unresizable = true, DialogHideOnOk = false, MinSize = new Vector2I(360, 150) };
        game.AddChild(npcInput); npcInput.GetOkButton().Text = "确定"; npcInput.GetCancelButton().Text = "取消";
        var content = new VBoxContainer(); content.AddThemeConstantOverride("separation", 8); npcInput.AddChild(content);
        npcInputPrompt = new Label { Text = "请输入所需的信息（最多50字）。", CustomMinimumSize = new Vector2(328, 0),
            AutowrapMode = TextServer.AutowrapMode.WordSmart }; content.AddChild(npcInputPrompt);
        npcInputValue = new LineEdit { Name = "NpcInputValue", MaxLength = 50, CustomMinimumSize = new Vector2(328, 28) };
        content.AddChild(npcInputValue);
        npcInputValue.TextSubmitted += _ => SubmitNpcInput(); npcInput.Confirmed += SubmitNpcInput;
        npcInput.Canceled += CloseNpcInput; npcInput.CloseRequested += CloseNpcInput;
    }

    private void CloseNpcInput()
    { npcInputRequest = null; guildWarInputNpc = guildCreateInputNpc = 0; npcInput?.Hide(); npcInputValue?.Clear(); }

    private void SubmitNpcInput()
    {
        if (guildWarInputNpc != 0 || guildCreateInputNpc != 0)
        {
            var creating = guildCreateInputNpc != 0;
            if (!npcInput.Visible || !HandshakeReady || State.Me?.Dead != false ||
                (creating ? State.InGuild : !State.InGuild || State.Guild?.MyRankId != 0) ||
                (creating ? guildCreateInputNpc : guildWarInputNpc) != npcDialogueId) { CloseNpcInput(); return; }
            var name = npcInputValue.Text.Trim();
            if (name.Length is < 3 or > 20 || name.Any(char.IsControl) || name.Contains('\\'))
            { npcInputPrompt.Text = "行会名称须为3–20字，不能包含反斜杠或控制字符。"; return; }
            CloseNpcInput();
            if (creating) Send(new C.GuildNameReturn { Name = name });
            else Send(new C.GuildWarReturn { Name = name });
            Log(creating ? "建会请求已发送，请以服务器会籍及费用回执为准；不会自动重发。" :
                "宣战请求已发送，请以服务器消息和行会资金回执为准；不会自动重发。", ChatType.Hint);
            return;
        }
        if (npcInputRequest is not { } request || !npcInput.Visible || !HandshakeReady || State.Me?.Dead != false ||
            request.NPCID != npcDialogueId) { CloseNpcInput(); return; }
        var value = npcInputValue.Text;
        CloseNpcInput();
        Send(new C.NPCConfirmInput { NPCID = request.NPCID, PageName = request.PageName, Value = value });
    }

    private void NpcInputPacket(Packet packet)
    {
        switch (packet)
        {
            case S.NPCRequestInput request when scene == "game" && State.Me?.Dead == false && request.NPCID != 0 &&
                request.PageName.StartsWith("[@@", StringComparison.Ordinal):
                CloseNpcInput(); npcDialogueId = request.NPCID; npcInputRequest = request;
                npcInput.Title = "NPC输入"; npcInputPrompt.Text = "请输入所需的信息（最多50字）。";
                npcInputValue.MaxLength = 50; npcInput.GetOkButton().Text = "确定";
                StopKeyboardMovement(); destination = null; actionTarget = 0; pendingAttack = null; MiniMap.Route = [];
                npcInput.PopupCentered(); npcInputValue.GrabFocus(); break;
            case S.GuildRequestWar when scene == "game" && State.Me?.Dead == false && State.InGuild && State.Guild?.MyRankId == 0 && npcDialogueId != 0:
                CloseNpcInput(); guildWarInputNpc = npcDialogueId;
                npcInput.Title = "行会宣战"; npcInputPrompt.Text = "输入目标行会名称（3–20字）。确认将按原NPC说明扣除行会战争费用。";
                npcInputValue.MaxLength = 20; npcInput.GetOkButton().Text = "宣战";
                StopKeyboardMovement(); destination = null; actionTarget = 0; pendingAttack = null; MiniMap.Route = [];
                npcInput.PopupCentered(); npcInputValue.GrabFocus(); break;
            case S.GuildNameRequest when scene == "game" && State.Me?.Dead == false && !State.InGuild && npcDialogueId != 0:
                CloseNpcInput(); guildCreateInputNpc = npcDialogueId;
                npcInput.Title = "创建行会"; npcInputPrompt.Text = "输入行会名称（3–20字）。确认将按原NPC说明消耗金币和建会材料。";
                npcInputValue.MaxLength = 20; npcInput.GetOkButton().Text = "创建";
                StopKeyboardMovement(); destination = null; actionTarget = 0; pendingAttack = null; MiniMap.Route = [];
                npcInput.PopupCentered(); npcInputValue.GrabFocus(); break;
            case S.GuildStatus when guildCreateInputNpc != 0 && State.InGuild:
                CloseNpcInput(); break;
            case S.GuildStatus or S.GuildMemberChange when guildWarInputNpc != 0 && (!State.InGuild || State.Guild?.MyRankId != 0):
                CloseNpcInput(); break;
            case S.NPCUpdate update:
                if (npcDialogueId != update.NPCID) { CloseNpcInput(); InvalidateNpcPurchase(); }
                npcDialogueId = update.NPCID; break;
            case S.NPCResponse or S.MapChanged or S.Death or S.Disconnect or S.LogOutSuccess or S.ReturnToLogin:
                CloseNpcInput(); break;
        }
    }
}
