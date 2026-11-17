using Crystal.Native;
using Godot;

public partial class SkillIconButton : Button
{
    public ResourceCache Assets = null!;
    public ClientMagic? Magic;
    public bool ShowSlotFrame;
    public Func<long>? GlobalCooldownUntil;
    internal long CooldownRemaining => Magic != null && !ClientSkillRules.IsPassive(Magic.Spell) && !ClientSkillRules.IsToggle(Magic.Spell)
        ? Math.Max(0, Math.Max(Magic.CastTime + ClientSkillRules.Cooldown(Magic), GlobalCooldownUntil?.Invoke() ?? 0) - Environment.TickCount64) : 0;
    public override void _Process(double delta) { if (IsVisibleInTree()) QueueRedraw(); }
    public override void _Draw()
    {
        if (ShowSlotFrame) OriginalArt.DrawQuickSlot(this, Assets);
        if (Magic == null) return;
        var image = Assets.Sprite("MagIcon2", Magic.Icon * 2 + (IsPressed() ? 1 : 0));
        if (image == null) return;
        if (ShowSlotFrame)
            // 原技能图自带边框，快捷格只取内部图案，避免双框和留白使图标显小。
            DrawTextureRectRegion(image.Texture, new Rect2(2, 2, Size.X - 4, Size.Y - 4), new Rect2(4, 4, image.Size.X - 8, image.Size.Y - 8), Disabled ? new Color(1, 1, 1, .4f) : Colors.White);
        else
        {
            var size = image.Size * Math.Min((Size.X - 4) / image.Size.X, (Size.Y - 4) / image.Size.Y);
            DrawTextureRect(image.Texture, new Rect2((Size - size) / 2, size), false, Disabled ? new Color(1, 1, 1, .4f) : Colors.White);
        }
        var remaining = CooldownRemaining;
        if (remaining > 0)
        {
            DrawRect(new Rect2(Vector2.Zero, new Vector2(Size.X, Size.Y * Math.Clamp(remaining / (float)Math.Max(1800, Magic.Delay), 0, 1))), new Color(0, 0, 0, .55f));
            var text = ((remaining + 999) / 1000).ToString();
            var at = new Vector2((Size.X - ThemeDB.FallbackFont.GetStringSize(text, fontSize: 11).X) / 2, Size.Y - 3);
            DrawStringOutline(ThemeDB.FallbackFont, at, text, fontSize: 11, size: 3, modulate: Colors.Black);
            DrawString(ThemeDB.FallbackFont, at, text, fontSize: 11, modulate: Colors.White);
        }
    }
}
