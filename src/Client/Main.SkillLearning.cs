using Godot;
using C = ClientPackets;
using S = ServerPackets;

public partial class Main
{
    private ConfirmationDialog learnSkillDialog = null!;
    private ulong learningBook;
    private S.UserInformation? learningSubject;
    private MirGridType learningGrid;

    private void BuildSkillLearning()
    {
        learnSkillDialog = new ConfirmationDialog { Name = "LearnSkillConfirmation", Title = "确认学习技能", MinSize = new Vector2I(360, 150) };
        game.AddChild(learnSkillDialog);
        learnSkillDialog.GetLabel().AutowrapMode = TextServer.AutowrapMode.WordSmart;
        learnSkillDialog.GetLabel().CustomMinimumSize = new Vector2(328, 64);
        learnSkillDialog.GetOkButton().Text = "学习"; learnSkillDialog.GetCancelButton().Text = "取消";
        learnSkillDialog.Canceled += CancelSkillLearning;
        learnSkillDialog.CloseRequested += CancelSkillLearning;
        learnSkillDialog.Confirmed += () =>
        {
            var id = learningBook; var subject = learningSubject; var grid = learningGrid;
            CancelSkillLearning();
            if (!ReferenceEquals(subject, EquipmentSubject) || grid != SubjectGrid || State.Me?.Dead != false ||
                State.User?.Inventory.Any(item => item?.UniqueID == id && item.Info.Type == ItemType.Book && item.Count > 0) != true) return;
            SendSubjectItem(new C.UseItem { Grid = grid, UniqueID = id });
        };
    }

    private void ConfirmSkillLearning(UserItem item)
    {
        if (learnSkillDialog.Visible || EquipmentSubject == null || heroItemPending != null) return;
        learningBook = item.UniqueID; learningSubject = EquipmentSubject; learningGrid = SubjectGrid;
        var subject = HeroInventoryContext ? $"英雄「{learningSubject.Name}」" : $"角色「{learningSubject.Name}」";
        learnSkillDialog.DialogText = $"确认让{subject}学习「{item.Info.FriendlyName}」？\n学习成功后消耗1本技能书。";
        learnSkillDialog.PopupCentered();
    }

    private void CancelSkillLearning()
    {
        learningBook = 0; learningSubject = null; learnSkillDialog?.Hide();
    }

    private void SkillLearningPacket(Packet packet)
    {
        if (packet is S.MapChanged or S.Death or S.Disconnect or S.LogOutSuccess or S.ReturnToLogin or S.UserInformation ||
            learningGrid == MirGridType.HeroInventory && packet is S.UpdateHeroSpawnState { State: not HeroSpawnState.Summoned }) CancelSkillLearning();
    }
}
