package com.neuspectra.spectraplayer.data

import android.content.Context
import android.net.Uri
import android.provider.DocumentsContract
import com.neuspectra.spectraplayer.core.NaturalComparer

/** What a folder holds: its videos in natural order, and any PotPlayer bookmark files that sit beside them. */
data class FolderContents(
    val entries: List<MediaEntry>,
    /** .pbf files by the video they belong to: relative path without extension (lower case) → document URI. */
    val bookmarkFiles: Map<String, String>,
)

object MediaExtensions {
    private val known = setOf(
        "mp4", "mkv", "avi", "mov", "wmv", "flv", "webm", "m4v", "ts", "m2ts", "mts", "mpg", "mpeg", "3gp", "ogv",
        "mp3", "flac", "wav", "m4a", "aac", "ogg", "opus",
    )

    fun isMedia(name: String, mime: String?): Boolean =
        name.substringAfterLast('.', "").lowercase() in known || mime?.startsWith("video/") == true || mime?.startsWith("audio/") == true
}

/** Reads a folder the user picked with Android's folder picker (a tree URI), recursively, through the content resolver. */
object FolderScanner {
    private const val MAX_DEPTH = 8

    /** The folder's own name, for the tab title. */
    fun displayName(context: Context, treeUri: Uri): String {
        val docUri = DocumentsContract.buildDocumentUriUsingTree(treeUri, DocumentsContract.getTreeDocumentId(treeUri))
        context.contentResolver.query(docUri, arrayOf(DocumentsContract.Document.COLUMN_DISPLAY_NAME), null, null, null)?.use { c ->
            if (c.moveToFirst()) c.getString(0)?.takeIf { it.isNotBlank() }?.let { return it }
        }
        return "Folder"
    }

    /** @throws SecurityException when the permission to read the folder has been lost. */
    fun scan(context: Context, treeUri: Uri): FolderContents {
        val entries = ArrayList<MediaEntry>()
        val bookmarkFiles = HashMap<String, String>()
        walk(context, treeUri, DocumentsContract.getTreeDocumentId(treeUri), "", 0, entries, bookmarkFiles)
        entries.sortWith { a, b -> NaturalComparer.compare(a.relativePath, b.relativePath) }
        return FolderContents(entries, bookmarkFiles)
    }

    private fun walk(
        context: Context, tree: Uri, parentDocId: String, prefix: String, depth: Int,
        entries: MutableList<MediaEntry>, bookmarkFiles: MutableMap<String, String>,
    ) {
        val children = DocumentsContract.buildChildDocumentsUriUsingTree(tree, parentDocId)
        val columns = arrayOf(
            DocumentsContract.Document.COLUMN_DOCUMENT_ID,
            DocumentsContract.Document.COLUMN_DISPLAY_NAME,
            DocumentsContract.Document.COLUMN_MIME_TYPE,
            DocumentsContract.Document.COLUMN_SIZE,
            DocumentsContract.Document.COLUMN_LAST_MODIFIED,
        )
        val folders = ArrayList<Pair<String, String>>() // document id, name
        context.contentResolver.query(children, columns, null, null, null)?.use { c ->
            while (c.moveToNext()) {
                val id = c.getString(0)
                val name = c.getString(1) ?: continue
                if (name.startsWith(".")) continue // hidden
                val mime = c.getString(2)
                if (mime == DocumentsContract.Document.MIME_TYPE_DIR) {
                    folders += id to name
                    continue
                }
                val relative = prefix + name
                val uri = DocumentsContract.buildDocumentUriUsingTree(tree, id).toString()
                when {
                    name.endsWith(".pbf", ignoreCase = true) ->
                        bookmarkFiles[relative.substringBeforeLast('.').lowercase()] = uri
                    MediaExtensions.isMedia(name, mime) ->
                        entries += MediaEntry(uri, name, relative, if (c.isNull(3)) 0 else c.getLong(3), if (c.isNull(4)) 0 else c.getLong(4))
                }
            }
        }
        if (depth < MAX_DEPTH) for ((id, name) in folders) walk(context, tree, id, "$prefix$name/", depth + 1, entries, bookmarkFiles)
    }
}
