using Client.MirObjects;
using Crystal.Native;
using Point = System.Drawing.Point;
using Vector2 = System.Numerics.Vector2;
using S = ServerPackets;

internal static class ClientAnimationChecks
{
    public static void Run(Action<bool, string> check)
    {
        var state = new GameState();
        state.Apply(new S.UserInformation { ObjectID = 1, Location = new Point(10, 10), Inventory = [], Equipment = new UserItem[14], QuestInventory = [] });
        var actor = state.Me!;
        foreach (var action in new[] { MirAction.Attack1, MirAction.Spell, MirAction.Harvest })
        {
            actor.Action = action; actor.ActionTime = 1000;
            state.Apply(new S.UserLocation { Location = new Point(10, 10), Direction = MirDirection.Right });
            check(actor.Action == action && actor.ActionTime == 1000, "An unchanged location acknowledgement interrupted an action");
        }
        state.Apply(new S.UserLocation { Location = new Point(11, 10), Direction = MirDirection.Right });
        check(actor.Action == MirAction.Walking && actor.Previous == new Point(10, 10), "A changed location failed to start an authoritative walk");
        actor.MotionTime = 2000;
        check(actor.PositionAt(2000, Actor.MoveDuration) == new Vector2(10, 10), "Walk did not begin at the previous tile");
        check(actor.PositionAt(2300, Actor.MoveDuration) == new Vector2(10.5f, 10), "Walk did not interpolate through the entire server movement period");
        check(actor.PositionAt(2550, Actor.MoveDuration).X > 10.9f && actor.PositionAt(2550, Actor.MoveDuration).X < 11, "Walk finished early and left an idle gap");
        state.Apply(new S.UserLocation { Location = new Point(11, 10), Direction = MirDirection.Right });
        check(actor.MotionTime == 2000 && actor.Action == MirAction.Walking, "A duplicate movement acknowledgement restarted interpolation");
        var end = actor.PositionAt(2600, Actor.MoveDuration);
        state.Apply(new S.UserLocation { Location = new Point(13, 10), Direction = MirDirection.Right });
        actor.MotionTime = 2600;
        check(actor.Action == MirAction.Running && actor.PositionAt(2600, Actor.MoveDuration) == end, "Walk/run transition jumped between tiles");
        check(actor.PositionAt(2900, Actor.MoveDuration) == new Vector2(12, 10), "Running failed to interpolate both tiles");
        check(actor.PositionAt(9999, Actor.MoveDuration) == new Vector2(13, 10), "Movement overshot its server-confirmed endpoint");
        var runner = new Actor { Kind = ObjectType.Player, Location = new Point(10, 10) };
        runner.Move(new Point(11, 10), MirDirection.Right, MirAction.Walking, 1000);
        runner.Move(new Point(13, 10), MirDirection.Right, MirAction.Running, 1640);
        check(runner.MovementDuration(600) == 640 && runner.PositionAt(2250, 600).X is > 12.8f and < 13, "Player stopped before the next normal movement acknowledgement");
        var visual = runner.PositionAt(2260, 600);
        runner.Move(new Point(15, 10), MirDirection.Right, MirAction.Running, 2260);
        check(runner.PositionAt(2260, 600) == visual, "A player acknowledgement snapped to the previous logical tile");
        check(runner.PositionAt(10000, 600) == new Vector2(15, 10), "Smoothed player moved beyond its authoritative endpoint");
        runner.Poison = PoisonType.Slow;
        check(runner.MoveDelay == 1200, "A slowed player lost the original doubled movement delay");
        runner.Poison = PoisonType.None;
        runner.Move(new Point(16, 10), MirDirection.Right, MirAction.Walking, 4000);
        check(runner.MovementDuration(600) == 600, "A stopped player resumed with a stale slow movement interval");
        runner.Move(new Point(14, 10), MirDirection.Left, MirAction.Jump, 4500);
        check(runner.MovementDuration(800) == 800, "Ordinary movement smoothing changed a skill movement duration");
        runner.Poison = PoisonType.Slow;
        check(runner.MovementDuration(800) == 800, "Ordinary slow-walk timing changed a skill movement duration");
        check(MapTileLibraries.Resolve(28) == "Map/WemadeMir2/Objects27" && MapTileLibraries.Resolve(100) == "Map/ShandaMir2/Tiles" &&
            MapTileLibraries.Resolve(374) == "" && MapTileLibraries.Resolve(375) == "" && MapTileLibraries.Resolve(-1) == "", "Map library caching changed original indices or bounds");
        check(MapTileLibraries.Resolve(200) == "Map/WemadeMir3/Tilesc" && MapTileLibraries.Resolve(360) == "Map/ShandaMir3/Tilescforest", "Map library caching changed original Mir3 themes");
        var guard = new Actor { Kind = ObjectType.Monster, Location = new Point(10, 10) };
        guard.Move(new Point(11, 10), MirDirection.Right, MirAction.Walking, 1000);
        var inFlight = guard.PositionAt(1500, 600);
        guard.Move(new Point(12, 10), MirDirection.Right, MirAction.Walking, 1500);
        check(guard.PositionAt(1500, 600) == inFlight, "A fast patrol packet snapped the guard to its logical previous tile");
        check(guard.MovementDuration(600) == 500 && guard.PositionAt(1990, 600).X > 11.9f, "Patrol did not follow its authoritative fast step cadence");
        guard.Move(new Point(13, 10), MirDirection.Right, MirAction.Walking, 2500);
        check(guard.MovementDuration(600) == 1000 && guard.PositionAt(3300, 600).X is > 12.7f and < 13, "Slow patrol finished its animation early and paused between steps");
        guard.Move(new Point(13, 10), MirDirection.Right, MirAction.Walking, 2900);
        check(guard.MotionTime == 2500, "Duplicate patrol packets restarted the same movement");
        check(guard.PositionAt(10000, 600) == new Vector2(13, 10), "Patrol extrapolated beyond the server location");
        guard.Move(new Point(30, 30), MirDirection.Down, MirAction.Standing, 10000);
        check(guard.PositionAt(10000, 600) == new Vector2(30, 30), "A patrol teleport interpolated through unrelated cells");
        state.Apply(new S.MapChanged { Location = new Point(5, 7), FileName = "other" });
        check(state.Me!.PositionAt(3000, Actor.MoveDuration) == new Vector2(5, 7), "Changing maps interpolated across the teleport");
        var attack = FrameSet.Player[MirAction.Attack1];
        check(attack.Duration == Actor.MoveDuration && attack.IndexAt(550) == 5, "The final attack frame was truncated");
        check(Enumerable.Range(0, attack.Count).Select(i => attack.IndexAt(i * attack.Interval)).SequenceEqual(Enumerable.Range(0, attack.Count)), "Attack skipped animation frames");
        check(attack.IndexAt(600) == 5 && attack.IndexAt(1200) == 5, "A one-shot animation wrapped back to its first frame");
        var standing = FrameSet.Player[MirAction.Standing];
        check(standing.IndexAt(standing.Duration, true) == 0, "Standing animation stopped looping");
        var reverse = FrameSet.Player[MirAction.Revive];
        check(reverse.IndexAt(0) == reverse.Count - 1 && reverse.IndexAt(reverse.Duration) == 0, "Reverse animation ignored the original frame metadata");
        check(attack.IndexAt(-1) == 0, "A future action timestamp selected an invalid frame");
        var bounds = MapViewBounds.Around(new Point(100, 100), 800, 800);
        check(bounds.ObjectToY >= 130 && bounds.FloorToY < 130, "An offscreen building anchor was clipped with the ground layer");
        var buildingTop = 352 + (130 - 100 + 1) * 32 - 960;
        check(buildingTop < 616 && bounds.ObjectToY == 142, "The original tall-building overhang no longer covers the visible roof");
        var edge = MapViewBounds.Around(Point.Empty, 8, 6);
        check(edge.FromX == 0 && edge.FromY == 0 && edge.ToX == 7 && edge.FloorToY == 5 && edge.ObjectToY == 5, "Map rendering escaped a small map boundary");
        edge = MapViewBounds.Around(new Point(799, 799), 800, 800);
        check(edge.ToX == 799 && edge.ObjectToY == 799 && edge.FromX >= 0 && edge.FromY >= 0, "Map rendering escaped the bottom-right boundary");
        check(OriginalSoundRules.Swing(1) == 10050 && OriginalSoundRules.Swing(4) == 10054 && OriginalSoundRules.Swing(-1) == 10056 && OriginalSoundRules.Swing(4, true) == 10050, "Weapon swings differ from the original sound rules");
        var cell = new CellInfo { BackImage = 1 };
        check(OriginalSoundRules.Step(cell, false, false) == 10001 && OriginalSoundRules.Step(cell, true, true) == 10004, "Walk/run footsteps lost the original left/right sound sequence");
        cell.FrontImage = 222;
        check(OriginalSoundRules.Step(cell, true, false) == 10007, "Stone floor footsteps ignored the original foreground tile override");
        var location = new S.CharacterLocation { CharacterIndex = 7, Found = true, MapTitle = "比奇", Location = new Point(299, 623) };
        var wasServer = Packet.IsServer;
        try
        {
            Packet.IsServer = false;
            var decoded = (S.CharacterLocation)Packet.ReceivePacket(location.GetPacketBytes().ToArray(), out var extra);
            check(decoded.CharacterIndex == 7 && decoded.Found && decoded.MapTitle == "比奇" && decoded.Location == location.Location && extra.Length == 0, "Character location response changed its shared wire contract");
            Packet.IsServer = true;
            var request = new ClientPackets.CharacterLocationRequest { CharacterIndex = 7 };
            check(Packet.ReceivePacket(request.GetPacketBytes().ToArray(), out extra) is ClientPackets.CharacterLocationRequest { CharacterIndex: 7 } && extra.Length == 0, "Character location request did not decode on the original TCP framer");
            check((short)ServerPacketIds.CharacterLocation == (short)ServerPacketIds.StoragePasswordResult + 1 && (short)ClientPacketIds.CharacterLocationRequest == (short)ClientPacketIds.RemoveStoragePassword + 1, "Optional character-location packets renumbered an existing protocol packet");
        }
        finally { Packet.IsServer = wasServer; }
    }
}
