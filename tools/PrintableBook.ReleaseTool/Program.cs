namespace PrintableBook.ReleaseTool;

using PrintableBook.UpdateSecurity;

internal static class Program
{
    private static int Main(string[] args)
    {
        try
        {
            return ReleaseToolCommandParser.Parse(args) switch
            {
                KeygenCommand command => Generate(command),
                SignReleaseCommand command => Sign(command),
                VerifyReleaseCommand command => Verify(command),
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
        catch (InvalidDataException exception)
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

    private static int Sign(SignReleaseCommand command)
    {
        var encodedPrivateKey = Environment.GetEnvironmentVariable("PRINTABLEBOOK_UPDATE_SIGNING_PRIVATE_KEY");
        if (string.IsNullOrWhiteSpace(encodedPrivateKey)) return (int)ReleaseToolExitCode.SigningKeyMissing;
        byte[] privateSeed;
        try { privateSeed = Convert.FromBase64String(encodedPrivateKey.Trim()); }
        catch (FormatException) { return (int)ReleaseToolExitCode.SigningKeyMissing; }
        if (privateSeed.Length != 32) return (int)ReleaseToolExitCode.SigningKeyMissing;
        new ReleaseSigner().Sign(ReleaseArtifactPaths.Create(command.ReleaseRoot, command.Version, command.RuntimeIdentifier), command.Version, command.RuntimeIdentifier, privateSeed, ProductionUpdateSigningKey.GetPublicKey());
        return (int)ReleaseToolExitCode.Success;
    }

    private static int Verify(VerifyReleaseCommand command)
    {
        new ReleaseVerifier().Verify(ReleaseArtifactPaths.Create(command.ReleaseRoot, command.Version, command.RuntimeIdentifier), command.Version, command.RuntimeIdentifier, ProductionUpdateSigningKey.GetPublicKey());
        return (int)ReleaseToolExitCode.Success;
    }
}
