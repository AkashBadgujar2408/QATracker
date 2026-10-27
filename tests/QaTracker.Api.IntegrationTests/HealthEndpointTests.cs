using System.Net;
using QaTracker.Api.IntegrationTests.Infrastructure;

namespace QaTracker.Api.IntegrationTests;

public sealed class HealthEndpointTests(ApiFactory api)
{
    [Fact]
    public async Task Health_IsAnonymous_AndReturnsOk()
    {
        var response = await api.CreateBrowser().GetAsync(new Uri("/health", UriKind.Relative), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
