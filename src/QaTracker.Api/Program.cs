using System.Text.Json.Serialization;
using QaTracker.Api.Auth;
using QaTracker.Api.Common;
using QaTracker.Api.Endpoints;
using QaTracker.Application;
using QaTracker.Infrastructure;
using QaTracker.Infrastructure.Persistence.Seeding;
using Scalar.AspNetCore;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    // preserveStaticLogger: the bootstrap logger is not frozen, so several hosts (integration tests) can start.
    builder.Services.AddSerilog(
        (services, configuration) => configuration
            .ReadFrom.Configuration(builder.Configuration)
            .ReadFrom.Services(services)
            .Enrich.FromLogContext(),
        preserveStaticLogger: true);

    builder.Services.AddApplication();
    builder.Services.AddInfrastructure(builder.Configuration);
    builder.Services.AddQaAuth(builder.Configuration);
    builder.Services.ConfigureHttpJsonOptions(options =>
        options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

    builder.Services.AddProblemDetails();
    builder.Services.AddOpenApi(options => options.AddDocumentTransformer<BearerSecurityTransformer>());
    builder.Services.AddHealthChecks();

    const string AngularDevCorsPolicy = "AngularDev";
    builder.Services.AddCors(options => options.AddPolicy(AngularDevCorsPolicy, policy => policy
        .WithOrigins(builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [])
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials()));

    var app = builder.Build();

    // Schema comes from database/apply-scripts.ps1 (database-first); only reference data is seeded here.
    if (app.Configuration.GetValue<bool>("Database:SeedOnStartup"))
    {
        await using var scope = app.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<DatabaseSeeder>().SeedAsync(CancellationToken.None);
    }

    app.UseExceptionHandler();
    app.UseStatusCodePages();
    app.UseSerilogRequestLogging(options =>
        options.EnrichDiagnosticContext = (diagnostics, http) =>
            diagnostics.Set("CorrelationId", http.TraceIdentifier));

    if (app.Environment.IsDevelopment())
    {
        app.MapOpenApi().AllowAnonymous();
        app.MapScalarApiReference().AllowAnonymous();
        app.UseCors(AngularDevCorsPolicy);
    }
    else
    {
        app.UseHsts();
    }

    app.UseHttpsRedirection();
    app.UseAuthentication();
    app.UseAuthorization();
    app.UseRateLimiter();

    app.MapHealthChecks("/health").AllowAnonymous();
    app.MapAuthEndpoints();
    app.MapUsersEndpoints();
    app.MapMastersEndpoints();
    app.MapSprintsEndpoints();
    app.MapIssuesEndpoints();
    app.MapWorkflowEndpoints();
    app.MapTimelineEndpoints();
    app.MapWorkQueuesEndpoints();
    app.MapAttachmentsEndpoints();

    await app.RunAsync();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "QA Tracker API terminated unexpectedly");
    throw;
}
finally
{
    await Log.CloseAndFlushAsync();
}

public partial class Program;
