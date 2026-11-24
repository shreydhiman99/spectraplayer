package com.neuspectra.spectraplayer.data

import android.content.ContentValues
import android.content.Context
import android.database.sqlite.SQLiteDatabase
import android.database.sqlite.SQLiteOpenHelper
import com.neuspectra.spectraplayer.core.MsRange
import com.neuspectra.spectraplayer.core.Note
import com.neuspectra.spectraplayer.core.TimeRanges
import com.neuspectra.spectraplayer.core.TranscriptSegment

/**
 * The user's library in one SQLite file: which folders are open as tabs, per-video resume positions, notes, bookmarks,
 * cached video lengths and a few settings. Playlists themselves are not stored; they are read from the folder each time,
 * so they never go stale. (Same shape as the Windows app's library.)
 */
class Library(context: Context, name: String? = "library.db") : SQLiteOpenHelper(context, name, null, SCHEMA_VERSION) {
    companion object {
        private const val SCHEMA_VERSION = 2
    }

    override fun onCreate(db: SQLiteDatabase) {
        db.execSQL(
            """CREATE TABLE tabs (
                id          INTEGER PRIMARY KEY AUTOINCREMENT,
                title       TEXT    NOT NULL,
                tree_uri    TEXT    NOT NULL UNIQUE,
                sort_order  INTEGER NOT NULL,
                last_video  TEXT
            )""",
        )
        db.execSQL(
            """CREATE TABLE progress (
                uri          TEXT    PRIMARY KEY,
                position_ms  INTEGER NOT NULL,
                duration_ms  INTEGER NOT NULL,
                updated      INTEGER NOT NULL
            )""",
        )
        db.execSQL("CREATE TABLE settings (key TEXT PRIMARY KEY, value TEXT NOT NULL)")
        db.execSQL(
            """CREATE TABLE notes (
                id          INTEGER PRIMARY KEY AUTOINCREMENT,
                video_uri   TEXT    NOT NULL,
                start_ms    INTEGER NOT NULL,
                text        TEXT    NOT NULL,
                created     INTEGER NOT NULL,
                updated     INTEGER NOT NULL
            )""",
        )
        db.execSQL("CREATE INDEX ix_notes_video ON notes (video_uri, start_ms)")
        db.execSQL(
            """CREATE TABLE bookmarks (
                id          INTEGER PRIMARY KEY AUTOINCREMENT,
                video_uri   TEXT    NOT NULL,
                position_ms INTEGER NOT NULL,
                name        TEXT    NOT NULL,
                raw         TEXT    NOT NULL DEFAULT ''
            )""",
        )
        db.execSQL("CREATE INDEX ix_bookmarks_video ON bookmarks (video_uri, position_ms)")
        db.execSQL(
            """CREATE TABLE media_durations (
                uri         TEXT    PRIMARY KEY,
                size_bytes  INTEGER NOT NULL,
                modified    INTEGER NOT NULL,
                duration_ms INTEGER NOT NULL
            )""",
        )
    }

    override fun onUpgrade(db: SQLiteDatabase, oldVersion: Int, newVersion: Int) {
        // Each step upgrades from the version before it, so a library from any release reaches the current schema.
        if (oldVersion < 2) createTranscriptTables(db)
    }

    private fun createTranscriptTables(db: SQLiteDatabase) {
        db.execSQL(
            """CREATE TABLE transcript_segments (
                id          INTEGER PRIMARY KEY AUTOINCREMENT,
                video_uri   TEXT    NOT NULL,
                model       TEXT    NOT NULL,
                start_ms    INTEGER NOT NULL,
                end_ms      INTEGER NOT NULL,
                text        TEXT    NOT NULL
            )""",
        )
        db.execSQL("CREATE INDEX ix_segments ON transcript_segments (video_uri, model, start_ms)")
        db.execSQL(
            """CREATE TABLE transcribed_ranges (
                id          INTEGER PRIMARY KEY AUTOINCREMENT,
                video_uri   TEXT    NOT NULL,
                model       TEXT    NOT NULL,
                start_ms    INTEGER NOT NULL,
                end_ms      INTEGER NOT NULL
            )""",
        )
        db.execSQL("CREATE INDEX ix_ranges ON transcribed_ranges (video_uri, model)")
    }

    // ---- transcripts -----------------------------------------------------------------------------------------

    fun getTranscribedRanges(videoUri: String, model: String): List<MsRange> = readableDatabase.rawQuery(
        "SELECT start_ms, end_ms FROM transcribed_ranges WHERE video_uri = ? AND model = ? ORDER BY start_ms", arrayOf(videoUri, model),
    ).use { c -> buildList { while (c.moveToNext()) add(MsRange(c.getLong(0), c.getLong(1))) } }

    fun getSegments(videoUri: String, model: String): List<TranscriptSegment> = readableDatabase.rawQuery(
        "SELECT id, start_ms, end_ms, text FROM transcript_segments WHERE video_uri = ? AND model = ? ORDER BY start_ms, id",
        arrayOf(videoUri, model),
    ).use { c -> buildList { while (c.moveToNext()) add(TranscriptSegment(c.getLong(0), c.getLong(1), c.getLong(2), c.getString(3))) } }

