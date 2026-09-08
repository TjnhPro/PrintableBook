using System.Globalization;

namespace PrintableBook.Updater;

public static class UpdaterCommandParser
{
    private static readonly string[] RequiredOptions =
    ["--wait-pid", "--app-root", "--payload-dir", "--updates-root", "--current-version"];

    public static bool TryParse(string[] args, out UpdaterCommand? command, out string? error)
    {
        command = null;
        error = null;
        if (args is null || args.Length != RequiredOptions.Length * 2)
        {
            error = "Exactly five option/value pairs are required.";
            return false;
        }

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 0; index < args.Length; index += 2)
        {
            if (!RequiredOptions.Contains(args[index], StringComparer.Ordinal) ||
                !values.TryAdd(args[index], args[index + 1]) ||
                string.IsNullOrWhiteSpace(args[index + 1]))
            {
                error = "Arguments must contain each supported option exactly once with a value.";
                return false;
            }
        }

        if (!int.TryParse(values["--wait-pid"], NumberStyles.None, CultureInfo.InvariantCulture, out var waitPid) || waitPid <= 0)
        {
            error = "--wait-pid must be a positive integer.";
            return false;
        }

        var versionText = values["--current-version"];
        if (!Version.TryParse(versionText, out var currentVersion) || currentVersion.Build < 0 || currentVersion.Revision >= 0 || versionText.Split('.').Length != 3)
        {
            error = "--current-version must use M.m.p format.";
            return false;
        }

        command = new UpdaterCommand(waitPid, values["--app-root"], values["--payload-dir"], values["--updates-root"], currentVersion);
        return true;
    }
}
