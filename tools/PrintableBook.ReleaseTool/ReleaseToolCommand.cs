namespace PrintableBook.ReleaseTool;

internal abstract record ReleaseToolCommand;

internal sealed record KeygenCommand(string PublicOutputPath, string PrivateOutputPath) : ReleaseToolCommand;

internal sealed record SignReleaseCommand(string ReleaseRoot, Version Version, string RuntimeIdentifier) : ReleaseToolCommand;

internal sealed record VerifyReleaseCommand(string ReleaseRoot, Version Version, string RuntimeIdentifier) : ReleaseToolCommand;

internal enum ReleaseToolExitCode
{
    Success = 0,
    InvalidArguments = 2,
    ValidationFailed = 3,
    SigningKeyMissing = 4,
    UnexpectedFailure = 9
}
