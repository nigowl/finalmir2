using Server.MirDatabase;
using Server.MirNetwork;

namespace Server.MirObjects
{
    public class AssassinHero : HeroObject
    {
        public AssassinHero(CharacterInfo info, PlayerObject owner) : base(info, owner) { }
        protected override void Load(CharacterInfo info, MirConnection connection)
        {
            base.Load(info, connection);

            if (HasMagic(Spell.DoubleSlash))
                Info.DoubleSlash = true;
        }
        protected override bool InAttackRange()
        {
            return Target.CurrentMap == CurrentMap && Target.CurrentLocation != CurrentLocation && Functions.InRange(CurrentLocation, Target.CurrentLocation, 1);
        }
        protected override void ProcessFriend()
        {
            if (!CanCast || Target == null) return;
            if (TryHeroBuff(Spell.Haste, BuffType.Haste)) return;
            TryHeroBuff(Spell.LightBody, BuffType.LightBody);
        }
        protected override void ProcessAttack()
        {
            if (!CanCast || Target == null || Target.Dead || PhysicalOnlyTarget) return;
            TargetDistance = Functions.MaxDistance(CurrentLocation, Target.CurrentLocation);
            Direction = Functions.DirectionFromPoint(CurrentLocation, Target.CurrentLocation);
            if (!Target.PoisonList.Any(poison => poison.PType.HasFlag(PoisonType.Green)) && TryHeroMagic(Spell.PoisonSword, Target)) return;
            var targets = CombatTargets(1, CurrentLocation);
            if (targets.Count(target => Enumerable.Range(-2, 5).Any(step => target.CurrentLocation == Functions.PointMove(CurrentLocation,
                (MirDirection)(((int)Direction + step + 8) % 8), 1))) >= 2 && TryHeroMagic(Spell.CrescentSlash, Target)) return;
            if ((TargetDistance > 1 || ForwardTargets(3) >= 2) && TryHeroMagic(Spell.HeavenlySword, Target)) return;
        }
        protected override void Attack()
        {
            if (Target == null || !Target.IsAttackTarget(Owner) || !CanAttack) return;
            Direction = Functions.DirectionFromPoint(CurrentLocation, Target.CurrentLocation);
            var magic = GetMagic(Spell.DoubleSlash);
            var spell = !PhysicalOnlyTarget && magic != null && magic.Info.BaseCost + magic.Level * magic.Info.LevelCost <= MP ? Spell.DoubleSlash : Spell.None;
            Attack(Direction, spell);
        }
    }
}
