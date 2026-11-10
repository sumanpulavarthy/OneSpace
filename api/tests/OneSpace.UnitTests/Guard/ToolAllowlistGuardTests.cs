using System.Text.Json.Nodes;
using OneSpace.Assistant.Guard;

namespace OneSpace.UnitTests.Guard;

public class ToolAllowlistGuardTests
{
    private readonly ToolAllowlistGuard _guard = ToolAllowlistGuard.FromEmbeddedSpec();

    [Theory]
    [InlineData("getLeaveBalance")]
    [InlineData("fetchLeaveTypes")]
    [InlineData("listholidays")]
    [InlineData("getLeaveRecords")]
    [InlineData("getTeamMembersOnLeave")]
    [InlineData("getEmployeeShiftDetails")]
    public void Allows_phase0_tools(string tool) => Assert.True(_guard.Authorize(tool, null).Allowed);

    [Theory]
    [InlineData("applyLeave")]
    [InlineData("cancelLeave")]
    [InlineData("deleteLeaveType")]
    [InlineData("approveEnrollment")]
    [InlineData("addTimelog")]
    [InlineData("getTimelogs")] // read-only, but a Phase 1+ candidate: not allowed until the spec says so
    [InlineData("unknownTool")]
    [InlineData("")]
    public void Denies_everything_else(string tool) => Assert.False(_guard.Authorize(tool, null).Allowed);

    [Theory]
    [InlineData("fetchLeaveTypeDetails")]
    [InlineData("getAdminEnrollmentBeneficiaries")]
    public void Denies_admin_scoped_read_tools(string tool)
    {
        var decision = _guard.Authorize(tool, null);
        Assert.False(decision.Allowed);
        Assert.Equal("admin-scoped tool", decision.Reason);
    }

    [Theory]
    [InlineData("GETLEAVEBALANCE")]
    [InlineData("getleavebalance")]
    [InlineData(" getLeaveBalance")]
    public void Match_is_exact_and_case_sensitive(string tool) => Assert.False(_guard.Authorize(tool, null).Allowed);

    [Fact]
    public void Self_scope_argument_is_added_when_missing()
    {
        var decision = _guard.Authorize("getLeaveRecords", new JsonObject { ["from"] = "2026-01-01" });

        Assert.True(decision.Allowed);
        Assert.Equal("MINE", decision.Arguments["data_select"]!.GetValue<string>());
        Assert.Equal("2026-01-01", decision.Arguments["from"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("getLeaveRecords", "ALL")]
    [InlineData("getLeaveRecords", "DEPARTMENT")]
    [InlineData("getLeaveRecords", "MINE,SUBORDINATES")]
    public void Broader_data_scope_is_denied(string tool, string scope)
    {
        var decision = _guard.Authorize(tool, new JsonObject { ["data_select"] = scope });
        Assert.False(decision.Allowed);
    }

    [Fact]
    public void Authorize_does_not_mutate_caller_arguments()
    {
        var args = new JsonObject();
        _guard.Authorize("getLeaveRecords", args);
        Assert.False(args.ContainsKey("data_select"));
    }

    [Fact]
    public void Spec_with_write_tool_on_allowlist_fails_validation()
    {
        var spec = new ToolAllowlistSpec
        {
            Allowed = [new AllowedTool { Name = "applyLeave" }],
            DeniedVerbPrefixes = ["apply"]
        };
        Assert.Throws<InvalidOperationException>(() => new ToolAllowlistGuard(spec));
    }

    [Fact]
    public void Spec_with_allow_by_default_fails_validation()
    {
        var spec = new ToolAllowlistSpec { DefaultPolicy = "allow" };
        Assert.Throws<InvalidOperationException>(() => new ToolAllowlistGuard(spec));
    }
}
