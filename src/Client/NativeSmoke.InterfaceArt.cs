using Godot;

namespace Crystal.Native;

public partial class NativeSmoke
{
    private async Task VerifyCharacterCardArtwork(VBoxContainer list)
    {
        var rows = list.GetChildren().OfType<Control>().ToArray();
        await Until(() => rows.All(row => row.GetChildren().OfType<TextureRect>().Single().Texture != null));
        for (var i = 0; i < rows.Length; i++)
        {
            using var clean = rows[i].GetChildren().OfType<TextureRect>().Single().Texture.GetImage();
            var index = State.Characters[i].Class switch { MirClass.Warrior => 658, MirClass.Wizard => 659, MirClass.Taoist => 660, _ => 658 + (int)State.Characters[i].Class };
            using var source = Client.Assets.Sprite("Title", index)!.Texture.GetImage();
            if (clean.GetPixel(3, 3) != source.GetPixel(3, 3) || clean.GetPixel(20, 20) != source.GetPixel(20, 20) ||
                clean.GetPixel(90, 14) != source.GetPixel(65, 14) || clean.GetPixel(156, 35) != source.GetPixel(65, 35))
                throw new Exception("选角卡片未去除烘焙标签，或损坏了外框与职业图标。");
            var captions = rows[i].GetChildren().OfType<Label>().Where(label => label.Text is "名字" or "等级" or "职业").ToArray();
            if (captions.Length != 3 || captions.Any(label => label.GetThemeFontSize("font_size") != ClientUi.Small || label.GetThemeFont("font").GetInstanceId() != Client.Theme.DefaultFont.GetInstanceId()))
                throw new Exception("选角卡片标签没有改为统一字体的动态文字。");
        }
    }

    private async Task VerifyGenericSkinSizes()
    {
        var preview = new PanelContainer { Position = new Vector2(94, 272), CustomMinimumSize = new Vector2(650, 220), MouseFilter = Control.MouseFilterEnum.Ignore };
        preview.AddThemeStyleboxOverride("panel", Client.Theme.GetStylebox("panel", "PopupMenu")); Client.AddChild(preview);
        try
        {
            var stack = new VBoxContainer(); stack.AddThemeConstantOverride("separation", 14); preview.AddChild(stack);
            var heading = new Label { Text = "无字通用底图 · 原角色页签 / 原版操作按钮" }; heading.AddThemeFontSizeOverride("font_size", ClientUi.Title); stack.AddChild(heading);
            foreach (var tabs in new[] { true, false })
            {
                var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 8); stack.AddChild(row);
                foreach (var width in new[] { 60, 85, 120, 180 })
                {
                    var button = new Button { Text = tabs ? "页签" : "按钮", ThemeTypeVariation = tabs ? "GameTab" : "", ToggleMode = tabs,
                        ButtonPressed = tabs && width == 85, FocusMode = Control.FocusModeEnum.None,
                        CustomMinimumSize = new Vector2(width, tabs ? ClientUi.TabHeight : ClientUi.ButtonHeight) };
                    row.AddChild(button);
                }
            }
            var art = new HBoxContainer(); art.AddThemeConstantOverride("separation", 18); stack.AddChild(art);
            foreach (var state in new[] { "normal", "pressed" })
                art.AddChild(new TextureRect { Texture = ((StyleBoxTexture)Client.Theme.GetStylebox(state, "GameTab")).Texture,
                    CustomMinimumSize = new Vector2(48, 60), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                    StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, TextureFilter = CanvasItem.TextureFilterEnum.Nearest });
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            foreach (var button in preview.FindChildren("*", "Button", true, false).OfType<Button>())
                if (button.Size != button.CustomMinimumSize) throw new Exception("不同尺寸的通用底图被文字或默认主题撑大。");
            await Screenshot("generic-interface-skins");
        }
        finally { preview.QueueFree(); }
    }
}
