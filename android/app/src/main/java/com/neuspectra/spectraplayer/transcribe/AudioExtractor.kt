package com.neuspectra.spectraplayer.transcribe

import android.content.Context
import android.media.AudioFormat
import android.media.MediaCodec
import android.media.MediaExtractor
import android.media.MediaFormat
import android.net.Uri
import kotlinx.coroutines.CancellationException
import java.nio.ByteBuffer
import java.nio.ByteOrder
import kotlin.math.ceil
import kotlin.math.floor
import kotlin.math.max
import kotlin.math.min

class NoAudioException : Exception("This video has no audio track to transcribe.")

/** Cuts a stretch of audio out of any media file as 16 kHz mono floats, the format speech recognition wants. */
object AudioExtractor {
    const val TARGET_RATE = 16_000

    /** Decodes [startMs, endMs) of the first audio track. An empty array means there was no audio in that stretch. */
    fun extract(context: Context, uri: Uri, startMs: Long, endMs: Long, isCancelled: () -> Boolean = { false }): FloatArray {
        val startUs = startMs * 1000
        val endUs = endMs * 1000
        val extractor = MediaExtractor()
        try {
            extractor.setDataSource(context, uri, null)
            val track = (0 until extractor.trackCount).firstOrNull {
                extractor.getTrackFormat(it).getString(MediaFormat.KEY_MIME)?.startsWith("audio/") == true
            } ?: throw NoAudioException()
            extractor.selectTrack(track)
            val format = extractor.getTrackFormat(track)
            extractor.seekTo(startUs, MediaExtractor.SEEK_TO_PREVIOUS_SYNC)

            val codec = MediaCodec.createDecoderByType(format.getString(MediaFormat.KEY_MIME)!!)
            try {
                codec.configure(format, null, null, 0)
                codec.start()
                return decode(extractor, codec, format, startUs, endUs, isCancelled)
            } finally {
                runCatching { codec.stop() }
                codec.release()
            }
        } finally {
            extractor.release()
        }
    }

    private fun decode(
        extractor: MediaExtractor, codec: MediaCodec, inputFormat: MediaFormat, startUs: Long, endUs: Long, isCancelled: () -> Boolean,
    ): FloatArray {
        var rate = inputFormat.getInteger(MediaFormat.KEY_SAMPLE_RATE)
        var channels = inputFormat.getInteger(MediaFormat.KEY_CHANNEL_COUNT)
        var floatPcm = false
        val mono = FloatList()
        val info = MediaCodec.BufferInfo()
        var inputDone = false
        var outputDone = false

        while (!outputDone) {
            if (isCancelled()) throw CancellationException("Transcription was cancelled.")

            if (!inputDone) {
                val i = codec.dequeueInputBuffer(10_000)
                if (i >= 0) {
                    val buffer = codec.getInputBuffer(i)!!
                    val size = extractor.readSampleData(buffer, 0)
                    val pts = extractor.sampleTime
                    if (size < 0 || pts > endUs) {
                        codec.queueInputBuffer(i, 0, 0, 0, MediaCodec.BUFFER_FLAG_END_OF_STREAM)
                        inputDone = true
                    } else {
                        codec.queueInputBuffer(i, 0, size, pts, 0)
                        extractor.advance()
                    }
                }
            }

            val o = codec.dequeueOutputBuffer(info, 10_000)
            when {
                o == MediaCodec.INFO_OUTPUT_FORMAT_CHANGED -> {
                    val f = codec.outputFormat
                    rate = f.getInteger(MediaFormat.KEY_SAMPLE_RATE)
                    channels = f.getInteger(MediaFormat.KEY_CHANNEL_COUNT)
                    floatPcm = f.containsKey(MediaFormat.KEY_PCM_ENCODING) &&
                        f.getInteger(MediaFormat.KEY_PCM_ENCODING) == AudioFormat.ENCODING_PCM_FLOAT
                }
                o >= 0 -> {
                    if (info.size > 0) appendMono(codec.getOutputBuffer(o)!!, info, rate, channels, floatPcm, startUs, endUs, mono)
                    codec.releaseOutputBuffer(o, false)
                    if (info.flags and MediaCodec.BUFFER_FLAG_END_OF_STREAM != 0) outputDone = true
                }
            }
        }
        return resample(mono.toArray(), rate, TARGET_RATE)
    }

    /** Adds the part of one decoded buffer that lies inside [startUs, endUs), mixed down to one channel. */
    private fun appendMono(
        buffer: ByteBuffer, info: MediaCodec.BufferInfo, rate: Int, channels: Int, floatPcm: Boolean,
        startUs: Long, endUs: Long, out: FloatList,
    ) {
        val bytesPerSample = if (floatPcm) 4 else 2
        val frames = info.size / (bytesPerSample * channels)
        if (frames <= 0) return

        // which frames of this buffer fall inside the wanted stretch (the decoder starts at the sync point before it)
        val first = if (info.presentationTimeUs < startUs) ceil((startUs - info.presentationTimeUs) * rate / 1e6).toInt() else 0
        val last = min(frames.toDouble(), floor((endUs - info.presentationTimeUs) * rate / 1e6)).toInt()
        if (first >= last) return

        buffer.position(info.offset)
        buffer.limit(info.offset + info.size)
        val data = buffer.slice().order(ByteOrder.nativeOrder())
        for (frame in first until last) {
            var sum = 0f
            for (c in 0 until channels) {
                val index = (frame * channels + c) * bytesPerSample
                sum += if (floatPcm) data.getFloat(index) else data.getShort(index) / 32768f
            }
            out.add(sum / channels)
        }
    }

    /**
     * Area-averaging resampler: each output sample is the mean of the input stretch it covers, which doubles as a simple
     * low-pass filter when going down (48 kHz → 16 kHz) so high frequencies don't fold back into speech.
     */
    fun resample(input: FloatArray, fromRate: Int, toRate: Int): FloatArray {
        if (input.isEmpty() || fromRate == toRate) return input
        val ratio = fromRate.toDouble() / toRate
        val outCount = floor(input.size / ratio).toInt()
        val out = FloatArray(outCount)
        for (k in 0 until outCount) {
            val from = k * ratio
            val to = from + ratio
            val i0 = floor(from).toInt()
            val i1 = min(input.size, ceil(to).toInt())
            var sum = 0.0
            var weight = 0.0
            for (i in i0 until i1) {
                val w = min(to, i + 1.0) - max(from, i.toDouble())
                sum += input[i] * w
                weight += w
            }
            out[k] = if (weight > 0) (sum / weight).toFloat() else 0f
        }
        return out
    }

    /** A growing float array without boxing. */
    private class FloatList {
        private var data = FloatArray(1 shl 16)
        private var size = 0

        fun add(v: Float) {
            if (size == data.size) data = data.copyOf(data.size * 2)
            data[size++] = v
        }

        fun toArray(): FloatArray = data.copyOf(size)
    }
}
