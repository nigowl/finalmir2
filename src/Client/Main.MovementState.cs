using C = ClientPackets;

public partial class Main
{
    private readonly Dictionary<byte, long> doorRequests = new();

    private bool EnsureDoorsOpen(MirDirection direction, int distance, long now)
    {
        var ready = true;
        for (var step = 1; step <= distance; step++)
        {
            var point = Functions.PointMove(State.User!.Location, direction, step);
            var door = World.Map!.MapCells[point.X, point.Y].DoorIndex;
            if (door == 0 || State.OpenDoors.Contains(door)) continue;
            ready = false;
            if (!doorRequests.TryGetValue(door, out var sent))
            {
                doorRequests[door] = now;
                Send(new C.Opendoor { DoorIndex = door });
            }
            else if (now - sent >= 3000)
            {
                StopKeyboardMovement(); destination = null; actionTarget = 0; MiniMap.Route = [];
                doorRequests.Remove(door); ResetRouteRetry();
                Log("未收到开门确认，移动已停止，请重新选择目标。", ChatType.Hint);
                return false;
            }
        }
        return ready;
    }
}
