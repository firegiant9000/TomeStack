namespace TomeStack.AppService.Extensions;

/// <summary>Installed extension files: <c>&lt;data dir&gt;/extensions/&lt;sha256&gt;.zip</c>, read-only (ADR-011 "Storage and backup").</summary>
internal static class ExtensionFiles
{
    public static string PathOf(string directory, string sha256) => Path.Combine(directory, $"{sha256}.zip");

    /// <summary>Writes the file once (a file with that hash is already the same bytes), through a <c>.partial</c> copy.</summary>
    public static void Write(string directory, string sha256, byte[] bytes)
    {
        Directory.CreateDirectory(directory);
        var path = PathOf(directory, sha256);
        if (File.Exists(path))
            return;
        File.WriteAllBytes(path + ".partial", bytes);
        File.Move(path + ".partial", path, overwrite: true);
        File.SetAttributes(path, FileAttributes.ReadOnly);
    }

    /// <summary>The file's bytes when it is there and still has its hash; null otherwise.</summary>
    public static byte[]? ReadIntact(string directory, string sha256)
    {
        try
        {
            var bytes = File.ReadAllBytes(PathOf(directory, sha256));
            return ExtensionReader.Sha256(bytes) == sha256 ? bytes : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
