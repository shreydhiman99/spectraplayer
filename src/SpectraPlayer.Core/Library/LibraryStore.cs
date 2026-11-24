using System.Globalization;
using Microsoft.Data.Sqlite;
using SpectraPlayer.Core.Transcription;

namespace SpectraPlayer.Core.Library;

public sealed record TabRecord(long Id, string Title, string Folder, int Order, string? LastVideo);

public sealed record ProgressRecord(string Path, TimeSpan Position, TimeSpan Duration)
{
    /// <summary>How far through the video, 0–1.</summary>
    public double Fraction => Duration > TimeSpan.Zero ? Math.Clamp(Position / Duration, 0, 1) : 0;

    /// <summary>Counts as watched once 90% has played (credits and outros don't have to be sat through).</summary>
    public bool IsWatched => Fraction >= 0.9;
}

/// <summary>A stored video length. A zero duration means the file was tried and has no readable length.</summary>
public sealed record DurationRecord(string Path, long Size, long ModifiedTicks, TimeSpan Duration);

/// <summary>
/// The user's library in one SQLite file: which folders are open as tabs, per-video resume positions, and a few settings.
/// Playlists themselves are not stored; they are read from the folder each time, so they never go stale.
/// </summary>
public sealed class LibraryStore : IDisposable
{
    private const int SchemaVersion = 4;

    private readonly SqliteConnection _db;
    private readonly object _gate = new();

