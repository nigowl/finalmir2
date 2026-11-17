using Godot;
using Point = System.Drawing.Point;

namespace Crystal.Native;

public partial class GameMiniMap : Control
{
    public ResourceCache Assets = null!;
    public GameState State = null!;
    private ImageTexture? texture;
    private string file = "", assetVersion = "", revision = "", error = "";
    private int generation, width, height, collapsedIndex = -1;
    private bool loading;
    private double refresh;
    private Label caption = null!;
    internal string Revision => revision;
    internal string Error => error;
    public bool Expanded { get; private set; }
    public IReadOnlyList<Point> Route = [];
    public event Action<Point>? DestinationSelected;
    public event Action<Point>? TeleportRequested;
    public override void _Ready()
    {
        caption = new Label { Name = "MapCaption", HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, ClipText = true, MouseFilter = MouseFilterEnum.Ignore };
        caption.AddThemeColorOverride("font_color", Colors.Wheat); AddChild(caption);
        Layout();
    }
    internal Rect2 ImageRect
    {
        get
        {
            if (texture == null) return new Rect2();
            var top = Expanded ? 23 : 3;
            var available = Size - new Vector2(6, Expanded ? 42 : 6);
            var size = texture.GetSize() * Math.Min(available.X / texture.GetWidth(), available.Y / texture.GetHeight());
            return new Rect2(new Vector2((Size.X - size.X) / 2, top + (available.Y - size.Y) / 2), size);
        }
    }

    public void SetExpanded(bool expanded)
    {
        if (expanded && !Expanded) collapsedIndex = GetIndex();
        Expanded = expanded;
        Layout();
        MouseFilter = MouseFilterEnum.Stop;
        if (expanded) MoveToFront();
        else if (collapsedIndex >= 0) GetParent().MoveChild(this, collapsedIndex);
        QueueRedraw();
    }

    private void Layout()
    {
        var imageSize = texture?.GetSize() ?? new Vector2(3, 2);
        var padding = new Vector2(6, Expanded ? 42 : 6);
        var available = (Expanded ? new Vector2(820, 560) : new Vector2(180, 140)) - padding;
        Size = imageSize * Math.Min(available.X / imageSize.X, available.Y / imageSize.Y) + padding;
        Position = Expanded ? (new Vector2(1024, 616) - Size) / 2 : new Vector2(1016 - Size.X, 8);
        if (caption == null) return;
        caption.Visible = Expanded;
        caption.HorizontalAlignment = Expanded ? HorizontalAlignment.Left : HorizontalAlignment.Center;
        caption.Position = Expanded ? new Vector2(24, 2) : new Vector2(24, Size.Y - 19);
        caption.Size = Expanded ? new Vector2(Size.X - 54, 20) : new Vector2(Size.X - 48, 16);
    }

    internal Point? CellAt(Vector2 local)
    {
        var rect = ImageRect;
        if (texture == null || !rect.HasPoint(local)) return null;
        var fraction = (local - rect.Position) / rect.Size;
        return new Point(Math.Clamp((int)(fraction.X * width), 0, width - 1), Math.Clamp((int)(fraction.Y * height), 0, height - 1));
    }

    public override void _GuiInput(InputEvent input)
    {
        if (input is not InputEventMouseButton { Pressed: true } mouse || mouse.ButtonIndex is not (MouseButton.Left or MouseButton.Right)) return;
        AcceptEvent();
        if (!Expanded) { if (mouse.ButtonIndex == MouseButton.Left) SetExpanded(true); return; }
        if (mouse.ButtonIndex == MouseButton.Right)
        {
            if (CellAt(mouse.Position) is { } destination) TeleportRequested?.Invoke(destination);
            return;
        }
        var point = new Rect2(Size.X - 26, 0, 26, 22).HasPoint(mouse.Position) ? null : CellAt(mouse.Position);
        SetExpanded(false);
        if (point.HasValue) DestinationSelected?.Invoke(point.Value);
    }

