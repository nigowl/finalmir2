using Crystal.Native;
using Godot;

// Geometry and library indices follow MainDialogs, CharacterDialog and SelectScene.
public partial class OriginalArt : Control
{
    public ResourceCache Assets = null!;
    public GameState State = null!;
    public string Page = "hud";
    public SelectInfo? Character;
    public override void _Ready() { MouseFilter = MouseFilterEnum.Ignore; }
    public override void _Process(double delta) { if (IsVisibleInTree()) QueueRedraw(); }
    private void Sprite(string library, int index, Vector2 position, bool offset = false)
    {
        var image = Assets.Sprite(library, index);
        if (image != null) DrawTexture(image.Texture, position + (offset ? image.Offset : Vector2.Zero));
    }
    public override void _Draw()
    {
        if (Page == "select")
        {
            if (Character == null) return;
            var start = Character.Class switch { MirClass.Warrior => 20, MirClass.Wizard => 40, MirClass.Taoist => 60, _ => 20 };
            if (Character.Gender == MirGender.Female) start += 280;
            Sprite("ChrSel", start + (int)(Environment.TickCount64 / 250 % 16), new Vector2(260, 420), true);
            return;
        }
        var user = State.User;
        if (user == null) return;
        if (Page == "equipment")
        {
            var origin = new Vector2(8, 90);
            Sprite("Prguse", user.Gender == MirGender.Male ? 340 : 341, origin);
            foreach (var slot in new[] { EquipmentSlot.Armour, EquipmentSlot.Weapon, EquipmentSlot.Helmet })
                if (user.Equipment[(int)slot]?.Info is { } item) Sprite("Stateitem", item.Image, origin, true);
            if (user.Equipment[(int)EquipmentSlot.Helmet] == null)
                Sprite("Prguse", 441 + user.Hair + (user.Gender == MirGender.Female ? 40 : 0), origin, true);
            Sprite("Prguse", 100 + (int)user.Class, new Vector2(15, 33));
            return;
        }
        var stats = State.CalculateStats();
        var orb = Assets.Sprite("Prguse", 4);
        if (orb != null)
        {
            var hp = Math.Clamp(user.HP / (float)Math.Max(1, stats[Stat.HP]), 0, 1);
            var mp = Math.Clamp(user.MP / (float)Math.Max(1, stats[Stat.MP]), 0, 1);
            DrawTextureRectRegion(orb.Texture, new Rect2(0, 646 + 80 * (1 - hp), 50, 80 * hp), new Rect2(0, 80 * (1 - hp), 50, 80 * hp));
            DrawTextureRectRegion(orb.Texture, new Rect2(51, 646 + 80 * (1 - mp), 50, 80 * mp), new Rect2(51, 80 * (1 - mp), 50, 80 * mp));
        }
        var experience = Assets.Sprite("Prguse", 8);
        var fraction = Math.Clamp(user.Experience / (double)Math.Max(1, user.MaxExperience), 0, 1);
        if (experience != null) DrawTextureRectRegion(experience.Texture,
            new Rect2(9, 759, (float)(1001 * fraction), 8), new Rect2(0, 0, (float)(1001 * fraction), 8));
    }
}

public partial class OriginalButton : Button
{
    public ResourceCache Assets = null!;
    public string Library = "Prguse";
    public int Index, HoverIndex = -1, PressedIndex = -1;
    public override void _Ready()
    {
        FocusMode = FocusModeEnum.None;
        foreach (var state in new[] { "normal", "hover", "pressed", "focus" }) AddThemeStyleboxOverride(state, new StyleBoxEmpty());
    }
    public override void _Process(double delta) { if (IsVisibleInTree()) QueueRedraw(); }
    public override void _Draw()
    {
        var index = Index;
        if (IsPressed()) index = PressedIndex < 0 ? Index + 2 : PressedIndex;
        else if (IsHovered()) index = HoverIndex < 0 ? Index + 1 : HoverIndex;
        var image = Assets.Sprite(Library, index);
        if (image != null) { Size = image.Size; DrawTexture(image.Texture, Vector2.Zero, Disabled ? new Color(1, 1, 1, 0.4f) : Colors.White); }
    }
}
