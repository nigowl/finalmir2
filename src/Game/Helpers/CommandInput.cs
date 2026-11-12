using System;
using System.Linq;
using System.Text.RegularExpressions;
using Mir2.Commands;

namespace Server.Helpers;

internal static class CommandInput
{
    public static string Validate(GameCommandInfo command, string[] parts, int flagCount)
    {
        var required = Regex.Replace(command.Arguments, @"\[[^\]]*\]", "").Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        if (parts.Length <= required) return $"参数不足。用法：@{command.Name} {command.Arguments}";
        bool Integer(int index, int min = 0, int max = int.MaxValue) => index < parts.Length && int.TryParse(parts[index], out var value) && value >= min && value <= max;
        return command.Original switch
        {
            "MAPMOVE" when parts.Length > 5 || (parts.Length is 3 or 5) && !Integer(2, 1) ||
                parts.Length >= 4 && (!Integer(parts.Length - 2) || !Integer(parts.Length - 1)) => "传送参数错误：地图代码 [实例号] [X Y]，实例号须大于 0，坐标须为非负整数。",
            "MOVE" when parts.Length != 1 && (parts.Length != 3 || !Integer(1) || !Integer(2)) => "移动参数错误：请输入 X Y 两个非负整数，或省略坐标随机移动。",
            "LEVEL" or "LEVELHERO" when !Integer(parts.Length - 1, 1, ushort.MaxValue) && !(parts.Length == 2 && parts[1] == "-1") => "等级必须为 1–65535；自身等级可使用 -1。",
            "GIVEGOLD" or "GIVEPEARLS" or "GIVECREDIT" when !uint.TryParse(parts.Last(), out _) => "数量必须为非负整数且不超过 4294967295。",
            "SETFLAG" when !Integer(1, 0, flagCount - 1) => $"标记编号须为 0–{flagCount - 1}。",
            "SETQUEST" when !Integer(1, 1) || !Integer(2, 0, 1) => "任务编号须为正整数；状态为 0（取消）或 1（完成）。",
            "SETLIGHT" when !Integer(1, 0, 255) => "亮度须为 0–255。",
            "SETTIMER" when !Integer(2) || !Integer(3, 0, 255) => "计时器秒数须为非负整数，类型须为 0–255。",
            _ => null
        };
    }
}
