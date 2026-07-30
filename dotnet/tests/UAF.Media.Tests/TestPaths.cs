namespace UAF.Media.Tests;

/// <summary>Locates the committed test fixtures next to the test binary.</summary>
internal static class TestPaths
{
    public static string Asset(string name) =>
        Path.Combine(AppContext.BaseDirectory, "Assets", name);

    /// <summary>
    /// Writes bytes to a uniquely named temporary file and returns a handle that deletes it.
    /// </summary>
    /// <remarks>
    /// The loaders take paths rather than streams because that is what the engine has — a filename out
    /// of a design record — so testing them means putting bytes on disk. Unique names keep the tests
    /// safe to run in parallel.
    /// </remarks>
    public static TempFile Temp(string extension, byte[] contents)
    {
        string path = Path.Combine(Path.GetTempPath(),
                                   $"uaf-media-{Guid.NewGuid():N}{extension}");
        File.WriteAllBytes(path, contents);
        return new TempFile(path);
    }

    internal sealed class TempFile(string path) : IDisposable
    {
        public string Path { get; } = path;

        public void Dispose()
        {
            try
            {
                File.Delete(Path);
            }
            catch (IOException)
            {
                // A leaked temp file is not worth failing a test over.
            }
        }
    }
}
