using System.Security.Cryptography;

namespace TomeStack.AppService.Persistence;

/// <summary>A PDF that cannot be attached: not a PDF, too large, or unreadable. The message never contains a path.</summary>
public sealed class AttachmentException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

/// <summary>
/// ADR-005: managed copies live in <c>&lt;data dir&gt;/attachments/&lt;sha256&gt;.pdf</c>, de-duplicated by content and
/// read-only. Files are untrusted input (SPEC Q-02): only the PDF signature and the size are checked here, and the PDF is
/// never parsed or executed; the shell's viewer renders it.
/// </summary>
public static class AttachmentFiles
{
    /// <summary>Largest PDF TomeStack copies or links (a whole rulebook fits; the size is shown before copying).</summary>
    public const long MaxPdfBytes = 1L << 30;

    public const int MaxFileNameLength = 255;

    private static readonly byte[] PdfSignature = "%PDF-"u8.ToArray();

    /// <summary>Copies a PDF into the managed library (or finds the identical copy already there) and records it.</summary>
    public static Attachment ImportManaged(SqliteStore store, Stream content, string originalFileName, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(content);
        var name = SafeFileName(originalFileName);
        Directory.CreateDirectory(store.AttachmentsDirectory);
        var temporary = Path.Combine(store.AttachmentsDirectory, $"{Guid.NewGuid():N}.partial");
        string sha256;
        long length;
        try
        {
            using (var output = File.Create(temporary))
                (sha256, length) = CopyChecked(content, output);
            var target = ManagedPath(store, sha256);
            if (File.Exists(target))
                File.Delete(temporary);
            else
            {
                File.Move(temporary, target);
                File.SetAttributes(target, FileAttributes.ReadOnly);
            }
        }
        catch
        {
            TryDelete(temporary);
            throw;
        }

        if (store.FindManagedAttachment(sha256) is { } existing)
            return existing;
        var attachment = new Attachment(Guid.NewGuid(), sha256, name, length, AttachmentMode.Managed, null, now);
        store.AddAttachment(attachment);
        return attachment;
    }

    /// <summary>Records a PDF left where it is (ADR-005 option B), with its hash to detect later changes.</summary>
    public static Attachment Link(SqliteStore store, string path, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(store);
        var (sha256, length) = HashFile(path);
        var attachment = new Attachment(Guid.NewGuid(), sha256, SafeFileName(Path.GetFileName(path)), length, AttachmentMode.Linked, Path.GetFullPath(path), now);
        store.AddAttachment(attachment);
        return attachment;
    }

    /// <summary>
    /// Database migration v3: a legacy <c>pdfRef</c> becomes a managed copy when it is a readable PDF within the limit,
    /// and otherwise a linked record without a hash, which the UI shows as "missing; re-attach". Never throws for a
    /// missing or unreadable file, so the migration cannot fail because of one.
    /// </summary>
    internal static Attachment AdoptLegacyPath(SqliteStore store, string path, DateTimeOffset now)
    {
        try
        {
            if (File.Exists(path))
            {
                using var stream = File.OpenRead(path);
                return ImportManaged(store, stream, Path.GetFileName(path), now);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or AttachmentException or ArgumentException or NotSupportedException)
        {
            // fall through: recorded as linked and missing
        }
        if (store.FindLinkedAttachment(path) is { } same)
            return same;
        var fileName = SafeFileNameOrDefault(path);
        var attachment = new Attachment(Guid.NewGuid(), null, fileName, 0, AttachmentMode.Linked, path, now);
        store.AddAttachment(attachment);
        return attachment;
    }

    public static string ManagedPath(SqliteStore store, string sha256) => Path.Combine(store.AttachmentsDirectory, $"{sha256}.pdf");

    /// <summary>The file to open for an attachment: the managed copy, or the linked path.</summary>
    public static string PathOf(SqliteStore store, Attachment attachment) =>
        attachment.Mode == AttachmentMode.Managed ? ManagedPath(store, attachment.Sha256!) : attachment.LinkedPath!;

    /// <summary>Hashes a file after checking the signature and the size limit.</summary>
    public static (string Sha256, long Length) HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return CopyChecked(stream, Stream.Null);
    }

    /// <summary>Deletes a managed file when no attachment record uses its hash any more.</summary>
    public static void DeleteManagedIfUnused(SqliteStore store, string sha256)
    {
        if (store.AttachmentsWithHash(sha256) > 0)
            return;
        var path = ManagedPath(store, sha256);
        if (!File.Exists(path))
            return;
        File.SetAttributes(path, FileAttributes.Normal);
        File.Delete(path);
    }

    private static (string Sha256, long Length) CopyChecked(Stream input, Stream output)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
        {
            // The signature may arrive over several reads: check each byte of it at its offset.
            for (var i = 0; i < read && total + i < PdfSignature.Length; i++)
            {
                if (buffer[i] != PdfSignature[(int)total + i])
                    throw new AttachmentException("attachment.not-a-pdf", "The file is not a PDF (it does not start with the PDF signature).");
            }
            total += read;
            if (total > MaxPdfBytes)
                throw new AttachmentException("attachment.too-large", $"The PDF is larger than {MaxPdfBytes / (1024 * 1024)} MB.");
            hash.AppendData(buffer, 0, read);
            output.Write(buffer, 0, read);
        }
        if (total < PdfSignature.Length)
            throw new AttachmentException("attachment.not-a-pdf", "The file is not a PDF (it is empty or too short).");
        return (Convert.ToHexStringLower(hash.GetHashAndReset()), total);
    }

    private static string SafeFileName(string name)
    {
        var fileName = Path.GetFileName(name ?? "").Trim();
        if (fileName.Length is 0 or > MaxFileNameLength || fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new AttachmentException("attachment.file-name-invalid", "The PDF needs a file name of 1 to 255 valid characters.");
        return fileName;
    }

    private static string SafeFileNameOrDefault(string path)
    {
        try { return SafeFileName(Path.GetFileName(path)); }
        catch (Exception ex) when (ex is AttachmentException or ArgumentException) { return "attachment.pdf"; }
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* best effort */ }
    }
}
