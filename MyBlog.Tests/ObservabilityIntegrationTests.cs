using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace MyBlog.Tests;

public sealed class ObservabilityIntegrationTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public ObservabilityIntegrationTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Response_IncludesGeneratedCorrelationId_WhenRequestDoesNotSupplyOne()
    {
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        using var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.TryGetValues("X-Correlation-ID", out var values));
        var correlationId = values.FirstOrDefault();
        Assert.False(string.IsNullOrWhiteSpace(correlationId));
        Assert.True(correlationId!.Length <= 64);
    }

    [Fact]
    public async Task Response_EchoesCorrelationId_WhenRequestSuppliesOne()
    {
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        const string expectedCorrelationId = "integration-correlation-id-001";
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        request.Headers.Add("X-Correlation-ID", expectedCorrelationId);

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.TryGetValues("X-Correlation-ID", out var values));
        Assert.Equal(expectedCorrelationId, values.FirstOrDefault());
    }

    [Fact]
    public async Task NotFoundResponse_StillIncludesCorrelationId()
    {
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        using var response = await client.GetAsync("/route-that-does-not-exist");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.True(response.Headers.TryGetValues("X-Correlation-ID", out var values));
        Assert.False(string.IsNullOrWhiteSpace(values.FirstOrDefault()));
    }
}
