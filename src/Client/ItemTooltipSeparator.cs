using Godot;

public partial class ItemTooltipSeparator : Control
{
    public bool Vertical;
    public override void _Ready()
    {
        CustomMinimumSize = Vector2.One;
        MouseFilter = MouseFilterEnum.Ignore;
        Resized += QueueRedraw;
    }

    public override void _Draw()
    {
        var start = Vertical ? new Vector2(Size.X / 2, 0) : new Vector2(0, Size.Y / 2);
        var end = Vertical ? new Vector2(Size.X / 2, Size.Y) : new Vector2(Size.X, Size.Y / 2);
        DrawDashedLine(start, end, new Color(1, 1, 1, .18f), 1, 3);
    }
}
