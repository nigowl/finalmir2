using Godot;

public partial class MenuIconButton : OriginalButton
{
    public override void _Draw()
    {
        base._Draw();
        var center = Size / 2;
        // 保留原圆形金边，覆盖中心徽章，用三行列表表达菜单功能。
        DrawCircle(center, 11, new Color("182b40"));
        for (var row = 0; row < 3; row++)
        {
            var position = center + new Vector2(-7, -6 + row * 5);
            DrawRect(new Rect2(position + Vector2.Down, new Vector2(14, 3)), new Color("08101a"));
            DrawRect(new Rect2(position, new Vector2(14, 2)), IsHovered() || IsPressed() ? new Color("fff0b8") : new Color("d7bb78"));
        }
    }
}
