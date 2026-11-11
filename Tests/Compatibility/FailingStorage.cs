using Server;

// Fail the commit after the pre-edit snapshot has been safely backed up.
internal sealed class FailingStorage(DatabaseStorage inner) : DatabaseStorage
{
    public int WritesBeforeFailure = int.MaxValue;
    public override bool Contains(string name) => inner.Contains(name);
    public override Stream Read(string name) => inner.Read(name);
    public override string[] List(string directory, string pattern) => inner.List(directory, pattern);
    public override void Remove(string name) => inner.Remove(name);
    public override void Transaction(Action action) => inner.Transaction(action);
    public override void Write(string name, byte[] data)
    {
        if (--WritesBeforeFailure == 0) throw new IOException("Simulated definition storage failure.");
        inner.Write(name, data);
    }
}
