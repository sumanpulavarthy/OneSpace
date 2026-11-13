using System.Text.Json;
using System.Text.Json.Nodes;
using OneSpace.Assistant.Presentation;
using OneSpace.Domain;
using OneSpace.Domain.Assistant;

namespace OneSpace.UnitTests.Presentation;

public class ToolResultPresenterTests
{
    private readonly ToolResultPresenter _presenter = new(PresentationSpec.LoadEmbedded());
    private static readonly SourceAttribution Zoho = new(SourceSystem.ZohoPeople, "From Zoho People", "getLeaveRecords");

    [Fact]
    public void Zoho_wrapper_is_unwrapped_into_a_table_with_ids_hidden_and_dates_normalised()
    {
        const string raw = """
            {"response":{"message":"Data fetched successfully","status":0,"result":[
              {"leaveId":"884","Leavetype":"Casual Leave","From":"12-Nov-2026","To":"13-Nov-2026","Days":2,"ApprovalStatus":"Approved"},
              {"leaveId":"885","Leavetype":"Sick Leave","From":"02-Dec-2026","To":"02-Dec-2026","Days":1,"ApprovalStatus":"Pending"}
            ]}}
            """;

        var result = _presenter.Present("zoho_getLeaveRecords", "t1", null, raw, Zoho);

        var block = Assert.Single(result.Blocks);
        Assert.Equal("t1", block.Id);
        Assert.Equal(AnswerBlockKind.Table, block.Kind);
        Assert.Equal("Your leave records", block.Title);
        Assert.DoesNotContain(block.Columns, c => c.Key == "leaveId");
        var from = block.Columns.Single(c => c.Key == "From");
        Assert.Equal(FieldFormat.Date, from.Format);
        Assert.Equal("2026-11-12", block.Rows[0][block.Columns.ToList().IndexOf(from)]);
        Assert.Equal(2, block.TotalRows);
        Assert.Equal(Zoho, block.Source);
    }

    [Fact]
    public void A_map_keyed_by_name_becomes_a_table_with_a_name_column()
    {
        var data = JsonNode.Parse("""{"Casual Leave":{"available":4.5,"booked":2},"Sick Leave":{"available":6,"booked":0}}""");

        var block = Assert.Single(_presenter.Present("zoho_getLeaveBalance", "t1", data, "", Zoho).Blocks);

        Assert.Equal(["Leave type", "Available", "Booked"], block.Columns.Select(c => c.Label));
        Assert.Equal(["Casual Leave", "4.5", "2"], block.Rows[0]);
    }

    [Fact]
    public void Live_leave_balance_shape_shows_leave_types_via_the_spec_path()
    {
        var data = JsonNode.Parse("""
            {"data":[{"emp_disabled":false,"erecno":"382901000000315003","name":"Me","leavetypes":[
              {"leavetype_id":"1","code":"CL","name":"Casual Leave","available":12,"taken":0,"planned":1,"used":0,"color":2,"leavetypeUnit":"Day","isV2":true},
              {"leavetype_id":"2","code":"EL","name":"Earned Leave","available":10.5,"taken":1.5,"planned":0,"used":1.5,"color":10,"leavetypeUnit":"Day","isV2":true}]}]}
            """);

        var block = Assert.Single(_presenter.Present("zoho_getLeaveBalance", "t1", data, "", Zoho).Blocks);

        Assert.Equal(["Leave type", "Available", "Taken", "Planned", "Unit"], block.Columns.Select(c => c.Label));
        Assert.Equal(["Earned Leave", "10.5", "1.5", "0", "Day"], block.Rows[1]);
    }

    [Fact]
    public void Sensitive_fields_are_withheld_from_the_model_but_shown_to_the_employee()
    {
        var mail = JsonSerializer.SerializeToNode(new[]
        {
            new { Subject = "Q3 review", From = "Asha", ReceivedAt = "2026-11-10T09:30:00Z", Preview = "Salary revision attached", IsRead = false, Importance = "high", WebLink = "https://outlook.office.com/x" }
        });

        var result = _presenter.Present("get_my_mail", "t1", mail, "", null);

        var model = result.RenderForModel(DataClass.Personal);
        Assert.DoesNotContain("Salary revision", model, StringComparison.Ordinal);
        Assert.Contains(PresentedResult.Withheld, model, StringComparison.Ordinal);
        Assert.Contains("Q3 review", model, StringComparison.Ordinal);
        Assert.Contains("Salary revision", result.RenderForModel(DataClass.Confidential), StringComparison.Ordinal);

        var block = Assert.Single(result.Blocks);
        Assert.Contains(block.Rows[0], v => v == "Salary revision attached");
    }