    public override void _Process(double delta)
    {
        caption.Text = $"{State.Map?.Title} [{State.Map?.FileName}] · 左键寻路 / 右键传送（需权限） · M / Esc 关闭";
        var name = State.Map?.FileName ?? "";
        if (name != file || assetVersion != Assets.Version)
        {
            generation++; file = name; assetVersion = Assets.Version; revision = error = "";
            texture?.Dispose(); texture = null; loading = false; refresh = 0; Layout();
        }
        refresh -= delta;
        if (Assets.Ready && file.Length > 0 && !loading && refresh <= 0)
        { refresh = 5; _ = Refresh(file, generation); }
        QueueRedraw();
    }
    private async Task Refresh(string name, int ticket)
    {
        loading = true;
        try
        {
            var info = await Assets.ThumbnailInfo(name);
            if (ticket != generation || !IsInsideTree()) return;
            if (info.Revision == revision) { error = ""; return; }
            var bytes = await Assets.Thumbnail(name, info.Revision);
            if (ticket != generation || !IsInsideTree()) return;
            using var image = new Image();
            if (image.LoadPngFromBuffer(bytes) != Godot.Error.Ok || image.GetWidth() > 1024 || image.GetHeight() > 1024)
                throw new InvalidDataException("地图缩略图无效。");
            var replacement = ImageTexture.CreateFromImage(image);
            texture?.Dispose(); texture = replacement;
            revision = info.Revision; width = info.Width; height = info.Height; error = ""; TooltipText = "黄色标记为当前位置 · M 放大地图后点击寻路";
            Layout();
        }
        catch (Exception e)
        {
            if (ticket == generation && IsInsideTree()) { error = e.Message; TooltipText = "小地图加载失败，将自动重试：" + error; GD.PrintErr(TooltipText); }
        }
        finally { if (ticket == generation) loading = false; }
    }
    public override void _Draw()
    {
        // 角饰抠底后由地图或原面板底色填满，不能透出后面的场景。
        DrawRect(new Rect2(Vector2.Zero, Size), new Color("0e0c0b"));
        if (texture != null)
        {
            var rect = ImageRect;
            DrawTextureRect(texture, rect, false);
            DrawRoute(rect);
            if (State.User != null && width > 0 && height > 0)
            {
                var point = State.User.Location;
                DrawCircle(rect.Position + new Vector2((point.X + .5f) / width * rect.Size.X, (point.Y + .5f) / height * rect.Size.Y), 3, Colors.Yellow);
            }
        }
        if (HasThemeStylebox("panel", "GameMiniMap")) GetThemeStylebox("panel", "GameMiniMap").Draw(GetCanvasItem(), new Rect2(Vector2.Zero, Size));
        if (Expanded)
        {
            var close = Assets.Sprite("Prguse2", 360);
            if (close != null) DrawTexture(close.Texture, new Vector2(Size.X - 23, 2));
            var point = CellAt(GetLocalMousePosition());
            var text = error.Length > 0 ? "地图加载失败，将自动重试" : texture == null ? "地图加载中…" : point.HasValue ? $"目标坐标 {point.Value.X},{point.Value.Y}" : "黄色标记为当前位置";
            DrawString(ThemeDB.FallbackFont, new Vector2(24, Size.Y - 5), text, fontSize: 12, modulate: Colors.Wheat);
        }
    }

    private void DrawRoute(Rect2 rect)
    {
        if (Route.Count < 2 || State.User == null || width <= 0 || height <= 0) return;
        var start = 0;
        for (var i = 0; i < Route.Count; i++) if (Route[i] == State.User.Location) { start = i; break; }
        var points = new List<Vector2>();
        for (var i = start; i < Route.Count; i++)
        {
            if (i > start && i < Route.Count - 1 && Functions.DirectionFromPoint(Route[i - 1], Route[i]) == Functions.DirectionFromPoint(Route[i], Route[i + 1])) continue;
            points.Add(rect.Position + new Vector2((Route[i].X + .5f) / width, (Route[i].Y + .5f) / height) * rect.Size);
        }
        if (points.Count > 1) DrawPolyline(points.ToArray(), Colors.DeepSkyBlue, Expanded ? 2 : 1.5f, true);
        var target = Route[^1];
        DrawCircle(rect.Position + new Vector2((target.X + .5f) / width, (target.Y + .5f) / height) * rect.Size, Expanded ? 4 : 2, Colors.DeepSkyBlue);
    }
    public override void _ExitTree() { generation++; texture?.Dispose(); texture = null; }
}