    /**
     * Stores freshly recognised segments and records that [covered] has now been transcribed (even if it held no speech, so
     * it is never redone). Overlapping or touching covered ranges are merged.
     */
    fun addTranscribed(videoUri: String, model: String, covered: MsRange, segments: List<Triple<Long, Long, String>>) {
        val db = writableDatabase
        db.beginTransaction()
        try {
            for ((start, end, text) in segments) {
                db.insertOrThrow("transcript_segments", null, ContentValues().apply {
                    put("video_uri", videoUri); put("model", model); put("start_ms", start); put("end_ms", end); put("text", text)
                })
            }
            val merged = TimeRanges.merge(getTranscribedRanges(videoUri, model) + covered)
            db.delete("transcribed_ranges", "video_uri = ? AND model = ?", arrayOf(videoUri, model))
            for (r in merged) {
                db.insertOrThrow("transcribed_ranges", null, ContentValues().apply {
                    put("video_uri", videoUri); put("model", model); put("start_ms", r.startMs); put("end_ms", r.endMs)
                })
            }
            db.setTransactionSuccessful()
        } finally {
            db.endTransaction()
        }
    }

    fun clearTranscript(videoUri: String, model: String) {
        val db = writableDatabase
        db.delete("transcript_segments", "video_uri = ? AND model = ?", arrayOf(videoUri, model))
        db.delete("transcribed_ranges", "video_uri = ? AND model = ?", arrayOf(videoUri, model))
    }

    // ---- tabs ------------------------------------------------------------------------------------------------

    fun getTabs(): List<TabRecord> = readableDatabase.rawQuery(
        "SELECT id, title, tree_uri, sort_order, last_video FROM tabs ORDER BY sort_order, id", null,
    ).use { c ->
        buildList { while (c.moveToNext()) add(TabRecord(c.getLong(0), c.getString(1), c.getString(2), c.getInt(3), c.getString(4))) }
    }

    /** Adds a folder as a tab (or returns the existing tab for that folder). */
    fun addTab(title: String, treeUri: String): TabRecord {
        getTabs().firstOrNull { it.treeUri == treeUri }?.let { return it }
        val order = (readableDatabase.rawQuery("SELECT COALESCE(MAX(sort_order), -1) + 1 FROM tabs", null).use { c ->
            if (c.moveToFirst()) c.getInt(0) else 0
        })
        val id = writableDatabase.insertOrThrow("tabs", null, ContentValues().apply {
            put("title", title); put("tree_uri", treeUri); put("sort_order", order)
        })
        return TabRecord(id, title, treeUri, order, null)
    }

    fun renameTab(id: Long, title: String) {
        writableDatabase.update("tabs", ContentValues().apply { put("title", title) }, "id = ?", arrayOf(id.toString()))
    }

    fun removeTab(id: Long) {
        writableDatabase.delete("tabs", "id = ?", arrayOf(id.toString()))
    }

    fun setLastVideo(tabId: Long, uri: String?) {
        writableDatabase.update("tabs", ContentValues().apply { put("last_video", uri) }, "id = ?", arrayOf(tabId.toString()))
    }

    // ---- settings --------------------------------------------------------------------------------------------

    fun getSetting(key: String): String? = readableDatabase.rawQuery("SELECT value FROM settings WHERE key = ?", arrayOf(key)).use { c ->
        if (c.moveToFirst()) c.getString(0) else null
    }

    fun setSetting(key: String, value: String) {
        writableDatabase.execSQL(
            "INSERT INTO settings (key, value) VALUES (?, ?) ON CONFLICT(key) DO UPDATE SET value = excluded.value", arrayOf(key, value),
        )
    }

    // ---- progress --------------------------------------------------------------------------------------------

    fun saveProgress(uri: String, positionMs: Long, durationMs: Long) {
        writableDatabase.execSQL(
            """INSERT INTO progress (uri, position_ms, duration_ms, updated) VALUES (?, ?, ?, ?)
               ON CONFLICT(uri) DO UPDATE SET position_ms = excluded.position_ms, duration_ms = excluded.duration_ms, updated = excluded.updated""",
            arrayOf<Any?>(uri, positionMs, durationMs, System.currentTimeMillis()),
        )
    }

    /** Marks a video watched even if it was never played (its real length may be unknown, so a tiny stand-in is used). */
    fun markWatched(uri: String, durationMs: Long) {
        val d = if (durationMs > 0) durationMs else 1
        saveProgress(uri, d, d)
    }

    fun clearProgress(uri: String) {
        writableDatabase.delete("progress", "uri = ?", arrayOf(uri))
    }

    fun getProgress(uri: String): ProgressRecord? = readableDatabase.rawQuery(
        "SELECT uri, position_ms, duration_ms FROM progress WHERE uri = ?", arrayOf(uri),
    ).use { c -> if (c.moveToFirst()) ProgressRecord(c.getString(0), c.getLong(1), c.getLong(2)) else null }

