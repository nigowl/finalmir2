using Server.MirDatabase;
using Server.MirEnvir;

namespace Server.MirObjects
{
    public class ArcherHero : HeroObject
    {
        public ArcherHero(CharacterInfo info, PlayerObject owner) : base(info, owner) { }

        protected override bool InAttackRange()
        {
            return Target.CurrentMap == CurrentMap && (HasClassWeapon && Functions.InRange(CurrentLocation, Target.CurrentLocation, ViewRange) ||
                Target.CurrentLocation != CurrentLocation && Functions.InRange(CurrentLocation, Target.CurrentLocation, 1));
        }

        protected override void ProcessFriend()
        {
            if (!CanCast || Target == null || !HasClassWeapon) return;
            if ((HasElemental || HasMagic(Spell.Meditation)) && TryHeroBuff(Spell.ElementalBarrier, BuffType.ElementalBarrier)) return;
            TryHeroBuff(Spell.Concentration, BuffType.Concentration);
        }

        protected override void ProcessAttack()
        {
            if (!CanCast || Target == null || Target.Dead || !HasClassWeapon || PhysicalOnlyTarget) return;
            TargetDistance = Functions.MaxDistance(CurrentLocation, Target.CurrentLocation);
            if (PercentHealth <= 50 && TryHeroMagic(Spell.VampireShot, Target)) return;
            var nearby = CombatTargets(1, Target.CurrentLocation).Count;
            if (nearby >= 2 && Buffs.Any(buff => !buff.Paused && buff.Type is BuffType.PoisonShot or BuffType.VampireShot) && TryHeroMagic(Spell.CrippleShot, Target)) return;
            if (!Target.PoisonList.Any(poison => poison.PType.HasFlag(PoisonType.Green)) && TryHeroMagic(Spell.PoisonShot, Target)) return;
            if (nearby >= 2 && TryHeroMagic(Spell.NapalmShot, Target)) return;
            foreach (var spell in new[] { Spell.DoubleShot, Spell.ElementalShot, Spell.StraightShot })
                if (TryHeroMagic(spell, Target)) return;
        }

        protected override void ProcessTarget()
        {
            if (ProcessQueuedMagic()) return;
            var distanceToPlayer = Functions.MaxDistance(CurrentLocation, Owner.CurrentLocation);
            if (CanMove && ((Owner.Info.HeroBehaviour == HeroBehaviour.CounterAttack && distanceToPlayer > 3) ||
                (Owner.Info.HeroBehaviour == HeroBehaviour.Attack && distanceToPlayer > 5)))
            {
                FollowOwner();
                return;
            }
            ProcessRangedTarget(false, HasClassWeapon);
        }
        public bool HasWeapon => Info.Equipment[(int)EquipmentSlot.Weapon] != null && Info.Equipment[(int)EquipmentSlot.Weapon].CurrentDura > 0;
        public bool HasClassWeapon
        {
            get
            {
                var classweapon = Info.Equipment[(int)EquipmentSlot.Weapon];
                return classweapon != null && classweapon.CurrentDura > 0 &&
                    Functions.GetRealItem(classweapon.Info, Level, Class, Envir.ItemInfoList).Shape / Globals.ClassWeaponCount == 2;
            }
        }
    }
}
