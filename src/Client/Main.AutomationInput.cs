using Godot;
using Point = System.Drawing.Point;

public partial class Main
{
    private ConfirmationDialog stopBattleConfirmation = null!;
    private Point? pendingGroundMove;
    private bool pendingGroundRun;

    private void BuildAutoBattleConfirmation()
    {
        stopBattleConfirmation = new ConfirmationDialog { Name = "StopBattleConfirmation", Title = "停止挂机", DialogText = "停止挂机并移动到所选位置？" };
        game.AddChild(stopBattleConfirmation);
        stopBattleConfirmation.GetOkButton().Text = "确定"; stopBattleConfirmation.GetCancelButton().Text = "取消";
        stopBattleConfirmation.Confirmed += () =>
        {
            var destination = pendingGroundMove; pendingGroundMove = null;
            if (scene != "game" || State.Me?.Dead != false || destination == null || World.Loading || !World.Walkable(destination.Value)) return;
            StopAutoBattle(); SetDestination(destination.Value, pendingGroundRun);
        };
        stopBattleConfirmation.Canceled += () => pendingGroundMove = null;
    }

    private bool ConfirmGroundMove(Point destination, bool run)
    {
        if (!autoBattleEnabled) return false;
        if (!World.Walkable(destination)) return true;
        pendingGroundMove = destination; pendingGroundRun = run;
        stopBattleConfirmation.PopupCentered();
        return true;
    }

    private void CloseGroundMove()
    { pendingGroundMove = null; stopBattleConfirmation.Hide(); }
}
