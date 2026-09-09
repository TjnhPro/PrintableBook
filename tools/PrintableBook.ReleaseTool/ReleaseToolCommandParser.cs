using PrintableBook.UpdateSecurity;

namespace PrintableBook.ReleaseTool;

internal static class ReleaseToolCommandParser
{
    public static ReleaseToolCommand Parse(string[] args)
    {
        if (args is null || args.Length == 0) throw new ArgumentException("A release-tool command is required.", nameof(args));
        var commandName = args[0];
        var options = ParseOptions(args.AsSpan(1));
        return commandName switch
        {
            "keygen" => new KeygenCommand(Required(options, "--public-output"), Required(options, "--private-output")),
            "sign-release" => new SignReleaseCommand(Required(options, "--release-root"), ParseVersion(Required(options, "--version")), ParseRuntime(Required(options, "--runtime"))),
            "verify-release" => new VerifyReleaseCommand(Required(options, "--release-root"), ParseVersion(Required(options, "--version")), ParseRuntime(Required(options, "--runtime"))),
            _ => throw new ArgumentException("Unknown release-tool command.", nameof(args))
        };
    }

    private static Dictionary<string, string> ParseOptions(ReadOnlySpan<string> args)
    {
        if (args.Length % 2 != 0) throw new ArgumentException("Options must be provided as name/value pairs.", nameof(args));
        var options = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 0; index < args.Length; index += 2)
        {
            var name = args[index];
            var value = args[index + 1];
            if (string.IsNullOrEmpty(name) || !name.StartsWith("--", StringComparison.Ordinal) || string.IsNullOrWhiteSpace(value) || !options.TryAdd(name, value))
            {
                throw new ArgumentException("Release-tool options are invalid.", nameof(args));
            }
        }

        return options;
    }

    private static string Required(IReadOnlyDictionary<string, string> options, string name)
    {
        if (!options.TryGetValue(name, out var value) || options.Count != (name is "--public-output" or "--private-output" ? 2 : 3))
        {
            throw new ArgumentException("Release-tool options are invalid.", nameof(options));
        }

        return value;
    }

    private static Version ParseVersion(string value)
    {
        if (!Version.TryParse(value, out var version) || version is null || version.Build < 0 || version.Revision >= 0 || version.ToString(3) != value)
        {
            throw new ArgumentException("Version must use strict M.m.p format.", nameof(value));
        }

        return version;
    }

    private static string ParseRuntime(string value)
    {
        if (value != UpdateManifestContract.RuntimeIdentifier) throw new ArgumentException("Runtime must be win-x64.", nameof(value));
        return value;
    }
}
