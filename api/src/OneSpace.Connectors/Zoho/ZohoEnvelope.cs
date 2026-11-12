using System.Text.Json.Nodes;

namespace OneSpace.Connectors.Zoho;

/// <summary>
/// Zoho People MCP replies look like <c>{"status":"success","data":{"response":{"result":...,"status":0}},"headers":{...}}</c>
/// (verified live 2026-11-12). The outer status is "success" even when the call failed; the real outcome is in
/// <c>result.status</c> / <c>result.code</c>. This strips the envelope and HTTP headers and reports failures.
/// </summary>
internal static class ZohoEnvelope
{
    public static bool TryUnwrap(JsonNode? node, out JsonNode? payload, out string? failure)
    {
        payload = null;
        failure = null;
        if (node?["data"]?["response"] is not JsonObject response) return false;

        var result = response["result"];
        if (result is JsonObject obj && IsFailure(obj))
        {
            failure = $"Zoho People rejected the request: {Str(obj["code"]) ?? "error"}: {Str(obj["message"]) ?? Str(response["message"]) ?? "no details"}";
            return true;
        }

        payload = (result ?? response).DeepClone();
        return true;
    }

    private static bool IsFailure(JsonObject result) =>
        string.Equals(Str(result["status"]), "failure", StringComparison.OrdinalIgnoreCase)
        || (result["code"] is not null && result["details"] is not null && result.Count <= 4);

    private static string? Str(JsonNode? node) =>
        node is JsonValue v && v.TryGetValue<string>(out var s) ? s : node?.ToJsonString();
}
