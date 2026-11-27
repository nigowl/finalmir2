namespace Mir2.Hosting;

public static class ProgramVersion
{
    public static string Current { get; } = typeof(ProgramVersion).Assembly.GetName().Version!.ToString(3);
}
