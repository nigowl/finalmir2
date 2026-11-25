using Crystal.Native;
using Godot;

public partial class Main
{
    private readonly HashSet<Key> movementKeys = [];
    private bool keyboardMoving;
    private MirDirection? KeyboardDirection => ClientMovement.KeyboardDirection(movementKeys.Contains(Key.Up), movementKeys.Contains(Key.Down), movementKeys.Contains(Key.Left), movementKeys.Contains(Key.Right));

    private bool KeyboardInput(InputEvent input)
    {
        if (input is not InputEventKey key || key.PhysicalKeycode is not (Key.Up or Key.Down or Key.Left or Key.Right)) return false;
        if (!key.Pressed)
        {
            movementKeys.Remove(key.PhysicalKeycode);
            if (KeyboardDirection == null) StopKeyboardMovement();
            return false;
        }
        if (key.Echo || key.CtrlPressed || key.AltPressed || key.MetaPressed || settingsDialog.Visible || MiniMap.Expanded ||
            GetViewport().GuiGetFocusOwner() is LineEdit or TextEdit) return false;
        if (autoBattleEnabled)
        {
            var dir = ClientMovement.KeyboardDirection(key.PhysicalKeycode == Key.Up, key.PhysicalKeycode == Key.Down, key.PhysicalKeycode == Key.Left, key.PhysicalKeycode == Key.Right)!.Value;
            ConfirmGroundMove(Functions.PointMove(State.User!.Location, dir, 1), preferences.DefaultRun);
        }
        else movementKeys.Add(key.PhysicalKeycode);
        GetViewport().SetInputAsHandled(); return true;
    }

    private void StopKeyboardMovement()
    {
        movementKeys.Clear();
        if (!keyboardMoving) return;
        keyboardMoving = false; destination = null; MiniMap.Route = [];
    }

    private void ProcessKeyboardMovement()
    {
        if (movementKeys.Count == 0) return;
        if (State.Me?.Dead != false || settingsDialog.Visible || MiniMap.Expanded || State.Trading || State.StorageActive || npc.Visible ||
            GuildStorageOpen || GetViewport().GuiGetFocusOwner() is LineEdit or TextEdit || Window.GetFocusedWindow() != GetWindow())
        { StopKeyboardMovement(); return; }
        if (moving) return;
        if (State.InTrapRock) return;
        if (KeyboardDirection is not { } direction) { if (keyboardMoving) { destination = null; MiniMap.Route = []; } return; }
        if (!keyboardMoving) { CancelQuestNavigation(); keyboardMoving = true; actionTarget = 0; World.Selected = 0; autoPickupTarget = 0; pendingAttack = null; }
        running = preferences.DefaultRun;
        var steps = running && ClientMovement.CanRun(State, true, lastMoved, Environment.TickCount64) ? ClientMovement.RunDistance(State) : 1;
        var point = State.User!.Location;
        // 只延伸当前方向，不交给寻路绕行；逐格检查，碰墙时停住。
        for (var step = 0; step < steps; step++)
        {
            var next = Functions.PointMove(point, direction, 1);
            if (!World.Walkable(next) || State.Actors.Values.Any(actor => actor.Id != State.User.ObjectID && actor.Location == next &&
                !actor.Dead && !actor.Hidden && actor.Kind is not (ObjectType.Item or ObjectType.Spell))) break;
            point = next;
        }
        destination = point; MiniMap.Route = [];
    }
}
