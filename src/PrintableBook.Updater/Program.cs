namespace PrintableBook.Updater;

public static class Program
{
    public static Task<int> Main(string[] args)
    {
        return Task.FromResult((int)UpdaterExitCode.InvalidArguments);
    }
}
