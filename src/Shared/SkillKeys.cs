public static class SkillKeys
{
    // 17–24 仍属于原英雄快捷键，F9–F12 追加到 25–28，保留旧角色存档含义。
    public static bool IsPlayer(byte key) => key <= 16 || key is >= 25 and <= 28;
    public static bool IsHero(byte key) => key is >= 17 and <= 24;
    public static byte FunctionKey(int number, bool shift = false) => number is >= 1 and <= 8 ? (byte)(number + (shift ? 8 : 0)) :
        number is >= 9 and <= 12 && !shift ? (byte)(number + 16) : (byte)0;
    public static string Label(byte key) => key == 0 ? "无" : key <= 8 ? $"F{key}" : key <= 16 ? $"Shift+F{key - 8}" : key is >= 25 and <= 28 ? $"F{key - 16}" : "未知";
}
