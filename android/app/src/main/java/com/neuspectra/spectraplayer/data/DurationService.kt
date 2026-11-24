package com.neuspectra.spectraplayer.data

import android.content.Context
import android.media.MediaMetadataRetriever
import android.net.Uri
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.currentCoroutineContext
import kotlinx.coroutines.ensureActive
import kotlinx.coroutines.withContext

/**
 * How long each video of a playlist is. Answers come from the library when the file is unchanged (same size and
 * modification time); the rest are measured one after another in the background and remembered, so a folder is only
 * ever measured once. A stored zero means "tried, no readable length".
 */
class DurationService(private val context: Context, private val library: Library) {

    /** What is already known for these files, keyed by URI. Files missing from the result still need measuring. */
    fun loadKnown(entries: List<MediaEntry>): Map<String, Long> {
        val stored = library.getDurations(entries.map { it.uri })
        val known = HashMap<String, Long>()
        for (e in entries) {
            val r = stored[e.uri] ?: continue
            if (r.size == e.size && r.modified == e.modified) known[e.uri] = r.durationMs
        }
        return known
    }

    /** Measures the files in order, storing each answer and reporting it as soon as it is known. Stops when cancelled. */
    suspend fun measure(entries: List<MediaEntry>, onMeasured: suspend (uri: String, durationMs: Long) -> Unit) {
        for (e in entries) {
            currentCoroutineContext().ensureActive()
            val ms = withContext(Dispatchers.IO) { probe(e.uri) } ?: 0L
            library.saveDuration(e.uri, e.size, e.modified, ms)
            onMeasured(e.uri, ms)
        }
    }

    /** The player has just learned a file's real length by playing it: keep it, no need to measure. */
    fun remember(entry: MediaEntry, durationMs: Long) {
        if (durationMs > 0) library.saveDuration(entry.uri, entry.size, entry.modified, durationMs)
    }

    private fun probe(uri: String): Long? {
        val retriever = MediaMetadataRetriever()
        return try {
            retriever.setDataSource(context, Uri.parse(uri))
            retriever.extractMetadata(MediaMetadataRetriever.METADATA_KEY_DURATION)?.toLongOrNull()?.takeIf { it > 0 }
        } catch (e: CancellationException) {
            throw e
        } catch (_: Exception) {
            null // unreadable: recorded as "no length"
        } finally {
            runCatching { retriever.release() }
        }
    }
}
