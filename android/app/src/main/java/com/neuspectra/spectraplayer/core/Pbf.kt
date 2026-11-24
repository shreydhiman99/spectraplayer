package com.neuspectra.spectraplayer.core

import java.nio.charset.Charset

/** A named position in a video. Mirrors one line of a PotPlayer .pbf file. */
data class PbfBookmark(
    val positionMs: Long,
    val name: String,
    /** Opaque third field of a .pbf line (PotPlayer stores a thumbnail/hash there); kept so a round trip loses nothing. */
    val raw: String = "",
)

/**
 * Reads and writes PotPlayer bookmark files (.pbf), the same format the Windows app uses.
 *
 * Layout (community-documented, not an official spec): UTF-16 text; first line `[Bookmark]`; then one line per
 * bookmark, `{index}={milliseconds}*{name}*{opaque}`, index starting at 0; then a closing `{count}=` line.
 */
object PbfFile {
    const val HEADER = "[Bookmark]"

    // Names cannot contain '*' (it is the field separator), so the name group excludes it.
    private val line = Regex("""^(\d+)=(\d+)\*([^*]*)\*(.*)$""")

    /** Parses .pbf text. Throws [IllegalArgumentException] when the header is missing. */
    fun parse(text: String): List<PbfBookmark> {
        val lines = text.split("\r\n", "\n", "\r")
        var i = 0
        while (i < lines.size && lines[i].isBlank()) i++
        require(i < lines.size && lines[i].trim().trimStart('﻿') == HEADER) {
            "Not a PotPlayer bookmark file: missing [Bookmark] header."
        }

        val result = mutableListOf<PbfBookmark>()
        for (n in i + 1 until lines.size) {
            val m = line.matchEntire(lines[n].trimEnd()) ?: continue // the "{count}=" sentinel, blank lines, anything unrecognised
            val ms = m.groupValues[2].toLongOrNull() ?: continue
            val name = m.groupValues[3].trim().ifEmpty { TimeFormat.format(ms) }
            result += PbfBookmark(ms, name, m.groupValues[4])
        }
        // sortedBy is stable, so bookmarks at the same position keep their file order.
        return result.sortedBy { it.positionMs }
    }

    fun serialize(bookmarks: List<PbfBookmark>): String {
        val sb = StringBuilder()
        sb.append(HEADER).append("\r\n")
        var index = 0
        for (b in bookmarks) {
            sb.append(index++).append('=').append(b.positionMs).append('*')
                .append(sanitize(b.name)).append('*')
                .append(sanitize(b.raw)).append("\r\n")
        }
        sb.append(index).append("=\r\n\r\n")
        return sb.toString()
    }

    /** UTF-16 LE with a byte-order mark: what PotPlayer writes. */
    fun encode(text: String): ByteArray = byteArrayOf(0xFF.toByte(), 0xFE.toByte()) + text.toByteArray(Charsets.UTF_16LE)

    fun decode(bytes: ByteArray): String = when {
        bytes.size >= 2 && bytes[0] == 0xFF.toByte() && bytes[1] == 0xFE.toByte() -> String(bytes, 2, bytes.size - 2, Charsets.UTF_16LE)
        bytes.size >= 2 && bytes[0] == 0xFE.toByte() && bytes[1] == 0xFF.toByte() -> String(bytes, 2, bytes.size - 2, Charsets.UTF_16BE)
        bytes.size >= 3 && bytes[0] == 0xEF.toByte() && bytes[1] == 0xBB.toByte() && bytes[2] == 0xBF.toByte() ->
            String(bytes, 3, bytes.size - 3, Charsets.UTF_8)
        // No BOM: ASCII-range text in UTF-16 LE has a zero second byte.
        bytes.size >= 2 && bytes[0] != 0.toByte() && bytes[1] == 0.toByte() -> String(bytes, Charset.forName("UTF-16LE"))
        else -> String(bytes, Charsets.UTF_8)
    }

    // fullwidth asterisk keeps the field layout intact
    private fun sanitize(value: String) = value.replace('*', '＊').replace('\r', ' ').replace('\n', ' ')
}