    fun getProgress(uris: Collection<String>): Map<String, ProgressRecord> {
        val map = HashMap<String, ProgressRecord>()
        for (chunk in uris.chunked(500)) {
            val marks = chunk.joinToString(",") { "?" }
            readableDatabase.rawQuery("SELECT uri, position_ms, duration_ms FROM progress WHERE uri IN ($marks)", chunk.toTypedArray()).use { c ->
                while (c.moveToNext()) map[c.getString(0)] = ProgressRecord(c.getString(0), c.getLong(1), c.getLong(2))
            }
        }
        return map
    }

    // ---- video lengths ---------------------------------------------------------------------------------------

    fun saveDuration(uri: String, size: Long, modified: Long, durationMs: Long) {
        writableDatabase.execSQL(
            """INSERT INTO media_durations (uri, size_bytes, modified, duration_ms) VALUES (?, ?, ?, ?)
               ON CONFLICT(uri) DO UPDATE SET size_bytes = excluded.size_bytes, modified = excluded.modified, duration_ms = excluded.duration_ms""",
            arrayOf<Any?>(uri, size, modified, durationMs),
        )
    }

    fun getDurations(uris: Collection<String>): Map<String, DurationRecord> {
        val map = HashMap<String, DurationRecord>()
        for (chunk in uris.chunked(500)) {
            val marks = chunk.joinToString(",") { "?" }
            readableDatabase.rawQuery(
                "SELECT uri, size_bytes, modified, duration_ms FROM media_durations WHERE uri IN ($marks)", chunk.toTypedArray(),
            ).use { c ->
                while (c.moveToNext()) map[c.getString(0)] = DurationRecord(c.getString(0), c.getLong(1), c.getLong(2), c.getLong(3))
            }
        }
        return map
    }

    // ---- notes -----------------------------------------------------------------------------------------------

    fun getNotes(videoUri: String): List<Note> = readableDatabase.rawQuery(
        "SELECT id, video_uri, start_ms, text, created FROM notes WHERE video_uri = ? ORDER BY start_ms, id", arrayOf(videoUri),
    ).use { c -> buildList { while (c.moveToNext()) add(Note(c.getLong(0), c.getString(1), c.getLong(2), c.getString(3), c.getLong(4))) } }

    fun getNotes(videoUris: Collection<String>): List<Note> {
        val all = ArrayList<Note>()
        for (chunk in videoUris.chunked(500)) {
            val marks = chunk.joinToString(",") { "?" }
            readableDatabase.rawQuery(
                "SELECT id, video_uri, start_ms, text, created FROM notes WHERE video_uri IN ($marks) ORDER BY start_ms, id", chunk.toTypedArray(),
            ).use { c -> while (c.moveToNext()) all += Note(c.getLong(0), c.getString(1), c.getLong(2), c.getString(3), c.getLong(4)) }
        }
        return all
    }

    fun addNote(videoUri: String, startMs: Long, text: String): Note {
        val now = System.currentTimeMillis()
        val id = writableDatabase.insertOrThrow("notes", null, ContentValues().apply {
            put("video_uri", videoUri); put("start_ms", startMs); put("text", text); put("created", now); put("updated", now)
        })
        return Note(id, videoUri, startMs, text, now)
    }

    fun updateNote(id: Long, text: String) {
        writableDatabase.update("notes", ContentValues().apply {
            put("text", text); put("updated", System.currentTimeMillis())
        }, "id = ?", arrayOf(id.toString()))
    }

    fun deleteNote(id: Long) {
        writableDatabase.delete("notes", "id = ?", arrayOf(id.toString()))
    }

    // ---- bookmarks -------------------------------------------------------------------------------------------

    fun getBookmarks(videoUri: String): List<BookmarkRecord> = readableDatabase.rawQuery(
        "SELECT id, video_uri, position_ms, name, raw FROM bookmarks WHERE video_uri = ? ORDER BY position_ms, id", arrayOf(videoUri),
    ).use { c ->
        buildList { while (c.moveToNext()) add(BookmarkRecord(c.getLong(0), c.getString(1), c.getLong(2), c.getString(3), c.getString(4))) }
    }

    fun addBookmark(videoUri: String, positionMs: Long, name: String, raw: String = ""): BookmarkRecord {
        val id = writableDatabase.insertOrThrow("bookmarks", null, ContentValues().apply {
            put("video_uri", videoUri); put("position_ms", positionMs); put("name", name); put("raw", raw)
        })
        return BookmarkRecord(id, videoUri, positionMs, name, raw)
    }

    fun renameBookmark(id: Long, name: String) {
        writableDatabase.update("bookmarks", ContentValues().apply { put("name", name) }, "id = ?", arrayOf(id.toString()))
    }

    fun deleteBookmark(id: Long) {
        writableDatabase.delete("bookmarks", "id = ?", arrayOf(id.toString()))
    }
}
