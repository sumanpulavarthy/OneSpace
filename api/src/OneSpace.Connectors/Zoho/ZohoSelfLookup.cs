using System.Text.Json.Nodes;

namespace OneSpace.Connectors.Zoho;

/// <summary>
/// Finds the caller's erecno in Zoho People's attendance user report
/// (<c>/people/api/attendance/getUserReport</c>, verified live 2026-11-12). Each entry carries
/// <c>employeeDetails: { "mail id", "erecno", ... }</c>. A regular employee's token returns only their own entry;
/// an admin's returns everyone, so the entry must match the caller's verified (Entra) email.
/// </summary>
internal static class ZohoSelfLookup
{
    public static string ReportPath(DateOnly day)
    {
        var d = day.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        return $"/people/api/attendance/getUserReport?sdate={d}&edate={d}&dateFormat=yyyy-MM-dd";
    }

    public static long? FindErecno(JsonNode? report, string email)
    {
        if (report?["result"] is not JsonArray entries) return null;

        var employees = entries
            .Select(e => e?["employeeDetails"] as JsonObject)
            .Where(e => e is not null)
            .Select(e => (Mail: e!["mail id"]?.GetValue<string>(), Erecno: e["erecno"]?.GetValue<string>()))
            .Where(e => long.TryParse(e.Erecno, out _))
            .ToList();

        var match = employees.FirstOrDefault(e => string.Equals(e.Mail?.Trim(), email.Trim(), StringComparison.OrdinalIgnoreCase));
        return match.Erecno is null ? null : long.Parse(match.Erecno, System.Globalization.CultureInfo.InvariantCulture);
    }
}
