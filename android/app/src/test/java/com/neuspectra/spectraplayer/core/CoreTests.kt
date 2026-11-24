package com.neuspectra.spectraplayer.core

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Assert.fail
import org.junit.Test

class TimeFormatTest {
    @Test fun `format is m-ss under an hour and h-mm-ss after`() {
        assertEquals("00:00", TimeFormat.format(0))
        assertEquals("00:12", TimeFormat.format(12_000))
        assertEquals("12:34", TimeFormat.format((12 * 60 + 34) * 1000L))
        assertEquals("1:02:03", TimeFormat.format((3600 + 2 * 60 + 3) * 1000L))
        assertEquals("00:00", TimeFormat.format(-5))
    }

    @Test fun `clock always has three parts`() {
        assertEquals("00:00:00", TimeFormat.clock(0))
        assertEquals("00:00:12", TimeFormat.clock(12_000))
        assertEquals("00:01:00", TimeFormat.clock(59_600)) // rounds to the nearest second
        assertEquals("08:42:15", TimeFormat.clock((8 * 3600 + 42 * 60 + 15) * 1000L))
        assertEquals("100:00:00", TimeFormat.clock(100 * 3600 * 1000L))
        assertEquals("00:00:00", TimeFormat.clock(-5000))
    }

    @Test fun `total reads naturally`() {
        assertEquals("42 s", TimeFormat.total(42_000))
        assertEquals("1 min", TimeFormat.total(60_000))
        assertEquals("2 min", TimeFormat.total(90_000))
        assertEquals("42 min", TimeFormat.total(42 * 60_000L))
        assertEquals("1 h", TimeFormat.total((59 * 60 + 40) * 1000L))
        assertEquals("1 h 5 min", TimeFormat.total((3600 + 5 * 60) * 1000L))
        assertEquals("8 h 42 min", TimeFormat.total((8 * 3600 + 42 * 60) * 1000L))
    }
}

class NaturalComparerTest {
    private fun sorted(vararg names: String) = names.sortedWith(NaturalComparer)

    @Test fun `numbers compare by value`() {
        assertEquals(listOf("Lecture 2", "Lecture 10", "Lecture 11"), sorted("Lecture 10", "Lecture 11", "Lecture 2"))
    }

    @Test fun `case is ignored`() {
        assertEquals(listOf("alpha", "Beta", "gamma"), sorted("gamma", "Beta", "alpha"))
    }

    @Test fun `leading zeros keep a stable order`() {
        assertEquals(listOf("a1", "a01", "a2"), sorted("a01", "a2", "a1"))
    }

    @Test fun `a prefix sorts first`() {
        assertEquals(listOf("Week 1", "Week 1 notes"), sorted("Week 1 notes", "Week 1"))
    }
}

class PbfFileTest {
    @Test fun `round trips bookmarks including the opaque field`() {
        val original = listOf(PbfBookmark(65_000, "Definition", "abc123"), PbfBookmark(125_500, "Proof", ""))
        val parsed = PbfFile.parse(PbfFile.serialize(original))
        assertEquals(original, parsed)
    }

    @Test fun `parses what potplayer writes`() {
        val text = "[Bookmark]\r\n0=10000*Intro*\r\n1=90000*Key idea*thumb\r\n2=\r\n\r\n"
        val list = PbfFile.parse(text)
        assertEquals(2, list.size)
        assertEquals(PbfBookmark(10_000, "Intro", ""), list[0])
        assertEquals("thumb", list[1].raw)
    }

    @Test fun `bookmarks come out sorted by position and a blank name becomes the time`() {
        val list = PbfFile.parse("[Bookmark]\r\n0=90000**\r\n1=10000*First*\r\n")
        assertEquals(listOf(10_000L, 90_000L), list.map { it.positionMs })
        assertEquals("01:30", list[1].name)
    }

