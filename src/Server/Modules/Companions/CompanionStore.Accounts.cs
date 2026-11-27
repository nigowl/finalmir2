using System.Globalization;
using Mir2.Companions;
using Server.MirEnvir;

namespace Server.ConsoleHost;

public sealed partial class CompanionStore
{
    public string[] NewAccounts(int count)
    {
        var used = Envir.Main.AccountList.Select(a => a.AccountID).Concat(Records.Values.Select(r => r.Account)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var names = new List<string>();
        for (long number = 1; names.Count < count; number++)
        {
            var name = "pw" + number.ToString(CultureInfo.InvariantCulture);
            if (name.Length > 15) throw new InvalidOperationException("陪玩账户编号已用尽。");
            if (used.Add(name)) names.Add(name);
        }
        return names.ToArray();
    }

    public void NormalizeAccounts()
    {
        Ready();
        var records = Records.Values.Where(r => r.Account.Length < 3 || !r.Account.StartsWith("pw", StringComparison.Ordinal) ||
            r.Account.AsSpan(2).ContainsAnyExceptInRange('0', '9')).OrderBy(r => r.AccountId).ThenBy(r => r.Profile.Id).ToArray();
        if (records.Length == 0) return;
        var accounts = records.Select(r => Envir.Main.GetAccount(r.Account)).ToArray();
        for (var i = 0; i < records.Length; i++)
        {
            var r = records[i]; var account = accounts[i];
            if (r.Registered != (account != null) || r.AccountId != (account?.Index ?? 0) || account?.AdminAccount == true ||
                r.CharacterId > 0 && account?.Characters.Any(c => c.Index == r.CharacterId && c.Name == r.Profile.Name) != true)
                throw new InvalidOperationException("陪玩账户编号迁移身份不匹配，已保留原账户和档案。");
            if (account != null) PlayerAdministration.Offline(account);
        }
        var names = NewAccounts(records.Length);
        var credentials = accounts.Select(a=>a?.CaptureCredentials()).ToArray();
        // 原账户与陪玩凭据同事务改名，保持编号、密码、资产及人工保护。
        changes.Commit(() =>
        {
            for (var i = 0; i < records.Length; i++)
            {
                if (accounts[i] is { } account)
                {
                    // 只迁移已验证的自动仓库凭据，人工密码保持原样。
                    if (account.ValidateStoragePassword(CompanionStorageRules.Password(records[i].Account,records[i].Password)))
                        account.StoragePassword=CompanionStorageRules.Password(names[i],records[i].Password);
                    account.AccountID = names[i];
                }
                var next = records[i] with { Account = names[i] };
                Write(Key(next.Profile.Id), next); Records[next.Profile.Id] = next;
            }
        }, () =>
        {
            for (var i = 0; i < records.Length; i++)
            {
                if (accounts[i] is { } account) account.AccountID = records[i].Account;
                credentials[i]?.Invoke();
                Records[records[i].Profile.Id] = records[i];
            }
        });
    }
}
