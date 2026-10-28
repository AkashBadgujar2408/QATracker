using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace QaTracker.Api.IntegrationTests.Infrastructure;

public static class HttpAssertions
{
    /// <summary>Asserts the status and returns the ProblemDetails JSON (for code / errors checks).</summary>
    public static async Task<JsonElement> ShouldBeProblemAsync(this HttpResponseMessage response, HttpStatusCode status)
    {
        response.StatusCode.ShouldBe(status, await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    /// <summary>Asserts the status and the problem's code.</summary>
    public static async Task ShouldBeProblemAsync(this HttpResponseMessage response, HttpStatusCode status, string code) =>
        (await response.ShouldBeProblemAsync(status)).Code().ShouldBe(code);

    public static string? Code(this JsonElement problem) =>
        problem.TryGetProperty("code", out var code) ? code.GetString() : null;

    public static async Task<T> ShouldBeOkAsync<T>(this HttpResponseMessage response, HttpStatusCode status = HttpStatusCode.OK)
    {
        response.StatusCode.ShouldBe(status, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<T>(ApiFactory.Json))!;
    }
}
