using System.Globalization;
using Mir2.Management;
using Server;
using Server.ConsoleHost;

internal static class SystemDefaultsChecks
{
    public static void Run(Action<bool, string> check)
    {
        var defaults = new ServerSettings();
        // 全字段检查防止新增设置只更新页面默认值、遗漏服务端实际初始值。
        foreach (var property in typeof(ServerSettings).GetProperties())
        {
            var field = typeof(Settings).GetField(property.Name);
            check(field != null && Convert.ToString(field.GetValue(null), CultureInfo.InvariantCulture) ==
                Convert.ToString(property.GetValue(defaults), CultureInfo.InvariantCulture), $"默认值与服务端不一致：{property.Name}");
        }
        var flags = new[] { nameof(Settings.AllowCreateWarrior), nameof(Settings.AllowCreateWizard), nameof(Settings.AllowCreateTaoist),
            nameof(Settings.AllowCreateAssassin), nameof(Settings.AllowCreateArcher) };
        try
        {
            foreach (var enabled in Enum.GetValues<MirClass>())
            {
                foreach (var role in Enum.GetValues<MirClass>()) typeof(Settings).GetField(flags[(int)role])!.SetValue(null, role == enabled);
                foreach (var role in Enum.GetValues<MirClass>()) check(Settings.CanCreateClass(role) == (role == enabled), "职业开关没有独立生效：" + role);
                check(CompanionValidation.AvailableClasses.All(value => Settings.CanCreateClass((MirClass)value)), "陪玩随机建角包含已停用职业");
            }
            foreach (var flag in flags) typeof(Settings).GetField(flag)!.SetValue(null, false);
            check(Enum.GetValues<MirClass>().All(role => !Settings.CanCreateClass(role)) && !Settings.CanCreateClass((MirClass)255) &&
                CompanionValidation.AvailableClasses.Length == 0, "全部关闭或非法职业绕过建角开关");
        }
        finally { foreach (var flag in flags) typeof(Settings).GetField(flag)!.SetValue(null, true); }
    }
}
