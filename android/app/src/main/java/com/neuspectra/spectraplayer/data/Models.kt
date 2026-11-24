package com.neuspectra.spectraplayer.data

import kotlin.math.max
import kotlin.math.min

/** A subject: a folder shown as a playlist. [treeUri] is the folder the user picked (Android's Storage Access Framework). */
data class TabRecord(val id: Long, val title: String, val treeUri: String, val order: Int, val lastVideo: String?)

/** One video (or audio file) of a playlist. [uri] is a content:// document URI the player can open directly. */
data class MediaEntry(
    val uri: String,
    val name: String,
    /** Path inside the picked folder, '/'-separated, e.g. "Week 1/Lecture 2.mp4". */
    val relativePath: String,
    val size: Long,
    val modified: Long,
) {
    val title: String get() = name.substringBeforeLast('.', name)

    /** The subfolder the file sits in, relative to the picked folder; empty at the top level. */
    val subfolder: String get() = relativePath.substringBeforeLast('/', "")
}

data class ProgressRecord(val uri: String, val positionMs: Long, val durationMs: Long) {
    /** How far through the video, 0–1. */
    val fraction: Double get() = if (durationMs > 0) min(1.0, max(0.0, positionMs.toDouble() / durationMs)) else 0.0

    /** Counts as watched once 90% has played (credits and outros don't have to be sat through). */
    val isWatched: Boolean get() = fraction >= 0.9
}

/** A stored video length. A zero duration means the file was tried and has no readable length. */
data class DurationRecord(val uri: String, val size: Long, val modified: Long, val durationMs: Long)

data class BookmarkRecord(val id: Long, val videoUri: String, val positionMs: Long, val name: String, val raw: String)
