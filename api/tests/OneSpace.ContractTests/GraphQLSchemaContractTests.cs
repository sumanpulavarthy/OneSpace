using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace OneSpace.ContractTests;

/// <summary>
/// The schema served by the API must match the reviewed contract in specs/graphql/schema.graphql.
/// To change the API, change the spec first, then regenerate with:
/// <c>dotnet run --project src/OneSpace.Api -- schema export --output ../specs/graphql/schema.graphql</c>.
/// </summary>
public sealed class GraphQLSchemaContractTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Served_schema_matches_spec()
    {
        using var client = factory.CreateClient();

        var served = await client.GetStringAsync(new Uri("/graphql?sdl", UriKind.Relative));
        var spec = await File.ReadAllTextAsync(SpecPaths.GraphQLSchema);

        Assert.Equal(SpecPaths.Normalize(spec), SpecPaths.Normalize(served));
    }

    [Fact]
    public async Task Schema_exposes_no_argument_that_selects_another_employee()
    {
        var spec = await File.ReadAllTextAsync(SpecPaths.GraphQLSchema);

        foreach (var forbidden in new[] { "employeeId:", "userId:", "email:", "upn:" })
        {
            // Output fields may carry these names; arguments (inside parentheses) must not.
            Assert.DoesNotMatch($@"\([^)]*\b{forbidden}", spec);
        }
    }
}

public sealed class ApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureTestServices(services =>
        {
            // No database or sync job is needed to read the schema.
            services.RemoveAll<IHostedService>();
        });
    }
}

internal static class ServiceCollectionTestExtensions
{
    public static void RemoveAll<T>(this IServiceCollection services)
    {
        foreach (var descriptor in services.Where(d => d.ServiceType == typeof(T)).ToList()) services.Remove(descriptor);
    }
}
