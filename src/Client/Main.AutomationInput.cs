using Godot;
using Point = System.Drawing.Point;

public partial class Main
{
    private ConfirmationDialog stopBattleConfirmation = null!;
    private Action? pendingGroundAction;
    private Point? pendingGroundMove;
    private bool pendingGroundRun;
    private bool pendingGroundTeleport;

    private void BuildAutoBattleConfirmation()
    {
        stopBattleConfirmation = new ConfirmationDialog { Name = "StopBattleConfirmation", Title = "停止挂机", DialogText = "停止挂机并移动到所选位置？" };
        game.AddChild(stopBattleConfirmation);
        stopBattleConfirmation.GetOkButton().Text = "确定"; stopBattleConfirmation.GetCancelButton().Text = "取消";
        stopBattleConfirmation.Confirmed += () =>
        {
            var action = pendingGroundAction; pendingGroundAction = null;
            if (action != null) { if (scene == "game" && State.Me?.Dead == false) { StopAutoBattle(); action(); } return; }
            var destination = pendingGroundMove; var teleport = pendingGroundTeleport; pendingGroundMove = null;
            if (scene != "game" || State.Me?.Dead != false || destination == null || World.Loading || !World.Walkable(destination.Value)) return;
            StopAutoBattle();
            if (teleport) SendMapTeleport(destination.Value); else SetDestination(destination.Value, pendingGroundRun);
        };
        stopBattleConfirmation.Canceled += () => { pendingGroundMove = null; pendingGroundAction = null; };
    }

    private bool ConfirmGroundMove(Point destination, bool run, bool teleport = false)
    {
        if (!autoBattleEnabled) return false;
        if (!World.Walkable(destination)) { if (teleport) Log("目标坐标不可通行。"); return true; }
        pendingGroundAction = null; pendingGroundMove = destination; pendingGroundRun = run; pendingGroundTeleport = teleport;
        stopBattleConfirmation.DialogText = teleport ? "停止挂机并传送到所选坐标？" : "停止挂机并移动到所选位置？";
        stopBattleConfirmation.PopupCentered();
        return true;
    }

    private void CloseGroundMove()
    { pendingGroundAction = null; pendingGroundMove = null; pendingGroundTeleport = false; stopBattleConfirmation.Hide(); }
}
