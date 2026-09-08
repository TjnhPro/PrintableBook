using PrintableBook.Updater;

namespace PrintableBook.Updater.Tests;

public sealed class UpdaterCommandParserTests
{
    [Fact]
    public void TryParseAcceptsExactValidArguments()
    {
        string[] args =
        [
            "--wait-pid", "1234",
            "--app-root", @"C:\PrintableBook",
            "--payload-dir", @"C:\Users\Test\AppData\Local\PrintableBook\Updates\staging\0.2.1\payload",
            "--updates-root", @"C:\Users\Test\AppData\Local\PrintableBook\Updates",
            "--current-version", "0.2.0",
        ];

        var parsed = UpdaterCommandParser.TryParse(args, out var command, out var error);

        Assert.True(parsed, error);
        Assert.Equal(1234, command!.WaitPid);
        Assert.Equal(@"C:\PrintableBook", command.AppRoot);
        Assert.Equal(new Version(0, 2, 0), command.CurrentVersion);
        Assert.EndsWith(Path.Combine("backup", "0.2.0"), command.BackupDirectory);
    }

    [Theory]
    [MemberData(nameof(InvalidArguments))]
    public void TryParseRejectsInvalidArguments(string[] args)
    {
        Assert.False(UpdaterCommandParser.TryParse(args, out _, out _));
    }

    public static IEnumerable<object[]> InvalidArguments()
    {
        yield return new object[] { Array.Empty<string>() };
        yield return new object[] { new[] { "--wait-pid", "1" } };
        yield return new object[] { new[] { "--wait-pid", "0", "--app-root", "a", "--payload-dir", "b", "--updates-root", "c", "--current-version", "0.2.0" } };
        yield return new object[] { new[] { "--wait-pid", "x", "--app-root", "a", "--payload-dir", "b", "--updates-root", "c", "--current-version", "0.2.0" } };
        yield return new object[] { new[] { "--wait-pid", "1", "--wait-pid", "2", "--payload-dir", "b", "--updates-root", "c", "--current-version", "0.2.0" } };
        yield return new object[] { new[] { "--unknown", "1", "--app-root", "a", "--payload-dir", "b", "--updates-root", "c", "--current-version", "0.2.0" } };
        yield return new object[] { new[] { "--wait-pid", "1", "--app-root", "a", "--payload-dir", "b", "--updates-root", "c", "--current-version", "0.2" } };
        yield return new object[] { new[] { "--wait-pid", "1", "--app-root", "a", "--payload-dir", "b", "--updates-root", "c", "--current-version", "0.2.0.1" } };
        yield return new object[] { new[] { "--wait-pid", "1", "--app-root", "a", "--payload-dir", "b", "--updates-root", "c", "--current-version", "v0.2.0" } };
    }
}
