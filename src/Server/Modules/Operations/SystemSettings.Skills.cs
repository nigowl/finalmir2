using Mir2.Management;
using Server.MirEnvir;
using Server.MirObjects;

namespace Server.ConsoleHost;

public sealed partial class SystemSettings
{
    private static AutoLearnSkillOption[] SkillOptions() => Enum.GetValues<MirClass>().SelectMany(role =>
        PlayerObject.ClassBooks(Envir.Main.ItemInfoList, Envir.Main.MagicInfoList, role).Select(book =>
        {
            var magic = Envir.Main.MagicInfoList.First(info => info.Spell == (Spell)book.Shape);
            return new AutoLearnSkillOption(role, magic.Spell, ChineseText.Name(magic.Name),
                Math.Max(magic.Level1, book.RequiredType == RequiredType.Level ? book.RequiredAmount : 0));
        })).OrderBy(skill => skill.Class).ThenBy(skill => skill.Level).ThenBy(skill => skill.Spell).ToArray();

    private static void ValidateSkills(ServerSettings settings)
    {
        var available = SkillOptions().Select(skill => (skill.Class, skill.Spell)).ToHashSet();
        foreach (var role in Enum.GetValues<MirClass>())
        {
            var selected = settings.SkillsFor(role);
            if (selected == null || selected.Length > 255 || selected.Distinct().Count() != selected.Length)
                throw new InvalidDataException($"{role.ToLocalizedString()}自动学习列表无效或包含重复技能。");
            foreach (var spell in selected)
                if (!available.Contains((role, spell)))
                    throw new InvalidDataException($"{role.ToLocalizedString()}自动学习技能不存在或不属于此职业：{spell}，请刷新后重新选择。");
        }
    }
}
