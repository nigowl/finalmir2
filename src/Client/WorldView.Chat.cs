using System.Globalization;
using Godot;

namespace Crystal.Native;

public partial class WorldView
{
    internal sealed record ChatBubble(string[] Lines, float Width, long Started);
    private readonly Dictionary<uint, ChatBubble> chatBubbles = new();
    internal IReadOnlyDictionary<uint, ChatBubble> ChatBubbles => chatBubbles;
    internal int ChatBubblesDrawn { get; private set; }
    private readonly StyleBoxFlat chatBubbleSkin = new() {
        BgColor = new Color("211b14d9"), BorderColor = new Color("8b744cb8"),
        BorderWidthLeft = 1, BorderWidthRight = 1, BorderWidthTop = 1, BorderWidthBottom = 1,
        CornerRadiusTopLeft = 3, CornerRadiusTopRight = 3, CornerRadiusBottomLeft = 3, CornerRadiusBottomRight = 3 };

    private void ShowChatBubble(uint id, string message)
    {
        if (!State.Actors.TryGetValue(id, out var actor) || actor.Hidden && id != State.User?.ObjectID) return;
        var text = ChatChannels.BubbleText(message, actor.Name);
        if (text.Length == 0) { chatBubbles.Remove(id); return; }
        var lines = new List<string>(); var line = "";
        var elements = StringInfo.GetTextElementEnumerator(text);
        while (elements.MoveNext())
        {
            var next = elements.GetTextElement();
            if (ThemeDB.FallbackFont.GetStringSize(line + next, fontSize: ClientUi.Small).X <= 164) { line += next; continue; }
            if (lines.Count == 0) { lines.Add(line); line = next; continue; }
            while (line.Length > 0 && ThemeDB.FallbackFont.GetStringSize(line + "…", fontSize: ClientUi.Small).X > 164)
                line = line[..StringInfo.ParseCombiningCharacters(line)[^1]];
            line += "…"; break;
        }
        lines.Add(line);
        if (chatBubbles.Count >= 64 && !chatBubbles.ContainsKey(id)) chatBubbles.Remove(chatBubbles.MinBy(pair => pair.Value.Started).Key);
        chatBubbles[id] = new(lines.ToArray(), lines.Max(value => ThemeDB.FallbackFont.GetStringSize(value, fontSize: ClientUi.Small).X) + 16, Environment.TickCount64);
    }

    private void DrawChatBubbles(CanvasItem canvas)
    {
        ChatBubblesDrawn = 0;
        if (State.User == null || Loading) return;
        foreach (var (id, bubble) in chatBubbles)
        {
            if (frameTime - bubble.Started >= 5000 || !State.Actors.TryGetValue(id, out var actor) || actor.Hidden && id != State.User.ObjectID) continue;
            var anchor = HeadAnchor(actor, frameTime);
            if (anchor.X < -32 || anchor.X > 1056 || anchor.Y < -32 || anchor.Y > 648) continue;
            var size = new Vector2(bubble.Width, bubble.Lines.Length * 14 + 10);
            var position = new Vector2(Mathf.Clamp(anchor.X - size.X / 2, 4, 1020 - size.X), Mathf.Clamp(anchor.Y - 36 - size.Y, 4, 608 - size.Y)).Round();
            var tail = new Vector2(Mathf.Clamp(anchor.X, position.X + 6, position.X + size.X - 6), position.Y + size.Y);
            canvas.DrawColoredPolygon([tail + new Vector2(-4, 0), tail + new Vector2(4, 0), tail + new Vector2(0, 6)], chatBubbleSkin.BgColor);
            canvas.DrawStyleBox(chatBubbleSkin, new Rect2(position, size));
            for (var i = 0; i < bubble.Lines.Length; i++)
                canvas.DrawString(ThemeDB.FallbackFont, position + new Vector2(8, 15 + i * 14), bubble.Lines[i], fontSize: ClientUi.Small, modulate: new Color("eee4cf"));
            ChatBubblesDrawn++;
        }
    }
}
