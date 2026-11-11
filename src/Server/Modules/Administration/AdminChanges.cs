using System.Security.Cryptography;
using System.Text.Json;
using Mir2.Hosting;
using Server.MirEnvir;

namespace Server.ConsoleHost;

public sealed class AdminChanges(ProjectPaths paths, SqliteDatabaseStorage storage)
{
    public static string Version(object value) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value))).ToLowerInvariant();
    public static void Match(string current, string supplied)
    { if (current != supplied) throw new InvalidOperationException("Data changed since it was opened. Refresh before applying this operation."); }
    public static void Confirm(string supplied, string expected)
    { if (supplied != expected) throw new InvalidDataException("Confirmation must exactly match the selected target name."); }
    public static string Name(string value, int max = 40)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > max || value.Any(c => char.IsControl(c) || c is '/' or '\\' or ':')) throw new InvalidDataException("Invalid name.");
        return value.Trim();
    }
    public static int Next(int counter, IEnumerable<int> values)
    {
        var maximum = Math.Max(0, Math.Max(counter, values.DefaultIfEmpty(0).Max()));
        if (maximum == int.MaxValue) throw new InvalidOperationException("ID space is exhausted.");
        return maximum + 1;
    }
    public static DateTime Expiry(bool enabled, DateTime? value)
    {
        if (!enabled) return DateTime.MinValue;
        if (value == null) return DateTime.MaxValue;
        if (value.Value.ToUniversalTime() <= DateTime.UtcNow) throw new InvalidDataException("Expiry must be in the future, or empty for permanent.");
        return value.Value.ToLocalTime();
    }
    public static DateTime? PublicExpiry(DateTime value) => value == DateTime.MinValue || value == DateTime.MaxValue ? null : value;
    public void Commit(Action change, Action undo)
    {
        storage.Transaction(Envir.Main.SaveAll);
        var backup = Path.Combine(paths.Runtime, "backups", "admin-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff") + "-" + Guid.NewGuid().ToString("N")[..8], "mir2.sqlite3");
        storage.Backup(backup);
        try { storage.Transaction(() => { change(); Envir.Main.SaveAll(); }); }
        catch { undo(); throw; }
    }
}
