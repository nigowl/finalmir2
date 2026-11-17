namespace Mir2;

public static class StorageRules
{
    public const int MaxPages = 4;
    public static int Capacity(int length, bool expanded) => Math.Min(length, Globals.StorageGridSize * (expanded ? MaxPages : 1));
    public static string PageName(int page) => "仓库" + "一二三四"[page];
}

public sealed record StorageUnlockHint(int Page, int QuestId, string Description);
