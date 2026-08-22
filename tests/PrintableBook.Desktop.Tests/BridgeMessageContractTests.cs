using System.Reflection;
using PrintableBook.Desktop;

namespace PrintableBook.Desktop.Tests;

public sealed class BridgeMessageContractTests
{
    [Fact]
    public void PingRequestUsesTheCamelCaseJsonContract()
    {
        var handler = typeof(MainWindow).GetMethod("TryHandleMessage", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(handler);

        var response = handler.Invoke(null, ["""{"version":1,"id":"request-1","command":"app.ping"}"""]);
        Assert.NotNull(response);

        var responseType = response.GetType();
        Assert.True((bool)responseType.GetProperty("Ok")!.GetValue(response)!);
        Assert.Equal("request-1", responseType.GetProperty("Id")!.GetValue(response));
        Assert.Equal("app.pong", responseType.GetProperty("Command")!.GetValue(response));
    }
}
