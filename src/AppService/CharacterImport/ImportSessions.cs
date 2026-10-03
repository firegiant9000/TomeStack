namespace TomeStack.AppService.CharacterImport;

/// <summary>
/// The sheets <c>ddb.read</c> parsed, held in memory under one-use tokens (<c>features/ddb-pdf-import.md</c> "Tokens"):
/// at most <see cref="MaxLive"/> (a new one pushes out the oldest), each for <see cref="Lifetime"/> by the app's clock,
/// and none after <see cref="Clear"/> (the app's <c>Dispose</c>). Nothing is written to disk. <see cref="Peek"/> is for
/// previews (the token stays); <see cref="Take"/> spends it.
/// </summary>
internal sealed class ImportSessions(TimeProvider time)
{
    public const int MaxLive = 8;

    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(30);

    private readonly Lock _gate = new();

    /// <summary>Oldest first.</summary>
    private readonly List<(Guid Token, DdbSheet Sheet, DateTimeOffset ReadAt)> _live = [];

    public int Count
    {
        get
        {
            lock (_gate)
            {
                DropExpired();
                return _live.Count;
            }
        }
    }

    public Guid Add(DdbSheet sheet)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        lock (_gate)
        {
            DropExpired();
            while (_live.Count >= MaxLive)
                _live.RemoveAt(0);
            var token = Guid.NewGuid();
            _live.Add((token, sheet, time.GetUtcNow()));
            return token;
        }
    }

    public DdbSheet? Peek(Guid token)
    {
        lock (_gate)
        {
            DropExpired();
            return _live.FirstOrDefault(s => s.Token == token).Sheet;
        }
    }

    public DdbSheet? Take(Guid token)
    {
        lock (_gate)
        {
            DropExpired();
            var at = _live.FindIndex(s => s.Token == token);
            if (at < 0)
                return null;
            var sheet = _live[at].Sheet;
            _live.RemoveAt(at);
            return sheet;
        }
    }

    public bool Discard(Guid token) => Take(token) is not null;

    public void Clear()
    {
        lock (_gate)
            _live.Clear();
    }

    private void DropExpired()
    {
        var now = time.GetUtcNow();
        _live.RemoveAll(s => now - s.ReadAt >= Lifetime);
    }
}
