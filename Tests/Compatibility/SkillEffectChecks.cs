using Client.MirObjects;
using Crystal.Native;
using Point = System.Drawing.Point;
using S = ServerPackets;

internal static class SkillEffectChecks
{
    public static void Run(Action<bool, string> check)
    {
        check(!ClientSkillRules.IsImplemented(Spell.FastMove) && !ClientSkillRules.IsImplemented((Spell)199) && ClientSkillRules.IsImplemented(Spell.FlamingSword), "未实现的瞬步或未知技能仍可由界面施放");
        foreach (var (poison, colour) in new[] { (PoisonType.None, System.Drawing.Color.White), (PoisonType.Green, System.Drawing.Color.Green),
            (PoisonType.Red | PoisonType.Green, System.Drawing.Color.Red), (PoisonType.Red | PoisonType.Paralysis, System.Drawing.Color.Gray),
            (PoisonType.Red | PoisonType.Frozen, System.Drawing.Color.Blue), (PoisonType.Bleeding, System.Drawing.Color.DarkRed) })
            check(OriginalSpellEffects.PoisonColour(poison) == colour, $"原中毒/麻痹/冰冻颜色或优先级错误：{poison}");
        var noSeparateSprite = new HashSet<Spell> { Spell.Fencing, Spell.SpiritSword, Spell.FastMove, Spell.EnergyShield,
            Spell.Meditation, Spell.Concentration, Spell.MentalState, Spell.ShoulderDash, Spell.FlashDash, Spell.BackStep,
            Spell.FatalSword, Spell.MPEater, Spell.Hemorrhage };
        foreach (var spell in Enum.GetValues<Spell>().Where(spell => (byte)spell is > 0 and < 200))
        {
            var casts = OriginalSpellEffects.Casting(spell, MirDirection.Right);
            var missile = OriginalSpellEffects.Missile(spell);
            var hit = OriginalSpellEffects.Impact(spell, MirDirection.Right);
            var melee = OriginalSpellEffects.Melee(spell, 3, MirDirection.Right);
            check(casts.Length > 0 || missile != null || hit != null || melee != null || OriginalSpellEffects.Ground(spell, 0, false) != null || noSeparateSprite.Contains(spell),
                $"技能 {spell} 没有原版素材或明确的被动/回执呈现路径");
            foreach (var clip in casts.Concat(hit == null ? [] : new[] { hit.Value }).Concat(melee == null ? [] : new[] { melee.Value }))
                check(clip.FrameAt(-1) == -1 && clip.FrameAt(0) == clip.Index && clip.FrameAt(clip.Duration - 1) == clip.Index + clip.Count - 1 && clip.FrameAt(clip.Duration) == -1,
                    $"技能 {spell} 丢失首尾帧或一次性动画重播");
            if (missile is { } projectile)
                check(projectile.DirectionStride == 10 && projectile.Flight.Duration == projectile.Flight.Count * projectile.Interval,
                    $"技能 {spell} 把弹道跳帧参数误当速度");
        }
        foreach (var direction in Enum.GetValues<MirDirection>())
        {
            check(OriginalSpellEffects.Melee(Spell.FlamingSword, 3, direction) is { Library: "Magic", Index: var index, Count: 6, Duration: 600 } && index == 3480 + (int)direction * 10,
                "烈火方向/图号/挥砍帧数不符原版");
            check(OriginalSpellEffects.Melee(Spell.Slaying, 3, direction)?.Index == 1820 + 270 + (int)direction * 10 &&
                OriginalSpellEffects.Melee(Spell.Thrusting, 2, direction)?.Index == 2190 + 180 + (int)direction * 10, "攻杀/刺杀丢失等级图号");
            check(OriginalSpellEffects.Melee(Spell.DoubleSlash, 0, direction, true)?.Index == 2050 + (int)direction * 10, "双连斩缺少第二段特效");
        }
        var wall = OriginalSpellEffects.Ground(Spell.FireWall, 0, false)!.Value;
        check(wall.Repeat && wall.Clip.FrameAt(720, true) == 1630 && wall.Clip.FrameAt(719, true) == 1635, "火墙不是完整六帧循环");
        check(OriginalSpellEffects.Ground(Spell.Blizzard, 0, false) is { Repeat: false, Clip.Y: -20 } &&
            OriginalSpellEffects.Ground(Spell.ExplosiveTrap, 0, true) is { Repeat: false, Clip.Index: 1570 }, "持续技能生命周期与爆炸阶段混淆");
        check(OriginalSpellEffects.FlightDuration(new(0, 0), new(50, 3)) == 2500, "远距离弹道被固定2.2秒截断");
        var assassin = OriginalPlayerArt.Resolve(MirClass.Assassin, MirGender.Female, 0, 0, 100, MirAction.Attack1);
        check(assassin.Body == "AArmour/00" && assassin.Hair == "AHair/00" && assassin.BodyOffset == 512 && assassin.Weapon == "AWeapon/00 R" && assassin.SecondWeapon == "AWeapon/00 L", "刺客技能未使用原双手武器和女性动画偏移");
        var archer = OriginalPlayerArt.Resolve(MirClass.Archer, MirGender.Female, 0, 0, 200, MirAction.AttackRange2);
        check(archer.Body == "ARArmour/00" && archer.Weapon == "ARWeapon/00 S" && archer.BodyOffset == 352 &&
            OriginalPlayerArt.Animation(MirClass.Archer, 200, MirAction.Walking) == MirAction.WalkingBow, "弓手技能/移动素材仍使用普通挥剑图集");
        check(OriginalPlayerArt.Resolve(MirClass.Warrior, MirGender.Female, 1, 1, 1, MirAction.Attack1).BodyOffset == 808 &&
            OriginalPlayerArt.Resolve(MirClass.Archer, MirGender.Male, 0, 0, 1, MirAction.Attack1).Body == "CArmour/00", "职业素材切换改变了普通战士或错误武器的原动作");
        CheckState(check);
    }

