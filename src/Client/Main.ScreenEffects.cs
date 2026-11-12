using Godot;

public partial class Main
{
    private ColorRect crtOverlay = null!;

    private void BuildScreenEffects()
    {
        var layer = new CanvasLayer { Layer = 100 }; AddChild(layer);
        crtOverlay = new ColorRect { Name = "CrtFilter", Size = new Vector2(1024, 768), MouseFilter = MouseFilterEnum.Ignore, Visible = false,
            Material = new ShaderMaterial { Shader = GD.Load<Shader>("res://Shaders/Crt.gdshader") } };
        layer.AddChild(crtOverlay);
    }
}