    @Test fun `a missing header is an error`() {
        try {
            PbfFile.parse("0=1000*x*")
            fail("expected an error")
        } catch (_: IllegalArgumentException) {
        }
    }

    @Test fun `asterisks in a name cannot break the layout`() {
        val text = PbfFile.serialize(listOf(PbfBookmark(1000, "a*b", "")))
        val parsed = PbfFile.parse(text)
        assertEquals(1, parsed.size)
        assertEquals("a＊b", parsed[0].name)
    }

    @Test fun `files are utf-16 with a byte order mark and decode back`() {
        val text = PbfFile.serialize(listOf(PbfBookmark(5000, "Résumé ✓", "")))
        val bytes = PbfFile.encode(text)
        assertEquals(0xFF.toByte(), bytes[0])
        assertEquals(0xFE.toByte(), bytes[1])
        assertEquals(text, PbfFile.decode(bytes))
    }

    @Test fun `utf-8 and bom-less utf-16 files also decode`() {
        val text = "[Bookmark]\r\n0=1000*x*\r\n"
        assertEquals(text, PbfFile.decode(text.toByteArray(Charsets.UTF_8)))
        assertEquals(text, PbfFile.decode(text.toByteArray(Charsets.UTF_16LE)))
    }
}

class NotesMarkdownTest {
    private fun note(id: Long, ms: Long, text: String) = Note(id, "content://x/1", ms, text)

    @Test fun `a video's notes become bullets in time order`() {
        val md = NotesMarkdown.forVideo("Lecture 1", listOf(note(2, 90_000, "later"), note(1, 5_000, "early")))
        assertEquals("# Lecture 1\n\n- **[00:05]** early\n- **[01:30]** later\n", md)
    }

    @Test fun `multi-line notes stay in one bullet`() {
        val md = NotesMarkdown.forVideo("V", listOf(note(1, 0, "first\nsecond\n\nthird")))
        assertEquals("# V\n\n- **[00:00]** first\n  second\n\n  third\n", md)
    }

    @Test fun `no notes says so`() {
        assertEquals("# V\n\n_No notes._\n", NotesMarkdown.forVideo("V", emptyList()))
    }

    @Test fun `a tab export groups by video and counts`() {
        val md = NotesMarkdown.forTab(
            "Physics",
            listOf(
                NotesSection("Lecture 1", null, listOf(note(1, 1000, "a"))),
                NotesSection("Lecture 2", "Week 1", listOf(note(2, 2000, "b"), note(3, 3000, "c"))),
                NotesSection("Lecture 3", null, emptyList()),
            ),
        )
        assertTrue(md.startsWith("# Physics — notes\n\n_3 notes in 2 videos_\n\n## Lecture 1\n\n- **[00:01]** a\n"))
        assertTrue(md.contains("## Lecture 2\n_Week 1_\n"))
        assertFalse(md.contains("Lecture 3"))
    }
}

class FocusTimerTest {
    private var day = java.time.LocalDate.of(2026, 11, 24)
    private val ended = mutableListOf<FocusPhase>()

    private fun timer() = FocusTimer { day }.also { t -> t.onPartEnded = { ended += it } }
    private fun min(m: Int) = m * 60_000L

    @Test fun `starts stopped at the full length of the first preset`() {
        val t = timer()
        assertEquals(FocusPhase.Focus, t.phase)
        assertFalse(t.running)
        assertEquals(min(25), t.remainingMs)
        assertEquals(1500, t.secondsLeft)
        assertFalse(t.isActive)
        assertEquals("Session 1 of 4", t.sessionText)
    }

    @Test fun `time only passes while running`() {
        val t = timer()
        t.tick(min(3))
        assertEquals(min(25), t.remainingMs)
        t.start(); t.tick(min(3))
        assertEquals(min(22), t.remainingMs)
        t.pause(); t.tick(min(3))
        assertEquals(min(22), t.remainingMs)
        assertTrue(t.isActive)
    }

