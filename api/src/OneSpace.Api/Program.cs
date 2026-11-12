using Azure.Identity;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using OneSpace.Api.Assistant;
using OneSpace.Api.Authentication;
using OneSpace.Assistant.Telemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using OneSpace.Api.GraphQL;
using OneSpace.Application;
using Microsoft.Extensions.Options;
using OneSpace.Application.Integrations;
using OneSpace.Application.Policies;
using OneSpace.Connectors.Zoho;
using OneSpace.Assistant;
using OneSpace.Connectors;
using OneSpace.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// Secrets (LLM provider keys, Zoho MCP credential, Graph client secret, DB connection string) come from Key Vault
// in Azure. Secret names use "--" for ":" (e.g. Assistant--ApiKey).
if (Uri.TryCreate(builder.Configuration["KeyVault:Uri"], UriKind.Absolute, out var keyVaultUri))
{
    builder.Configuration.AddAzureKeyVault(keyVaultUri, new DefaultAzureCredential());
}

// FR-4 logging piggybacks on Application Insights when a connection string is present.
if (!string.IsNullOrWhiteSpace(builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]))
{
    builder.Services.AddOpenTelemetry().UseAzureMonitor()
        // Assistant spans/metrics plus GenAI semantic-convention LLM telemetry (model, tokens, latency; never contents).
        .WithTracing(t => t.AddSource(AssistantTelemetry.SourceName, AssistantTelemetry.LlmSourceName))
        .WithMetrics(m => m.AddMeter(AssistantTelemetry.SourceName, AssistantTelemetry.LlmSourceName));
}

var allowedOrigins = (builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [])
    .Where(o => !string.IsNullOrWhiteSpace(o))
    .Select(o => o.Trim().TrimEnd('/'))
    .Distinct(StringComparer.OrdinalIgnoreCase)
    .ToArray();
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod()));

builder.Services
    .AddOneSpaceAuthentication(builder.Configuration, builder.Environment)
    .AddOneSpaceApplication()
    .AddOneSpaceInfrastructure(builder.Configuration, builder.Environment.IsDevelopment())
    .AddOneSpaceConnectors(builder.Configuration)
    .AddOneSpaceAssistant(builder.Configuration, builder.Environment.IsDevelopment());
builder.Services.AddOneSpaceGraphQL();
builder.Services.AddHealthChecks();

var app = builder.Build();

app.UseCors();
app.UseMiddleware<AuthenticationMiddleware>();
app.MapHealthChecks("/health");
app.MapGraphQL("/graphql");
app.MapAssistantStream();

// Zoho redirects the browser here after consent. Anonymous by necessity: the single-use, user-bound state
// identifies the employee. The return target is fixed in config, never taken from the request (no open redirect).
app.MapGet(ZohoConnectionMutations.CallbackPath, async (
    string? state, string? code, string? error,
    IZohoConnectionService zoho, IOptions<ZohoMcpOptions> zohoOptions, CancellationToken ct) =>
{
    var connected = await zoho.CompleteAsync(state, code, error, ct);
    var target = zohoOptions.Value.OAuth.PostConnectRedirectUri;
    return Results.Redirect($"{target}{(target.Contains('?', StringComparison.Ordinal) ? '&' : '?')}zoho={(connected ? "connected" : "failed")}");
});

if (app.Environment.IsDevelopment())
{
    app.MapGet("/", () => Results.Redirect("/graphql"));
    // Manual trigger for the policy sync while developing; production relies on the scheduled job.
    app.MapPost("/dev/sync-policies", (SyncPoliciesUseCase sync, CancellationToken ct) => sync.ExecuteAsync(ct));
    // Diagnostics: which tools the Zoho MCP server actually exposes vs. the allowlist (names only, no data).
    app.MapGet("/dev/zoho/tools", async (IZohoPeopleClient zoho, IToolAllowlistGuard guard, IOptions<ZohoMcpOptions> o, CancellationToken ct) =>
    {
        var mcp = (ZohoMcpClient)zoho;
        var server = (await mcp.ListServerToolsAsync(ct)).Select(t => t.Name).Order().ToList();
        var exposedTools = await zoho.ListAllowedToolsAsync(ct);
        var exposed = exposedTools.Select(t => t.Name).Order().ToList();
        return Results.Ok(new
        {
            serverTools = server,
            allowlisted = guard.AllowedTools.Order(),
            exposedToAssistant = exposed,
            allowlistedButNotOnServer = guard.AllowedTools.Except(exposed).Order(),
            instructions = await mcp.GetServerInstructionsAsync(ct),
            schemas = exposedTools.ToDictionary(t => t.Name, t => new { t.Description, inputSchema = t.InputSchema.ToJsonString() }),
        });
    });
    // Diagnostics: one server tool's raw wire schema (before OneSpace strips arguments). Schema only, no data.
    app.MapGet("/dev/zoho/tools/{name}", async (string name, IZohoPeopleClient zoho, CancellationToken ct) =>
    {
        var tool = (await ((ZohoMcpClient)zoho).ListServerToolsAsync(ct)).FirstOrDefault(t => t.Name == name);
        return tool is null ? Results.NotFound() : Results.Ok(new { tool.Name, tool.Description, inputSchema = tool.InputSchema.ToJsonString() });
    });
}

// Supports `dotnet run -- schema export --output ../../../specs/graphql/schema.graphql`.
await app.RunWithGraphQLCommandsAsync(args);

public partial class Program;
