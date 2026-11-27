using Crystal.Native;
using Client.MirObjects;
using Godot;

// Geometry and library indices follow MainDialogs, CharacterDialog and SelectScene.
public partial class OriginalArt : Control
{
    internal static Vector2 QuickSlotPosition(int slot) => new(237 + slot * 34 + (slot >= 6 ? 6 : 0), 629);
    internal static void DrawQuickSlot(Control control, ResourceCache assets)
    {
        var rect = new Rect2(Vector2.Zero, control.Size);
        var source = new Rect2(10, 0, 37, 38);
        var background = assets.Sprite("Prguse", 1933);
        if (background != null) control.DrawTextureRectRegion(background.Texture, rect, source, new Color(1, 1, 1, .5f));
        var border = assets.Sprite("Prguse", 1932);
        if (border != null) control.DrawTextureRectRegion(border.Texture, rect, source);
    }
    internal static Rect2 SlotIconRect(Vector2 slot, Vector2 source)
    {
        var inner = slot - Vector2.One * (2 * ClientUi.SlotInset);
        var size = (source * Math.Min(inner.X / source.X, inner.Y / source.Y)).Floor().Max(Vector2.One);
        return new Rect2(((slot - size) / 2).Floor(), size);
    }
    public ResourceCache Assets = null!;
    public GameState State = null!;
    public string Page = "hud";
    public bool Hero;
    public SelectInfo? Character;
    private SelectInfo? previewCharacter;
    private SpriteImage? previewImage;
    private long previewStarted;
    internal int PreviewFrame { get; private set; } = -1;
    internal bool PreviewReady { get; private set; }
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
            if (Character != previewCharacter)
            { previewCharacter = Character; previewImage = null; PreviewFrame = -1; PreviewReady = false; previewStarted = 0; }
            if (Character == null) return;
            var start = OriginalPlayerArt.SelectionStart(Character.Class, Character.Gender);
            if (!PreviewReady)
            {
                var ready = true;
                for (var frame = 0; frame < 16; frame++) ready &= Assets.PrepareSprite("ChrSel", start + frame);
                if (ready) { PreviewReady = true; previewStarted = Environment.TickCount64; }
            }
            var index = start + (PreviewReady ? (int)((Environment.TickCount64 - previewStarted) / 250 % 16) : 0);
            // 切帧未就绪时保留上一张；先备齐原16帧，不让加载间隙画成空白。
            if (Assets.Sprite("ChrSel", index) is { } image) { previewImage = image; PreviewFrame = index; }
            if (previewImage != null) DrawTexture(previewImage.Texture, new Vector2(260, 420) + previewImage.Offset);
            return;
        }
        var user = Page == "equipment" && Hero ? State.Hero : State.User;
        if (user == null) return;
        if (Page == "inventory-grid")
        {
            var count = GetChildCount();
            if (Assets.Sprite("Title", 196) is { } background)
                for (var slot = 0; slot < count; slot += ClientUi.InventoryColumns)
                {
                    var y = slot / ClientUi.InventoryColumns * 33;
                    var width = Math.Min(295, (count - slot) * 37 - 1);
                    DrawTextureRectRegion(background.Texture, new Rect2(0, y, width, 33), new Rect2(9, 70, width, 33));
                }
            return;
        }
        if (Page == "inventory")
        {
            var ratio = Math.Clamp((float)State.InventoryWeight / (float)Math.Max(1, State.CalculateStats()[Stat.BagWeight]), 0, 1);
            var bar = Assets.Sprite("Prguse", 24);
            if (bar != null && ratio > 0)
            {
                var size = new Vector2((bar.Size.X - 3) * ratio, bar.Size.Y);
                DrawTextureRectRegion(bar.Texture, new Rect2(new Vector2(182, 217 + ClientUi.InventoryExtraHeight), size), new Rect2(Vector2.Zero, size), WeightTint(ratio));
            }
            return;
        }
        if (Page == "equipment")
        {
            var origin = new Vector2(8, 90);
            Sprite("Prguse", user.Gender == MirGender.Male ? 340 : 341, origin);
            foreach (var slot in new[] { EquipmentSlot.Armour, EquipmentSlot.Weapon, EquipmentSlot.Helmet })
                if (user.Equipment[(int)slot]?.Info is { } item) Sprite("Stateitem", item.Image, Vector2.Zero, true);
            if (user.Equipment[(int)EquipmentSlot.Helmet] == null)
                Sprite("Prguse", 441 + user.Hair + (user.Class == MirClass.Assassin ? 20 : 0) + (user.Gender == MirGender.Female ? 40 : 0),
                    user.Class == MirClass.Assassin ? user.Gender == MirGender.Male ? new Vector2(6, 25) : new Vector2(4, 18) : Vector2.Zero, true);
            Sprite("Prguse", 100 + (int)user.Class, new Vector2(15, 33));
            return;
        }
        var stats = State.CalculateStats();
        var hpOnly = user.Class == MirClass.Warrior && user.Level < 26;
        var orb = Assets.Sprite("Prguse", hpOnly ? 6 : 4);
        if (orb != null)
        {
            var hp = Math.Clamp(user.HP / (float)Math.Max(1, stats[Stat.HP]), 0, 1);
            var mp = Math.Clamp(user.MP / (float)Math.Max(1, stats[Stat.MP]), 0, 1);
            DrawTextureRectRegion(orb.Texture, new Rect2(0, 646 + 80 * (1 - hp), hpOnly ? 100 : 50, 80 * hp), new Rect2(0, 80 * (1 - hp), hpOnly ? 100 : 50, 80 * hp));
            if (!hpOnly) DrawTextureRectRegion(orb.Texture, new Rect2(51, 646 + 80 * (1 - mp), 50, 80 * mp), new Rect2(51, 80 * (1 - mp), 50, 80 * mp));
        }
        var weightRatio = Math.Clamp((float)State.InventoryWeight / (float)Math.Max(1, stats[Stat.BagWeight]), 0, 1);
        var weight = Assets.Sprite("Prguse", 76);
        if (weight != null && weightRatio > 0)
        {
            var fill = new Vector2((weight.Size.X - 2) * weightRatio, weight.Size.Y);
            DrawTextureRectRegion(weight.Texture, new Rect2(new Vector2(919, 719), fill), new Rect2(Vector2.Zero, fill), WeightTint(weightRatio));
        }
        var experience = Assets.Sprite("Prguse", 8);
        var fraction = Math.Clamp(user.Experience / (double)Math.Max(1, user.MaxExperience), 0, 1);
        if (experience != null) DrawTextureRectRegion(experience.Texture,
            new Rect2(9, 759, (float)(1001 * fraction), 8), new Rect2(0, 0, (float)(1001 * fraction), 8));
    }

    private static Color WeightTint(float ratio) => ratio <= .5f ? Colors.White : ratio <= .75f ? new Color("ffcc66") : new Color("ff7777");
}

