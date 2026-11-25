using System.Reflection;
using Godot;
using C = ClientPackets;
using S = ServerPackets;

namespace Crystal.Native;

public partial class NativeSmoke
{
    private async Task VerifySkillRemoval()
    {
        if (!(Environment.GetEnvironmentVariable("MIR2_CLIENT_CACHE") ?? "").Contains("mir2-skill-removal.", StringComparison.Ordinal) || AssetsPortIsDefault())
            throw new InvalidOperationException("技能移除检查仅允许隔离版本。");
        stage = "skill removal login";
        await Until(() => Client.HandshakeReady); Client.Login("NativeGM", "Native123");
        Client.Send(new C.StartGame { CharacterIndex = (await Packet<S.LoginSuccess>()).Characters.Single().Index });
        await Until(() => State.User != null && !Client.World.Loading);
        SkillIconButton Shortcut(int number) => (SkillIconButton)Client.FindChild($"SkillShortcut{number}", true, false);
        if (OS.GetCmdlineUserArgs().Contains("--verify-skill-removal-save"))
        {
            if (State.User!.Magics.Count != 0 || Shortcut(1).Magic != null || Shortcut(2).Magic != null)
                throw new Exception("正常保存重启后恢复了已删除技能或旧快捷键。");
            GD.Print("PASS native skill removal saved: original server skill list and empty shortcuts survive a complete restart.");
            await Client.Quit(); return;
        }
        async Task Give(Spell spell)
        {
            packets.RemoveAll(packet => packet is S.NewMagic);
            Client.Send(new C.Chat { Message = $"@给予技能 {spell} 3" });
            if ((await Packet<S.NewMagic>()).Magic.Spell != spell) throw new Exception("隔离原GM学习回执错误。");
        }
        async Task Remove(Spell spell, Action? duringPacket = null)
        {
            var index = State.User!.Magics.FindIndex(magic => magic.Spell == spell);
            void Received(Packet packet) { if (packet is S.RemoveMagic) duringPacket?.Invoke(); }
            Client.PacketReceived += Received; packets.RemoveAll(packet => packet is S.RemoveMagic);
            try
            {
                Client.Send(new C.Chat { Message = $"@删除技能 {spell}" });
                if ((await Packet<S.RemoveMagic>()).PlaceId != index || State.User.Magics.Any(magic => magic.Spell == spell))
                    throw new Exception("原技能移除位置或列表更新错误：" + spell);
            }
            finally { Client.PacketReceived -= Received; }
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (Client.FindChild($"Skill{spell}", true, false) != null) throw new Exception("技能面板遗留已移除技能：" + spell);
        }
        stage = "original GM learning and bound skill rows";
        await Give(Spell.Fencing); await Give(Spell.FireBall); await Give(Spell.MagicShield);
        Client.BindMagic(State.User!.Magics.FindIndex(magic => magic.Spell == Spell.FireBall), 1);
        Client.BindMagic(State.User.Magics.FindIndex(magic => magic.Spell == Spell.MagicShield), 2);
        OpenSkills(); await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        var shield = State.User.Magics.Single(magic => magic.Spell == Spell.MagicShield);
        var oldShieldButton = SkillRow(Spell.MagicShield).GetNode<SkillIconButton>("SkillIcon");
        var mp = State.User.MP; var cost = ClassSkillRules.ManaCost(State, shield);
        stage = "removing an earlier passive keeps the old surviving row bound to its spell";
        packets.RemoveAll(packet => packet is S.Magic);
        await Remove(Spell.Fencing, () => oldShieldButton.EmitSignal(BaseButton.SignalName.Pressed));
        if ((await Packet<S.Magic>()) is not { Spell: Spell.MagicShield, Cast: true }) throw new Exception("移除前项后旧技能行施放了错误技能。");
        await Until(() => State.UserBuffs.ContainsKey(BuffType.MagicShield));
        if (cost != 50 || State.User.MP != mp - cost) throw new Exception("旧保留技能控件施盾未按原服扣除50MP。");
        if (Shortcut(1).Magic?.Spell != Spell.FireBall || Shortcut(2).Magic != shield) throw new Exception("移除前项改变了保留技能快捷键。");
        stage = "removed row callbacks cannot cast or rebind the next spell";
        var fire = SkillRow(Spell.FireBall);
        var oldFireButton = fire.GetNode<SkillIconButton>("SkillIcon");
        var oldFireKeys = fire.GetChildren().OfType<OptionButton>().Single();
        await Remove(Spell.FireBall, () =>
        {
            oldFireButton.EmitSignal(BaseButton.SignalName.Pressed);
            oldFireKeys.EmitSignal(OptionButton.SignalName.ItemSelected, oldFireKeys.GetItemIndex(3));
        });
        if (Shortcut(1).Magic != null || !Shortcut(1).Disabled || Shortcut(1).TooltipText.Contains("火球") || shield.Key != 2 || Shortcut(2).Magic != shield)
            throw new Exception("移除后快捷栏仍可用，或旧技能行把快捷键改给了下一项。");
        await Screenshot("skill-removed");

        stage = "removed queued cast cancels without clearing an unacknowledged item swap";
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        FieldInfo Field(string name) => typeof(Main).GetField(name, flags)!;
        await Until(() => Environment.TickCount64 >= Math.Max(shield.CastTime + shield.Delay, (long)Field("spellTime").GetValue(Client)!) &&
            !(bool)typeof(Main).GetMethod("ActionInProgress", flags)!.Invoke(Client, [State.Me!, Environment.TickCount64, false])!);
        Field("moving").SetValue(Client, true); Field("moveTime").SetValue(Client, Environment.TickCount64);
        Client.Cast(State.User.Magics.FindIndex(magic => magic.Spell == Spell.MagicShield));
        if (Field("preparedSkill").GetValue(Client) == null) throw new Exception("未建立等待移动确认的施法测试。");
        const ulong pendingSwap = ulong.MaxValue - 1;
        Field("skillEquipmentPending").SetValue(Client, pendingSwap); Field("skillEquipmentSince").SetValue(Client, Environment.TickCount64);
        Field("classPendingSpell").SetValue(Client, Spell.MagicShield);
        await Remove(Spell.MagicShield, () =>
        {
            if (Field("preparedSkill").GetValue(Client) != null || (Spell)Field("classPendingSpell").GetValue(Client)! != Spell.None ||
                (ulong)Field("skillEquipmentPending").GetValue(Client)! != pendingSwap)
                throw new Exception("删除技能后仍保留未发施法，或错误解除未决物品锁。");
            Field("skillEquipmentPending").SetValue(Client, 0UL); Field("moving").SetValue(Client, false);
        });
        if (State.User.Magics.Count != 0 || Shortcut(2).Magic != null || !Shortcut(2).Disabled || !State.UserBuffs.ContainsKey(BuffType.MagicShield))
            throw new Exception("末项移除后技能未清空，或错误移除了仍有效的服务器增益。");
        await Screenshot("skills-empty");
        await Until(() => Environment.TickCount64 - shield.CastTime > Globals.LogDelay + 200);
        Client.Send(new C.LogOut()); await Packet<S.LogOutSuccess>();
        GD.Print("PASS native skill removal: actual original TCP list indices, surviving row casts correctly, deleted row cannot cast/rebind, shortcuts clear, injected queued cast cancels with item lock retained, existing buff remains and real logout succeeds.");
        await Client.Quit();
    }
}
