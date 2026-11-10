namespace OneSpace.ContractTests;

internal static class SpecPaths
{
    public static string Root { get; } = FindRepoRoot();

    public static string GraphQLSchema => Path.Combine(Root, "specs", "graphql", "schema.graphql");

    public static string ToolAllowlist => Path.Combine(Root, "specs", "mcp", "tool-allowlist.json");

    private static string FindRepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "specs"))) return dir.FullName;
        }
        throw new DirectoryNotFoundException("Could not find the repository's /specs folder.");
    }

    public static string Normalize(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal).Trim();
}
