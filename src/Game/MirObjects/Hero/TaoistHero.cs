using Server.MirDatabase;

namespace Server.MirObjects
{
    public class TaoistHero : HeroObject
    {
        public TaoistHero(CharacterInfo info, PlayerObject owner) : base(info, owner) { }
        protected override bool InAttackRange()
        {
            return Target.CurrentMap == CurrentMap && (HasRangedSpell || Target.CurrentLocation != CurrentLocation && Functions.InRange(CurrentLocation, Target.CurrentLocation, 1));
        }
        protected override void ProcessFriend()
        {
            if (!CanCast) return;
            var friends = new MapObject[] { this, Owner }.Where(target => !target.Dead && target.CurrentMap == CurrentMap)
                .OrderBy(target => target.PercentHealth).ToArray();
            foreach (var target in friends)
            {
                if ((target.PoisonList.Count > 0 || target.Buffs.Any(buff => buff.Properties.HasFlag(BuffProperty.Debuff))) && TryHeroMagic(Spell.Purification, target)) return;
                var heal = GetMagic(Spell.Healing);
                if (target.PercentHealth <= 70 && target.HealAmount == 0 && target.PotHealthAmount == 0 && heal != null &&
                    Envir.Time >= heal.CastTime + 5000 && TryHeroMagic(Spell.Healing, target)) return;
            }
            if (Target == null) return;
            if (!CurrentMap.Info.NoPets && Pets.Count(pet => !pet.Dead && pet.Race == ObjectType.Monster) < 2)
                foreach (var summon in new[] { (Spell.SummonHolyDeva, Server.Settings.AngelName), (Spell.SummonShinsu, Server.Settings.ShinsuName), (Spell.SummonSkeleton, Server.Settings.SkeletonName) })
                    if (Envir.GetMonsterInfo(summon.Item2) != null && !Pets.Any(pet => !pet.Dead && pet.Info.Name == summon.Item2) && TryHeroMagic(summon.Item1)) return;
            foreach (var target in friends)
            {
                if (TryHeroBuff(Spell.SoulShield, BuffType.SoulShield, target)) return;
                if (TryHeroBuff(Spell.BlessedArmour, BuffType.BlessedArmour, target)) return;
                if (TryHeroBuff(Spell.UltimateEnhancer, BuffType.UltimateEnhancer, target)) return;
                if (TryHeroBuff(Spell.EnergyShield, BuffType.EnergyShield, target)) return;
            }
            foreach (var pet in Pets.Where(pet => !pet.Dead && pet.CurrentMap == CurrentMap))
                if (TryHeroBuff(Spell.PetEnhancer, BuffType.PetEnhancer, pet)) return;
        }

        protected override void ProcessAttack()
        {
            if (!CanCast || Target == null || Target.Dead || PhysicalOnlyTarget) return;
            TargetDistance = Functions.MaxDistance(CurrentLocation, Target.CurrentLocation);
            var poison = GetPoison(1);
            if (poison != null && !Target.PoisonList.Any(status => status.PType.HasFlag(poison.Info.Shape == 2 ? PoisonType.Red : PoisonType.Green)) &&
                TryHeroMagic(Spell.Poisoning, Target)) return;
            if (!Target.HasBuff(BuffType.Curse) && TryHeroMagic(Spell.Curse, Target)) return;
            if (CombatTargets(2, Target.CurrentLocation).Count >= 3 && TryHeroMagic(Spell.PoisonCloud, Target)) return;
            TryHeroMagic(Spell.SoulFireBall, Target);
        }

        protected override void ProcessTarget()
        {
            ProcessRangedTarget(HasRangedSpell);
        }

        private bool HasRangedSpell => HasRangedMagic(Spell.SoulFireBall);
    }
}
