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
    private readonly List<(SpriteImage image, Vector2 position)> glow = new();
    private GlowLayer glowLayer = null!;
    private readonly List<(uint id, int damage, long time)> damage = new();
    private readonly List<(Spell spell, uint caster, uint target, Point source, Point destination, long time)> spells = new();
    public uint Selected;
    public bool Loading { get; private set; }
    public string LoadError { get; private set; } = "";
    public Vector2 Centre = new(480, 352);

    public override void _Ready()
    {
        TextureFilter = TextureFilterEnum.Nearest;
        glowLayer = new GlowLayer { OwnerView = this, Material = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Add } };
        AddChild(glowLayer);
    }

    public async Task Load(string file)
    {
        var ticket = ++mapTicket; Loading = true; LoadError = ""; Map = null; spells.Clear();
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
        finally { if (ticket == mapTicket) Loading = false; }
    }

    public bool Walkable(Point point) => Map != null && point.X >= 0 && point.Y >= 0 && point.X < Map.Width && point.Y < Map.Height &&
        (Map.MapCells[point.X, point.Y].BackImage & 0x20000000) == 0 && (Map.MapCells[point.X, point.Y].FrontImage & 0x8000) == 0;

    public Point? NextStep(Point target, bool run = false)
    {
        if (Map == null || State.User == null || !Walkable(target)) return null;
        // Players can share a login/revive tile; keep the start open so they can walk apart.
        var occupied = State.Actors.Values.Where(a => a.Id != State.User.ObjectID && a.Location != State.User.Location && a.Kind != ObjectType.Item && !a.Dead && !a.Hidden && Walkable(a.Location)).Select(a => new Vector2I(a.Location.X, a.Location.Y)).Distinct().ToArray();
        foreach (var point in occupied) navigation.SetPointSolid(point);
        var current = State.User.Location;
        var path = navigation.GetIdPath(new Vector2I(current.X, current.Y), new Vector2I(target.X, target.Y), true);
        foreach (var point in occupied) navigation.SetPointSolid(point, false);
        if (path.Count <= 1) return null;
        var step = run && path.Count > 2 && path[1] - path[0] == path[2] - path[1] ? 2 : 1;
        return new Point(path[step].X, path[step].Y);
    }

    public Point CellAt(Vector2 screen)
    {
        var me = State.Me;
        if (me == null) return Point.Empty;
        var camera = Visual(me);
        return new Point((int)Math.Floor((screen.X - Centre.X) / 48 + camera.X), (int)Math.Floor((screen.Y - Centre.Y) / 32 + camera.Y));
    }

    public Actor? ActorAt(Vector2 screen)
    {
        return State.Actors.Values.Where(a => !a.Hidden && a.Id != State.User?.ObjectID)
            .OrderByDescending(a => a.Location.Y).FirstOrDefault(a =>
            {
                var p = Screen(a.Location);
                return a.Kind == ObjectType.Item ? new Rect2(p, new Vector2(48, 32)).HasPoint(screen)
                    : new Rect2(p + new Vector2(-5, -65), new Vector2(60, 95)).HasPoint(screen);
            });
    }

    public void ShowDamage(uint id, int amount) => damage.Add((id, amount, Environment.TickCount64));
    public void ShowSpell(Spell spell, uint caster, uint target, Point destination)
    {
        if (State.Actors.TryGetValue(caster, out var actor)) spells.Add((spell, caster, target, actor.Location, destination, Environment.TickCount64));
    }
    private static Vector2 Visual(Actor actor)
    {
        if (actor.Action is not (MirAction.Walking or MirAction.Running)) return new Vector2(actor.Location.X, actor.Location.Y);
        var fraction = Math.Clamp((Environment.TickCount64 - actor.MotionTime) / 400f, 0, 1);
        return new Vector2(actor.Previous.X, actor.Previous.Y).Lerp(new Vector2(actor.Location.X, actor.Location.Y), fraction);
    }
    public Vector2 Screen(Point point) => Screen(new Vector2(point.X, point.Y));
    private Vector2 Screen(Vector2 point)
    {
        var camera = State.Me == null ? Vector2.Zero : Visual(State.Me);
        return (point - camera) * new Vector2(48, 32) + Centre;
    }

    public override void _Process(double delta) { QueueRedraw(); glowLayer.QueueRedraw(); }
    public override void _Draw()
    {
        glow.Clear();
        if (State.User == null) return;
        DrawRect(new Rect2(0, 0, 1024, 768), new GColor("111710"));
        if (Map == null) return;
        var user = State.User.Location;
        var fromX = Math.Max(0, user.X - 13); var toX = Math.Min(Map.Width - 1, user.X + 13);
        var fromY = Math.Max(0, user.Y - 12); var toY = Math.Min(Map.Height - 1, user.Y + 17);
        var now = Environment.TickCount64;
        for (var y = fromY; y <= toY; y++)
        for (var x = fromX; x <= toX; x++)
        {
            var cell = Map.MapCells[x, y]; var position = Screen(new Point(x, y));
            if (x % 2 == 0 && y % 2 == 0) DrawTile(cell.BackIndex, (cell.BackImage & 0x1FFFFFFF) - 1, position, false);
            var middle = Assets.Sprite(MapLibrary(cell.MiddleIndex), cell.MiddleImage - 1);
            if (middle != null && IsFloor(middle)) DrawTexture(middle.Texture, position);
            var front = Assets.Sprite(MapLibrary(cell.FrontIndex), (cell.FrontImage & 0x7FFF) - 1);
            if (front != null && IsFloor(front) && cell.FrontAnimationFrame == 0) DrawTexture(front.Texture, position);
        }
        for (var y = fromY; y <= toY; y++)
        {
            foreach (var actor in State.Actors.Values.Where(a => a.Location.Y == y && (a.Dead || a.Kind == ObjectType.Item))) DrawActor(actor, now);
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
                var mid = Assets.Sprite(MapLibrary(cell.MiddleIndex), middleIndex);
                if (mid != null && !IsFloor(mid)) DrawTexture(mid.Texture, position - new Vector2(0, mid.Size.Y));
                var index = (cell.FrontImage & 0x7FFF) - 1;
                var animation = cell.FrontAnimationFrame & 0x7F;
                if (animation > 0) index += (int)(now / (100 * (1 + cell.FrontAnimationTick)) % animation);
                var front = Assets.Sprite(MapLibrary(cell.FrontIndex), index);
                if (front == null || (IsFloor(front) && animation == 0)) continue;
                if ((cell.FrontAnimationFrame & 0x80) != 0) glow.Add((front, position - new Vector2(0, front.Size.Y)));
                else DrawTexture(front.Texture, position - new Vector2(0, front.Size.Y));
            }
            foreach (var actor in State.Actors.Values.Where(a => a.Location.Y == y && !a.Dead && a.Kind != ObjectType.Item).OrderBy(a => a.Location.X)) DrawActor(actor, now);
        }
        foreach (var actor in State.Actors.Values.Where(a => a.Id == Selected || a.Id == State.User.ObjectID || a.Kind == ObjectType.Merchant))
        {
            if (actor.Hidden) continue;
            var p = Screen(Visual(actor)) + new Vector2(24, -46);
            DrawString(ThemeDB.FallbackFont, p - new Vector2(ThemeDB.FallbackFont.GetStringSize(actor.Name, fontSize: 13).X / 2, 0), actor.Name,
                fontSize: 13, modulate: actor.Kind == ObjectType.Monster ? new GColor("f0b09c") : new GColor("f4edca"));
            if (actor.Id == Selected && actor.Kind == ObjectType.Monster && !actor.Dead)
            {
                DrawRect(new Rect2(p + new Vector2(-25, 5), new Vector2(50, 5)), Colors.Black);
                DrawRect(new Rect2(p + new Vector2(-24, 6), new Vector2(actor.Health * 0.48f, 3)), Colors.Red);
            }
        }
        damage.RemoveAll(d => now - d.time > 900);
        foreach (var hit in damage)
            if (State.Actors.TryGetValue(hit.id, out var actor))
                DrawString(ThemeDB.FallbackFont, Screen(Visual(actor)) + new Vector2(22, -55 - (now - hit.time) / 20f),
                    hit.damage == 0 ? "Miss" : Math.Abs(hit.damage).ToString(), fontSize: 18, modulate: hit.damage > 0 ? Colors.LightGreen : Colors.OrangeRed);
        DrawSpells(now);
    }

    private void DrawSpells(long now)
    {
        // Common original spell indices from PlayerObject; the server decides all hits and damage.
        spells.RemoveAll(s => now - s.time > 2200);
        foreach (var spell in spells)
        {
            var elapsed = now - spell.time;
            var source = State.Actors.TryGetValue(spell.caster, out var caster) ? Screen(Visual(caster)) : Screen(spell.source);
            var target = State.Actors.TryGetValue(spell.target, out var actor) ? actor.Location : spell.destination;
            void Effect(string library, int index, Vector2 position)
            {
                var image = Assets.Sprite(library, index);
                if (image != null) glow.Add((image, position + image.Offset));
            }
            if (spell.spell is Spell.FireBall or Spell.GreatFireBall or Spell.SoulFireBall)
            {
                var offset = spell.spell == Spell.FireBall ? 0 : 400;
                var soul = spell.spell == Spell.SoulFireBall;
                var flight = Math.Max(50, Math.Max(Math.Abs(target.X - spell.source.X), Math.Abs(target.Y - spell.source.Y)) * 50);
                if (elapsed < 600) { if (!soul) Effect("Magic", offset + (int)(elapsed / 60), source); }
                else if (elapsed < 600 + flight)
                {
                    var angle = Math.Atan2(target.X - spell.source.X, spell.source.Y - target.Y);
                    var direction = ((int)Math.Round(angle * 8 / Math.PI) + 16) % 16;
                    Effect("Magic", (soul ? 1160 : offset + 10) + direction * 10 + (int)((elapsed - 600) / 30 % (soul ? 3 : 6)), Screen(spell.source).Lerp(Screen(target), (elapsed - 600) / (float)flight));
                }
                else if (elapsed < 1200 + flight) Effect("Magic", (soul ? 1360 : offset + 170) + (int)((elapsed - 600 - flight) / 60), Screen(target));
            }
            else if (spell.spell == Spell.Healing)
            {
                if (elapsed < 600) Effect("Magic", 200 + (int)(elapsed / 60), source);
                else if (elapsed < 1400) Effect("Magic", 370 + (int)((elapsed - 600) / 80), Screen(target));
            }
            else if (spell.spell == Spell.ThunderBolt)
            {
                if (elapsed < 300) Effect("Magic2", 20 + (int)(elapsed / 100), source);
                else if (elapsed is >= 600 and < 1000) Effect("Magic2", 10 + (int)((elapsed - 600) / 80), Screen(target));
            }
        }
    }

    private static bool IsFloor(SpriteImage image) => image.Size == new Vector2(48, 32) || image.Size == new Vector2(96, 64);
    private void DrawTile(int library, int index, Vector2 position, bool offset)
    { var image = Assets.Sprite(MapLibrary(library), index); if (image != null) DrawTexture(image.Texture, position + (offset ? image.Offset : Vector2.Zero)); }

    private void DrawActor(Actor actor, long now)
    {
        if (actor.Hidden) return;
        var position = Screen(Visual(actor));
        if (position.X < -250 || position.X > 1300 || position.Y < -250 || position.Y > 900) return;
        if (actor.Kind == ObjectType.Item)
        {
            var item = Assets.Sprite("DNItems", actor.Image);
            if (item != null) DrawTexture(item.Texture, position + (new Vector2(48, 32) - item.Size) / 2);
            return;
        }
        var library = actor.Kind == ObjectType.Player ? $"CArmour/{actor.Armour:00}" : actor.Kind == ObjectType.Merchant
            ? actor.Image >= 1000 ? $"Flag/{actor.Image - 1000:00}" : $"NPC/{actor.Image:00}"
            : actor.Image >= 10000 ? $"Pet/{actor.Image - 10000:00}" : actor.Image is >= 950 and < 960 ? $"Gate/{actor.Image - 950:00}"
            : actor.Image is >= 940 and < 950 ? $"Siege/{actor.Image - 940:00}" : $"Monster/{actor.Image:000}";
        var frames = Assets.Frames(library);
        if (frames == null) return;
        var action = actor.Dead ? now - actor.ActionTime > 1000 ? MirAction.Dead : MirAction.Die
            : now - actor.ActionTime > 550 ? MirAction.Standing : actor.Action;
        if (!frames.Frames.TryGetValue(action, out var frame) && !frames.Frames.TryGetValue(MirAction.Standing, out frame)) return;
        var index = frame.Start + (int)actor.Direction * frame.OffSet + (int)((now - (action == MirAction.Standing ? 0 : actor.ActionTime)) / Math.Max(40, frame.Interval) % Math.Max(1, frame.Count));
        if (actor.Kind == ObjectType.Player)
        {
            var weaponBehind = actor.Direction is MirDirection.Left or MirDirection.Up or MirDirection.UpLeft or MirDirection.DownLeft;
            if (weaponBehind && actor.Weapon >= 0) DrawSprite($"CWeapon/{actor.Weapon:00}", index + (actor.Gender == MirGender.Female ? 416 : 0), position);
            DrawSprite(library, index + (actor.Gender == MirGender.Female ? 808 : 0), position);
            DrawSprite($"CHair/{actor.Hair:00}", index + (actor.Gender == MirGender.Female ? 808 : 0), position);
            if (!weaponBehind && actor.Weapon >= 0) DrawSprite($"CWeapon/{actor.Weapon:00}", index + (actor.Gender == MirGender.Female ? 416 : 0), position);
        }
        else DrawSprite(library, index, position);
        if (actor.Kind == ObjectType.Merchant && State.QuestsAt(actor.Id).FirstOrDefault() is { } quest)
        {
            var progress = State.Quests.GetValueOrDefault(quest.Index);
            var marker = Assets.Sprite("Prguse", 981 + (int)quest.GetQuestIcon(progress != null, progress?.Completed == true) * 2 + (int)(now / 350 % 2));
            var body = Assets.Sprite(library, frame.Start);
            if (marker != null && body != null) DrawTexture(marker.Texture, position + body.Offset + new Vector2(body.Size.X / 2 - 28, -40));
        }
    }
    private void DrawSprite(string library, int index, Vector2 position)
    { var image = Assets.Sprite(library, index); if (image != null) DrawTexture(image.Texture, position + image.Offset); }

    public static string MapLibrary(int index) => MapTileLibraries.Resolve(index);

    private partial class GlowLayer : Node2D
    {
        public WorldView OwnerView = null!;
        public override void _Draw() { foreach (var (image, position) in OwnerView.glow) DrawTexture(image.Texture, position); }
    }
    public override void _ExitTree() { navigation.Dispose(); }
}
