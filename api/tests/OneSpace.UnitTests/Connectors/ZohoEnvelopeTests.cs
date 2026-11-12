using System.Text.Json.Nodes;
using OneSpace.Connectors.Zoho;

namespace OneSpace.UnitTests.Connectors;

public class ZohoEnvelopeTests
{
    [Fact]
    public void Success_envelope_unwraps_to_result_without_headers()
    {
        var node = JsonNode.Parse("""
            {"status":"success","data":{"response":{"result":{"Casual Leave":{"available":4}},"message":"Success","status":0}},
             "headers":{"content-length":"80","content-type":"application/json"}}
            """);

        Assert.True(ZohoEnvelope.TryUnwrap(node, out var payload, out var failure));
        Assert.Null(failure);
        Assert.Equal(4, payload!["Casual Leave"]!["available"]!.GetValue<int>());
    }

    [Fact]
    public void In_band_failure_is_reported_as_error()
    {
        var node = JsonNode.Parse("""
            {"status":"success","data":{"response":{"result":{"code":"TOOL_PARAMETER_MISMATCH","details":{},"message":"employee missing","status":"failure"},"message":"Success","status":0}},
             "headers":{"content-length":"2333"}}
            """);

        Assert.True(ZohoEnvelope.TryUnwrap(node, out var payload, out var failure));
        Assert.Null(payload);
        Assert.Contains("TOOL_PARAMETER_MISMATCH", failure);
    }

    [Fact]
    public void Non_envelope_is_left_alone() =>
        Assert.False(ZohoEnvelope.TryUnwrap(JsonNode.Parse("""{"result":[1]}"""), out _, out _));
}
