using System.ComponentModel;
using System.Diagnostics;
using PrintableBook.Core.Abstractions;

namespace PrintableBook.Desktop.Tests;

public sealed class LocalOutputActionServiceTests
{
    [Fact]
    public async Task Shell_actions_accept_a_null_process_when_Windows_reuses_an_existing_handler()
    {
        var launches = new List<ProcessStartInfo>();
        var service = new LocalOutputActionService(startInfo =>
        {
            launches.Add(startInfo);
            return null;
        });

        await service.OpenAsync(new FileReference("book.pdf"));
        await service.OpenFolderAsync(new DirectoryReference("Output"));
        await service.RevealAsync(new FileReference("book.pdf"));

        Assert.Equal(3, launches.Count);
        Assert.Equal("book.pdf", launches[0].FileName);
        Assert.True(launches[0].UseShellExecute);
        Assert.Equal("Output", launches[1].FileName);
        Assert.True(launches[1].UseShellExecute);
        Assert.Equal("explorer.exe", launches[2].FileName);
        Assert.Contains("book.pdf", launches[2].Arguments, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Shell_actions_still_propagate_real_Windows_launch_failures()
    {
        var service = new LocalOutputActionService(_ => throw new Win32Exception(5, "Access denied"));

        var exception = await Assert.ThrowsAsync<Win32Exception>(
            () => service.OpenFolderAsync(new DirectoryReference("Output")).AsTask());

        Assert.Equal(5, exception.NativeErrorCode);
    }
}
