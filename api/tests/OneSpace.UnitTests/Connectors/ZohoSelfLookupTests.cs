using System.Text.Json.Nodes;
using OneSpace.Connectors.Zoho;

namespace OneSpace.UnitTests.Connectors;

public class ZohoSelfLookupTests
{
    // Shape of /people/api/attendance/getUserReport as returned live (admin token: every employee is listed).
    private static readonly JsonNode Report = JsonNode.Parse("""
        {"result":[
          {"attendanceDetails":{},"employeeDetails":{"mail id":"someone.else@example.com","erecno":"382901000000315603","id":"S2"}},
          {"attendanceDetails":{},"employeeDetails":{"mail id":"Me@Example.com","erecno":"382901000000315003","id":"1"}}
        ]}
        """)!;

    [Fact]
    public void Finds_the_entry_matching_the_callers_email() =>
        Assert.Equal(382901000000315003L, ZohoSelfLookup.FindErecno(Report, "me@example.com"));

    [Fact]
    public void Never_falls_back_to_another_employee() =>
        Assert.Null(ZohoSelfLookup.FindErecno(Report, "nobody@example.com"));

    [Theory]
    [InlineData("https://org-1.zohomcp.in/mcp/key/message", null, "https://people.zoho.in")]
    [InlineData("https://org-1.zohomcp.com/mcp/key/message", null, "https://people.zoho.com")]
    [InlineData("https://org-1.zohomcp.in/mcp/key/message", "https://people.zoho.eu/", "https://people.zoho.eu")]
    public void People_api_base_follows_the_mcp_data_centre(string endpoint, string? configured, string expected) =>
        Assert.Equal(expected, new ZohoMcpOptions { Endpoint = endpoint, PeopleApiBaseUrl = configured }.ResolvePeopleApiBaseUrl());
}
