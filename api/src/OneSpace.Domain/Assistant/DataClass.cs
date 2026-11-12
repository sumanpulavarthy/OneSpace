namespace OneSpace.Domain.Assistant;

/// <summary>
/// Sensitivity of data a tool returns (FR-7), in increasing order. Each LLM provider is configured with the
/// highest class it may receive; tools above it are not offered and fields above it are masked.
/// </summary>
public enum DataClass
{
    /// <summary>Public information.</summary>
    Public = 0,

    /// <summary>Company-internal but not about a person (policies, holiday calendar, team names).</summary>
    Internal = 1,

    /// <summary>About the employee (leave, profile, calendar, mail metadata).</summary>
    Personal = 2,

    /// <summary>Free text written by people (mail and chat previews, leave reasons).</summary>
    Confidential = 3
}
