using Godot;

namespace Crystal.Native;

public partial class NativeSmoke
{
    private async Task VerifyQuestSelectionAndConfirmation(int id)
    {
        stage = "bounded long quest selection and lowered confirmation buttons";
        var definition = State.QuestDefinitions[id]; var original = definition.Name;
        try
        {
            definition.Name = new string('测', 100) + "任务";
            Client.OpenQuestJournal();
            var row = Client.FindChildren("*", "Button", true, false).OfType<Button>().Single(button => button.Text.Contains(definition.Name));
            await ClickSelectionControl(row);
            row = Client.FindChildren("*", "Button", true, false).OfType<Button>().Single(button => button.Text.Contains(definition.Name));
            var scroll = row.GetParent().GetParent<ScrollContainer>();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (!row.ButtonPressed || !row.ClipText || row.GetGlobalRect().End.X > scroll.GetGlobalRect().End.X || scroll.GetHScrollBar().Visible || row.Size.X > 280)
                throw new Exception("长任务名把选中范围或横向滚动撑出列表。");
            var heading = Client.FindChildren("*", "Label", true, false).OfType<Label>().Single(label => label.Text == definition.Name);
            if (!heading.ClipText || heading.TooltipText != definition.Name || heading.Size.X > 280 || heading.GetGlobalRect().End.X > heading.GetParent<Control>().GetGlobalRect().End.X)
                throw new Exception("长任务标题超出详情窗口或无法查看完整名称。");
            await Screenshot("quest-selection-bounds");
            var abandon = Client.FindChildren("*", "Button", true, false).OfType<Button>().Single(button => button.Text == "放弃任务");
            await ClickSelectionControl(abandon);
            var confirm = Client.FindChildren("*", "ConfirmationDialog", true, false).OfType<ConfirmationDialog>().Single(dialog => dialog.Title == "确认放弃任务");
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var ok = confirm.GetOkButton().GetGlobalRect(); var cancel = confirm.GetCancelButton().GetGlobalRect();
            if (!confirm.Visible || Math.Abs(ok.End.Y - confirm.Size.Y) > 2 || Math.Abs(ok.Position.Y - cancel.Position.Y) > 1)
                throw new Exception($"确认按钮未下移到底栏：size={confirm.Size}, ok={ok}, cancel={cancel}。");
            await Screenshot("quest-confirmation-footer");
            confirm.GetCancelButton().EmitSignal(BaseButton.SignalName.Pressed);
            if (!State.Quests.ContainsKey(id)) throw new Exception("取消确认仍然放弃任务。");
            await ClickSelectionControl(abandon); confirm.GetOkButton().EmitSignal(BaseButton.SignalName.Pressed);
        }
        finally { definition.Name = original; }
        Client.FindChildren("*", "Button", true, false).OfType<OriginalButton>().Single(button => button.TooltipText == "关闭任务").EmitSignal(BaseButton.SignalName.Pressed);
        GD.Print("PASS native quests: long selected rows stay inside the journal, ellipsis/tooltip, lowered aligned confirmation footer and cancel protection.");
    }
}
