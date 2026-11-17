using Godot;
using S = ServerPackets;

namespace Crystal.Native;

public partial class WorldView
{
    internal readonly record struct FloatingText(uint Actor, string Text, int Size, Color Colour, long Start, int Duration, int Lane, bool Status);
    private readonly List<FloatingText> combatTexts = new();
    private readonly Dictionary<(uint Actor, BuffType Type), bool> combatBuffs = new();
    internal IReadOnlyList<FloatingText> CombatTexts => combatTexts;
    internal int CombatTextsDrawn { get; private set; }

    private void ClearCombatText() { combatTexts.Clear(); combatBuffs.Clear(); }

    public void ShowDamage(uint id, int amount, DamageType type = DamageType.Hit)
    {
        var colour = amount == 0 || type == DamageType.Miss ? new Color("c4c4c4") : amount > 0 ? new Color("83f3a0")
            : type == DamageType.Critical ? new Color("ffdf65") : id == State.User?.ObjectID ? new Color("ff7272") : new Color("ffb264");
        AddCombatText(id, CombatPresentation.DamageText(amount, type), CombatPresentation.DamageFontSize(amount, type), colour, false);
    }

    private void AddCombatText(uint id, string text, int size, Color colour, bool status)
    {
        if (!State.Actors.TryGetValue(id, out var actor) || actor.Hidden && id != State.User?.ObjectID) return;
        var now = Environment.TickCount64;
        var lane = combatTexts.Count(item => item.Actor == id && item.Status == status && now - item.Start < 450) % 3;
        if (combatTexts.Count >= 128) combatTexts.RemoveAt(0);
        combatTexts.Add(new(id, text, size, colour, now, status ? 1500 : 1100, lane, status));
    }

    private void CombatBuff(uint id, BuffType type, bool active, string prefix = "+ ")
    {
        if (type == BuffType.None) return;
        var key = (id, type);
        if (combatBuffs.TryGetValue(key, out var previous) && previous == active || combatBuffs.Count >= 1024 && !combatBuffs.ContainsKey(key)) return;
        combatBuffs[key] = active;
        if (active || previous) AddCombatText(id, active ? prefix + type.ToLocalizedString() : type.ToLocalizedString() + "消失", 13,
            active ? new Color("94e4ff") : new Color("bfb5a4"), true);
    }

    private void CombatTextPacket(Packet packet)
    {
        switch (packet)
        {
            case S.UserInformation or S.MapChanged or S.Disconnect or S.LogOutSuccess or S.ReturnToLogin: ClearCombatText(); break;
            case S.ObjectRemove p:
                combatTexts.RemoveAll(text => text.Actor == p.ObjectID);
                foreach (var key in combatBuffs.Keys.Where(key => key.Actor == p.ObjectID).ToArray()) combatBuffs.Remove(key);
                break;
            case S.AddBuff p when p.Buff.Visible: CombatBuff(p.Buff.ObjectID, p.Buff.Type, !p.Buff.Paused); break;
            case S.RemoveBuff p:
                CombatBuff(p.ObjectID, p.Type, false); combatBuffs.Remove((p.ObjectID, p.Type)); break;
            case S.PauseBuff p when combatBuffs.ContainsKey((p.ObjectID, p.Type)):
                if (p.Paused) combatBuffs[(p.ObjectID, p.Type)] = false;
                else CombatBuff(p.ObjectID, p.Type, true, "恢复 ");
                break;
            case S.ObjectEffect p when p.Effect is SpellEffect.MagicShieldUp or SpellEffect.MagicShieldDown:
                CombatBuff(p.ObjectID, BuffType.MagicShield, p.Effect == SpellEffect.MagicShieldUp); break;
            case S.ObjectEffect p when p.Effect is SpellEffect.ElementalBarrierUp or SpellEffect.ElementalBarrierDown:
                CombatBuff(p.ObjectID, BuffType.ElementalBarrier, p.Effect == SpellEffect.ElementalBarrierUp); break;
        }
    }

    private void DrawCombatText(CanvasItem canvas)
    {
        CombatTextsDrawn = 0;
        if (State.User == null || Loading) return;
        foreach (var text in combatTexts)
        {
            if (!State.Actors.TryGetValue(text.Actor, out var actor) || actor.Hidden && actor.Id != State.User.ObjectID) continue;
            var progress = Math.Clamp((frameTime - text.Start) / (float)text.Duration, 0, 1);
            var alpha = 1 - Mathf.SmoothStep(0, 1, Math.Clamp((progress - .65f) / .35f, 0, 1));
            var pulse = text.Status ? 1 : 1 + .25f * MathF.Sin(Math.Min(progress / .22f, 1) * MathF.PI);
            var size = (int)MathF.Round(text.Size * pulse);
            var offset = text.Status ? new Vector2(24, -92 - text.Lane * 18 - progress * 25)
                : new Vector2(24 + (text.Lane - 1) * (10 + 20 * progress), -54 - text.Lane * 26 - (1 - MathF.Pow(1 - progress, 2)) * 48);
            var position = Screen(Visual(actor, frameTime)) + offset;
            position.X -= ThemeDB.FallbackFont.GetStringSize(text.Text, fontSize: size).X / 2;
            canvas.DrawStringOutline(ThemeDB.FallbackFont, position.Round(), text.Text, fontSize: size, size: 2, modulate: new Color(0, 0, 0, alpha));
            canvas.DrawString(ThemeDB.FallbackFont, position.Round(), text.Text, fontSize: size, modulate: new Color(text.Colour, alpha));
            CombatTextsDrawn++;
        }
    }

    private partial class CombatTextLayer : Node2D
    {
        internal WorldView View = null!;
        public override void _Draw() => View.DrawCombatText(this);
    }
}