    [Fact]
    public void Links_are_shown_to_the_employee_but_not_sent_to_the_model()
    {
        var files = JsonSerializer.SerializeToNode(new[] { new { Name = "Plan.docx", WebUrl = "https://contoso.sharepoint.com/plan", SizeBytes = 2048, IsFolder = false } });

        var result = _presenter.Present("get_my_files", "t1", files, "", null);

        Assert.DoesNotContain("sharepoint", result.RenderForModel(DataClass.Confidential), StringComparison.Ordinal);
        var block = Assert.Single(result.Blocks);
        Assert.Contains(block.Columns, c => c.Format == FieldFormat.Link);
        Assert.DoesNotContain(block.Columns, c => c.Key == "IsFolder");
        Assert.Contains("2 KB", result.RenderForModel(DataClass.Confidential), StringComparison.Ordinal);
    }

    [Fact]
    public void Object_with_nested_parts_becomes_one_block_per_part()
    {
        var org = JsonSerializer.SerializeToNode(new
        {
            Manager = new { DisplayName = "Ravi", JobTitle = "Director" },
            DirectReports = new[] { new { DisplayName = "Meera", JobTitle = "Engineer" } }
        });

        var blocks = _presenter.Present("get_my_org", "t2", org, "", null).Blocks;

        Assert.Equal(["t2", "t2.2"], blocks.Select(b => b.Id));
        Assert.Equal(AnswerBlockKind.KeyValue, blocks[0].Kind);
        Assert.Equal("Manager", blocks[0].Title);
        Assert.Equal(AnswerBlockKind.Table, blocks[1].Kind);
    }

    [Fact]
    public void Unstructured_text_has_no_blocks_and_is_passed_to_the_model()
    {
        var result = _presenter.Present("zoho_listholidays", "t1", null, "No holidays configured for your location.", Zoho);

        Assert.Empty(result.Blocks);
        Assert.True(result.IsUnstructured);
        Assert.Contains("No holidays configured", result.RenderForModel(DataClass.Internal), StringComparison.Ordinal);
    }

    [Fact]
    public void Empty_list_is_shown_as_no_records()
    {
        var block = Assert.Single(_presenter.Present("get_my_tasks", "t1", new JsonArray(), "", null).Blocks);
        Assert.Equal(AnswerBlockKind.Text, block.Kind);
        Assert.Equal("No records found.", block.Text);
    }

    [Fact]
    public void Grounding_values_include_totals_and_counts()
    {
        var data = JsonNode.Parse("""[{"type":"Casual","days":2},{"type":"Casual","days":1.5},{"type":"Sick","days":1}]""");

        var values = _presenter.Present("zoho_getLeaveRecords", "t1", data, "", Zoho).GroundingValues().ToList();

        Assert.Contains("4.5", values); // sum of days
        Assert.Contains("3", values);   // row count
        Assert.Contains("2", values);   // two "Casual" rows
    }

    [Theory]
    [InlineData("LastModifiedAt", "Last modified at")]
    [InlineData("leave_type", "Leave type")]
    [InlineData("fromDate", "From date")]
    public void Field_names_are_humanised(string key, string label) => Assert.Equal(label, ValueFormatter.Humanize(key));

    [Fact]
    public void Leading_zero_codes_stay_text()
    {
        var data = JsonNode.Parse("""[{"code":"007","count":"12"}]""");
        var block = Assert.Single(_presenter.Present("zoho_x", "t1", data, "", null).Blocks);
        Assert.Equal(FieldFormat.Text, block.Columns.Single(c => c.Key == "code").Format);
        Assert.Equal(FieldFormat.Number, block.Columns.Single(c => c.Key == "count").Format);
    }
}
