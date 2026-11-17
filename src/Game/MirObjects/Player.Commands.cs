using System;
using System.Collections.Generic;
using System.Linq;
using Server.MirDatabase;

namespace Server.MirObjects;

public partial class PlayerObject
{
    public static IEnumerable<ItemInfo> ClassBooks(IEnumerable<ItemInfo> books, IEnumerable<MagicInfo> magics, MirClass role)
    {
        var defined = magics.Select(magic => magic.Spell).ToHashSet();
        return books.Where(book => book.Type == ItemType.Book && book.RequiredClass.HasFlag((RequiredClass)(1 << (int)role)) &&
                book.Shape > 0 && book.Shape <= byte.MaxValue && Enum.IsDefined((Spell)book.Shape) && defined.Contains((Spell)book.Shape))
            .OrderBy(book => book.RequiredAmount).ThenBy(book => book.Index).DistinctBy(book => book.Shape);
    }

    private void GiveClassBooks(MirClass role)
    {
        var items = ClassBooks(Envir.ItemInfoList, Envir.MagicInfoList, role).Select(info =>
        { var item = Envir.CreateFreshItem(info); item.Count = 1; item.GMMade = true; return item; }).ToArray();
        if (items.Length == 0) { ReceiveChat($"当前版本没有{role.ToLocalizedString()}可用的技能书。", ChatType.System); return; }
        if (!CanGainItems(items) || CurrentBagWeight + items.Sum(item => item.Weight) > Stats[Stat.BagWeight])
        { ReceiveChat("技能书未发放：背包空间或负重不足，请整理后重试。", ChatType.System); return; }
        foreach (var item in items) GainItem(item);
        ReceiveChat($"发放完成：{role.ToLocalizedString()}技能书 {items.Length} 本，每种一本；请在背包中自行使用学习。", ChatType.System);
    }
}
