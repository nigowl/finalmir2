using Mir2.Companions;

namespace Server.ConsoleHost;

public sealed partial class CompanionStore
{
    public void SaveArrivalState(CompanionArrivalState value)
    {
        PrepareWrite();var old=Configuration;var next=old with{Arrivals=value};
        storage.Transaction(()=>Write("companions/settings.json",next));Configuration=next;
    }
    public void BeginSession(CompanionRecord record,DateTimeOffset now,DateTimeOffset windowEnd)
    {
        Mutate(record,next=>
        {
            if(next.SessionUntil==null||next.SessionUntil<=now)
            {
                var plan=CompanionScheduling.Session(next.Profile,Settings.SmartScheduling,unchecked(next.Seed+next.SessionNumber*397));
                next.SessionNumber=unchecked(next.SessionNumber+1);
                next.SessionDeaths=0;
                next.SessionStarted=now;next.SessionUntil=now.AddMinutes(plan.OnlineMinutes);next.SessionRestMinutes=plan.RestMinutes;
            }
            var remaining=next.Profile.Schedule.DailyMinutes*60-Math.Max(TodaySeconds(next,now),RollingSeconds(next,now));
            next.SessionUntil=new[]{next.SessionUntil.Value,windowEnd,now.AddSeconds(remaining)}.Min();
            next.QueueSince=null;next.RestUntil=null;
        });
    }
    public void EndSession(CompanionRecord record,DateTimeOffset now,string reason)
    {
        Mutate(record,next=>
        {
            next.RestUntil=now.AddMinutes(next.SessionRestMinutes);
            next.SessionUntil=null;next.SessionStarted=null;next.QueueSince=null;next.LastSessionReason=reason;
        });
    }
    public static IOrderedEnumerable<CompanionRecord> Waiting(IEnumerable<CompanionRecord> records) =>
        records.OrderBy(r=>r.QueueSince??DateTimeOffset.MaxValue).ThenBy(r=>r.Seed).ThenBy(r=>r.Profile.Id);
}
