using System.Text.RegularExpressions;
using TinyPinyin;

namespace Mir2.Admin.Services;

public static class NpcScriptName
{
    // shortcut: 使用单字无声调拼音；人名、地名的多音字需在表单中手工校正。
    public static string From(string name)
    {
        var value = Regex.Replace(PinyinHelper.GetPinyin(name, "").ToLowerInvariant(), "[^a-z0-9_-]+", "_").Trim('_');
        return value.Length == 0 ? "npc" : value;
    }
}
