package com.neuspectra.spectraplayer.core

import kotlin.math.max
import kotlin.math.roundToLong

/** Times as people read them. Everything is in milliseconds. */
object TimeFormat {
    /** "12:34", or "1:02:03" from an hour up (minutes and seconds always two digits). */
    fun format(ms: Long): String {
        val total = max(0L, ms) / 1000
        val h = total / 3600
        val m = (total % 3600) / 60
        val s = total % 60
        return if (h >= 1) "%d:%02d:%02d".format(h, m, s) else "%02d:%02d".format(m, s)
    }

    /** Always three parts: "08:42:15" (hours grow past two digits if they have to). */
    fun clock(ms: Long): String {
        val total = (max(0L, ms) / 1000.0).roundToLong()
        return "%02d:%02d:%02d".format(total / 3600, (total % 3600) / 60, total % 60)
    }

    /** "42 s", "42 min", "8 h", "8 h 42 min". Rounded to the nearest minute from one minute up. */
    fun total(ms: Long): String {
        if (ms < 60_000) return "${max(0L, (ms / 1000.0).roundToLong())} s"
        val minutes = (ms / 60_000.0).roundToLong()
        val hours = minutes / 60
        val rest = minutes % 60
        return when {
            hours == 0L -> "$rest min"
            rest == 0L -> "$hours h"
            else -> "$hours h $rest min"
        }
    }
}
