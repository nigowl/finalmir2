using Crystal.Native;
using Godot;

public partial class SkillIconButton : Button
{
    public ResourceCache Assets = null!;
    public ClientMagic? Magic;
    public bool ShowSlotFrame;
    internal long CooldownRemaining => Magic == null ? 0 : ClientSkillRules.CooldownRemaining(Magic, Environment.TickCount64);
    public override void _Process(double delta) { if (IsVisibleInTree()) QueueRedraw(); }
    public override void _Draw()
    {
        if (ShowSlotFrame) OriginalArt.DrawQuickSlot(this, Assets);
        if (Magic == null) return;
        var image = Assets.Sprite("MagIcon2", Magic.Icon * 2 + (IsPressed() ? 1 : 0));
        if (image == null) return;
        // 快捷格已有边框，只取技能图案；列表保留原图的边框。
        var inset = ShowSlotFrame && image.Size.X > 8 && image.Size.Y > 8 ? 4 : 0;
        var source = new Rect2(Vector2.Zero, image.Size).Grow(-inset);
        var icon = OriginalArt.SlotIconRect(Size, source.Size);
        DrawTextureRectRegion(image.Texture, icon, source, Disabled ? new Color(1, 1, 1, .4f) : Colors.White);
        var remaining = CooldownRemaining;
        if (remaining > 0)
        {
            DrawRect(new Rect2(icon.Position, new Vector2(icon.Size.X, icon.Size.Y * Math.Clamp(remaining / (float)Math.Max(1, ClientSkillRules.Cooldown(Magic)), 0, 1))), new Color(0, 0, 0, .55f));
            var text = ((remaining + 999) / 1000).ToString();
            var at = new Vector2((Size.X - ThemeDB.FallbackFont.GetStringSize(text, fontSize: 11).X) / 2, Size.Y - 3);
            DrawStringOutline(ThemeDB.FallbackFont, at, text, fontSize: 11, size: 3, modulate: Colors.Black);
            DrawString(ThemeDB.FallbackFont, at, text, fontSize: 11, modulate: Colors.White);
        }
    }
}
