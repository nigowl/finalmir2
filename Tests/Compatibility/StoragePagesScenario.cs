using Mir2.Hosting;
using Server;
using Server.ConsoleHost;
using Server.MirEnvir;

internal static class StoragePagesScenario
{
    internal static void Verify(ProjectPaths paths, int accountId)
    {
        if (!paths.Data.Contains("mir2-companions-quests-", StringComparison.Ordinal)) throw new InvalidOperationException("仅检查本测试的隔离版本。");
        using var ownership = new FileStream(Path.Combine(paths.Data, ".server.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        Directory.SetCurrentDirectory(paths.World);
        using var storage = new SqliteDatabaseStorage(paths.Database); DatabaseStorage.Current = storage;
        var world = Envir.Main; if (!world.LoadDB()) throw new InvalidDataException("隔离世界读取失败。");
        world.LoadAccounts(); var account = world.AccountList.Single(account => account.Index == accountId);
        if (account.Storage.Length != 320 || !account.HasExpandedStorage || account.ExpandedStorageExpiryDate != DateTime.MaxValue || !account.IsValidStorageIndex(319))
            throw new InvalidDataException("原TCP完成任务后四页仓库没有完整持久化。");
        Console.WriteLine("PASS 原TCP真实交付后仓库二至四永久开通，正常重启/原账户快照仍320格，原仓库物品由主场景核对守恒。");
    }
}
