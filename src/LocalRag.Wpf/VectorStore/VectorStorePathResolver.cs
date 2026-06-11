using System.IO;

namespace LocalRag.Wpf.VectorStore;

public static class VectorStorePathResolver
{
    public static string ResolveDatabasePath(string path)
    {
        var expanded = ExpandEnvironmentVariables(path);
        if (!Path.IsPathRooted(expanded))
        {
            expanded = Path.Combine(AppContext.BaseDirectory, expanded);
        }

        return Path.GetFullPath(expanded);
    }

    public static string ResolveExtensionPath(string path)
    {
        var expanded = ExpandEnvironmentVariables(path);
        if (!Path.IsPathRooted(expanded))
        {
            expanded = Path.Combine(AppContext.BaseDirectory, expanded);
        }

        return Path.GetFullPath(expanded);
    }

    private static string ExpandEnvironmentVariables(string value)
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Environment.ExpandEnvironmentVariables(value.Replace("%LOCALAPPDATA%", localAppData, StringComparison.OrdinalIgnoreCase));
    }
}
