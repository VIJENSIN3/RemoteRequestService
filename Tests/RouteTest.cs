namespace Service.Tests;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.VisualStudio.TestPlatform.TestHost;
using Xunit;

public class RouteTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public RouteTests(WebApplicationFactory<Program> factory) => _factory = factory;

    [Fact]
    public async Task Ping_ReturnsRunning()
    {
        var client = _factory.CreateClient();
        var response = await client.GetStringAsync("/ping");
        Assert.Equal("running", response);
    }

    [Fact]
    public async Task ApiRoute_ReturnsJson()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/test");
        var json = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"RequestId\"", json);
        Assert.Contains("\"ExecutorType\":\"http\"", json);
    }
}