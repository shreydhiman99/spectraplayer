package com.neuspectra.spectraplayer.core

/** A stretch of a video, in milliseconds: [startMs, endMs). */
data class MsRange(val startMs: Long, val endMs: Long) {
    val length: Long get() = endMs - startMs
    val isEmpty: Boolean get() = endMs <= startMs
}

/** What the speech recogniser heard in one stretch, with times relative to the start of the video. */
data class TranscriptSegment(val id: Long, val startMs: Long, val endMs: Long, val text: String) {
    val timeLabel: String get() = TimeFormat.format(startMs)
}

object TimeRanges {
    /** Sorts and joins ranges that overlap or touch. */
    fun merge(ranges: List<MsRange>): List<MsRange> {
        val result = mutableListOf<MsRange>()
        for (r in ranges.filter { !it.isEmpty }.sortedBy { it.startMs }) {
            val last = result.lastOrNull()
            if (last != null && r.startMs <= last.endMs) result[result.size - 1] = MsRange(last.startMs, maxOf(last.endMs, r.endMs))
            else result += r
        }
        return result
    }

    /** The parts of [wanted] not covered by [done]: what still has to be transcribed. */
    fun subtract(wanted: MsRange, done: List<MsRange>): List<MsRange> {
        val gaps = mutableListOf<MsRange>()
        var cursor = wanted.startMs
        for (d in merge(done)) {
            if (d.endMs <= cursor) continue
            if (d.startMs >= wanted.endMs) break
            if (d.startMs > cursor) gaps += MsRange(cursor, d.startMs)
            cursor = maxOf(cursor, d.endMs)
        }
        if (cursor < wanted.endMs) gaps += MsRange(cursor, wanted.endMs)
        return gaps
    }

    /** Cuts a range into pieces of at most [maxLengthMs]. */
    fun chunk(range: MsRange, maxLengthMs: Long): List<MsRange> {
        require(maxLengthMs > 0)
        val pieces = mutableListOf<MsRange>()
        var s = range.startMs
        while (s < range.endMs) {
            val e = minOf(range.endMs, s + maxLengthMs)
            pieces += MsRange(s, e)
            s = e
        }
        return pieces
    }
}

/**
 * Each chunk is recognised with a little extra audio on both sides, so words at the cut aren't garbled. A segment belongs
 * to the chunk its centre falls in, so a sentence that straddles two chunks is kept once, not twice or not at all.
 */
object SegmentFilter {
    const val PADDING_MS = 2_000L

    /** [windowStartMs] is where the audio handed to the recogniser began; the segment times are relative to it. */
    fun belongsTo(chunk: MsRange, windowStartMs: Long, segStartMs: Long, segEndMs: Long): Boolean {
        val centre = windowStartMs + (segStartMs + segEndMs) / 2
        return centre >= chunk.startMs && centre < chunk.endMs
    }
}

/** Parses the bridge's "t0|t1|text" lines. */
object RawSegments {
    fun parse(text: String): List<Triple<Long, Long, String>> =
        text.lineSequence().mapNotNull { line ->
            val first = line.indexOf('|')
            val second = if (first >= 0) line.indexOf('|', first + 1) else -1
            if (second < 0) return@mapNotNull null
            val t0 = line.substring(0, first).toLongOrNull() ?: return@mapNotNull null
            val t1 = line.substring(first + 1, second).toLongOrNull() ?: return@mapNotNull null
            val words = line.substring(second + 1).trim()
            if (words.isEmpty() || isNonSpeech(words)) null else Triple(t0, t1, words)
        }.toList()

    /** Whisper marks silence and noise with things like "[BLANK_AUDIO]" or "(music)": not words worth keeping. */
    private fun isNonSpeech(text: String): Boolean {
        val t = text.trim()
        return (t.startsWith("[") && t.endsWith("]")) || (t.startsWith("(") && t.endsWith(")")) || (t.startsWith("*") && t.endsWith("*"))
    }
}

/** Transcripts as Markdown or as a subtitle file, the same shapes as the Windows app's exports. */
object TranscriptExport {
    fun markdown(title: String, segments: List<TranscriptSegment>): String {
        val sb = StringBuilder()
        sb.append("# ").append(title.trim()).append(" — transcript\n\n")
        if (segments.isEmpty()) sb.append("_No transcript._\n")
        for (s in segments.sortedBy { it.startMs }) sb.append("- **[").append(s.timeLabel).append("]** ").append(s.text.trim()).append('\n')
        return sb.toString()
    }

    fun srt(segments: List<TranscriptSegment>): String {
        val sb = StringBuilder()
        for ((i, s) in segments.sortedBy { it.startMs }.withIndex()) {
            sb.append(i + 1).append('\n')
                .append(srtTime(s.startMs)).append(" --> ").append(srtTime(maxOf(s.endMs, s.startMs + 500))).append('\n')
                .append(s.text.trim()).append("\n\n")
        }
        return sb.toString()
    }

    fun srtTime(ms: Long): String {
        val t = maxOf(0L, ms)
        return "%02d:%02d:%02d,%03d".format(t / 3_600_000, t / 60_000 % 60, t / 1000 % 60, t % 1000)
    }
}
