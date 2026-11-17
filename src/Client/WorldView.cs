using Client.MirObjects;
using Godot;
using Point = System.Drawing.Point;
using GColor = Godot.Color;

namespace Crystal.Native;

public partial class WorldView : Node2D
{
    public GameState State = null!;
    public ResourceCache Assets = null!;
    internal MapReader? Map;
    private int mapTicket;
    private readonly AStarGrid2D navigation = new();
    private readonly List<(SpriteImage image, Vector2 position, float opacity)> glow = new();
    private GlowLayer glowLayer = null!;
    private CombatTextLayer combatTextLayer = null!;
    private readonly List<Actor> drawActors = new();
    public uint Selected;
    private bool loadingMap;
    public bool Loading => loadingMap || Map != null && !initialTerrainReady;
    public string LoadError { get; private set; } = "";
    public Vector2 Centre = new(480, 352);
    private Vector2 cameraPixels;
    private Vector2 renderScale = Vector2.One;
    private long frameTime;
    internal double LastDrawMilliseconds { get; private set; }

    public override void _Ready()
    {
        TextureFilter = TextureFilterEnum.Nearest;
        glowLayer = new GlowLayer { OwnerView = this, Material = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Add } };
        AddChild(glowLayer);
        combatTextLayer = new CombatTextLayer { View = this }; AddChild(combatTextLayer);
    }

    public async Task Load(string file)
    {
        var ticket = ++mapTicket; loadingMap = true; LoadError = ""; Map = null; ClearEffects(); ResetTerrainPrefetch();
        try
        {
            var map = await Assets.LoadMap(file);
            if (ticket != mapTicket) return;
            Map = map;
            navigation.Region = new Rect2I(0, 0, map.Width, map.Height);
            navigation.DiagonalMode = AStarGrid2D.DiagonalModeEnum.Always;
            navigation.DefaultComputeHeuristic = AStarGrid2D.Heuristic.Octile;
            navigation.DefaultEstimateHeuristic = AStarGrid2D.Heuristic.Octile;
            navigation.Update();
            for (var x = 0; x < map.Width; x++)
            for (var y = 0; y < map.Height; y++)
                if (!Walkable(new Point(x, y))) navigation.SetPointSolid(new Vector2I(x, y));
        }
        catch (Exception error) { if (ticket == mapTicket) LoadError = error.Message; }
        finally { if (ticket == mapTicket) loadingMap = false; }
    }

    public bool Walkable(Point point) => Map != null && point.X >= 0 && point.Y >= 0 && point.X < Map.Width && point.Y < Map.Height &&
        (Map.MapCells[point.X, point.Y].BackImage & 0x20000000) == 0 && (Map.MapCells[point.X, point.Y].FrontImage & 0x8000) == 0;

    public Point? NextStep(Point target, bool run = false)
    {
        return NextStep(target, run, out _);
    }

    public Point? NextStep(Point target, bool run, out Point[] route)
    {
        route = PathTo(target);
        if (route.Length <= 1) return null;
        var step = ClientMovement.StepIndex(route, run ? ClientMovement.RunDistance(State) : 1);
        return route[step];
    }

    public Point[] PathTo(Point target)
    {
        if (Map == null || State.User == null || !Walkable(target)) return [];
        // Players can share a login/revive tile; keep the start open so they can walk apart.
        var occupied = State.Actors.Values.Where(a => a.Id != State.User.ObjectID && a.Location != State.User.Location && a.Kind is not (ObjectType.Item or ObjectType.Spell) && !a.Dead && !a.Hidden && Walkable(a.Location)).Select(a => new Vector2I(a.Location.X, a.Location.Y)).Distinct().ToArray();
        foreach (var point in occupied) navigation.SetPointSolid(point);
        var current = State.User.Location;
        try { return navigation.GetIdPath(new Vector2I(current.X, current.Y), new Vector2I(target.X, target.Y)).Select(p => new Point(p.X, p.Y)).ToArray(); }
        finally { foreach (var point in occupied) navigation.SetPointSolid(point, false); }
    }

    internal Point? Approach(Actor target, int runDistance = 1)
    {
        if (State.User == null) return null;
        var cells = Enumerable.Range(-1, 3).SelectMany(dx => Enumerable.Range(-1, 3).Select(dy => new Point(target.Location.X + dx, target.Location.Y + dy)))
            .Where(point => point != target.Location && Walkable(point)).OrderBy(point => Functions.MaxDistance(point, State.User.Location))
            .ThenBy(point => Math.Abs(point.X - State.User.Location.X) + Math.Abs(point.Y - State.User.Location.Y));
        Point? nearest = null, flank = null;
        foreach (var point in cells)
        {
            if (point == State.User.Location) return point;
            var route = PathTo(point);
            if (route.Length <= 1) continue;
            nearest ??= point;
            if (route.Length == 2 && Functions.DirectionFromPoint(State.User.Location, point) != Functions.DirectionFromPoint(State.User.Location, target.Location)) flank ??= point;
            // 近身追击优先可直接跑到的相邻格，避免永远一步步追着移动目标走。
            if (runDistance <= 1 || Functions.MaxDistance(State.User.Location, target.Location) > runDistance + 1 ||
                ClientMovement.StepIndex(route, runDistance) == runDistance && (nearest == point || route.Length <= runDistance + 1)) return point;
        }
        // 正前方两格被步行目标占着时先贴侧面，下一步即可跑到邻格，避免同速尾随。
        return runDistance > 1 && target.Action == MirAction.Walking && Environment.TickCount64 - target.MotionTime < target.MovementDuration(600) + 100 ? flank ?? nearest : nearest;
    }

    public Point CellAt(Vector2 screen)
    {
        var me = State.Me;
        if (me == null) return Point.Empty;
        var camera = cameraPixels / new Vector2(48, 32);
        return new Point((int)Math.Floor((screen.X - Centre.X) / 48 + camera.X), (int)Math.Floor((screen.Y - Centre.Y) / 32 + camera.Y));
    }

    public Actor? ActorAt(Vector2 screen)
    {
        return State.Actors.Values.Where(a => !a.Hidden && a.Kind != ObjectType.Spell && a.Id != State.User?.ObjectID)
            .OrderByDescending(a => a.Kind == ObjectType.Item).ThenByDescending(a => a.Location.Y).FirstOrDefault(a =>
            {
                var p = Screen(a.Location);
                return a.Kind == ObjectType.Item ? new Rect2(p, new Vector2(48, 32)).HasPoint(screen)
                    : new Rect2(p + new Vector2(-5, -65), new Vector2(60, 95)).HasPoint(screen);
            });
    }

    public Vector2 Screen(Point point) => Screen(new Vector2(point.X, point.Y));
    private Vector2 Screen(Vector2 point)
    {
        return SnapWorldPixel(point * new Vector2(48, 32)) - cameraPixels + Centre;
    }
    private Vector2 SnapWorldPixel(Vector2 point) => (point * renderScale).Round() / renderScale;

    public uint Hovered { get; private set; }
    internal bool ShowNpcNames = true, ShowMonsterNames, ShowItemNames;
    internal bool NameVisible(Actor actor) => actor.Id == State.User?.ObjectID || (actor.Kind switch
    {
        ObjectType.Merchant => ShowNpcNames || actor.Id == Hovered || actor.Id == Selected,
        ObjectType.Monster => ShowMonsterNames || actor.Id == Hovered || actor.Id == Selected,
        ObjectType.Item => ShowItemNames || actor.Id == Hovered || actor.Id == Selected,
        _ => actor.Id == Selected
    });
    internal void UpdateHover(Vector2 screen, bool overUi) => Hovered = overUi ? 0 : ActorAt(screen)?.Id ?? 0;
    public override void _Process(double delta)
    {
        frameTime = Environment.TickCount64;
        // 按实际屏幕像素取整，高DPI下不再每次跳一整逻辑像素；整帧仍共用同一镜头。
        renderScale = GetViewport().GetStretchTransform().Scale.Abs().Max(new Vector2(.01f, .01f));
        cameraPixels = State.Me == null ? Vector2.Zero : SnapWorldPixel(Visual(State.Me, frameTime) * new Vector2(48, 32));
        UpdateHover(GetGlobalMousePosition(), GetViewport().GuiGetHoveredControl() != null);
        PrefetchTerrain();
        ProcessEffects(frameTime);
        combatTexts.RemoveAll(text => frameTime - text.Start >= text.Duration);
        QueueRedraw(); glowLayer.QueueRedraw(); combatTextLayer.QueueRedraw();
    }
    public override void _Draw()
    {
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        DrawWorld();
        LastDrawMilliseconds = System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds;
    }

    private void DrawWorld()
    {
        glow.Clear();
        if (State.User == null) return;
        DrawRect(new Rect2(0, 0, 1024, 768), new GColor("111710"));
        if (Map == null) return;
        if (Loading)
        {
            DrawString(ThemeDB.FallbackFont, new Vector2(410, 320), "正在准备地图素材…", fontSize: 16, modulate: Colors.Wheat);
            return;
        }
        var user = State.User.Location;
        // 原客户端物件层向下多扫描 25 行，建筑锚点可能在屏外，屋顶仍伸入当前画面。
        var bounds = MapViewBounds.Around(user, Map.Width, Map.Height);
        var fromX = bounds.FromX; var toX = bounds.ToX; var fromY = bounds.FromY;
        var now = frameTime;
        drawActors.Clear();
        foreach (var actor in State.Actors.Values)
            if (actor.Location.Y >= fromY && actor.Location.Y <= bounds.ObjectToY) drawActors.Add(actor);
        drawActors.Sort(CompareDrawOrder);
        var actorIndex = 0;
        for (var y = fromY; y <= bounds.FloorToY; y++)
        for (var x = fromX; x <= toX; x++)
        {
            var cell = Map.MapCells[x, y]; var position = Screen(new Point(x, y));
            if (x % 2 == 0 && y % 2 == 0) DrawTile(cell.BackIndex, (cell.BackImage & 0x1FFFFFFF) - 1, position, false);
            var middle = Assets.Sprite(MapLibrary(cell.MiddleIndex), cell.MiddleImage - 1);
            if (middle != null && IsFloor(middle)) DrawTexture(middle.Texture, position);
            var front = Assets.Sprite(MapLibrary(cell.FrontIndex), (cell.FrontImage & 0x7FFF) - 1);
            if (front != null && IsFloor(front) && cell.FrontAnimationFrame == 0) DrawTexture(front.Texture, position);
        }
        for (var y = fromY; y <= bounds.ObjectToY; y++)
        {
            while (actorIndex < drawActors.Count && drawActors[actorIndex].Location.Y == y && (drawActors[actorIndex].Dead || drawActors[actorIndex].Kind == ObjectType.Item))
                DrawActor(drawActors[actorIndex++], now);
            for (var x = fromX; x <= toX; x++)
            {
                var cell = Map.MapCells[x, y]; var position = Screen(new Point(x, y)) + new Vector2(0, 32);
                var middleIndex = cell.MiddleImage - 1;
                var middleFrames = cell.MiddleAnimationFrame;
                if (middleFrames is > 0 and < 255)
                {
                    var count = (middleFrames & 0x0f) > 0 ? middleFrames & 0x0f : middleFrames;
                    middleIndex += (int)(now / (100 * (1 + cell.MiddleAnimationTick)) % count);
                }
                var mid = Assets.Sprite(MapLibrary(cell.MiddleIndex), middleIndex) ?? Assets.Sprite(MapLibrary(cell.MiddleIndex), cell.MiddleImage - 1);
                if (mid != null && !IsFloor(mid)) DrawTexture(mid.Texture, position - new Vector2(0, mid.Size.Y));
                var index = (cell.FrontImage & 0x7FFF) - 1;
                var animation = cell.FrontAnimationFrame & 0x7F;
                if (animation > 0) index += (int)(now / (100 * (1 + cell.FrontAnimationTick)) % animation);
                var front = Assets.Sprite(MapLibrary(cell.FrontIndex), index) ?? Assets.Sprite(MapLibrary(cell.FrontIndex), (cell.FrontImage & 0x7FFF) - 1);
                if (front == null || (IsFloor(front) && animation == 0)) continue;
                if ((cell.FrontAnimationFrame & 0x80) != 0) glow.Add((front, position - new Vector2(0, front.Size.Y), 1));
                else DrawTexture(front.Texture, position - new Vector2(0, front.Size.Y));
            }
            while (actorIndex < drawActors.Count && drawActors[actorIndex].Location.Y == y) DrawActor(drawActors[actorIndex++], now);
        }
        foreach (var actor in State.Actors.Values)
        {
            if (!NameVisible(actor)) continue;
            if (actor.Hidden && actor.Id != State.User?.ObjectID) continue;
            var p = NameAnchor(actor, now);
            var namePosition = p - new Vector2(ThemeDB.FallbackFont.GetStringSize(actor.Name, fontSize: 12).X / 2, 0);
            if (actor.Kind is ObjectType.Player or ObjectType.Merchant or ObjectType.Monster)
                namePosition.Y += (ThemeDB.FallbackFont.GetAscent(12) - ThemeDB.FallbackFont.GetDescent(12)) / 2;
            DrawStringOutline(ThemeDB.FallbackFont, namePosition, actor.Name, fontSize: 12, size: 2, modulate: Colors.Black);
            DrawString(ThemeDB.FallbackFont, namePosition, actor.Name,
                fontSize: 12, modulate: actor.Kind == ObjectType.Monster ? new GColor("f0b09c") : new GColor("f4edca"));
            if (actor.Id == Selected && actor.Kind == ObjectType.Monster && !actor.Dead)
            {
                p = Screen(Visual(actor, now)) + new Vector2(24, -46);
                DrawRect(new Rect2(p + new Vector2(-25, 5), new Vector2(50, 5)), Colors.Black);
                DrawRect(new Rect2(p + new Vector2(-24, 6), new Vector2(actor.Health * 0.48f, 3)), Colors.Red);
            }
        }
        DrawSpells(now);
    }

    private static int CompareDrawOrder(Actor left, Actor right)
    {
        var order = left.Location.Y.CompareTo(right.Location.Y);
        if (order != 0) return order;
        order = (right.Dead || right.Kind == ObjectType.Item).CompareTo(left.Dead || left.Kind == ObjectType.Item);
        if (order != 0) return order;
        order = left.Location.X.CompareTo(right.Location.X);
        return order != 0 ? order : left.Id.CompareTo(right.Id);
    }

    private static bool IsFloor(SpriteImage image) => image.Size == new Vector2(48, 32) || image.Size == new Vector2(96, 64);
    private void DrawTile(int library, int index, Vector2 position, bool offset)
    { var image = Assets.Sprite(MapLibrary(library), index); if (image != null) DrawTexture(image.Texture, position + (offset ? image.Offset : Vector2.Zero)); }

    public static string MapLibrary(int index) => MapTileLibraries.Resolve(index);

    private partial class GlowLayer : Node2D
    {
        public WorldView OwnerView = null!;
        public override void _Draw() { foreach (var (image, position, opacity) in OwnerView.glow) DrawTexture(image.Texture, position, new Color(1, 1, 1, opacity)); }
    }
    public override void _ExitTree() { navigation.Dispose(); }
}