    @Test fun `a finished focus session counts and starts the break`() {
        val t = timer()
        t.start(); t.tick(min(25))
        assertEquals(listOf(FocusPhase.Focus), ended)
        assertEquals(FocusPhase.Break, t.phase)
        assertTrue(t.running)
        assertEquals(min(5), t.remainingMs)
        assertEquals(1, t.completedSessions)
        assertEquals(1, t.sessionsToday)
        assertEquals("Break after session 1 of 4", t.sessionText)
    }

    @Test fun `only focus time is credited to today`() {
        val t = timer()
        t.start(); t.tick(min(25)); t.tick(min(5)); t.tick(min(10))
        assertEquals(min(35), t.focusedTodayMs)
    }

    @Test fun `a round is four sessions then it starts over`() {
        val t = timer()
        t.start()
        for (i in 1..4) {
            t.tick(min(25))
            assertEquals(i, t.completedSessions)
            if (i < 4) t.tick(min(5))
        }
        assertEquals("Break after session 4 of 4", t.sessionText)
        t.tick(min(5))
        assertEquals(0, t.completedSessions)
        assertEquals("Session 1 of 4", t.sessionText)
        assertEquals(4, t.sessionsToday)
    }

    @Test fun `skipping does not count a session`() {
        val t = timer()
        t.start(); t.tick(min(10)); t.skip()
        assertEquals(FocusPhase.Break, t.phase)
        assertEquals(0, t.completedSessions)
        assertEquals(0, t.sessionsToday)
        assertTrue(t.running)
        assertTrue(ended.isEmpty())
        assertEquals(min(10), t.focusedTodayMs)
    }

    @Test fun `choosing a length starts a fresh stopped session`() {
        val t = timer()
        t.start(); t.tick(min(25))
        t.selectPreset(1)
        assertEquals(1, t.presetIndex)
        assertEquals(FocusPhase.Focus, t.phase)
        assertFalse(t.running)
        assertEquals(min(50), t.remainingMs)
        t.selectPreset(99)
        assertEquals(3, t.presetIndex)
    }

    @Test fun `today starts again after midnight`() {
        val t = timer()
        t.start(); t.tick(min(25))
        assertEquals(1, t.sessionsToday)
        day = day.plusDays(1)
        t.tick(min(2))
        assertEquals(0, t.sessionsToday)
        assertEquals(0L, t.focusedTodayMs)
    }

    @Test fun `choices and totals survive a restart but the timer itself starts stopped`() {
        val t = timer()
        t.selectPreset(2)
        t.pauseVideoOnBreaks = false; t.chimeWhenPartEnds = false; t.countOnlyWhilePlaying = true
        t.start(); t.tick(min(90))

        val again = timer()
        again.loadJson(t.toJson())
        assertEquals(2, again.presetIndex)
        assertFalse(again.pauseVideoOnBreaks)
        assertFalse(again.chimeWhenPartEnds)
        assertTrue(again.countOnlyWhilePlaying)
        assertEquals(min(90), again.focusedTodayMs)
        assertEquals(1, again.sessionsToday)
        assertFalse(again.running)
        assertEquals(min(90), again.remainingMs)
    }

    @Test fun `unreadable saved data means the defaults`() {
        for (json in listOf(null, "", "not json", "[]")) {
            val t = timer()
            t.selectPreset(3)
            t.loadJson(json)
            assertEquals(0, t.presetIndex)
            assertTrue(t.pauseVideoOnBreaks)
            assertEquals(0L, t.focusedTodayMs)
        }
    }

    @Test fun `totals from another day are not restored`() {
        val t = timer()
        t.selectPreset(1); t.start(); t.tick(min(50))
        val json = t.toJson()
        day = day.plusDays(3)
        val later = timer()
        later.loadJson(json)
        assertEquals(1, later.presetIndex)
        assertEquals(0L, later.focusedTodayMs)
        assertEquals(0, later.sessionsToday)
    }
}
