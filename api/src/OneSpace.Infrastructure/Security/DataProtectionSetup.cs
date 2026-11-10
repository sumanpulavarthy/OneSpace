using System.Xml.Linq;
using Azure.Identity;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using OneSpace.Infrastructure.Persistence;

namespace OneSpace.Infrastructure.Security;

/// <summary>
/// Encryption for OAuth tokens at rest (ASP.NET Core Data Protection, AES-256-CBC + HMACSHA256, keys rotated every 90 days).
/// The key ring is stored in Cosmos so every API instance shares it, and in Azure each key is wrapped by an
/// RSA key in Key Vault: a database copy alone cannot decrypt anything.
/// </summary>
public static class DataProtectionSetup
{
    public const string KeyVaultKeyUriSetting = "DataProtection:KeyVaultKeyUri";
    public const string KeysCollection = "dataProtectionKeys";

    public static IServiceCollection AddOneSpaceDataProtection(this IServiceCollection services, IConfiguration configuration, bool isDevelopment)
    {
        var builder = services.AddDataProtection().SetApplicationName("onespace");

        services.AddSingleton<IConfigureOptions<KeyManagementOptions>>(sp =>
            new ConfigureOptions<KeyManagementOptions>(o => o.XmlRepository = new MongoXmlRepository(sp.GetRequiredService<MongoContext>())));

        var keyUri = configuration[KeyVaultKeyUriSetting];
        if (Uri.TryCreate(keyUri, UriKind.Absolute, out var uri))
        {
            builder.ProtectKeysWithAzureKeyVault(uri, new DefaultAzureCredential());
        }
        else if (!isDevelopment)
        {
            // Without a key-encryption key the key ring would sit in the database unencrypted next to the tokens.
            throw new InvalidOperationException($"{KeyVaultKeyUriSetting} is required outside Development to protect stored tokens.");
        }

        return services;
    }
}

/// <summary>Stores the Data Protection key ring in Cosmos DB (Mongo API).</summary>
public sealed class MongoXmlRepository(MongoContext context) : IXmlRepository
{
    private IMongoCollection<KeyDocument> Keys => context.Database.GetCollection<KeyDocument>(DataProtectionSetup.KeysCollection);

    public IReadOnlyCollection<XElement> GetAllElements() =>
        Keys.Find(FilterDefinition<KeyDocument>.Empty).ToList().Select(k => XElement.Parse(k.Xml)).ToList();

    public void StoreElement(XElement element, string friendlyName) =>
        Keys.InsertOne(new KeyDocument { Id = friendlyName, Xml = element.ToString(SaveOptions.DisableFormatting) });

    private sealed class KeyDocument
    {
        public required string Id { get; init; }
        public required string Xml { get; init; }
    }
}
