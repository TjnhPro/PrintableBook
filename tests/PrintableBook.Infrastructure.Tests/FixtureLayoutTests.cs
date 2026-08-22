namespace PrintableBook.Infrastructure.Tests;

public sealed class FixtureLayoutTests
{
    [Fact]
    public void TestData_directory_is_available_to_real_input_integration_tests()
    {
        var fixtureDirectory = Path.Combine(AppContext.BaseDirectory, "TestData");

        Assert.True(Directory.Exists(fixtureDirectory));
        Assert.True(File.Exists(Path.Combine(fixtureDirectory, "README.md")));
    }
}
