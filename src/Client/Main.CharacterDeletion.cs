using Godot;
using C = ClientPackets;

public partial class Main
{
    private ConfirmationDialog characterDeletion = null!;
    private Button deleteCharacterButton = null!;
    private int deleteCandidate = -1, pendingCharacterDeletion = -1;
    private long characterDeleteSentAt;

    private void BuildCharacterDeletion()
    {
        deleteCharacterButton = Button(selection, "删除角色", 460, 736, 100, OpenCharacterDeletion);
        deleteCharacterButton.Name = "DeleteCharacter";
        characterDeletion = new ConfirmationDialog { Name = "DeleteCharacterDialog", Title = "确认删除角色", MinSize = new Vector2I(360, 160) };
        characterDeletion.GetOkButton().Text = "删除角色"; characterDeletion.GetCancelButton().Text = "取消";
        selection.AddChild(characterDeletion);
        characterDeletion.Confirmed += DeleteSelectedCharacter;
        characterDeletion.Canceled += () => deleteCandidate = -1;
    }

    private void OpenCharacterDeletion()
    {
        if (scene != "selection" || characterCreateSentAt != 0 || pendingCharacterDeletion >= 0 ||
            State.Characters.ElementAtOrDefault(selectedCharacter) is not { } character) return;
        deleteCandidate = character.Index;
        characterDeletion.DialogText = $"确定删除角色“{character.Name}”吗？\n等级 {character.Level} · {character.Class.ToLocalizedString()}\n删除后该角色将无法进入游戏，请谨慎操作。";
        characterDeletion.PopupCentered();
    }

    private void DeleteSelectedCharacter()
    {
        if (scene != "selection" || characterCreateSentAt != 0 || pendingCharacterDeletion >= 0 ||
            !State.Characters.Any(character => character.Index == deleteCandidate)) return;
        if (!HandshakeReady || !Connection.Connected) { selectionNotice.Text = "连接已断开，请重新登录。"; return; }
        pendingCharacterDeletion = deleteCandidate; deleteCandidate = -1; characterDeleteSentAt = Environment.TickCount64;
        deleteCharacterButton.Disabled = true; selectionNotice.Text = "正在删除角色，请稍候…";
        Send(new C.DeleteCharacter { CharacterIndex = pendingCharacterDeletion });
    }

    private void CharacterDeleted(int index)
    {
        if (pendingCharacterDeletion == index) pendingCharacterDeletion = -1;
        characterDeleteSentAt = 0; RenderCharacters();
        selectionNotice.Text = "角色已删除。";
    }

    private void CharacterDeletionFailed(byte result)
    {
        pendingCharacterDeletion = -1; characterDeleteSentAt = 0;
        deleteCharacterButton.Disabled = State.Characters.Count == 0;
        selectionNotice.Text = result == 0 ? "服务器暂不允许删除角色，请联系管理员。" : "未找到该账户下的角色，请重新登录核对。";
    }

    private void ResetCharacterDeletion()
    {
        deleteCandidate = pendingCharacterDeletion = -1; characterDeleteSentAt = 0;
        characterDeletion.Hide(); deleteCharacterButton.Disabled = State.Characters.Count == 0;
    }
}
