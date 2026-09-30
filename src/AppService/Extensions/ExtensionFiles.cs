namespace TomeStack.AppService.Extensions;

/// <summary>Installed extension files: <c>&lt;data dir&gt;/extensions/&lt;sha256&gt;.zip</c>, read-only (ADR-011 "Storage and backup").</summary>
internal static class ExtensionFiles
{
    public static string PathOf(string directory, string sha256) => Path.Combine(directory, $"{sha256}.zip");

    /// <summary>
    /// Writes the file through a <c>.partial</c> copy, unless an intact one is there already. A damaged or changed copy is
    /// replaced (review fix: installing the same file again is how <c>extension.file-changed</c> is repaired).
    /// </summary>
    public static void Write(string directory, string sha256, byte[] bytes)
    {
        Directory.CreateDirectory(directory);
        var path = PathOf(directory, sha256);
        if (ReadIntact(directory, sha256) is not null)
            return;
        if (File.Exists(path))
            File.SetAttributes(path, FileAttributes.Normal);
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
