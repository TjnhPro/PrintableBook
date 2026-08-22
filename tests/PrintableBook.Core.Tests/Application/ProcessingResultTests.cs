using PrintableBook.Core.Application.Progress;
using PrintableBook.Core.Application.Results;

namespace PrintableBook.Core.Tests.Application;

public sealed class ProcessingResultTests
{
    [Fact]
    public void Failed_result_preserves_a_structured_issue()
    {
        var result = ProcessingResult.Failed(new ProcessingIssue("asset.unreadable", "The asset cannot be read.", "inspect"));

        Assert.Equal(ProcessingStatus.Failure, result.Status);
        Assert.Equal("asset.unreadable", Assert.Single(result.Issues).Code);
    }

    [Fact]
    public void Cancelled_result_has_no_implicit_error()
    {
        var result = ProcessingResult.Cancelled();

        Assert.Equal(ProcessingStatus.Cancelled, result.Status);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void Progress_rejects_a_completed_count_larger_than_the_total()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ProcessingProgress("render", 3, 2));
    }
}
