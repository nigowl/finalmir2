using Client.MirObjects;
using Crystal.Native;
using Point = System.Drawing.Point;
using Vector2 = System.Numerics.Vector2;
using S = ServerPackets;

internal static class ClientAnimationChecks
{
    public static void Run(Action<bool, string> check)
    {
        GridMovement(check);
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
        check(actor.PositionAt(2000 + actor.MovementDuration(600) / 2, Actor.MoveDuration) == new Vector2(10.5f, 10), "Walk did not interpolate through the entire server movement period");
        check(actor.PositionAt(2000 + actor.MovementDuration(600) - 20, Actor.MoveDuration).X > 10.9f && actor.PositionAt(2000 + actor.MovementDuration(600) - 20, Actor.MoveDuration).X < 11, "Walk finished early and left an idle gap");
        state.Apply(new S.UserLocation { Location = new Point(11, 10), Direction = MirDirection.Right });
        check(actor.MotionTime == 2000 && actor.Action == MirAction.Walking, "A duplicate movement acknowledgement restarted interpolation");
        var end = actor.PositionAt(2600, Actor.MoveDuration);
        state.Apply(new S.UserLocation { Location = new Point(13, 10), Direction = MirDirection.Right });
        actor.MotionTime = 2600;
        check(actor.Action == MirAction.Running && actor.PositionAt(2600, Actor.MoveDuration) == end, "Walk/run transition jumped between tiles");
        check(actor.PositionAt(2600 + actor.MovementDuration(600) / 2, Actor.MoveDuration) == new Vector2(12, 10), "Running failed to interpolate both tiles");
        check(actor.PositionAt(9999, Actor.MoveDuration) == new Vector2(13, 10), "Movement overshot its server-confirmed endpoint");
        var runner = new Actor { Kind = ObjectType.Player, Location = new Point(10, 10) };
        runner.Move(new Point(11, 10), MirDirection.Right, MirAction.Walking, 1000);
        runner.Move(new Point(13, 10), MirDirection.Right, MirAction.Running, 1640);
        check(runner.MovementDuration(600) == 640 + Actor.MovementBuffer && runner.PositionAt(2250, 600).X is > 12.8f and < 13, "Player stopped before the next normal movement acknowledgement");
        var visual = runner.PositionAt(2260, 600);
        runner.Move(new Point(15, 10), MirDirection.Right, MirAction.Running, 2260);
        check(runner.PositionAt(2260, 600) == visual, "A player acknowledgement snapped to the previous logical tile");
        check(runner.PositionAt(10000, 600) == new Vector2(15, 10), "Smoothed player moved beyond its authoritative endpoint");
        runner.Poison = PoisonType.Slow;
        check(runner.MoveDelay == PlayerActionTiming.DefaultMoveMilliseconds * 2, "A slowed player lost the original doubled movement delay");
        runner.Poison = PoisonType.None;
        runner.Move(new Point(16, 10), MirDirection.Right, MirAction.Walking, 4000);
        check(runner.MovementDuration(600) == PlayerActionTiming.DefaultMoveMilliseconds + Actor.MovementBuffer, "A stopped player resumed with a stale slow movement interval");
        runner.Move(new Point(14, 10), MirDirection.Left, MirAction.Jump, 4500);
        check(runner.MovementDuration(800) == PlayerActionTiming.Default.Scale(800), "Ordinary movement smoothing changed a skill movement duration");
        runner.Poison = PoisonType.Slow;
        check(runner.MovementDuration(800) == PlayerActionTiming.Default.Scale(800), "Ordinary slow-walk timing changed a skill movement duration");
        var configuredTiming = new PlayerActionTiming(650, 90);
        var configuredRunner = new Actor { Kind = ObjectType.Player, PlayerTiming = configuredTiming, Poison = PoisonType.Slow };
        configuredRunner.Move(new Point(2, 0), MirDirection.Right, MirAction.Running, 1000);
        check(configuredRunner.MovementDuration(600) == 1300 + Actor.MovementBuffer && configuredRunner.RunContinuationWindow > 1300 &&
            configuredTiming.CastDelay(Spell.FireBall) == 2000, "Configured speeds did not reach walk/run interpolation or skill timing");
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

    private static void GridMovement(Action<bool, string> check)
    {
        foreach(var directions in new[]{(MirDirection.Right,MirDirection.Down),(MirDirection.DownRight,MirDirection.UpRight),
            (MirDirection.Down,MirDirection.Left),(MirDirection.Right,MirDirection.Left),(MirDirection.Right,MirDirection.Right)})
        foreach(var slowed in new[]{false,true})
        {
            var actor=new Actor{Kind=ObjectType.Player,Location=new(10,10),Poison=slowed?PoisonType.Slow:PoisonType.None};
            var corner=Functions.PointMove(actor.Location,directions.Item1,1);var end=Functions.PointMove(corner,directions.Item2,2);
            actor.Move(corner,directions.Item1,MirAction.Walking,1000);
            var now=1000+PlayerActionTiming.DefaultMoveMilliseconds*(slowed?2:1);var origin=actor.PositionAt(now,600);
            actor.Move(end,directions.Item2,MirAction.Running,now);
            check(actor.PositionAt(now,600)==origin,"角色/英雄转弯时回跳了插值起点");
            for(var elapsed=17;elapsed<actor.MovementDuration(600);elapsed+=71)
            {
                var point=actor.PositionAt(now+elapsed,600);
                check(OnSegment(point,origin,new(corner.X,corner.Y))||OnSegment(point,new(corner.X,corner.Y),new(end.X,end.Y)),
                    "角色/英雄转弯斜切了服务端确认拐点，画面离开合法格子路径");
            }
            check(actor.PositionAt(now+actor.MovementDuration(600),600)==new Vector2(end.X,end.Y),"转弯后未停在确认格");
        }
        var state=new GameState();state.Apply(new S.UserInformation{ObjectID=1,Location=new(10,10),Inventory=[],Equipment=new UserItem[14],QuestInventory=[]});
        state.Me!.Move(new(11,10),MirDirection.Right,MirAction.Walking,Environment.TickCount64-300);
        state.Apply(new S.UserDash{Location=new(14,10),Direction=MirDirection.Right});
        check(state.Me.MovementDuration(600)==state.PlayerTiming.Scale(600),"技能冲刺错误继承普通走跑的回执间隔或显示余量");
        check(state.Me.PositionAt(state.Me.MotionTime,600)==new Vector2(11,10),"技能冲刺从未完成的普通插值点起步");
        state.Apply(new S.UserLocation{Location=new(30,25),Direction=MirDirection.Down});
        check(state.Me.PositionAt(state.Me.MotionTime,600)==new Vector2(30,25)&&state.Me.Action==MirAction.Standing,
            "普通位置纠正被误当成跨越多格的跑步，穿过未确认格子");

        static bool OnSegment(Vector2 point,Vector2 from,Vector2 to) =>
            Math.Abs((point.X-from.X)*(to.Y-from.Y)-(point.Y-from.Y)*(to.X-from.X))<.0001f&&
            point.X>=Math.Min(from.X,to.X)-.0001f&&point.X<=Math.Max(from.X,to.X)+.0001f&&
            point.Y>=Math.Min(from.Y,to.Y)-.0001f&&point.Y<=Math.Max(from.Y,to.Y)+.0001f;
    }
}
