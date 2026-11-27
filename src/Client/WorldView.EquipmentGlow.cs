using Client.MirObjects;
using Godot;

namespace Crystal.Native;

public partial class WorldView
{
    private readonly List<(SpriteImage Image, Vector2 Position, float Opacity)> equipmentGlows = new();
    private EquipmentGlowLayer equipmentGlowLayer = null!;
    internal int EquipmentGlowSprites => equipmentGlows.Count;
    internal int EquipmentCastFlashes { get; private set; }

    private void BuildEquipmentGlow()
    {
        var shader = new Shader { Code = """
            shader_type canvas_item;
            render_mode unshaded, blend_add;
            float solid(sampler2D source, vec2 uv) {
                if (uv.x < 0.0 || uv.y < 0.0 || uv.x > 1.0 || uv.y > 1.0) return 0.0;
                return step(0.8, texture(source, uv).a);
            }
            void fragment() {
                vec2 p = TEXTURE_PIXEL_SIZE;
                vec2 uv = (UV * (1.0 / p + vec2(6.0)) - vec2(3.0)) * p;
                float centre = solid(TEXTURE, uv);
                float edge = 0.0;
                edge = max(edge, solid(TEXTURE, uv + p * vec2(2.0, 0.0)));
                edge = max(edge, solid(TEXTURE, uv + p * vec2(-2.0, 0.0)));
                edge = max(edge, solid(TEXTURE, uv + p * vec2(0.0, 2.0)));
                edge = max(edge, solid(TEXTURE, uv + p * vec2(0.0, -2.0)));
                edge = max(edge, solid(TEXTURE, uv + p * vec2(1.5, 1.5)));
                edge = max(edge, solid(TEXTURE, uv + p * vec2(-1.5, 1.5)));
                edge = max(edge, solid(TEXTURE, uv + p * vec2(1.5, -1.5)));
                edge = max(edge, solid(TEXTURE, uv + p * vec2(-1.5, -1.5)));
                COLOR = vec4(1.0, 0.48, 0.10, (max(0.0, edge - centre) * 0.65 + centre * 0.06) * COLOR.a);
            }
            """ };
        equipmentGlowLayer = new EquipmentGlowLayer { OwnerView = this, Material = new ShaderMaterial { Shader = shader } };
        AddChild(equipmentGlowLayer);
    }

    private void QueueEquipmentGlow(Actor actor, PlayerArt art, int frame, SpriteImage? body, Vector2 position, float opacity, long now)
    {
        if (actor.Dead || actor.EquipmentGlow == EquipmentGlow.None) return;
        var strength = opacity * (.72f + .12f * MathF.Sin(now / 280f));
        void Sprite(SpriteImage? image)
        {
            if (image != null) equipmentGlows.Add((image, (position + image.Offset).Round(), strength));
        }
        if (actor.EquipmentGlow.HasFlag(EquipmentGlow.Equipment)) Sprite(body);
        if (actor.EquipmentGlow.HasFlag(EquipmentGlow.Weapon) && actor.Weapon >= 0)
        {
            if (art.Weapon.Length > 0) Sprite(Assets.Sprite(art.Weapon, frame + art.WeaponOffset));
            if (art.SecondWeapon.Length > 0) Sprite(Assets.Sprite(art.SecondWeapon, frame + art.WeaponOffset));
        }
    }

    private void ShowEquipmentCast(Actor actor, long start)
    {
        if (actor.Kind != ObjectType.Player || actor.Dead || actor.EquipmentGlow == EquipmentGlow.None || actor.Hidden && actor.Id != State.User?.ObjectID) return;
        // 只在原成功施法回执之后叠加原觉醒灵光，不增加命中、伤害或声音。
        AddEffect(new("Magic3", 880, 5, 500, Opacity: .75f), actor.Id, actor.Location, start);
        EquipmentCastFlashes++;
    }

    private partial class EquipmentGlowLayer : Node2D
    {
        public WorldView OwnerView = null!;
        public override void _Draw()
        {
            foreach (var (image, position, opacity) in OwnerView.equipmentGlows)
                DrawTextureRect(image.Texture, new Rect2(position - Vector2.One * 3, image.Size + Vector2.One * 6), false, new Color(1, 1, 1, opacity));
        }
    }
}
