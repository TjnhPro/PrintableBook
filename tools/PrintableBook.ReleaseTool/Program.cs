namespace PrintableBook.ReleaseTool;

internal static class Program
{
    private static int Main(string[] args)
    {
        try
        {
            return ReleaseToolCommandParser.Parse(args) switch
            {
                KeygenCommand command => Generate(command),
                _ => (int)ReleaseToolExitCode.ValidationFailed
            };
        }
        catch (ArgumentException exception)
        {
            Console.Error.WriteLine(exception.Message);
            return (int)ReleaseToolExitCode.InvalidArguments;
        }
        catch (IOException exception)
        {
            Console.Error.WriteLine(exception.Message);
            return (int)ReleaseToolExitCode.ValidationFailed;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.Message);
            return (int)ReleaseToolExitCode.UnexpectedFailure;
        }
    }

    private static int Generate(KeygenCommand command)
    {
        var paths = ReleaseKeyGenerator.Generate(command.PublicOutputPath, command.PrivateOutputPath);
        Console.WriteLine($"Public key written: {paths.PublicKeyPath}");
        Console.WriteLine($"Private key written: {paths.PrivateKeyPath}");
        return (int)ReleaseToolExitCode.Success;
    }
}
