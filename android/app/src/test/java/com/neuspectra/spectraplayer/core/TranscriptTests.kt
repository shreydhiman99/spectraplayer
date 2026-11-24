package com.neuspectra.spectraplayer.core

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

private fun r(a: Long, b: Long) = MsRange(a, b)

class TimeRangesTest {
    @Test fun `overlapping and touching ranges are merged`() {
        assertEquals(listOf(r(0, 30), r(40, 50)), TimeRanges.merge(listOf(r(10, 30), r(0, 15), r(40, 50), r(50, 50))))
        assertEquals(listOf(r(0, 20)), TimeRanges.merge(listOf(r(0, 10), r(10, 20))))
    }

    @Test fun `subtract leaves only what is not done`() {
        assertEquals(listOf(r(0, 100)), TimeRanges.subtract(r(0, 100), emptyList()))
        assertEquals(listOf(r(0, 20), r(60, 100)), TimeRanges.subtract(r(0, 100), listOf(r(20, 60))))
        assertEquals(emptyList<MsRange>(), TimeRanges.subtract(r(10, 50), listOf(r(0, 100))))
        assertEquals(listOf(r(50, 100)), TimeRanges.subtract(r(0, 100), listOf(r(0, 50), r(30, 40))))
        assertEquals(listOf(r(0, 100)), TimeRanges.subtract(r(0, 100), listOf(r(200, 300))))
    }

    @Test fun `chunking never exceeds the maximum and covers everything`() {
        val pieces = TimeRanges.chunk(r(0, 250), 100)
        assertEquals(listOf(r(0, 100), r(100, 200), r(200, 250)), pieces)
        assertEquals(emptyList<MsRange>(), TimeRanges.chunk(r(5, 5), 100))
    }
}

class SegmentFilterTest {
    @Test fun `a segment belongs to the chunk its centre is in`() {
        val chunk = r(120_000, 240_000)
        val windowStart = 118_000L // two seconds of padding before the chunk
        // centre at 118_000 + 1_000 = 119_000: before the chunk, so the previous chunk keeps it
        assertFalse(SegmentFilter.belongsTo(chunk, windowStart, 0, 2_000))
        // centre at 118_000 + 3_000 = 121_000: inside
        assertTrue(SegmentFilter.belongsTo(chunk, windowStart, 2_000, 4_000))
        // centre exactly on the end belongs to the next chunk
        assertFalse(SegmentFilter.belongsTo(chunk, windowStart, 121_000, 123_000))
    }

    @Test fun `adjacent chunks keep a straddling segment exactly once`() {
        val a = r(0, 120_000)
        val b = r(120_000, 240_000)
        // a sentence from 119 s to 123 s (absolute), centre 121 s
        val windowA = 0L
        val windowB = 118_000L
        val inA = SegmentFilter.belongsTo(a, windowA, 119_000, 123_000)
        val inB = SegmentFilter.belongsTo(b, windowB, 119_000 - 118_000, 123_000 - 118_000)
        assertEquals(1, listOf(inA, inB).count { it })
    }
}

class ResampleTest {
    private fun resample(input: FloatArray, from: Int, to: Int) =
        com.neuspectra.spectraplayer.transcribe.AudioExtractor.resample(input, from, to)

    @Test fun `the same rate returns the input untouched`() {
        val input = floatArrayOf(0.1f, 0.2f)
        assertTrue(resample(input, 16_000, 16_000) === input)
    }

    @Test fun `going from 48 kHz to 16 kHz keeps a third of the samples and a constant level`() {
        val out = resample(FloatArray(48_000) { 0.5f }, 48_000, 16_000)
        assertEquals(16_000, out.size)
        assertTrue(out.all { kotlin.math.abs(it - 0.5f) < 1e-5f })
    }

    @Test fun `each output sample is the mean of the input it covers`() {
        val out = resample(floatArrayOf(0f, 3f, 6f, 9f, 3f, 3f), 48_000, 16_000)
        assertEquals(listOf(3f, 5f), out.toList())
    }

    @Test fun `a non-integer ratio like 44100 to 16000 gives the right length`() {
        assertEquals(16_000, resample(FloatArray(44_100), 44_100, 16_000).size)
    }

    @Test fun `empty input stays empty`() {
        assertEquals(0, resample(FloatArray(0), 44_100, 16_000).size)
    }
}

class RawSegmentsTest {
    @Test fun `parses lines and drops silence markers`() {
        val parsed = RawSegments.parse("0|2500| Hello there.\n2500|4000|[BLANK_AUDIO]\n4000|6000|(music)\n6000|9000|Entropy | rises.\n")
        assertEquals(listOf(Triple(0L, 2500L, "Hello there."), Triple(6000L, 9000L, "Entropy | rises.")), parsed)
    }

    @Test fun `junk lines are ignored`() {
        assertEquals(emptyList<Triple<Long, Long, String>>(), RawSegments.parse("\nnonsense\nx|y|z\n"))
    }
}

class TranscriptExportTest {
    private val segs = listOf(
        TranscriptSegment(2, 65_000, 70_000, "Second line."),
        TranscriptSegment(1, 5_000, 9_500, "First line."),
    )

    @Test fun `markdown is time ordered`() {
        assertEquals("# Lecture 1 — transcript\n\n- **[00:05]** First line.\n- **[01:05]** Second line.\n", TranscriptExport.markdown("Lecture 1", segs))
    }

    @Test fun `srt has numbered cues with comma milliseconds`() {
        assertEquals(
            "1\n00:00:05,000 --> 00:00:09,500\nFirst line.\n\n2\n00:01:05,000 --> 00:01:10,000\nSecond line.\n\n",
            TranscriptExport.srt(segs),
        )
    }

    @Test fun `an empty transcript says so`() {
        assertEquals("# V — transcript\n\n_No transcript._\n", TranscriptExport.markdown("V", emptyList()))
    }

    @Test fun `srt times roll over hours`() {
        assertEquals("01:02:03,004", TranscriptExport.srtTime(3_723_004))
    }
}
