package com.neuspectra.spectraplayer.transcribe

import com.neuspectra.spectraplayer.core.RawSegments
import kotlinx.coroutines.CancellationException
import java.io.IOException

/** The JNI functions of the whisper.cpp bridge (see app/src/main/cpp/whisper_jni.cpp). */
object WhisperNative {
    init {
        System.loadLibrary("spectra_whisper")
    }

    external fun nInit(path: String): Long
    external fun nFree(handle: Long)
    external fun nCancel(handle: Long)
    external fun nProgress(handle: Long): Int
    external fun nTranscribe(handle: Long, samples: FloatArray, threads: Int, language: String): ByteArray?
    external fun nSystemInfo(): String
}

/** One loaded speech model. Not thread-safe: one transcription at a time; [cancel] and [progress] may be called from anywhere. */
class WhisperEngine(modelPath: String) : AutoCloseable {
    private var handle: Long = WhisperNative.nInit(modelPath)

    @Volatile
    private var cancelRequested = false

    init {
        if (handle == 0L) throw IOException("The speech model could not be loaded. Remove it and download it again.")
    }

    /** Percent (0–100) of the current [transcribe] call. */
    @Synchronized
    fun progress(): Int = if (handle == 0L) 0 else WhisperNative.nProgress(handle)

    /** Asks a running [transcribe] to stop. Safe to call from any thread, and after [close] (then it does nothing). */
    @Synchronized
    fun cancel() {
        cancelRequested = true
        if (handle != 0L) WhisperNative.nCancel(handle)
    }

    /**
     * Recognises 16 kHz mono samples. Blocking and CPU heavy: call it off the main thread.
     * Returns (start ms, end ms, text) relative to the start of [samples]; silence and noise markers are dropped.
     */
    fun transcribe(samples: FloatArray, threads: Int = defaultThreads()): List<Triple<Long, Long, String>> {
        check(handle != 0L) { "The engine was closed." }
        cancelRequested = false
        val bytes = WhisperNative.nTranscribe(handle, samples, threads, "en")
        if (bytes == null) {
            if (cancelRequested) throw CancellationException("Transcription was cancelled.")
            throw IOException("The speech recogniser failed.")
        }
        return RawSegments.parse(String(bytes, Charsets.UTF_8))
    }

    @Synchronized
    override fun close() {
        if (handle != 0L) {
            WhisperNative.nFree(handle)
            handle = 0
        }
    }

    companion object {
        /** Most phones have 4 big cores; more threads than that mostly add heat. */
        fun defaultThreads(): Int = Runtime.getRuntime().availableProcessors().coerceIn(2, 4)
    }
}
