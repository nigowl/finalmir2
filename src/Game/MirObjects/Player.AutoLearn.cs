using Server.MirDatabase;

namespace Server.MirObjects;

public partial class PlayerObject
{
    private bool LearnSkill(Spell spell)
    {
        if (Info.Magics.Any(magic => magic.Spell == spell)) return false;
        var magic = new UserMagic(spell);
        if (magic.Info == null) return false;
        Info.Magics.Add(magic);
        SendMagicInfo(magic);
        RefreshStats();
        return true;
    }

    internal void AutoLearnSelectedSkills()
    {
        if (!Settings.AutoLearnSkills) return;
        var selected = Settings.AutoLearnFor(Class).ToHashSet();
        if (selected.Count == 0) return;
        // 只使用当前版本实际技能书的学习条件；不造书、不改库存，也不重复学习或提升熟练度。
        var books = Envir.ItemInfoList.Where(book => book.Type == ItemType.Book && book.Shape > 0 && book.Shape <= byte.MaxValue &&
            book.RequiredClass.HasFlag((RequiredClass)(1 << (int)Class)) && book.RequiredGender.HasFlag((RequiredGender)(1 << (int)Gender)));
        foreach (var book in books)
        {
            var spell = (Spell)book.Shape;
            if (!selected.Contains(spell) || Info.Magics.Any(magic => magic.Spell == spell)) continue;
            var magic = Envir.MagicInfoList.FirstOrDefault(info => info.Spell == spell);
            if (magic == null || Level < magic.Level1 || book.RequiredType == RequiredType.Level && Level < book.RequiredAmount || !CanUseItem(new UserItem(book))) continue;
            if (LearnSkill(spell)) ReceiveChat($"升级自动学习：{ChineseText.Name(magic.Name)}。", ChatType.System);
        }
    }
}
