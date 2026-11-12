using Godot;

namespace Crystal.Native;

public partial class NativeSmoke
{
    private async Task VerifyLoginPresentation()
    {
        stage = "aligned login actions";
        var buttons = Client.FindChildren("*", "Button", true, false).OfType<OriginalButton>().Where(button => button.Library == "Title").ToArray();
        await Until(() => Client.Theme.GetStylebox("normal", "Button") is StyleBoxTexture);
        foreach (var (state, index) in new[] { ("normal", 276), ("hover", 277), ("pressed", 278) })
        {
            if (Client.Theme.GetStylebox(state, "Button") is not StyleBoxTexture style || style.Texture.GetSize() != new Vector2(24, 25))
                throw new Exception("Text actions did not reuse the marked original button states.");
            using var actual = style.Texture.GetImage(); using var source = Client.Assets.Sprite("Title", index)!.Texture.GetImage();
            if (actual.GetPixel(3, 3) != source.GetPixel(3, 3) || actual.GetPixel(21, 5) != source.GetPixel(source.GetWidth() - 3, 5))
                throw new Exception("Processing a button damaged its original border pixels.");
        }
        var register = buttons.Single(button => button.Index == 323); var quit = buttons.Single(button => button.Index == 329);
        if (register.Position.Y != quit.Position.Y || register.Position.Y != 176 || register.GetParent<Control>().GetRect().Size.Y < quit.GetRect().End.Y)
            throw new Exception("Login register/quit art did not share a horizontal baseline inside the lower panel.");
        await Screenshot("login-layout");
        GD.Print("PASS native login: original register/quit actions align horizontally within the login frame.");
    }

    private async Task VerifyInventoryPresentation()
    {
        stage = "inventory task hover, gold, weight and free slots";
        var button = Client.FindChildren("*", "Button", true, false).OfType<OriginalButton>().Single(button => button.TooltipText == "任务物品");
        var bag = button.GetParent<Control>(); bag.Show();
        if (button.Index != 739 || button.HoverIndex != 198 || button.PressedIndex != 198) throw new Exception("Task tab hover still selected the unrelated next library image.");
        await Until(() => Client.Assets.Sprite("Title", button.HoverIndex) != null);
        var weight = bag.GetNode<Label>("BagWeight"); var free = bag.GetNode<Label>("BagFree");
        var expected = $"{State.InventoryWeight}/{State.CalculateStats()[Stat.BagWeight]}";
        if (weight.Text != expected || free.Text != State.User!.Inventory.Count(item => item == null).ToString()) throw new Exception("Inventory weight or free-cell count is missing its current value.");
        var gold = bag.FindChildren("*", "Label", true, false).Cast<Label>().Single(label => label.Position == new Vector2(40, 212));
        if (gold.Text != State.User.Gold.ToString() || gold.GetRect().End.X > 151) throw new Exception("Inventory gold overlapped its original icon/field.");
        using var motion = new InputEventMouseMotion { Position = button.GetGlobalRect().GetCenter(), GlobalPosition = button.GetGlobalRect().GetCenter() };
        GetViewport().PushInput(motion, true);
        await Screenshot("inventory-task-hover");
        bag.Hide();
        GD.Print("PASS native inventory: matching original task hover art, aligned gold and populated weight/free-cell fields.");
    }
}
