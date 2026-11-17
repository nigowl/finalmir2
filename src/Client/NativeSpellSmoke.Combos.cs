using Godot;
using Client.MirObjects;
using Point = System.Drawing.Point;
using C = ClientPackets;
using S = ServerPackets;

namespace Crystal.Native;

public partial class NativeSpellSmoke
{
    private void Battle(bool enabled)
    {
        var menu = (PopupMenu)Client.FindChild("GameMenu", true, false);
        if ((menu.GetItemText(menu.GetItemIndex(7)) == "停止挂机") != enabled) menu.EmitSignal(PopupMenu.SignalName.IdPressed, 7L);
    }

    private async Task VerifyClassCombosOnly()
    {
        foreach (var job in Enum.GetValues<MirClass>().Where(job=>!OS.GetCmdlineUserArgs().Contains("--class-extension-only")||job is MirClass.Assassin or MirClass.Archer))
        {
            stage = $"{job}职业挂机组合";
            if (job != MirClass.Warrior) await Client.Connect(Argument("--server"));
            await Until(() => Client.HandshakeReady);
            Client.Login(job == MirClass.Warrior ? "NativeSkills" : "Native" + job, "Native123");
            await Until(() => State.Characters.Any(character => character.Class == job));
            Client.Send(new C.StartGame { CharacterIndex = State.Characters.Single(character => character.Class == job).Index });
            await Until(() => State.User?.Class == job && !Client.World.Loading && ClientAutomation.Targets(State).Any());
            // 前一职业的召唤物仍可能停留，隔离场景不能硬走同一个占用格。
            var point = Enumerable.Range(0,8).Select(d=>Functions.PointMove(State.User!.Location,(MirDirection)d,1))
                .First(p=>Client.World.Walkable(p)&&!State.Actors.Values.Any(actor=>actor.Id!=State.User!.ObjectID&&!actor.Dead&&actor.Location==p&&actor.Kind is not (ObjectType.Item or ObjectType.Spell)));
            Client.SetDestination(point); await Until(() => State.User.Location == point); await Pause(650);
            var target = ClientAutomation.Targets(State).First();
            if (job == MirClass.Warrior) { await VerifySlayingCombo(target); await VerifyThrustingCombo(target); }
            var panel = ClassPanel(); ClassOption(panel, "智能组合").ButtonPressed = true;
            await Shot("combo-settings-" + job); CloseClassPanel();
            packets.Clear(); Battle(true); Client.SelectTarget(target.Id);
            if (job == MirClass.Warrior)
            {
                var used = new HashSet<Spell>();
                await Until(() =>
                {
                    used.Add(State.Me!.ActionSpell);
                    return used.Contains(Spell.FlamingSword) && used.Contains(Spell.TwinDrakeBlade) &&
                        new[] { BuffType.ProtectionField, BuffType.Rage, BuffType.Fury, BuffType.CounterAttack }.All(State.UserBuffs.ContainsKey);
                });
            }
            else if (job == MirClass.Wizard)
            {
                await Until(() => State.Me!.MagicShield && State.UserBuffs.ContainsKey(BuffType.MagicBooster) && ClassSkillRules.HasMirror(State) &&
                    packets.OfType<S.Magic>().Any(packet => packet.Cast && packet.Spell is Spell.IceStorm or Spell.FireWall));
                Battle(false);
                var hidden = ClientAutomation.Targets(State).Where(actor => actor.Id != target.Id).ToArray();
                foreach (var actor in hidden) actor.Hidden = true;
                try
                {
                    packets.Clear(); Client.SelectTarget(target.Id);
                    await Until(() => packets.OfType<S.Magic>().Any(packet => packet.Cast && packet.Spell == Spell.FlameDisruptor));
                }
                finally { foreach (var actor in hidden) actor.Hidden = false; }
            }
            else if(job==MirClass.Taoist)
            {
                await Until(() => ClassSkillRules.HasPet(State) && new[] { BuffType.SoulShield, BuffType.BlessedArmour, BuffType.UltimateEnhancer, BuffType.EnergyShield }.All(State.UserBuffs.ContainsKey) &&
                    State.Actors.Values.Any(actor => actor.MasterObjectId == State.User.ObjectID && actor.Buffs.Contains(BuffType.PetEnhancer)) &&
                    target.Poison.HasFlag(PoisonType.Red) && target.Poison.HasFlag(PoisonType.Green) &&
                    packets.OfType<S.Magic>().Any(packet => packet.Cast && packet.Spell is Spell.SoulFireBall or Spell.PoisonCloud));
                var sequence = packets.OfType<S.Magic>().Where(packet => packet.Cast).Select(packet => packet.Spell).ToList();
                if (sequence.IndexOf(Spell.Poisoning) < 0 || sequence.IndexOf(Spell.Poisoning) > sequence.IndexOf(Spell.SoulShield))
                    throw new Exception("道士实际施法顺序没有先补毒再加增益。");
            }
            else
            {
                await Until(()=>ClassSkillRules.Maintained(job).All(spell=>ClassSkillRules.Active(State,spell)) &&
                    packets.OfType<S.Magic>().Any(packet=>packet.Cast && (job==MirClass.Assassin?packet.Spell is Spell.CrescentSlash or Spell.HeavenlySword:packet.Spell==Spell.NapalmShot)));
                var buffs=packets.OfType<S.Magic>().Count(packet=>ClassSkillRules.Maintained(job).Contains(packet.Spell));
                await Pause(2100);
                if(packets.OfType<S.Magic>().Count(packet=>ClassSkillRules.Maintained(job).Contains(packet.Spell))!=buffs)throw new Exception($"{job}已有增益仍重复施放");
                Battle(false);
                var others=ClientAutomation.Targets(State).Where(actor=>actor.Id!=target.Id).ToArray();
                foreach(var other in others)other.Hidden=true;
                try
                {
                    panel=ClassPanel();ClassOption(panel,"智能组合").ButtonPressed=false;
                    foreach(var spell in ClassSkillRules.Maintained(job))ClassOption(panel,"自动"+spell.ToLocalizedString());
                    await Shot("manual-settings-"+job);ClassOption(panel,"智能组合").ButtonPressed=true;CloseClassPanel();
                    packets.Clear();var mp=State.User!.MP;Client.SelectTarget(target.Id);
                    await Until(()=>job==MirClass.Assassin?State.Me!.ActionSpell==Spell.DoubleSlash && State.User.MP<mp && packets.OfType<S.DamageIndicator>().Any(packet=>packet.Damage<0):
                        packets.OfType<S.Magic>().Any(packet=>packet.Cast&&packet.Spell==Spell.DoubleShot));
                }
                finally{foreach(var other in others)other.Hidden=false;}
            }
            await Shot("combo-active-" + job); Battle(false);
            if (!packets.OfType<S.DamageIndicator>().Any(packet => packet.Damage < 0)) throw new Exception($"{job}组合没有真实伤害回执。");
            GD.Print($"PASS native {job} class combo: original TCP preparation/buffs/pets, automatic attacks and actual damage.");
            await LeaveCharacter();
        }
        await Client.Quit();
    }

