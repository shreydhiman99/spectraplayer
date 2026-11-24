package com.neuspectra.spectraplayer.transcribe

import android.content.Context
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.currentCoroutineContext
import kotlinx.coroutines.ensureActive
import kotlinx.coroutines.withContext
import java.io.File
import java.io.IOException
import java.net.HttpURLConnection
import java.net.URL
import java.security.MessageDigest

/** A speech model the app knows how to fetch. Checksums and sizes are Hugging Face's published ones (the same as the Windows app). */
data class ModelInfo(
    val id: String,
    val title: String,
    val description: String,
    val fileName: String,
    val sizeBytes: Long,
    val sha256: String,
) {
    val url: String get() = "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/$fileName"
    val sizeText: String get() = "%.0f MB".format(sizeBytes / 1024.0 / 1024.0)
}

object WhisperModels {
    val base = ModelInfo(
        "base.en-q5_1", "Standard (base, English)", "Fast. The better choice on most phones.",
        "ggml-base.en-q5_1.bin", 59_721_011, "4baf70dd0d7c4247ba2b81fafd9c01005ac77c2f9ef064e00dcf195d0e2fdd2f",
    )
    val small = ModelInfo(
        "small.en-q5_1", "Better accuracy (small, English)", "More accurate on difficult audio, but several times slower.",
        "ggml-small.en-q5_1.bin", 190_098_681, "bfdff4894dcb76bbf647d56263ea2a96645423f1669176f4844a1bf8e478ad30",
    )
    val all = listOf(base, small)
    val default get() = base

    fun byId(id: String?): ModelInfo = all.firstOrNull { it.id == id } ?: default
}

/**
 * Keeps the speech model files in the app's private storage. It never downloads by itself: the caller must have asked the
 * person first. Downloads resume after an interruption, check free space, and are verified against the published checksum.
 */
class ModelManager(context: Context) {
    private val dir = File(context.filesDir, "models").apply { mkdirs() }

    fun fileFor(model: ModelInfo): File = File(dir, model.fileName)

    fun isInstalled(model: ModelInfo): Boolean = fileFor(model).let { it.exists() && it.length() == model.sizeBytes }

    fun delete(model: ModelInfo) {
        fileFor(model).delete()
        File(dir, model.fileName + ".part").delete()
    }

    /** Downloads the model (resuming a partial file). Cancel by cancelling the coroutine. */
    suspend fun download(model: ModelInfo, onProgress: (done: Long, total: Long) -> Unit) = withContext(Dispatchers.IO) {
        val target = fileFor(model)
        if (isInstalled(model)) return@withContext
        val part = File(dir, model.fileName + ".part")

        var have = if (part.exists()) part.length() else 0L
        if (have > model.sizeBytes) { part.delete(); have = 0 }
        val needed = model.sizeBytes - have
        if (dir.usableSpace < needed + 50L * 1024 * 1024) {
            throw IOException("Not enough free space: the ${model.sizeText} model needs room to download.")
        }

        val connection = (URL(model.url).openConnection() as HttpURLConnection).apply {
            connectTimeout = 20_000
            readTimeout = 30_000
            if (have > 0) setRequestProperty("Range", "bytes=$have-")
        }
        try {
            val code = connection.responseCode
            if (code == 200 && have > 0) { part.delete(); have = 0 } // the server ignored the Range request: start over
            else if (code != 200 && code != 206) throw IOException("The download failed (HTTP $code).")

            connection.inputStream.use { input ->
                java.io.FileOutputStream(part, /* append = */ have > 0).use { output ->
                    val buffer = ByteArray(64 * 1024)
                    var done = have
                    var lastReport = 0L
                    onProgress(done, model.sizeBytes)
                    while (true) {
                        currentCoroutineContext().ensureActive()
                        val n = input.read(buffer)
                        if (n < 0) break
                        output.write(buffer, 0, n)
                        done += n
                        val now = System.nanoTime()
                        if (now - lastReport > 150_000_000L) { lastReport = now; onProgress(done, model.sizeBytes) }
                    }
                    onProgress(done, model.sizeBytes)
                }
            }
        } finally {
            connection.disconnect()
        }

        if (part.length() != model.sizeBytes) throw IOException("The download was incomplete. Try again.")
        if (!sha256(part).equals(model.sha256, ignoreCase = true)) {
            part.delete()
            throw IOException("The downloaded model did not match its checksum, so it was discarded.")
        }
        target.delete()
        if (!part.renameTo(target)) throw IOException("Could not save the model.")
    }

    private suspend fun sha256(file: File): String {
        val digest = MessageDigest.getInstance("SHA-256")
        file.inputStream().use { input ->
            val buffer = ByteArray(256 * 1024)
            while (true) {
                currentCoroutineContext().ensureActive()
                val n = input.read(buffer)
                if (n < 0) break
                digest.update(buffer, 0, n)
            }
        }
        return digest.digest().joinToString("") { "%02x".format(it) }
    }
}
