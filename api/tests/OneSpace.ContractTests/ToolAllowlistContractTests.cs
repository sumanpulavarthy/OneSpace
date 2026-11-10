using OneSpace.Assistant.Guard;

namespace OneSpace.ContractTests;

/// <summary>
/// FR-5 contract: the guard compiled into the API is exactly the reviewed spec, the active scope is exactly
/// the PRD's Phase 0 list, and every write/admin tool in PRD Appendix A is denied.
/// </summary>
public sealed class ToolAllowlistContractTests
{
    private static readonly string[] Phase0Tools =
        ["getLeaveBalance", "fetchLeaveTypes", "listholidays", "getLeaveRecords", "getTeamMembersOnLeave", "getEmployeeShiftDetails"];

    /// <summary>PRD Appendix A — "Removed entirely — write/admin/mutating".</summary>
    public static TheoryData<string> WriteAndAdminTools => new(
        "addEnrollment", "unpublishBenefitPlan", "addDependent", "addTimelog", "editTimelog", "cancelcompoff",
        "enableEnrollmentWindow", "addAdminEnrollment", "addAdminBeneficiary", "createBenefitPlan", "deleteTimelogs",
        "addLeaveType", "deleteLeaveType", "publishJobSchedule", "disableBenefitPlan", "createEnrollmentWindow",
        "createBenefitCategory", "editLeaveType", "terminateEnrollment", "createPlanYear", "addProvider",
        "approveEnrollment", "completeEnrollmentWindow", "enablePlanYear", "disableEnrollmentWindow", "disablePlanYear",
        "enableBenefitPlan", "editJobSchedule", "addJobSchedule", "addClient", "cancelLeave", "waiveEnrollment",
        "denyEnrollment", "addShiftChangeRequest", "withdrawEnrollment", "applyLeave", "publishBenefitPlan",
        "sumbitTimesheet", "startTimer", "addWindowEnrollment", "addUserBeneficiary",
        // Read-only but admin-scoped (FR-5)
        "fetchLeaveTypeDetails", "getAdminEnrollmentBeneficiaries");

    [Fact]
    public async Task Embedded_allowlist_matches_spec_file()
    {
        await using var file = File.OpenRead(SpecPaths.ToolAllowlist);
        var spec = ToolAllowlistGuard.Parse(file);
        var embedded = ToolAllowlistGuard.LoadEmbeddedSpec();

        Assert.Equal(spec.Allowed.Select(t => t.Name), embedded.Allowed.Select(t => t.Name));
        Assert.Equal(spec.ExcludedAdminReadTools, embedded.ExcludedAdminReadTools);
    }

    [Fact]
    public void Active_scope_is_exactly_phase0()
    {
        var guard = ToolAllowlistGuard.FromEmbeddedSpec();
        Assert.Equal(Phase0Tools.Order(), guard.AllowedTools.Order());
    }

    [Theory]
    [MemberData(nameof(WriteAndAdminTools))]
    public void Write_and_admin_tools_are_denied(string tool) =>
        Assert.False(ToolAllowlistGuard.FromEmbeddedSpec().Authorize(tool, null).Allowed);
}
