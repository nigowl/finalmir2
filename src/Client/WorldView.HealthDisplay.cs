using Godot;

namespace Crystal.Native;

public partial class WorldView
{
    internal bool ShowHealthBars;
    internal const int HealthBarWidth = 48;
    internal int HealthBarsDrawn { get; private set; }
    internal bool HealthBarVisible(Actor actor) => !actor.Dead && (!actor.Hidden || actor.Id == State.User?.ObjectID) &&
        actor.Kind is ObjectType.Player or ObjectType.Monster &&
        (ShowHealthBars || actor.Id == Selected && actor.Kind == ObjectType.Monster);

    internal static string HealthLabel(Actor actor) => actor.CurrentHealth is { } hp && actor.MaximumHealth is > 0 and var maximum ? $"{hp}/{maximum}" : "";

    internal Vector2 HeadAnchor(Actor actor, long now) => Screen(Visual(actor, now)) +
        (NameBody(actor) is { } body ? body.Offset + body.HeadAnchor : new Vector2(24, -64));

    internal Vector2 HealthBarAnchor(Actor actor, long now) => (HeadAnchor(actor, now) - new Vector2(0, 9)).Round();

    private void DrawHealthBar(Actor actor, long now)
    {
        if (!HealthBarVisible(actor)) return;
        var centre = HealthBarAnchor(actor, now);
        var fraction = actor.CurrentHealth is { } hp && actor.MaximumHealth is > 0 and var maximum ? hp / (float)maximum : actor.Health / 100f;
        DrawRect(new Rect2(centre + new Vector2(-HealthBarWidth / 2 - 1, 0), new Vector2(HealthBarWidth + 2, 5)), Colors.Black);
        DrawRect(new Rect2(centre + new Vector2(-HealthBarWidth / 2, 1), new Vector2(HealthBarWidth * Math.Clamp(fraction, 0, 1), 3)), Colors.Red);
        var text = HealthLabel(actor);
        var position = centre - new Vector2(ThemeDB.FallbackFont.GetStringSize(text, fontSize: ClientUi.Micro).X / 2, 3);
        DrawStringOutline(ThemeDB.FallbackFont, position, text, fontSize: ClientUi.Micro, size: 2, modulate: Colors.Black);
        DrawString(ThemeDB.FallbackFont, position, text, fontSize: ClientUi.Micro, modulate: Colors.White);
        HealthBarsDrawn++;
    }
}
