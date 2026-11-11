#nullable enable
using System.Security.Cryptography;
using System.Text;

namespace Mir2.Hosting;

public sealed class ProjectPaths
{
    public string Root { get; }
    public string Data { get; }
    public string VersionName => Path.GetFileName(Data);
    public string VersionId => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Data)));
    public string Runtime { get; }
    public string World => Path.Combine(Data, "world");
    public string Assets => Path.Combine(Data, "assets");
    public string Import => Environment.GetEnvironmentVariable("MIR2_IMPORT_ROOT") ?? Path.Combine(Data, "import");
    public string Database => Path.Combine(Runtime, "mir2.sqlite3");
    public string AdminKeyFile => Path.Combine(Runtime, "admin.key");
    public ProjectPaths(string? data = null, string? runtime = null, string? versionDirectory = null)
    {
        Root = Environment.GetEnvironmentVariable("MIR2_PROJECT_ROOT") ?? FindRoot();
        var configured = string.IsNullOrWhiteSpace(versionDirectory) ? Path.Combine(Root, "data", "versions", "default") : Path.GetFullPath(versionDirectory, AppContext.BaseDirectory);
        Data = Path.TrimEndingDirectorySeparator(Path.GetFullPath(data ?? Environment.GetEnvironmentVariable("MIR2_DATA_ROOT") ?? configured));
        Runtime = Path.GetFullPath(runtime ?? Environment.GetEnvironmentVariable("MIR2_RUNTIME_ROOT") ?? Path.Combine(Data, "runtime"));
    }
    private static string FindRoot()
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
            for (var directory = new DirectoryInfo(start); directory != null; directory = directory.Parent)
                if (File.Exists(Path.Combine(directory.FullName, "Mir2.sln"))) return directory.FullName;
        return Directory.GetCurrentDirectory();
    }
    public string AdminKey()
    {
        Directory.CreateDirectory(Runtime);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(Runtime, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var configured = Environment.GetEnvironmentVariable("MIR2_ADMIN_KEY");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            if (configured.Length < 32) throw new InvalidDataException("MIR2_ADMIN_KEY must contain at least 32 characters.");
            return configured;
        }
        if (!File.Exists(AdminKeyFile))
        {
            var temporary = AdminKeyFile + "." + Guid.NewGuid() + ".tmp";
            var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write };
            if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            try
            {
                using (var file = new FileStream(temporary, options))
                { file.Write(Encoding.UTF8.GetBytes(Convert.ToHexString(RandomNumberGenerator.GetBytes(32)))); file.Flush(true); }
                try { File.Move(temporary, AdminKeyFile); } catch (IOException) when (File.Exists(AdminKeyFile)) { }
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        var key = File.ReadAllText(AdminKeyFile).Trim();
        if (key.Length < 32) throw new InvalidDataException($"Invalid management key: {AdminKeyFile}");
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(AdminKeyFile, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        return key;
    }
    public static bool KeyEquals(string? supplied, string expected) => supplied != null && CryptographicOperations.FixedTimeEquals(
        SHA256.HashData(Encoding.UTF8.GetBytes(supplied)), SHA256.HashData(Encoding.UTF8.GetBytes(expected)));

    public static string SafePath(string root, string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Contains('\\') || name.Split('/').Any(p => p is ".." or "." or ""))
            throw new InvalidDataException("Invalid relative path.");
        var directory = new DirectoryInfo(Path.GetFullPath(root));
        var physicalRoot = directory.Exists ? directory.ResolveLinkTarget(true)?.FullName ?? directory.FullName : directory.FullName;
        var path = Path.GetFullPath(Path.Combine(physicalRoot, name));
        if (!path.StartsWith(physicalRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new InvalidDataException("Path is outside the managed directory.");
        var current = physicalRoot;
        foreach (var part in name.Split('/'))
        {
            current = Path.Combine(current, part);
            if (File.Exists(current) || Directory.Exists(current))
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Nested symbolic links are not allowed.");
        }
        return path;
    }
}
