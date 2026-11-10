using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OneSpace.Application.Integrations;
using OneSpace.Connectors.ComplyKaro;
using OneSpace.Connectors.Microsoft365;
using OneSpace.Connectors.Zoho;

namespace OneSpace.Connectors;

public static class DependencyInjection
{
    public static IServiceCollection AddOneSpaceConnectors(this IServiceCollection services, IConfiguration configuration)
    {
        services.TryAddTimeProvider();

        // Zoho People (MCP)
        services.AddOptions<ZohoMcpOptions>().Bind(configuration.GetSection(ZohoMcpOptions.SectionName));
        services.AddSingleton<ZohoToolListCache>();
        services.AddSingleton<ZohoAuthMetadataCache>();
        services.AddHttpClient<IZohoPeopleClient, ZohoMcpClient>((sp, client) =>
            client.Timeout = sp.GetRequiredService<IOptions<ZohoMcpOptions>>().Value.Timeout);

        // Per-employee Zoho OAuth ("Connect Zoho People"); one scoped instance serves both interfaces.
        services.AddHttpClient<ZohoOAuthService>(client => client.Timeout = TimeSpan.FromSeconds(20));
        services.AddScoped<IZohoConnectionService>(sp => sp.GetRequiredService<ZohoOAuthService>());
        services.AddScoped<IZohoAccessTokenProvider>(sp => sp.GetRequiredService<ZohoOAuthService>());

        // Microsoft 365 (Graph, on-behalf-of)
        services.AddOptions<Microsoft365Options>().Bind(configuration.GetSection(Microsoft365Options.SectionName));
        services.AddSingleton<OnBehalfOfTokenProvider>();
        services.AddHttpClient<IMicrosoft365Client, GraphMicrosoft365Client>((sp, client) =>
        {
            client.BaseAddress = new Uri(sp.GetRequiredService<IOptions<Microsoft365Options>>().Value.GraphBaseUrl);
            client.Timeout = TimeSpan.FromSeconds(15);
        });

        // ComplyKaro (policy documents for the sync)
        var complyKaro = configuration.GetSection(ComplyKaroOptions.SectionName);
        services.AddOptions<ComplyKaroOptions>().Bind(complyKaro);
        if (string.Equals(complyKaro["Mode"], "Api", StringComparison.OrdinalIgnoreCase))
        {
            services.AddHttpClient<IComplyKaroSource, HttpComplyKaroSource>((sp, client) =>
                client.BaseAddress = new Uri(sp.GetRequiredService<IOptions<ComplyKaroOptions>>().Value.BaseUrl
                    ?? throw new InvalidOperationException($"{ComplyKaroOptions.SectionName}:BaseUrl is required in Api mode.")));
        }
        else
        {
            services.AddSingleton<IComplyKaroSource, SampleComplyKaroSource>();
        }

        return services;
    }

    private static void TryAddTimeProvider(this IServiceCollection services)
    {
        if (services.All(d => d.ServiceType != typeof(TimeProvider))) services.AddSingleton(TimeProvider.System);
    }
}
