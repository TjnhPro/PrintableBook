using PrintableBook.Core.Application.S3Storage;

namespace PrintableBook.Core.Tests.S3Storage;

public sealed class BookOutputLeaseCoordinatorTests
{
    [Fact]
    public async Task Lease_rejects_overlapping_work_for_the_same_book_and_reopens_after_release()
    {
        var coordinator = new BookOutputLeaseCoordinator();
        var first = await coordinator.TryAcquireAsync("Book One");

        Assert.NotNull(first);
        Assert.Null(await coordinator.TryAcquireAsync("book one"));

        await first!.DisposeAsync();
        var next = await coordinator.TryAcquireAsync("Book One");
        Assert.NotNull(next);
        await next!.DisposeAsync();
    }

    [Fact]
    public async Task Lease_allows_unrelated_books_to_publish_independently()
    {
        var coordinator = new BookOutputLeaseCoordinator();
        await using var first = await coordinator.TryAcquireAsync("Book One");
        await using var second = await coordinator.TryAcquireAsync("Book Two");

        Assert.NotNull(first);
        Assert.NotNull(second);
    }
}