    /// <param name="dataSource">A file path, or ":memory:" for tests.</param>
    public LibraryStore(string dataSource)
    {
        if (dataSource != ":memory:")
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(dataSource));
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        }

        _db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = dataSource, Pooling = false }.ToString());
        _db.Open();

        Execute("PRAGMA journal_mode=WAL");
        Execute("PRAGMA synchronous=NORMAL");
        Migrate();
    }

    private void Migrate()
    {
        var version = Convert.ToInt32(Scalar("PRAGMA user_version"), CultureInfo.InvariantCulture);

        // Each step upgrades from the version before it, so a database from any release reaches the current schema.
        if (version < 1) Execute("""
            CREATE TABLE IF NOT EXISTS tabs (
                id          INTEGER PRIMARY KEY AUTOINCREMENT,
                title       TEXT    NOT NULL,
                folder      TEXT    NOT NULL UNIQUE COLLATE NOCASE,
                sort_order  INTEGER NOT NULL,
                last_video  TEXT
            );
            CREATE TABLE IF NOT EXISTS progress (
                path         TEXT    PRIMARY KEY COLLATE NOCASE,
                position_ms  INTEGER NOT NULL,
                duration_ms  INTEGER NOT NULL,
                updated_utc  TEXT    NOT NULL
            );
            CREATE TABLE IF NOT EXISTS settings (
                key    TEXT PRIMARY KEY,
                value  TEXT NOT NULL
            );
            """);

        if (version < 2) Execute("""
            CREATE TABLE IF NOT EXISTS notes (
                id           INTEGER PRIMARY KEY AUTOINCREMENT,
                video_path   TEXT    NOT NULL COLLATE NOCASE,
                start_ms     INTEGER NOT NULL,
                end_ms       INTEGER,
                text         TEXT    NOT NULL,
                source       TEXT    NOT NULL DEFAULT 'manual',
                created_utc  TEXT    NOT NULL,
                updated_utc  TEXT    NOT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_notes_video ON notes (video_path, start_ms);
            """);

        if (version < 3) Execute("""
            CREATE TABLE IF NOT EXISTS transcript_segments (
                id           INTEGER PRIMARY KEY AUTOINCREMENT,
                video_path   TEXT    NOT NULL COLLATE NOCASE,
                model        TEXT    NOT NULL,
                start_ms     INTEGER NOT NULL,
                end_ms       INTEGER NOT NULL,
                text         TEXT    NOT NULL,
                created_utc  TEXT    NOT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_segments ON transcript_segments (video_path, model, start_ms);
            CREATE TABLE IF NOT EXISTS transcribed_ranges (
                id          INTEGER PRIMARY KEY AUTOINCREMENT,
                video_path  TEXT    NOT NULL COLLATE NOCASE,
                model       TEXT    NOT NULL,
                start_ms    INTEGER NOT NULL,
                end_ms      INTEGER NOT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_ranges ON transcribed_ranges (video_path, model);
            """);

        if (version < 4) Execute("""
            CREATE TABLE IF NOT EXISTS media_durations (
                path           TEXT    PRIMARY KEY COLLATE NOCASE,
                size_bytes     INTEGER NOT NULL,
                modified_ticks INTEGER NOT NULL,
                duration_ms    INTEGER NOT NULL
            );
            """);

        if (version < SchemaVersion) Execute($"PRAGMA user_version = {SchemaVersion}");
    }

    // ---- video lengths ---------------------------------------------------------------------------------------

    /// <summary>Stores how long a file is, together with the size and modification time it had then (to spot a changed file).</summary>
    public void SaveDuration(string path, long sizeBytes, long modifiedTicks, TimeSpan duration) =>
        Execute("""
            INSERT INTO media_durations (path, size_bytes, modified_ticks, duration_ms) VALUES ($p, $s, $m, $d)
            ON CONFLICT(path) DO UPDATE SET size_bytes = $s, modified_ticks = $m, duration_ms = $d
            """,
            ("$p", Path.GetFullPath(path)), ("$s", sizeBytes), ("$m", modifiedTicks), ("$d", (long)duration.TotalMilliseconds));

    /// <summary>All stored lengths for files under the folder, keyed by full path (case-insensitive).</summary>
    public IReadOnlyDictionary<string, DurationRecord> GetDurationsUnder(string folder)
    {
        var prefix = NormalizeFolder(folder) + Path.DirectorySeparatorChar;
        lock (_gate)
        {
            using var cmd = Command(
                "SELECT path, size_bytes, modified_ticks, duration_ms FROM media_durations WHERE substr(path, 1, $n) = $prefix COLLATE NOCASE",
                ("$n", prefix.Length), ("$prefix", prefix));
            using var reader = cmd.ExecuteReader();
            var map = new Dictionary<string, DurationRecord>(StringComparer.OrdinalIgnoreCase);
            while (reader.Read())
                map[reader.GetString(0)] = new DurationRecord(reader.GetString(0), reader.GetInt64(1), reader.GetInt64(2),
                    TimeSpan.FromMilliseconds(reader.GetInt64(3)));
            return map;
        }
    }

    // ---- transcripts -----------------------------------------------------------------------------------------

    /// <summary>
    /// Stores freshly recognised segments and records that <paramref name="covered"/> has now been transcribed
    /// (even if it held no speech, so it is never redone). Overlapping or touching covered ranges are merged.
    /// </summary>
    public void AddTranscribed(string videoPath, string model, TimeRange covered,
        IReadOnlyList<(TimeSpan Start, TimeSpan End, string Text)> segments)
    {
        var path = Path.GetFullPath(videoPath);
        var now = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
        lock (_gate)
        {
            using var tx = _db.BeginTransaction();

            foreach (var s in segments)
            {
                using var cmd = Command("""
                    INSERT INTO transcript_segments (video_path, model, start_ms, end_ms, text, created_utc)
                    VALUES ($p, $m, $s, $e, $t, $now)
                    """,
                    ("$p", path), ("$m", model), ("$s", (long)s.Start.TotalMilliseconds), ("$e", (long)s.End.TotalMilliseconds),
                    ("$t", s.Text), ("$now", now));
                cmd.Transaction = tx;
                cmd.ExecuteNonQuery();
            }

            var ranges = ReadRanges(path, model, tx);
            ranges.Add(covered);
            using (var del = Command("DELETE FROM transcribed_ranges WHERE video_path = $p AND model = $m", ("$p", path), ("$m", model)))
            {
                del.Transaction = tx;
                del.ExecuteNonQuery();
            }
            foreach (var r in TimeRanges.Merge(ranges))
            {
                using var ins = Command("INSERT INTO transcribed_ranges (video_path, model, start_ms, end_ms) VALUES ($p, $m, $s, $e)",
                    ("$p", path), ("$m", model), ("$s", (long)r.Start.TotalMilliseconds), ("$e", (long)r.End.TotalMilliseconds));
                ins.Transaction = tx;
                ins.ExecuteNonQuery();
            }

            tx.Commit();
        }
    }

    /// <summary>The parts of a video already transcribed with this model, merged and in order.</summary>
    public IReadOnlyList<TimeRange> GetTranscribedRanges(string videoPath, string model)
    {
        lock (_gate) return TimeRanges.Merge(ReadRanges(Path.GetFullPath(videoPath), model, null));
    }

    /// <summary>Stored segments that overlap [start, end), in time order.</summary>
    public IReadOnlyList<TranscriptSegment> GetTranscript(string videoPath, string model, TimeSpan start, TimeSpan end)
    {
        lock (_gate)
        {
            using var cmd = Command("""
                SELECT id, video_path, model, start_ms, end_ms, text FROM transcript_segments
                WHERE video_path = $p AND model = $m AND end_ms > $s AND start_ms < $e
                ORDER BY start_ms, id
                """,
                ("$p", Path.GetFullPath(videoPath)), ("$m", model),
                ("$s", (long)start.TotalMilliseconds), ("$e", (long)end.TotalMilliseconds));
            using var r = cmd.ExecuteReader();
            var list = new List<TranscriptSegment>();
            while (r.Read())
                list.Add(new TranscriptSegment(r.GetInt64(0), r.GetString(1), r.GetString(2),
                    TimeSpan.FromMilliseconds(r.GetInt64(3)), TimeSpan.FromMilliseconds(r.GetInt64(4)), r.GetString(5)));
            return list;
        }
    }

    /// <summary>True if a stored segment ends exactly at <paramref name="time"/>: a natural place for new text to begin.</summary>
    public bool HasSegmentEndingAt(string videoPath, string model, TimeSpan time)
    {
        lock (_gate)
        {
            using var cmd = Command(
                "SELECT 1 FROM transcript_segments WHERE video_path = $p AND model = $m AND end_ms = $t LIMIT 1",
                ("$p", Path.GetFullPath(videoPath)), ("$m", model), ("$t", (long)time.TotalMilliseconds));
            return cmd.ExecuteScalar() is not null;
        }
    }

    /// <summary>Forgets a video's transcript (for one model, or all models) so it can be redone.</summary>
    public void ClearTranscript(string videoPath, string? model = null)
    {
        var path = Path.GetFullPath(videoPath);
        lock (_gate)
        {
            using var tx = _db.BeginTransaction();
            foreach (var table in new[] { "transcript_segments", "transcribed_ranges" })
            {
                using var cmd = model is null
                    ? Command($"DELETE FROM {table} WHERE video_path = $p", ("$p", path))
                    : Command($"DELETE FROM {table} WHERE video_path = $p AND model = $m", ("$p", path), ("$m", model));
                cmd.Transaction = tx;
                cmd.ExecuteNonQuery();
            }
            tx.Commit();
        }
    }

    private List<TimeRange> ReadRanges(string fullPath, string model, SqliteTransaction? tx)
    {
        using var cmd = Command("SELECT start_ms, end_ms FROM transcribed_ranges WHERE video_path = $p AND model = $m",
            ("$p", fullPath), ("$m", model));
        cmd.Transaction = tx;
        using var r = cmd.ExecuteReader();
        var list = new List<TimeRange>();
        while (r.Read())
            list.Add(new TimeRange(TimeSpan.FromMilliseconds(r.GetInt64(0)), TimeSpan.FromMilliseconds(r.GetInt64(1))));
        return list;
    }

    // ---- notes -----------------------------------------------------------------------------------------------

    public Note AddNote(string videoPath, TimeSpan start, string text, TimeSpan? end = null, string source = Note.ManualSource)
    {
        var path = Path.GetFullPath(videoPath);
        var now = DateTime.UtcNow;
        lock (_gate)
        {
            using var cmd = Command("""
                INSERT INTO notes (video_path, start_ms, end_ms, text, source, created_utc, updated_utc)
                VALUES ($p, $s, $e, $t, $src, $now, $now)
                """,
                ("$p", path), ("$s", (long)start.TotalMilliseconds), ("$e", end is { } e ? (long)e.TotalMilliseconds : null),
                ("$t", text.Trim()), ("$src", source), ("$now", now.ToString("o", CultureInfo.InvariantCulture)));
            cmd.ExecuteNonQuery();
            var id = Convert.ToInt64(Scalar("SELECT last_insert_rowid()"), CultureInfo.InvariantCulture);
            return new Note(id, path, start, end, text.Trim(), source, now, now);
        }
    }

    /// <summary>Notes for one video, in time order.</summary>
    public IReadOnlyList<Note> GetNotes(string videoPath)
    {
        lock (_gate)
        {
            using var cmd = Command($"{NoteSelect} WHERE video_path = $p ORDER BY start_ms, id", ("$p", Path.GetFullPath(videoPath)));
            return ReadNotes(cmd);
        }
    }

    /// <summary>Notes for every video under a folder, grouped by video path (each group in time order).</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<Note>> GetNotesUnder(string folder)
    {
        var prefix = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder)) + Path.DirectorySeparatorChar;
        lock (_gate)
        {
            using var cmd = Command(
                $"{NoteSelect} WHERE substr(video_path, 1, $n) = $prefix COLLATE NOCASE ORDER BY video_path, start_ms, id",
                ("$n", prefix.Length), ("$prefix", prefix));
            var map = new Dictionary<string, IReadOnlyList<Note>>(StringComparer.OrdinalIgnoreCase);
            foreach (var group in ReadNotes(cmd).GroupBy(n => n.VideoPath, StringComparer.OrdinalIgnoreCase))
                map[group.Key] = group.ToList();
            return map;
        }
    }

    /// <returns>The note as stored after the edit, or null if it no longer exists.</returns>
    public Note? UpdateNoteText(long id, string text)
    {
        lock (_gate)
        {
            using (var cmd = Command("UPDATE notes SET text = $t, updated_utc = $now WHERE id = $id",
                       ("$t", text.Trim()), ("$now", DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture)), ("$id", id)))
            {
                if (cmd.ExecuteNonQuery() == 0) return null;
            }
            using var read = Command($"{NoteSelect} WHERE id = $id", ("$id", id));
            return ReadNotes(read).FirstOrDefault();
        }
    }

    public void DeleteNote(long id) => Execute("DELETE FROM notes WHERE id = $id", ("$id", id));

    private const string NoteSelect =
        "SELECT id, video_path, start_ms, end_ms, text, source, created_utc, updated_utc FROM notes";

    private static List<Note> ReadNotes(SqliteCommand cmd)
    {
        using var r = cmd.ExecuteReader();
        var list = new List<Note>();
        while (r.Read())
            list.Add(new Note(
                r.GetInt64(0), r.GetString(1), TimeSpan.FromMilliseconds(r.GetInt64(2)),
                r.IsDBNull(3) ? null : TimeSpan.FromMilliseconds(r.GetInt64(3)),
                r.GetString(4), r.GetString(5),
                DateTime.Parse(r.GetString(6), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                DateTime.Parse(r.GetString(7), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)));
        return list;
    }

    // ---- tabs ------------------------------------------------------------------------------------------------

    public IReadOnlyList<TabRecord> GetTabs()
    {
        lock (_gate)
        {
            using var cmd = Command("SELECT id, title, folder, sort_order, last_video FROM tabs ORDER BY sort_order, id");
            using var reader = cmd.ExecuteReader();
            var list = new List<TabRecord>();
            while (reader.Read())
                list.Add(new TabRecord(reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetInt32(3),
                                       reader.IsDBNull(4) ? null : reader.GetString(4)));
            return list;
        }
    }

    /// <summary>Adds a tab for the folder. If that folder already has a tab, that one is returned instead.</summary>
    public TabRecord AddTab(string folder, string? title = null)
    {
        var full = NormalizeFolder(folder);
        lock (_gate)
        {
            var existing = GetTabs().FirstOrDefault(t => string.Equals(t.Folder, full, StringComparison.OrdinalIgnoreCase));
            if (existing is not null) return existing;

            var order = Convert.ToInt32(Scalar("SELECT COALESCE(MAX(sort_order), -1) + 1 FROM tabs"), CultureInfo.InvariantCulture);
            var name = string.IsNullOrWhiteSpace(title) ? DefaultTitle(full) : title.Trim();

            using var cmd = Command("INSERT INTO tabs (title, folder, sort_order) VALUES ($t, $f, $o)",
                ("$t", name), ("$f", full), ("$o", order));
            cmd.ExecuteNonQuery();
            var id = Convert.ToInt64(Scalar("SELECT last_insert_rowid()"), CultureInfo.InvariantCulture);
            return new TabRecord(id, name, full, order, null);
        }
    }

    public void RemoveTab(long id) => Execute("DELETE FROM tabs WHERE id = $id", ("$id", id));

    public void RenameTab(long id, string title) =>
        Execute("UPDATE tabs SET title = $t WHERE id = $id", ("$t", title.Trim()), ("$id", id));

    public void SetLastVideo(long tabId, string? path) =>
        Execute("UPDATE tabs SET last_video = $p WHERE id = $id", ("$p", path), ("$id", tabId));

    /// <summary>Stores the given tab ids' order (first = leftmost).</summary>
    public void ReorderTabs(IEnumerable<long> idsInOrder)
    {
        lock (_gate)
        {
            using var tx = _db.BeginTransaction();
            var order = 0;
            foreach (var id in idsInOrder)
            {
                using var cmd = Command("UPDATE tabs SET sort_order = $o WHERE id = $id", ("$o", order++), ("$id", id));
                cmd.Transaction = tx;
                cmd.ExecuteNonQuery();
            }
            tx.Commit();
        }
    }

    // ---- progress --------------------------------------------------------------------------------------------

    public void SaveProgress(string path, TimeSpan position, TimeSpan duration) =>
        Execute("""
            INSERT INTO progress (path, position_ms, duration_ms, updated_utc) VALUES ($p, $pos, $dur, $t)
            ON CONFLICT(path) DO UPDATE SET position_ms = $pos, duration_ms = $dur, updated_utc = $t
            """,
            ("$p", Path.GetFullPath(path)), ("$pos", (long)position.TotalMilliseconds),
            ("$dur", (long)duration.TotalMilliseconds), ("$t", Now()));

    /// <summary>Marks a video watched even if it was never played (its real duration is unknown until it is).</summary>
    public void MarkWatched(string path) =>
        Execute("""
            INSERT INTO progress (path, position_ms, duration_ms, updated_utc) VALUES ($p, 1, 1, $t)
            ON CONFLICT(path) DO UPDATE SET position_ms = duration_ms, updated_utc = $t
            """,
            ("$p", Path.GetFullPath(path)), ("$t", Now()));

    public void ClearProgress(string path) =>
        Execute("DELETE FROM progress WHERE path = $p", ("$p", Path.GetFullPath(path)));

    public ProgressRecord? GetProgress(string path)
    {
        lock (_gate)
        {
            using var cmd = Command("SELECT path, position_ms, duration_ms FROM progress WHERE path = $p",
                ("$p", Path.GetFullPath(path)));
            using var reader = cmd.ExecuteReader();
            return reader.Read() ? ReadProgress(reader) : null;
        }
    }

    /// <summary>All progress records for files under the folder, keyed by full path (case-insensitive).</summary>
    public IReadOnlyDictionary<string, ProgressRecord> GetProgressUnder(string folder)
    {
        var prefix = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder)) + Path.DirectorySeparatorChar;
        lock (_gate)
        {
            using var cmd = Command(
                "SELECT path, position_ms, duration_ms FROM progress WHERE substr(path, 1, $n) = $prefix COLLATE NOCASE",
                ("$n", prefix.Length), ("$prefix", prefix));
            using var reader = cmd.ExecuteReader();
            var map = new Dictionary<string, ProgressRecord>(StringComparer.OrdinalIgnoreCase);
            while (reader.Read())
            {
                var rec = ReadProgress(reader);
                map[rec.Path] = rec;
            }
            return map;
        }
    }

    // ---- settings --------------------------------------------------------------------------------------------

    public string? GetSetting(string key)
    {
        lock (_gate)
        {
            using var cmd = Command("SELECT value FROM settings WHERE key = $k", ("$k", key));
            return cmd.ExecuteScalar() as string;
        }
    }

    public void SetSetting(string key, string value) =>
        Execute("INSERT INTO settings (key, value) VALUES ($k, $v) ON CONFLICT(key) DO UPDATE SET value = $v",
            ("$k", key), ("$v", value));

    public void Dispose() => _db.Dispose();

    // ---- helpers ---------------------------------------------------------------------------------------------

    private static ProgressRecord ReadProgress(SqliteDataReader r) =>
        new(r.GetString(0), TimeSpan.FromMilliseconds(r.GetInt64(1)), TimeSpan.FromMilliseconds(r.GetInt64(2)));

    private static string NormalizeFolder(string folder) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));

    private static string DefaultTitle(string fullFolder)
    {
        var name = Path.GetFileName(fullFolder);
        return string.IsNullOrEmpty(name) ? fullFolder : name; // a drive root has no folder name
    }

    private static string Now() => DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);

    private SqliteCommand Command(string sql, params (string Name, object? Value)[] args)
    {
        var cmd = _db.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in args) cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return cmd;
    }

    private void Execute(string sql, params (string Name, object? Value)[] args)
    {
        lock (_gate)
        {
            using var cmd = Command(sql, args);
            cmd.ExecuteNonQuery();
        }
    }

    private object? Scalar(string sql)
    {
        lock (_gate)
        {
            using var cmd = Command(sql);
            return cmd.ExecuteScalar();
        }
    }
}
