namespace Crystal.Native;

// 均为1024×768逻辑像素，窗口缩放只交给画布处理。
internal static class ClientUi
{
    public const int Title = 12, Body = 11, Small = 10, Micro = 9;
    public const int ButtonHeight = 26, TabHeight = 24, HeaderHeight = 32, Padding = 12, Gap = 4;
    public const int SlotInset = 4, HudActionSize = 30, SkillRowHeight = 46, SkillRowGap = 2;
    public const int CompanionPortraitSize = 52;
    public const int InventoryColumns = 8, InventoryRows = 10, InventoryExtraHeight = (InventoryRows - 5) * 33;
}
