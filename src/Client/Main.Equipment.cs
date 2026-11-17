using Godot;

public partial class Main
{
    private void RefreshEquipmentStats(Stats stats)
    {
        if (!equipment.Visible || equipmentPage is not (1 or 2) || State.User is not { } user) return;
        string Values(params Stat[] names) => string.Join('\n', names.Select(stat => $"{stat.ToLocalizedString()} {stats[stat]}"));
        if (equipmentPage == 1)
        {
            attributes.Text = $"生命 {user.HP}/{stats[Stat.HP]}\n" + Values(Stat.MinAC, Stat.MinMAC, Stat.MinDC, Stat.MinMC, Stat.MinSC,
                Stat.Accuracy, Stat.AttackSpeed, Stat.CriticalRate);
            attributesRight.Text = $"魔力 {user.MP}/{stats[Stat.MP]}\n" + Values(Stat.MaxAC, Stat.MaxMAC, Stat.MaxDC, Stat.MaxMC, Stat.MaxSC,
                Stat.Agility, Stat.Luck, Stat.CriticalDamage);
        }
        else
        {
            attributes.Text = $"背包负重 {State.InventoryWeight:0.##}/{stats[Stat.BagWeight]}\n穿戴负重 {State.WearWeight:0.##}/{stats[Stat.WearWeight]}\n手持负重 {State.HandWeight:0.##}/{stats[Stat.HandWeight]}\n" +
                Values(Stat.MagicResist, Stat.PoisonResist, Stat.HealthRecovery);
            attributesRight.Text = Values(Stat.SpellRecovery, Stat.PoisonRecovery, Stat.Holy, Stat.Freezing, Stat.PoisonAttack);
        }
        var percent = user.MaxExperience > 0 ? Math.Clamp(user.Experience / (double)user.MaxExperience * 100, 0, 100) : 0;
        attributeExperience.Text = $"经验 {percent:0.##}%\n{user.Experience}/{user.MaxExperience}";
        foreach (var label in new[] { attributes, attributesRight, attributeExperience }) label.TooltipText = label.Text;
    }
}
