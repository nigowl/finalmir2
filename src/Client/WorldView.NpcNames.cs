using Godot;

namespace Crystal.Native;

public partial class WorldView
{
    private void DrawNpcName(Actor actor, Vector2 centre)
    {
        var font = ThemeDB.FallbackFont;
        void Line(string value, int size, float y, Color colour)
        {
            var at = new Vector2(centre.X - font.GetStringSize(value, fontSize: size).X / 2, centre.Y + y);
            DrawStringOutline(font, at, value, fontSize: size, size: 2, modulate: Colors.Black);
            DrawString(font, at, value, fontSize: size, modulate: colour);
        }
        Line(actor.Name, 12, -3, new Color(actor.Colour.R / 255f, actor.Colour.G / 255f, actor.Colour.B / 255f));
        Line(string.IsNullOrWhiteSpace(actor.NpcRole) ? "江湖人士" : actor.NpcRole, 10, 11, Colors.White);
    }
}
