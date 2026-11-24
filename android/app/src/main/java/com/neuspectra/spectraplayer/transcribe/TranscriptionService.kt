package com.neuspectra.spectraplayer.transcribe

import android.content.Context
import android.net.Uri
import com.neuspectra.spectraplayer.core.MsRange
import com.neuspectra.spectraplayer.core.SegmentFilter
import com.neuspectra.spectraplayer.core.TimeRanges
import com.neuspectra.spectraplayer.data.Library
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.awaitCancellation
import kotlinx.coroutines.coroutineScope
import kotlinx.coroutines.currentCoroutineContext
import kotlinx.coroutines.delay
import kotlinx.coroutines.ensureActive
import kotlinx.coroutines.isActive
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext

/**
 * Turns the speech in a stretch of a video into text, on the phone. The stretch is done in chunks of two minutes, each
 * with a little extra audio on both sides; every finished chunk is saved straight away, so cancelling (or the app being
 * closed) loses at most the chunk in progress, and a stretch already done is never redone.
 */
class TranscriptionService(private val context: Context, private val library: Library, private val models: ModelManager) {
    companion object {
        const val CHUNK_MS = 120_000L
    }

    /**
     * @param onProgress overall progress 0–1 and a short description of what is happening.
     * @throws NoAudioException if the video has no audio track
     */
    suspend fun transcribe(
        videoUri: String,
        range: MsRange,
        durationMs: Long,
        model: ModelInfo,
        onProgress: (Float, String) -> Unit,
    ) = withContext(Dispatchers.Default) {
        val done = library.getTranscribedRanges(videoUri, model.id)
        val chunks = TimeRanges.subtract(range, done).flatMap { TimeRanges.chunk(it, CHUNK_MS) }
        if (chunks.isEmpty()) {
            onProgress(1f, "Already transcribed")
            return@withContext
        }

        val uri = Uri.parse(videoUri)
        val job = currentCoroutineContext()[Job]!!
        val engine = WhisperEngine(models.fileFor(model).path)
        try {
            coroutineScope {
                // The recogniser runs in native code and can't see coroutine cancellation, so a watcher tells it to stop.
                val watcher = launch {
                    try {
                        awaitCancellation()
                    } finally {
                        engine.cancel()
                    }
                }

                for ((index, chunk) in chunks.withIndex()) {
                    ensureActive()
                    val overall = { within: Float -> (index + within) / chunks.size }
                    onProgress(overall(0f), "Reading the audio (${index + 1} of ${chunks.size})…")

                    val windowStart = maxOf(0L, chunk.startMs - SegmentFilter.PADDING_MS)
                    val windowEnd = minOf(if (durationMs > 0) durationMs else Long.MAX_VALUE, chunk.endMs + SegmentFilter.PADDING_MS)
                    val samples = AudioExtractor.extract(context, uri, windowStart, windowEnd) { !job.isActive }
                    if (samples.size < AudioExtractor.TARGET_RATE / 2) { // under half a second: nothing to recognise
                        library.addTranscribed(videoUri, model.id, chunk, emptyList())
                        continue
                    }

                    val heard = coroutineScope {
                        val ticker = launch {
                            while (isActive) {
                                onProgress(overall(0.1f + 0.9f * engine.progress() / 100f), "Transcribing (${index + 1} of ${chunks.size})…")
                                delay(300)
                            }
                        }
                        try {
                            engine.transcribe(samples)
                        } finally {
                            ticker.cancel()
                        }
                    }

                    val kept = heard
                        .filter { (t0, t1, _) -> SegmentFilter.belongsTo(chunk, windowStart, t0, t1) }
                        .map { (t0, t1, text) -> Triple(windowStart + t0, windowStart + t1, text) }
                    library.addTranscribed(videoUri, model.id, chunk, kept)
                }
                watcher.cancel()
            }
            onProgress(1f, "Done")
        } finally {
            engine.close()
        }
    }
}
