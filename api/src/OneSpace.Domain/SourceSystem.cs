namespace OneSpace.Domain;

/// <summary>Systems OneSpace reads from. Shown to employees as answer attribution (FR-1).</summary>
public enum SourceSystem
{
    ZohoPeople,
    ComplyKaro,
    Microsoft365,
    OneSpace
}

public static class SourceSystemExtensions
{
    public static string DisplayName(this SourceSystem system) => system switch
    {
        SourceSystem.ZohoPeople => "Zoho People",
        SourceSystem.ComplyKaro => "ComplyKaro",
        SourceSystem.Microsoft365 => "Microsoft 365",
        _ => "OneSpace"
    };
}
