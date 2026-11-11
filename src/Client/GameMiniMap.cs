using Godot;

namespace Crystal.Native;

public partial class GameMiniMap : Control
{
    public ResourceCache Assets = null!;
    public GameState State = null!;
    private ImageTexture? texture;
    private string file = "", assetVersion = "", revision = "", error = "";
    private int generation, width, height;
    private bool loading;
    private double refresh;
    internal string Revision => revision;
    internal string Error => error;

    public override void _Process(double delta)
    {
        var name = State.Map?.FileName ?? "";
        if (name != file || assetVersion != Assets.Version)
        {
            generation++; file = name; assetVersion = Assets.Version; revision = error = "";
            texture?.Dispose(); texture = null; loading = false; refresh = 0;
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
                throw new InvalidDataException("Invalid map thumbnail.");
            var replacement = ImageTexture.CreateFromImage(image);
            texture?.Dispose(); texture = replacement;
            revision = info.Revision; width = info.Width; height = info.Height; error = ""; TooltipText = "黄色标记为当前位置";
        }
        catch (Exception e)
        {
            if (ticket == generation && IsInsideTree()) { error = e.Message; TooltipText = "小地图加载失败，将自动重试：" + error; GD.PrintErr(TooltipText); }
        }
        finally { if (ticket == generation) loading = false; }
    }
    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, Size), new Color(0, 0, 0, .8f));
        if (texture != null)
        {
            var scale = Math.Min((Size.X - 8) / texture.GetWidth(), (Size.Y - 8) / texture.GetHeight());
            var size = texture.GetSize() * scale;
            var rect = new Rect2((Size - size) / 2, size);
            DrawTextureRect(texture, rect, false);
            if (State.User != null && width > 0 && height > 0)
            {
                var point = State.User.Location;
                DrawCircle(rect.Position + new Vector2((point.X + .5f) / width * size.X, (point.Y + .5f) / height * size.Y), 3, Colors.Yellow);
            }
        }
        DrawRect(new Rect2(Vector2.Zero, Size), new Color("82704c"), false);
    }
    public override void _ExitTree() { generation++; texture?.Dispose(); texture = null; }
}
