namespace Mir2.Companion;

public sealed class CompanionCombatProgress
{
    private uint target;
    private int health;
    private long started, damaged;
    private string declinedGoal = "";
    private long retryAt;

    public void Reset() { target = 0; }

    public void Decline(string goal, long now) { Reset(); declinedGoal = goal; retryAt = now + 90_000; }
    public bool WaitingForPlan(string? goal, long now) => goal?.Length > 0 && goal == declinedGoal && now < retryAt;

    public bool TooHard(uint id, int remaining, long now, bool pursuing = false)
    {
        if (id != target || now < started)
        { target = id; health = remaining; started = damaged = now; return false; }
        if (remaining < health) damaged = now;
        health = remaining;
        // 只统计已可见的同一活怪；逃跑目标追逐30秒仍无收益也应换怪，不计前往刷新区的赶路。
        return now - damaged >= (pursuing ? 30_000 : 15_000) || now - started >= 30_000 && remaining >= 50;
    }
}