    private static void CheckState(Action<bool, string> check)
    {
        var state = new GameState(); var now = Environment.TickCount64;
        var fire = new ClientMagic { Spell = Spell.FireBall, CastTime = -1000, Delay = 2000 };
        state.Apply(new S.UserInformation { ObjectID = 1, Location = new(4, 4), Magics = [fire], Inventory = [], Equipment = new UserItem[14], QuestInventory = [] });
        check(fire.CastTime >= now - 1000 && fire.CastTime <= Environment.TickCount64 - 1000, "登录冷却没有将服务端相对时间归算到本地时钟");
        state.Apply(new S.MagicDelay { ObjectID = 2, Spell = fire.Spell, Delay = 5000 });
        check(fire.Delay == 2000, "他人或英雄的技能冷却覆盖本人");
        state.Apply(new S.MagicDelay { ObjectID = 1, Spell = fire.Spell, Delay = 3000 });
        check(fire.Delay == 3000, "本人技能延时回执未生效");
        var cast = fire.CastTime;
        state.Apply(new S.Magic { Spell = fire.Spell, Cast = false });
        check(fire.CastTime == cast && state.Me!.ActionSpell == fire.Spell, "失败施法误启动技能冷却或丢失起手动作");
        state.Apply(new S.MagicCast { Spell = fire.Spell });
        check(fire.CastTime >= now, "冲撞/后跳等MagicCast回执未开始冷却");
        state.Apply(new S.ObjectPlayer { ObjectID = 2, Location = new(5, 4), Effect = SpellEffect.MagicShieldUp });
        state.Apply(new S.ObjectAttack { ObjectID = 2, Location = new(5, 4), Spell = Spell.FlamingSword, Level = 3, Direction = MirDirection.Left });
        check(state.Actors[2].ActionSpell == Spell.FlamingSword && state.Actors[2].SpellLevel == 3 && !state.Actors[2].Casting && state.Actors[2].MagicShield,
            "远端烈火技能/等级或已存在的护盾未保留");
        state.Apply(new S.ObjectEffect { ObjectID = 2, Effect = SpellEffect.MagicShieldDown });
        check(!state.Actors[2].MagicShield, "护盾结束回执未移除效果");
        state.Apply(new S.AddBuff { Buff = new ClientBuff { ObjectID = 1, Type = BuffType.Rage, Stats = new Stats { [Stat.MaxDC] = 11 } } });
        check(state.CalculateStats()[Stat.MaxDC] == 11, "技能增益回执未计入角色属性展示");
        state.Apply(new S.PauseBuff { ObjectID = 1, Type = BuffType.Rage, Paused = true });
        check(state.CalculateStats()[Stat.MaxDC] == 0, "暂停的技能增益仍计入属性展示");
        state.Apply(new S.RemoveBuff { ObjectID = 2, Type = BuffType.Rage });
        check(state.UserBuffs.Count == 1, "他人移除增益清除了本人效果");
        state.Apply(new S.RemoveBuff { ObjectID = 1, Type = BuffType.Rage });
        check(state.UserBuffs.Count == 0, "增益移除未更新本人属性");
        state.Apply(new S.ObjectMagic { ObjectID = 2, Location = new(5, 4), Spell = Spell.TwinDrakeBlade, Cast = false });
        check(state.Actors[2].Casting && state.Actors[2].ActionSpell == Spell.TwinDrakeBlade, "蓄力通知Cast=false被当作无动作");
        state.Apply(new S.ObjectSpell { ObjectID = 9, Location = new(4, 5), Spell = Spell.FireWall });
        check(state.Actors[9].Kind == ObjectType.Spell && state.Actors[9].ActionSpell == Spell.FireWall, "服务端火墙对象未归约");
        state.Apply(new S.ObjectRemove { ObjectID = 9 }); check(!state.Actors.ContainsKey(9), "火墙移除后残留对象");
        state.Apply(new S.UserBackStep { Location = new(4, 3), Direction = MirDirection.Down });
        var me = state.Me!;
        check(state.User!.Location == new Point(4, 3) && me.Action == MirAction.Jump && me.PositionAt(me.MotionTime, 800) == new System.Numerics.Vector2(4, 4), "后跳回执丢失位置同步或跳跃插值");
        state.Apply(new S.ObjectMagic { ObjectID = 1, Location = me.Location, Spell = Spell.TwinDrakeBlade });
        check(state.ActiveSkills.Contains(Spell.TwinDrakeBlade), "本人双龙斩准备成功回执被忽略");
        state.Apply(new S.RangeAttack { Spell = Spell.Focus, TargetID = 2, Target = new(5, 4) });
        check(me.Action == MirAction.AttackRange1 && me.ActionSpell == Spell.Focus && me.Casting, "原弓箭回执仍显示近战动作");
        state.Apply(new S.ObjectHidden { ObjectID = 2, Hidden = true });
        check(state.Actors[2].Hidden, "隐身技能回执未更新远端可见性");
        state.Apply(new S.SetConcentration { ObjectID = 2, Enabled = true, Interrupted = true });
        check(state.Actors[2].Concentrating && state.Actors[2].ConcentrationInterrupted, "凝神/打断回执未保留");
        state.Apply(new S.SetElemental { ObjectID = 2, Enabled = true, ElementType = 1, ExpLast = 100, Value = 20 });
        state.Apply(new S.SetElemental { ObjectID = 2, Enabled = true, ElementType = 2, ExpLast = 100, Value = 40 });
        check(state.Actors[2].Elements.Count == 2, "元素球积累遗漏已存在元素");
        state.Apply(new S.SetElemental { ObjectID = 2, Enabled = true, ElementType = 4, ExpLast = 100, Value = 100 });
        check(state.Actors[2].Elements.Keys.SequenceEqual(new uint[] { 4 }), "满元素球没有替换原三色球");
        state.Apply(new S.SetElemental { ObjectID = 2, Enabled = false });
        check(state.Actors[2].Elements.Count == 0, "元素消耗后残留光球");
        state.Apply(new S.MapChanged()); check(state.Actors.Count == 1 && state.Actors.ContainsKey(1), "特效对象跨地图泄漏");
    }
}
