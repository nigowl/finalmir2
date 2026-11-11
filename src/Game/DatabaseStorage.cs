namespace Server;

// The desktop host keeps its existing files; headless hosts can supply a durable store.
public abstract class DatabaseStorage
{
    public static DatabaseStorage Current { get; set; }

    public abstract bool Contains(string name);
    public abstract Stream Read(string name);
    public abstract void Write(string name, byte[] data);
    public abstract void Remove(string name);
    public abstract string[] List(string directory, string pattern);
    public virtual void Transaction(Action action) => action();

    public static bool Exists(string name) => Current?.Contains(name) ?? File.Exists(name);
    public static Stream OpenRead(string name) => Current?.Read(name) ?? File.OpenRead(name);
    public static string[] GetFiles(string directory, string pattern) =>
        Current?.List(directory, pattern) ?? Directory.GetFiles(directory, pattern);

    public static void Delete(string name)
    {
        if (Current != null) Current.Remove(name);
        else File.Delete(name);
    }
}
