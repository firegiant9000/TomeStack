namespace TomeStack.AppService;

/// <summary>What <see cref="TomeStackApp.TakeDailyCopy"/> did (D77).</summary>
public enum DailyCopyOutcome { Written, AlreadyTaken, Unchanged, Off, NotEnoughSpace, Failed }

/// <param name="FileName">Relative to the data folder (<c>backups/daily/library-YYYY-MM-DD.tomestack.zip</c>): the copy written, or today's one already there.</param>
/// <param name="LeftOut">The codes of what a written copy left out (<c>backup.pdf-unreadable</c>, <c>backup.extension-unreadable</c>); no older copy was deleted then.</param>
public sealed record DailyCopyResult(DailyCopyOutcome Outcome, string? FileName, IReadOnlyList<string> LeftOut);

/// <param name="Copies">The daily copies kept, newest first, by file name only.</param>
/// <param name="Bytes">Their total size: each holds every managed PDF.</param>
public sealed record DailyCopiesState(bool On, IReadOnlyList<string> Copies, long Bytes);

public sealed partial class TomeStackApp
{
    /// <summary>
    /// D77: the day's automatic full backup, when due (<see cref="Packages.PackageService.TakeDailyCopy"/>). The shell calls it
    /// right after <see cref="Open"/>. It never throws: a failure, a skip for lack of space and anything a copy left out go to
    /// the error log (codes only), and opening goes on.
    /// </summary>
    public DailyCopyResult TakeDailyCopy()
    {
        DailyCopyResult result;
        try
        {
            result = _packages.TakeDailyCopy();
        }
        catch (Exception ex)
        {
            ErrorLog.Record(FileErrorLog.NewCorrelationId(), "library.dailyCopy", ex);
            return new(DailyCopyOutcome.Failed, null, []);
        }
        if (result.Outcome == DailyCopyOutcome.NotEnoughSpace)
            ErrorLog.Record(FileErrorLog.NewCorrelationId(), "library.dailyCopy", new IOException("Skipped: the drive would have less than 2 GB free afterwards."));
        else if (result.LeftOut.Count > 0)
            ErrorLog.Record(FileErrorLog.NewCorrelationId(), "library.dailyCopy", new InvalidDataException($"Written without: {string.Join(", ", result.LeftOut)}. The newest older copy holding each left-out file was kept."));
        return result;
    }

    public DailyCopiesState DailyCopies() => _packages.DailyCopies();

    public DailyCopiesState SetDailyCopies(bool on) => _packages.SetDailyCopies(on);
}
