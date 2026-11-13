using Godot;

public partial class ItemTipButton : Button
{
    public Func<Control>? TooltipBuilder;
    public override GodotObject _MakeCustomTooltip(string forText) => TooltipBuilder?.Invoke() ?? new Label { Text = forText };
}
