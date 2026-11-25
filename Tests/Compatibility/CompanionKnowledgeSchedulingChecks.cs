using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using Mir2.Hosting;
using Mir2.Companions;
using Server.ConsoleHost;

internal static class CompanionKnowledgeSchedulingChecks
{
    public static void Run(Action<bool, string> check)
    {
        var folder = Path.Combine(Path.GetTempPath(), "mir2-knowledge-scheduling-" + Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new ProjectPaths(folder); Directory.CreateDirectory(paths.Runtime);
            using var storage = new SqliteDatabaseStorage(paths.Database);
            var store = new CompanionStore(storage, new(paths, storage), paths);
            store.SaveSettings(new(store.Settings with { Enabled = true }));
            using var service = new CompanionService((GameRuntime)RuntimeHelpers.GetUninitializedObject(typeof(GameRuntime)), store, paths);
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var type = typeof(CompanionService).GetNestedType("LiveCompanion", BindingFlags.NonPublic)!;
            var live = (IDictionary)typeof(CompanionService).GetField("live", flags)!.GetValue(service)!;
            var method = typeof(CompanionService).GetMethod("PlanKnowledge", flags)!;
            var now = DateTimeOffset.UtcNow;
            var entries = new List<object>();
            for (var index = 0; index < 30; index++)
            {
                var id = Guid.NewGuid();
                store.Records[id] = new CompanionRecord { Profile = new() { Id = id, Name = "规划检查" + index, Enabled = index != 0 }, Desired = "run" };
                var entry = Activator.CreateInstance(type, true)!;
                type.GetField("Entered")!.SetValue(entry, true);
                type.GetField("NextKnowledge")!.SetValue(entry, now.AddSeconds(-30 + index));
                live.Add(id, entry); entries.Add(entry);
            }
            method.Invoke(service, [now]);
            check(entries.Count(entry => (DateTimeOffset)type.GetField("NextKnowledge")!.GetValue(entry)! > now) == 1, "单次知识刷新仍集中处理所有角色");
            check((DateTimeOffset)type.GetField("NextKnowledge")!.GetValue(entries[1])! == now.AddSeconds(3), "没有优先最久未刷新的角色或未保留3秒间隔");
            method.Invoke(service, [now.AddMilliseconds(100)]);
            check((DateTimeOffset)type.GetField("NextKnowledge")!.GetValue(entries[2])! == now.AddMilliseconds(3100), "无资格的队首阻塞了后续角色，或已刷新角色反复抢占");
            type.GetField("StoppingAt")!.SetValue(entries[3], now);
            method.Invoke(service, [now.AddMilliseconds(200)]);
            check((DateTimeOffset)type.GetField("NextKnowledge")!.GetValue(entries[4])! == now.AddMilliseconds(3200), "退出中的角色仍参与规划");
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }
}
