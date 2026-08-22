using PrintableBook.Desktop.Bridge;

namespace PrintableBook.Desktop.Tests;

public sealed class BridgeMessageContractTests
{
    [Fact]
    public void PingRequestIsRoutedWithoutReachingIntoMainWindow()
    {
        var router = new WebViewBridgeRouter();

        var response = router.Handle("""{"version":1,"id":"request-1","command":"app.ping"}""");

        Assert.True(response.Ok);
        Assert.Equal("request-1", response.Id);
        Assert.Equal("app.pong", response.Command);
    }

    [Fact]
    public void UnsupportedCommandKeepsTheRequestCorrelationId()
    {
        var response = new WebViewBridgeRouter().Handle("""{"version":1,"id":"request-2","command":"book.process"}""");

        Assert.False(response.Ok);
        Assert.Equal("request-2", response.Id);
        Assert.Equal("unsupported_command", response.Error);
    }
}
