package com.neuspectra.spectraplayer.core

import org.json.JSONException
import org.json.JSONObject
import java.time.LocalDate
import kotlin.math.ceil
import kotlin.math.max
import kotlin.math.min

enum class FocusPhase { Focus, Break }

/** A timer length: [focusMinutes] of work, then [breakMinutes] of rest. */
data class FocusPreset(val focusMinutes: Int, val breakMinutes: Int) {
    val label: String get() = "$focusMinutes / $breakMinutes"
}

/**
 * A study-session timer: focus for a while, rest for a while, four sessions to a round. It knows nothing about clocks or
 * screens: whoever owns it calls [tick] with the time that has passed (and can simply stop calling it while the video is
 * paused, if the student wants only watched time to count), which also makes it easy to test.
 * (The same state machine as the Windows app's FocusTimer.)
 */
class FocusTimer(private val today: () -> LocalDate = { LocalDate.now() }) {
    companion object {
        const val SESSIONS_PER_ROUND = 4
        val presets = listOf(FocusPreset(25, 5), FocusPreset(50, 10), FocusPreset(90, 15), FocusPreset(40, 8))
    }

    private var day = today()

    var phase = FocusPhase.Focus
        private set
    var running = false
        private set
    var presetIndex = 0
        private set

    /** Focus sessions finished in the current round of four. */
    var completedSessions = 0
        private set

    var remainingMs = 0L
        private set

    var focusedTodayMs = 0L
        private set
    var sessionsToday = 0
        private set

    var pauseVideoOnBreaks = true
    var chimeWhenPartEnds = true
    var countOnlyWhilePlaying = false

    /** Called when a part runs out (with the phase that just ended); the next part has already begun, still running. */
    var onPartEnded: ((FocusPhase) -> Unit)? = null

    /** Called after every change, ticks included. */
    var onChanged: (() -> Unit)? = null

    init {
        remainingMs = partLengthMs
    }

    val preset: FocusPreset get() = presets[presetIndex]

    val partLengthMs: Long
        get() = (if (phase == FocusPhase.Focus) preset.focusMinutes else preset.breakMinutes) * 60_000L

    /** How much of the current part is left, 1 down to 0 (the ring's length). */
    val fractionLeft: Double get() = (remainingMs.toDouble() / partLengthMs).coerceIn(0.0, 1.0)

    /** Whole seconds left, rounded up, so the display reaches 00:00 exactly when the part ends. */
    val secondsLeft: Int get() = ceil(remainingMs / 1000.0 - 1e-9).toInt()

    /** Started and not back at the full length: worth showing on the video. */
    val isActive: Boolean get() = running || remainingMs < partLengthMs

    val sessionText: String
        get() = if (phase == FocusPhase.Focus) "Session ${min(SESSIONS_PER_ROUND, completedSessions + 1)} of $SESSIONS_PER_ROUND"
        else "Break after session $completedSessions of $SESSIONS_PER_ROUND"

    // ---- doing things ----------------------------------------------------------------------------------------

    fun start() = setRunning(true)
    fun pause() = setRunning(false)
    fun toggle() = setRunning(!running)

    private fun setRunning(value: Boolean) {
        if (running == value) return
        running = value
        changed()
    }

    /** Back to the full length of the current part, stopped. */
    fun reset() {
        running = false
        remainingMs = partLengthMs
        changed()
    }

    /** Jumps to the next part (focus → break → focus) without counting the one left behind as a finished session. */
    fun skip() {
        nextPart(finished = false)
        changed()
    }

    /** Picks a length; starts a fresh, stopped focus session with it. */
    fun selectPreset(index: Int) {
        presetIndex = index.coerceIn(0, presets.size - 1)
        phase = FocusPhase.Focus
        running = false
        remainingMs = partLengthMs
        changed()
    }

    /** Time has passed. Counts down while running; credits focus time to today; moves on when a part ends. */
    fun tick(elapsedMs: Long) {
        if (!running || elapsedMs <= 0) return
        rollDay()

        val used = min(elapsedMs, remainingMs)
        if (phase == FocusPhase.Focus) focusedTodayMs += used
        remainingMs -= elapsedMs

        if (remainingMs <= 0) {
            val ended = phase
            nextPart(finished = true)
            onPartEnded?.invoke(ended)
        }
        changed()
    }

    private fun nextPart(finished: Boolean) {
        if (phase == FocusPhase.Focus) {
            if (finished) {
                completedSessions = min(SESSIONS_PER_ROUND, completedSessions + 1)
                rollDay()
                sessionsToday++
            }
            phase = FocusPhase.Break
        } else {
            if (completedSessions >= SESSIONS_PER_ROUND) completedSessions = 0 // a round is done: start the next one
            phase = FocusPhase.Focus
        }
        remainingMs = partLengthMs
    }

    private fun rollDay() {
        val now = today()
        if (now == day) return
        day = now
        focusedTodayMs = 0
        sessionsToday = 0
    }

    private fun changed() {
        onChanged?.invoke()
    }

    // ---- saving ----------------------------------------------------------------------------------------------

    /** The length chosen, the options, and today's totals. (Whether it is running is not kept.) */
    fun toJson(): String = JSONObject()
        .put("preset", presetIndex)
        .put("pauseVideo", pauseVideoOnBreaks)
        .put("chime", chimeWhenPartEnds)
        .put("onlyWhilePlaying", countOnlyWhilePlaying)
        .put("day", day.toString())
        .put("ms", focusedTodayMs)
        .put("sessions", sessionsToday)
        .toString()

    /** Restores what [toJson] saved. Anything unreadable is ignored; totals from another day are dropped. */
    fun loadJson(json: String?) {
        running = false
        phase = FocusPhase.Focus
        completedSessions = 0
        presetIndex = 0
        pauseVideoOnBreaks = true
        chimeWhenPartEnds = true
        countOnlyWhilePlaying = false
        day = today()
        focusedTodayMs = 0
        sessionsToday = 0

        val saved = try {
            if (json.isNullOrBlank()) null else JSONObject(json)
        } catch (_: JSONException) {
            null
        }

        if (saved != null) {
            presetIndex = saved.optInt("preset", 0).coerceIn(0, presets.size - 1)
            pauseVideoOnBreaks = saved.optBoolean("pauseVideo", true)
            chimeWhenPartEnds = saved.optBoolean("chime", true)
            countOnlyWhilePlaying = saved.optBoolean("onlyWhilePlaying", false)
            val savedDay = runCatching { LocalDate.parse(saved.optString("day")) }.getOrNull()
            val ms = saved.optLong("ms", 0)
            val sessions = saved.optInt("sessions", 0)
            if (savedDay == day && ms >= 0 && sessions >= 0) {
                focusedTodayMs = ms
                sessionsToday = sessions
            }
        }

        remainingMs = partLengthMs
        changed()
    }
}