    private async Task VerifySlayingCombo(Actor target)
    {
        stage = "攻杀原服准备回执与挂机普通攻击选择";
        var panel = ClassPanel(); ClassOption(panel, "智能组合").ButtonPressed = false; CloseClassPanel();
        var direction = Main.Direction(State.User!.Location, target.Location);
        var magic = State.User.Magics[Skill(Spell.Slaying)]; var experience = magic.Experience;
        await Until(() => Client.World.MeleeReady(Spell.Slaying, magic.Level, direction));
        for (var attempt = 0; attempt < 24 && !State.ActiveSkills.Contains(Spell.Slaying); attempt++)
        {
            Client.Attack(direction, Spell.None); await Pause(1150);
        }
        if (!State.ActiveSkills.Contains(Spell.Slaying)) throw new Exception("24次原普通攻击未收到攻杀准备回执。");
        drawn.Clear(); packets.Clear();
        Client.Attack(direction, Spell.None);
        if (State.Me!.ActionSpell != Spell.Slaying) throw new Exception("收到准备后挂机普通攻击仍没有选择攻杀。");
        var effect = OriginalSpellEffects.Melee(Spell.Slaying, magic.Level, direction)!.Value;
        await Until(() => magic.Experience > experience && drawn.Contains((effect.Library, effect.Index + effect.Count - 1)) && packets.OfType<S.DamageIndicator>().Any(packet => packet.Damage < 0));
        await Shot("slaying-original");
        GD.Print("PASS native Slaying: original readiness packet, automatic normal-attack path, complete six-frame effect and actual target damage.");
    }

    private async Task VerifyThrustingCombo(Actor target)
    {
        stage = "手动刺杀开关、隔位原地攻击和实际熟练度回执";
        var point = new Point(target.Location.X - 2, target.Location.Y);
        Client.SetDestination(point); await Until(() => State.User!.Location == point); await Pause(650);
        var magic = State.User!.Magics[Skill(Spell.Thrusting)]; var experience = magic.Experience;
        if (!State.ActiveSkills.Contains(Spell.Thrusting)) Client.Cast(Skill(Spell.Thrusting));
        var effect = OriginalSpellEffects.Melee(Spell.Thrusting, magic.Level, MirDirection.Right)!.Value;
        await Until(() => Client.World.MeleeReady(Spell.Thrusting, magic.Level, MirDirection.Right));
        packets.Clear(); drawn.Clear(); Client.SelectTarget(target.Id);
        await Until(() => magic.Experience > experience && drawn.Contains((effect.Library, effect.Index + effect.Count - 1)) &&
            packets.OfType<S.DamageIndicator>().Any(packet => packet.ObjectID == target.Id && packet.Damage < 0));
        Client.SetDestination(State.User.Location);
        if (State.User.Location != point || packets.OfType<S.MagicLeveled>().Count(packet => packet.Spell == Spell.Thrusting) != 1)
            throw new Exception("隔位刺杀仍走近或一次出刀重复增长熟练度。");
        await Shot("thrusting-second-tile"); Client.Cast(Skill(Spell.Thrusting));
        GD.Print("PASS native Thrusting: enabled original toggle, no extra smart setting, stationary two-tile hit, complete effect, real damage and one proficiency acknowledgement.");
    }
}
