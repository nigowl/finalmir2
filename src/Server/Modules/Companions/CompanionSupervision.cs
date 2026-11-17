namespace Server.ConsoleHost;

public static class CompanionSupervision
{
    public const string Heartbeat = "陪玩心跳超过30秒未更新，已停止；核对进程后人工重新启动。";
    public const string WorkerExit = "陪玩工作进程已退出，请核对后重新启动。";
    public const string RetireTimeout = "正常下线超过30秒未完成，已停止自动调度；请核对原连接。";
    public const string SessionExit = "陪玩客户端已结束，请核对后重新启动。";
    public const string DeathBudget = "达到本次会话死亡预算，请调整目标或配置后重新启动。";
    public static bool Legacy(CompanionRecord record) => !record.Profile.Enabled && record.Desired=="off" && !record.RecoveryDisabled && record.RecoveryAt==null &&
        record.SuppressedUntil==null && record.Error is Heartbeat or WorkerExit or RetireTimeout or SessionExit;
    public static TimeSpan Delay(int attempt) => TimeSpan.FromSeconds(Math.Min(900,30*Math.Pow(2,Math.Clamp(attempt-1,0,5))));
    public static bool Death(CompanionRecord record) => !record.RecoveryDisabled && record.SuppressedUntil==null && record.Desired!="pause" &&
        record.Error==DeathBudget && record.SessionDeaths>=record.Profile.DeathLimit;
    public static int Priority(CompanionRecord record) => Death(record)?1:0;
    public static TimeSpan Delay(CompanionRecord record) => Death(record)?TimeSpan.FromSeconds(Math.Min(900,120*Math.Pow(2,Math.Clamp(record.RecoveryAttempts-1,0,3)))):Delay(record.RecoveryAttempts);
}