public partial class OriginalButton : Button
{
    public ResourceCache Assets = null!;
    public string Library = "Prguse";
    public int Index, HoverIndex = -1, PressedIndex = -1;
    public int ArtSize;
    public override void _Ready()
    {
        FocusMode = FocusModeEnum.None;
        if (Text.Length > 0) return;
        foreach (var state in new[] { "normal", "hover", "pressed", "hover_pressed", "disabled", "focus" }) AddThemeStyleboxOverride(state, new StyleBoxEmpty());
    }
    public override void _Process(double delta) { if (IsVisibleInTree()) QueueRedraw(); }
    public override void _Draw()
    {
        if (Text.Length > 0) return;
        var index = Index;
        if (IsPressed()) index = PressedIndex < 0 ? Index + 2 : PressedIndex;
        else if (IsHovered()) index = HoverIndex < 0 ? Index + 1 : HoverIndex;
        var image = Assets.Sprite(Library, index);
        if (image == null) return;
        Size = ArtSize > 0 ? Vector2.One * ArtSize : image.Size;
        DrawTextureRect(image.Texture, new Rect2(Vector2.Zero, Size), false, Disabled ? new Color(1, 1, 1, .4f) : Colors.White);
    }
}

public partial class OriginalItemButton : ItemTipButton
{
    public ResourceCache? SlotAssets;
    public Texture2D? SlotIcon;
    public uint Count;
    public override void _Process(double delta) { if (SlotAssets != null && IsVisibleInTree()) QueueRedraw(); }
    public override void _Draw()
    {
        if (SlotAssets != null)
        {
            OriginalArt.DrawQuickSlot(this, SlotAssets);
            if (SlotIcon != null)
                DrawTextureRect(SlotIcon, OriginalArt.SlotIconRect(Size, SlotIcon.GetSize()), false, Disabled ? new Color(1, 1, 1, .4f) : Colors.White);
        }
        base._Draw();
        if (Count <= 1) return;
        var text = Count.ToString();
        var position = new Vector2(Size.X - ThemeDB.FallbackFont.GetStringSize(text, fontSize: 10).X - 1, Size.Y - 1);
        DrawStringOutline(ThemeDB.FallbackFont, position, text, fontSize: 10, size: 3, modulate: Colors.Black);
        DrawString(ThemeDB.FallbackFont, position, text, fontSize: 10, modulate: Colors.White);
    }
}
