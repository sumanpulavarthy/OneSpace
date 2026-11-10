using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.Options;
using OneSpace.Application.Integrations;
using OneSpace.Domain.Policies;

namespace OneSpace.Connectors.ComplyKaro;

public sealed class ComplyKaroOptions
{
    public const string SectionName = "Integrations:ComplyKaro";

    /// <summary>"Sample" (seed file, default for local/dev) or "Api" (pending Open Item 2: confirm the document API).</summary>
    public string Mode { get; set; } = "Sample";

    public string SampleFile { get; set; } = "SeedData/policies.sample.json";

    public string? BaseUrl { get; set; }

    /// <summary>Key Vault reference only.</summary>
    public string? ApiKey { get; set; }

    public string PoliciesPath { get; set; } = "api/v1/policies";

    public int PageSize { get; set; } = 50;
}

/// <summary>Seed policies for local development and demos until ComplyKaro API access is confirmed.</summary>
public sealed class SampleComplyKaroSource(IOptions<ComplyKaroOptions> options) : IComplyKaroSource
{
    public async IAsyncEnumerable<PolicyDocument> FetchPoliciesAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var path = Path.IsPathRooted(options.Value.SampleFile)
            ? options.Value.SampleFile
            : Path.Combine(AppContext.BaseDirectory, options.Value.SampleFile);

        await using var stream = File.OpenRead(path);
        var documents = await JsonSerializer.DeserializeAsync<List<PolicyDocument>>(stream, JsonSerializerOptions.Web, cancellationToken) ?? [];
        foreach (var document in documents) yield return document;
    }
}

/// <summary>
/// ComplyKaro REST source. The endpoint shape is an assumption until Open Item 2 is resolved
/// (does ComplyKaro expose document content via API at all?); adjust <see cref="Page"/> to the real contract.
/// </summary>
public sealed class HttpComplyKaroSource(HttpClient http, IOptions<ComplyKaroOptions> options) : IComplyKaroSource
{
    public async IAsyncEnumerable<PolicyDocument> FetchPoliciesAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var o = options.Value;
        for (var page = 1; ; page++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{o.PoliciesPath}?page={page}&pageSize={o.PageSize}");
            if (!string.IsNullOrWhiteSpace(o.ApiKey)) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", o.ApiKey);

            using var response = await http.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();
            var body = await response.Content.ReadFromJsonAsync<Page>(JsonSerializerOptions.Web, cancellationToken);
            if (body?.Items is not { Count: > 0 } items) yield break;

            foreach (var item in items)
            {
                yield return new PolicyDocument
                {
                    Id = item.Id,
                    Title = item.Title,
                    Category = item.Category ?? "General",
                    Content = item.Content ?? string.Empty,
                    SourceUrl = item.Url,
                    SourceUpdatedAt = item.UpdatedAt ?? DateTimeOffset.UtcNow
                };
            }

            if (items.Count < o.PageSize) yield break;
        }
    }

    private sealed record Page(List<Item>? Items);

    private sealed record Item(string Id, string Title, string? Category, string? Content, string? Url, DateTimeOffset? UpdatedAt);
}
