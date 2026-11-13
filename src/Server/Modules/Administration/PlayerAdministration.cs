using System.Drawing;
using System.Text.RegularExpressions;
using Mir2.Management;
using Server.MirDatabase;
using Server.MirEnvir;

namespace Server.ConsoleHost;

public sealed class PlayerAdministration(AdminChanges changes)
{
    private static Envir World => Envir.Main;
    internal static AccountInfo Account(int id) => World.AccountList.SingleOrDefault(a => a.Index == id) ?? throw new FileNotFoundException("未找到账户。");
    internal static CharacterInfo Character(int id) => World.CharacterList.SingleOrDefault(c => c.Index == id) ?? throw new FileNotFoundException("未找到角色。");
    internal static bool Online(AccountInfo account) => account.Connection != null || account.Characters.Any(c => c.Player != null);
    internal static void Offline(AccountInfo account)
    { if (Online(account)) throw new InvalidOperationException("修改玩家存档前，请先让此账户下线或将其踢下线。"); }
    private static bool Find(string? search, int id, params string[] values) => string.IsNullOrWhiteSpace(search) || id.ToString() == search || values.Any(v => v.Contains(search, StringComparison.OrdinalIgnoreCase));
    internal static ManagedItem[] Items(string grid, UserItem[] items) => items.Select((item, slot) => (item, slot)).Where(p => p.item != null).Select(p =>
        new ManagedItem(grid, p.slot, p.item.UniqueID.ToString(), p.item.ItemIndex, p.item.Info?.Name ?? "未知物品", p.item.Count, p.item.CurrentDura, p.item.MaxDura)).ToArray();
    private static CharacterRow Row(CharacterInfo c) => new(c.Index, c.Name, c.AccountInfo.Index, c.AccountInfo.AccountID, c.Class.ToString(), c.Level, c.Player != null, c.Deleted, c.Banned, c.CurrentMapIndex, c.CurrentLocation.X, c.CurrentLocation.Y);
    public AccountRow[] Accounts(string? search, bool onlineOnly) => World.AccountList.Where(a => Find(search, a.Index, a.AccountID, a.UserName, a.LastIP) && (!onlineOnly || Online(a)))
        .OrderBy(a => a.Index).Select(a => new AccountRow(a.Index, a.AccountID, Online(a), a.AdminAccount, a.Banned, a.Characters.Count(c => !c.Deleted), a.Gold, a.Credit)).ToArray();
    public CharacterRow[] Characters(string? search, bool onlineOnly) => World.CharacterList.Where(c => Find(search, c.Index, c.Name, c.AccountInfo.AccountID) && (!onlineOnly || c.Player != null)).OrderBy(c => c.Index).Select(Row).ToArray();
    public AccountDetails ReadAccount(int id)
    {
        var a = Account(id);
        var version = AdminChanges.Version(new { a.Index, a.Gold, a.Credit, a.AdminAccount, a.Banned, a.BanReason, a.ExpiryDate, a.RequirePasswordChange, a.Password, a.StoragePassword, a.StoragePasswordLastSet });
        return new(a.Index, a.AccountID, Online(a), a.AdminAccount, a.Banned, a.BanReason, AdminChanges.PublicExpiry(a.ExpiryDate), a.RequirePasswordChange, a.Gold, a.Credit,
            a.CreationDate, a.LastDate, a.LastIP, a.Characters.Select(Row).ToArray(), Items("Storage", a.Storage), version);
    }
    public AccountDetails CreateAccount(AccountCreate request)
    {
        if (!Regex.IsMatch(request.AccountID ?? "", "^[A-Za-z0-9]{3,15}$")) throw new InvalidDataException("账户名必须为3至15个英文字母或数字。");
        Password(request.Password);
        if (World.AccountList.Any(a => a.AccountID.Equals(request.AccountID, StringComparison.OrdinalIgnoreCase))) throw new InvalidOperationException("账户名已存在。");
        var account = new AccountInfo { Index = AdminChanges.Next(World.NextAccountID, World.AccountList.Select(a => a.Index)), AccountID = request.AccountID, Password = request.Password, CreationDate = World.Now, CreationIP = "127.0.0.1" };
        var counter = World.NextAccountID;
        changes.Commit(() => { World.AccountList.Add(account); World.NextAccountID = account.Index; }, () => { World.AccountList.Remove(account); World.NextAccountID = counter; });
        return ReadAccount(account.Index);
    }
    public void SaveAccount(AccountEdit edit)
    {
        var a = Account(edit.Id); Offline(a); var before = ReadAccount(edit.Id);
        AdminChanges.Match(before.Version, edit.Version); AdminChanges.Confirm(edit.Confirmation, a.AccountID);
        if (edit.RequirePasswordChange && !Settings.AllowChangePassword) throw new InvalidOperationException("要求修改密码前，请先在服务端设置中启用密码修改。");
        var expiry = AdminChanges.Expiry(edit.Banned, edit.BanUntil); var reason = BanReason(edit.Banned, edit.BanReason);
        var oldExpiry = a.ExpiryDate; var oldWrong = a.WrongPasswordCount;
        changes.Commit(() => { a.Gold = edit.Gold; a.Credit = edit.Credit; a.AdminAccount = edit.IsGM; a.Banned = edit.Banned; a.BanReason = reason; a.ExpiryDate = expiry; a.RequirePasswordChange = edit.RequirePasswordChange; a.WrongPasswordCount = 0; },
            () => { a.Gold = before.Gold; a.Credit = before.Credit; a.AdminAccount = before.IsGM; a.Banned = before.Banned; a.BanReason = before.BanReason; a.ExpiryDate = oldExpiry; a.RequirePasswordChange = before.RequirePasswordChange; a.WrongPasswordCount = oldWrong; });
        foreach (var c in a.Characters.Where(c => c.Level > 0 && !c.Deleted)) { World.RemoveRank(c); if (!a.AdminAccount) World.CheckRankUpdate(c); }
    }
    public void DeleteAccount(int id, AdminCommand command)
    {
        var account = Account(id); Offline(account);
        AdminChanges.Match(ReadAccount(id).Version, command.Version); AdminChanges.Confirm(command.Confirmation, account.AccountID);
        // 不级联清理角色或资产，包含软删除角色在内的账户必须先人工处理。
        if (account.Characters.Count != 0 || World.CharacterList.Any(c => ReferenceEquals(c.AccountInfo, account)) || account.Auctions.Count != 0
            || account.Gold != 0 || account.Credit != 0 || account.Storage.Any(i => i != null) || account.HasExpandedStorage || account.ExpandedStorageExpiryDate > World.Now)
            throw new InvalidOperationException("只能删除空账户。请先处理全部角色（包括已删除角色）、拍卖、资金、仓库和扩展仓库权益。");
        var position = World.AccountList.IndexOf(account);
        changes.Commit(() => World.AccountList.RemoveAt(position), () => World.AccountList.Insert(position, account));
    }
    public void ResetPassword(int id, PasswordReset reset, bool storage)
    {
        var a = Account(id); Offline(a); AdminChanges.Match(ReadAccount(id).Version, reset.Version); AdminChanges.Confirm(reset.Confirmation, a.AccountID); Password(reset.Password);
        if (!storage && reset.RequireChange && !Settings.AllowChangePassword) throw new InvalidOperationException("要求修改密码前，请先在服务端设置中启用密码修改。");
        var undo = a.CaptureCredentials(); var required = a.RequirePasswordChange; var wrong = a.WrongPasswordCount;
        changes.Commit(() => { if (storage) { a.StoragePassword = reset.Password; a.StoragePasswordLastSet = World.Now; } else { a.Password = reset.Password; a.RequirePasswordChange = reset.RequireChange; a.WrongPasswordCount = 0; } },
            () => { undo(); a.RequirePasswordChange = required; a.WrongPasswordCount = wrong; });
    }
    public void Kick(int id, AdminCommand command)
    {
        var a = Account(id); AdminChanges.Confirm(command.Confirmation, a.AccountID);

        a.Connection?.SendDisconnect(1);
        World.SaveAll();
    }
    public CharacterDetails ReadCharacter(int id)
    {
        var c = Character(id);
        var inventory = Items("Inventory", c.Inventory).Concat(Items("Equipment", c.Equipment)).Concat(Items("QuestInventory", c.QuestInventory)).ToArray();
        var result = new CharacterDetails(c.Index, c.Name, c.AccountInfo.Index, c.AccountInfo.AccountID, c.Class.ToString(), c.Gender.ToString(), c.Level, c.Experience, c.PKPoints, c.HP, c.MP,
            c.Player != null, c.Deleted, c.Banned, c.BanReason, AdminChanges.PublicExpiry(c.ExpiryDate), c.ChatBanned, AdminChanges.PublicExpiry(c.ChatBanExpiryDate), c.CurrentMapIndex, c.CurrentLocation.X, c.CurrentLocation.Y,
            c.BindMapIndex, c.BindLocation.X, c.BindLocation.Y, c.GuildIndex, World.GetGuild(c.GuildIndex)?.Name ?? "", inventory, c.Magics.Select(m => m.Spell.ToString()).ToArray(), c.CurrentQuests.Select(q => q.Index).ToArray(), c.CompletedQuests.ToArray(), "");
        return result with { Version = AdminChanges.Version(result) };
    }
    public CharacterDetails CreateCharacter(CharacterCreate request)
    {
        var account = Account(request.AccountId); Offline(account);
        if (!Envir.IsCharacterNameValid(request.Name)) throw new InvalidDataException("角色名称无效或属于保留名称。");
        if (World.CharacterList.Any(c => c.Name.Equals(request.Name, StringComparison.OrdinalIgnoreCase)) || World.HeroList.Any(c => c.Name.Equals(request.Name, StringComparison.OrdinalIgnoreCase))) throw new InvalidOperationException("角色名称已被使用。");
        if (request.Class is < 0 or > 2 || request.Gender is < 0 or > 1) throw new InvalidDataException("仅开放战士、法师、道士；角色性别须为男或女。");
        if (account.Characters.Count(c => !c.Deleted) >= Globals.MaxCharacterCount) throw new InvalidOperationException("账户的角色位置已满。");
        var character = new CharacterInfo { Index = AdminChanges.Next(World.NextCharacterID, World.CharacterList.Select(c => c.Index)), Name = request.Name, Class = (MirClass)request.Class, Gender = (MirGender)request.Gender,
            AccountInfo = account, CreationDate = World.Now, CreationIP = "127.0.0.1", Heroes = new HeroInfo[1] };
        var counter = World.NextCharacterID;
        changes.Commit(() => { account.Characters.Add(character); World.CharacterList.Add(character); World.NextCharacterID = character.Index; },
            () => { account.Characters.Remove(character); World.CharacterList.Remove(character); World.NextCharacterID = counter; });
        return ReadCharacter(character.Index);
    }
    public void SaveCharacter(CharacterEdit edit)
    {
        var c = Character(edit.Id); Offline(c.AccountInfo); var before = ReadCharacter(edit.Id);
        AdminChanges.Match(before.Version, edit.Version); AdminChanges.Confirm(edit.Confirmation, c.Name);
        if (edit.Level < 0 || edit.Level > Settings.ExperienceList.Count || edit.Experience < 0 || edit.PkPoints is < 0 or > 1000000) throw new InvalidDataException("等级、经验或杀戮值无效。");
        if (c.Level == 0 && (edit.Level != 0 || edit.Experience != 0)) throw new InvalidOperationException("修改此角色的等级前，请先登录一次，完成初始物品发放。");
        if (c.Level > 0 && edit.Level == 0) throw new InvalidDataException("已初始化的角色不能退回零级。");
        if (edit.Level > 0 && edit.Level < Settings.ExperienceList.Count && edit.Experience >= Settings.ExperienceList[edit.Level - 1]) throw new InvalidDataException("经验必须低于所选等级升至下一级的门槛。");
        var expiry = AdminChanges.Expiry(edit.Banned, edit.BanUntil); var mute = AdminChanges.Expiry(edit.Muted, edit.MuteUntil); var reason = BanReason(edit.Banned, edit.BanReason);
        var oldExpiry = c.ExpiryDate; var oldMute = c.ChatBanExpiryDate;
        changes.Commit(() => { c.Level = (ushort)edit.Level; c.Experience = edit.Experience; c.PKPoints = edit.PkPoints; c.Banned = edit.Banned; c.BanReason = reason; c.ExpiryDate = expiry; c.ChatBanned = edit.Muted; c.ChatBanExpiryDate = mute; },
            () => { c.Level = (ushort)before.Level; c.Experience = before.Experience; c.PKPoints = before.PkPoints; c.Banned = before.Banned; c.BanReason = before.BanReason; c.ExpiryDate = oldExpiry; c.ChatBanned = before.Muted; c.ChatBanExpiryDate = oldMute; });
        World.RemoveRank(c);
        if (c.Level > 0 && !c.AccountInfo.AdminAccount && !c.Deleted) World.CheckRankUpdate(c);
    }
    public void Travel(int id, CharacterTravel request)
    {
        var c = Character(id); AdminChanges.Confirm(request.Confirmation, c.Name);
        if (c.Deleted || c.Level == 0) throw new InvalidOperationException("角色必须已初始化，且未被删除。");
        var map = World.GetMap(request.MapId) ?? throw new InvalidDataException("地图尚未加载。");
        if (!map.ValidPoint(request.X, request.Y)) throw new InvalidDataException("目标位置必须是可行走的格子。");
        if (c.Player != null)
        {
            if (request.SetBind) throw new InvalidOperationException("修改回城绑定点时，账户必须离线。");

            if (!c.Player.Teleport(map, new Point(request.X, request.Y), false)) throw new InvalidOperationException("引擎拒绝了这次传送。");
            World.SaveAccounts(); return;
        }
        Offline(c.AccountInfo);
        var oldMap = c.CurrentMapIndex; var oldPoint = c.CurrentLocation; var oldBindMap = c.BindMapIndex; var oldBind = c.BindLocation;
        changes.Commit(() => { c.CurrentMapIndex = map.Info.Index; c.CurrentLocation = new(request.X, request.Y); if (request.SetBind) { c.BindMapIndex = map.Info.Index; c.BindLocation = c.CurrentLocation; } },
            () => { c.CurrentMapIndex = oldMap; c.CurrentLocation = oldPoint; c.BindMapIndex = oldBindMap; c.BindLocation = oldBind; });
    }
    public void Grant(int id, ItemGrant request)
    {
        var c = Character(id); Offline(c.AccountInfo); AdminChanges.Match(ReadCharacter(id).Version, request.Version); AdminChanges.Confirm(request.Confirmation, c.Name);
        if (c.Level == 0 || c.Deleted) throw new InvalidOperationException("发放物品前，请先初始化此角色。");
        var info = World.ItemInfoList.SingleOrDefault(i => i.Index == request.ItemId) ?? throw new InvalidDataException("未找到物品定义。");
        if (request.Count is < 1 or > 65535) throw new InvalidDataException("发放数量必须为1至65,535。");
        var inventory = info.Type == ItemType.Quest ? c.QuestInventory : c.Inventory;
        var stack = Math.Max(1, (int)info.StackSize); var needed = (request.Count + stack - 1) / stack;
        var free = Enumerable.Range(0, inventory.Length).Where(i => inventory[i] == null).Take(needed).ToArray();
        if (free.Length < needed) throw new InvalidOperationException("背包空位不足，未发放任何物品。");
        var counter = World.NextUserItemID;
        if (ulong.MaxValue - counter < (ulong)needed) throw new InvalidOperationException("物品编号空间已用尽。");
        changes.Commit(() => { var remaining = request.Count; foreach (var slot in free) { var item = World.CreateFreshItem(info); item.GMMade = true; item.Count = (ushort)Math.Min(stack, remaining); inventory[slot] = item; remaining -= item.Count; } },
            () => { foreach (var slot in free) inventory[slot] = null; World.NextUserItemID = counter; });
    }
    public void DeleteCharacter(int id, bool deleted, AdminCommand command)
    {
        var c = Character(id); Offline(c.AccountInfo); AdminChanges.Match(ReadCharacter(id).Version, command.Version); AdminChanges.Confirm(command.Confirmation, c.Name);
        if (c.Deleted == deleted) throw new InvalidOperationException("角色已经处于所要求的删除状态。");
        if (c.GuildIndex != -1 || c.Married != 0 || c.Mentor != 0) throw new InvalidOperationException("删除或恢复此角色前，请先处理行会、婚姻和师徒关系。");
        if (!deleted && (c.AccountInfo.Characters.Count(i => !i.Deleted) >= Globals.MaxCharacterCount || World.CharacterList.Any(i => i != c && !i.Deleted && i.Name.Equals(c.Name, StringComparison.OrdinalIgnoreCase)))) throw new InvalidOperationException("角色位置不足或名称冲突，无法恢复。");
        var wasDeleted = c.Deleted; var date = c.DeleteDate;
        changes.Commit(() => { c.Deleted = deleted; c.DeleteDate = deleted ? World.Now : DateTime.MinValue; }, () => { c.Deleted = wasDeleted; c.DeleteDate = date; });
        if (deleted) World.RemoveRank(c); else if (c.Level > 0 && !c.AccountInfo.AdminAccount) World.CheckRankUpdate(c);
    }
    private static void Password(string value)
    { if (!Envir.IsPasswordValid(value)) throw new InvalidDataException("密码必须为5至15个英文字母或数字。"); }
    private static string BanReason(bool banned, string value)
    {
        if (!banned) return "";
        if (value == null || value.Length > 200 || value.Any(char.IsControl)) throw new InvalidDataException("封禁原因无效。");
        return value.Trim();
    }
}
