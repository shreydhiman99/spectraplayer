package com.neuspectra.spectraplayer.core

/** One note on a video: the moment it is about and what was written. */
data class Note(val id: Long, val videoUri: String, val startMs: Long, val text: String, val createdMs: Long = 0) {
    val timeLabel: String get() = TimeFormat.format(startMs)
}

/** One video's notes inside a larger export. */
data class NotesSection(val title: String, val subtitle: String?, val notes: List<Note>)

/**
 * Renders notes as Markdown that pastes cleanly into Obsidian, Notion or a plain text editor:
 * `- **[12:34]** text`, with continuation lines indented so multi-line notes stay in one bullet.
 */
object NotesMarkdown {
    fun forVideo(videoTitle: String, notes: List<Note>): String {
        val sb = StringBuilder()
        sb.append("# ").append(videoTitle.trim()).append("\n\n")
        if (notes.isEmpty()) sb.append("_No notes._\n") else appendBullets(sb, notes)
        return sb.toString()
    }

    fun forTab(tabTitle: String, sections: List<NotesSection>): String {
        val list = sections.filter { it.notes.isNotEmpty() }
        val total = list.sumOf { it.notes.size }

        val sb = StringBuilder()
        sb.append("# ").append(tabTitle.trim()).append(" — notes\n\n")
        if (list.isEmpty()) {
            sb.append("_No notes._\n")
            return sb.toString()
        }

        sb.append('_').append(total).append(if (total == 1) " note" else " notes")
            .append(" in ").append(list.size).append(if (list.size == 1) " video" else " videos").append("_\n\n")

        for (section in list) {
            sb.append("## ").append(section.title.trim()).append('\n')
            if (!section.subtitle.isNullOrBlank()) sb.append('_').append(section.subtitle.trim()).append("_\n")
            sb.append('\n')
            appendBullets(sb, section.notes)
            sb.append('\n')
        }
        return sb.toString().trimEnd('\n') + "\n"
    }

    private fun appendBullets(sb: StringBuilder, notes: List<Note>) {
        for (note in notes.sortedWith(compareBy({ it.startMs }, { it.id }))) {
            val lines = note.text.replace("\r\n", "\n").replace('\r', '\n').trim().split('\n')
            sb.append("- **[").append(note.timeLabel).append("]** ").append(lines[0].trimEnd()).append('\n')
            for (line in lines.drop(1)) sb.append(if (line.isEmpty()) "" else "  " + line.trimEnd()).append('\n')
        }
    }
}
