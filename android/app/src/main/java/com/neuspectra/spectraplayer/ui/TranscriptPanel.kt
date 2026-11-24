package com.neuspectra.spectraplayer.ui

import android.content.Context
import android.content.Intent
import androidx.compose.foundation.ExperimentalFoundationApi
import androidx.compose.foundation.background
import androidx.compose.foundation.combinedClickable
import androidx.compose.foundation.horizontalScroll
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.itemsIndexed
import androidx.compose.foundation.lazy.rememberLazyListState
import androidx.compose.foundation.rememberScrollState
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Button
import androidx.compose.material3.DropdownMenu
import androidx.compose.material3.DropdownMenuItem
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.LinearProgressIndicator
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalClipboardManager
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.AnnotatedString
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.neuspectra.spectraplayer.AppViewModel
import com.neuspectra.spectraplayer.core.TimeFormat
import com.neuspectra.spectraplayer.core.TranscriptSegment
import kotlin.math.max

/**
 * Pick a stretch of the video (start and end, or a quick choice), turn its speech into text on the phone, then read along,
 * jump around, turn lines into notes, copy or share. Mirrors the Windows app's Transcript tab.
 */
@OptIn(ExperimentalFoundationApi::class)
@Composable
fun TranscriptPanel(vm: AppViewModel, modifier: Modifier = Modifier) {
    val context = LocalContext.current
    val clipboard = LocalClipboardManager.current
    var modelMenu by remember { mutableStateOf(false) }
    var shareMenu by remember { mutableStateOf(false) }
    val listState = rememberLazyListState()

    // the line being spoken now
    val currentIndex = vm.transcript.indexOfLast { it.startMs <= vm.positionMs }
        .takeIf { it >= 0 && vm.positionMs <= vm.transcript[it].endMs + 2_000 } ?: -1
    LaunchedEffect(currentIndex) {
        if (currentIndex >= 0 && vm.playing) listState.animateScrollToItem(max(0, currentIndex - 1))
    }

    Column(modifier) {
        // ---- the section ---------------------------------------------------------------------------------------
        Column(Modifier.fillMaxWidth().padding(horizontal = 14.dp, vertical = 10.dp)) {
            Row(verticalAlignment = Alignment.Bottom) {
                Text("Section", fontSize = 15.sp, fontWeight = FontWeight.SemiBold, modifier = Modifier.weight(1f))
                vm.section?.let { Text("${TimeFormat.format(it.length)} long", color = Palette.TextDim, fontSize = 12.sp) }
            }
            val start = vm.sectionStartMs
            val end = vm.sectionEndMs
            Text(
                when {
                    start == null && end == null -> "Mark the part of the video to turn into text."
                    end == null -> "From ${TimeFormat.format(start!!)}… now set the end."
                    start == null -> "Until ${TimeFormat.format(end)}… now set the start."
                    else -> "${TimeFormat.format(start)}  –  ${TimeFormat.format(end)}"
                },
                fontSize = 13.sp, color = if (vm.section != null) Palette.Selection else Palette.TextDim, modifier = Modifier.padding(top = 4.dp),
            )
            Row(Modifier.fillMaxWidth().padding(top = 8.dp), horizontalArrangement = androidx.compose.foundation.layout.Arrangement.spacedBy(8.dp)) {
                OutlinedButton(onClick = { vm.markSectionStart() }, enabled = vm.current != null, modifier = Modifier.weight(1f)) { Text("Set start", fontSize = 13.sp) }
                OutlinedButton(onClick = { vm.markSectionEnd() }, enabled = vm.current != null, modifier = Modifier.weight(1f)) { Text("Set end", fontSize = 13.sp) }
            }
            Row(Modifier.horizontalScroll(rememberScrollState()).padding(top = 4.dp)) {
                TextButton(onClick = { vm.selectLast(2) }, enabled = vm.current != null) { Text("Last 2 min", fontSize = 12.5.sp) }
                TextButton(onClick = { vm.selectLast(5) }, enabled = vm.current != null) { Text("Last 5 min", fontSize = 12.5.sp) }
                TextButton(onClick = { vm.selectWholeVideo() }, enabled = vm.current != null) { Text("Whole video", fontSize = 12.5.sp) }
                TextButton(onClick = { vm.clearSection() }, enabled = start != null || end != null) { Text("Clear", fontSize = 12.5.sp) }
            }
        }
        HorizontalDivider(color = Palette.Line)

        // ---- the action ----------------------------------------------------------------------------------------
        Column(Modifier.fillMaxWidth().padding(horizontal = 14.dp, vertical = 10.dp)) {
            val download = vm.modelDownload
            when {
                download != null -> {
                    Row(verticalAlignment = Alignment.CenterVertically) {
                        Text("Downloading the speech model…  ${(download * 100).toInt()}%", fontSize = 12.5.sp, modifier = Modifier.weight(1f))
                        TextButton(onClick = { vm.cancelTranscribe() }) { Text("Cancel") }
                    }
                    LinearProgressIndicator(progress = { download }, color = Palette.Accent, trackColor = Palette.PanelAlt, modifier = Modifier.fillMaxWidth().padding(top = 4.dp))
                }
                vm.transcribing -> {
                    Row(verticalAlignment = Alignment.CenterVertically) {
                        Text(vm.transcribeStage, fontSize = 12.5.sp, modifier = Modifier.weight(1f))
                        TextButton(onClick = { vm.cancelTranscribe() }) { Text("Cancel") }
                    }
                    LinearProgressIndicator(progress = { vm.transcribeProgress }, color = Palette.Accent, trackColor = Palette.PanelAlt, modifier = Modifier.fillMaxWidth().padding(top = 4.dp))
                }
                else -> Button(
                    onClick = { vm.requestTranscribe() }, enabled = vm.section != null && vm.current != null,
                    modifier = Modifier.fillMaxWidth(),
                ) { Text("Transcribe section") }
            }

            Box {
                TextButton(onClick = { modelMenu = true }, enabled = !vm.transcribing && vm.modelDownload == null) {
                    Text(
                        "${vm.transcribeModel.title} · ${vm.transcribeModel.sizeText}" + if (vm.modelInstalled) "  ✓ on this phone" else "  (not downloaded)",
                        fontSize = 12.sp, color = Palette.TextDim,
                    )
                }
                DropdownMenu(modelMenu, onDismissRequest = { modelMenu = false }) {
                    for (m in com.neuspectra.spectraplayer.transcribe.WhisperModels.all) {
                        DropdownMenuItem(
                            text = { Text((if (m.id == vm.transcribeModel.id) "✓  " else "     ") + "${m.title} · ${m.sizeText}") },
                            onClick = { modelMenu = false; vm.chooseModel(m) },
                        )
                    }
                    if (vm.modelInstalled) DropdownMenuItem(text = { Text("Remove the downloaded model") }, onClick = { modelMenu = false; vm.deleteModel() })
                }
            }
            Text(vm.transcribeModel.description, color = Palette.TextDim, fontSize = 11.5.sp)
            vm.transcribeError?.let { Text(it, color = Palette.Danger, fontSize = 12.5.sp, modifier = Modifier.padding(top = 6.dp)) }
        }
        HorizontalDivider(color = Palette.Line)

        // ---- the text ------------------------------------------------------------------------------------------
        Box(Modifier.weight(1f).fillMaxWidth()) {
            if (vm.transcript.isEmpty()) {
                Text(
                    if (vm.current == null) "Open a video first."
                    else "No transcript yet. Mark a section and press Transcribe. It is made on this phone; nothing leaves it.",
                    color = Palette.TextDim, fontSize = 13.sp, modifier = Modifier.padding(horizontal = 18.dp, vertical = 12.dp),
                )
            } else {
                LazyColumn(Modifier.fillMaxSize(), state = listState) {
                    itemsIndexed(vm.transcript, key = { _, s -> s.id }) { index, seg ->
                        TranscriptRow(seg, current = index == currentIndex, vm = vm, onCopy = { clipboard.setText(AnnotatedString(seg.text)) })
                    }
                }
            }
        }

        if (vm.transcript.isNotEmpty()) {
            HorizontalDivider(color = Palette.Line)
            Row(Modifier.fillMaxWidth().padding(horizontal = 6.dp), verticalAlignment = Alignment.CenterVertically) {
                TextButton(onClick = { vm.transcriptMarkdown()?.let { clipboard.setText(AnnotatedString(it)) } }) { Text("Copy all", fontSize = 13.sp) }
                Box {
                    TextButton(onClick = { shareMenu = true }) { Text("Share  ▾", fontSize = 13.sp) }
                    DropdownMenu(shareMenu, onDismissRequest = { shareMenu = false }) {
                        DropdownMenuItem(text = { Text("As Markdown") }, onClick = {
                            shareMenu = false
                            vm.transcriptMarkdown()?.let { shareTranscript(context, vm.current?.title ?: "Transcript", it) }
                        })
                        DropdownMenuItem(text = { Text("As subtitles (.srt text)") }, onClick = {
                            shareMenu = false
                            vm.transcriptSrt()?.let { shareTranscript(context, (vm.current?.title ?: "Transcript") + ".srt", it) }
                        })
                    }
                }
                Box(Modifier.weight(1f))
                TextButton(onClick = { vm.clearTranscript() }) { Text("Clear", fontSize = 13.sp, color = Palette.Danger) }
            }
        }
    }

    // ---- asking before the download -----------------------------------------------------------------------------
    vm.modelPrompt?.let { model ->
        AlertDialog(
            onDismissRequest = { vm.dismissModelPrompt() }, containerColor = Palette.Panel,
            title = { Text("Download the speech model?") },
            text = {
                Text(
                    "To turn speech into text on your phone, Spectraplayer needs a speech model: ${model.title}, ${model.sizeText}. " +
                        "It is downloaded once from Hugging Face (huggingface.co) and kept on this phone. The transcribing itself happens " +
                        "on the phone, and your videos and notes are never sent anywhere. Wi-Fi is best.",
                )
            },
            confirmButton = { TextButton(onClick = { vm.confirmModelDownload() }) { Text("Download ${model.sizeText}") } },
            dismissButton = { TextButton(onClick = { vm.dismissModelPrompt() }) { Text("Not now") } },
        )
    }
}

@OptIn(ExperimentalFoundationApi::class)
@Composable
private fun TranscriptRow(seg: TranscriptSegment, current: Boolean, vm: AppViewModel, onCopy: () -> Unit) {
    var menu by remember { mutableStateOf(false) }
    Box {
        Row(
            Modifier.fillMaxWidth().background(if (current) Palette.Hover else Color.Transparent)
                .combinedClickable(onClick = { vm.seekTo(seg.startMs) }, onLongClick = { menu = true })
                .padding(horizontal = 14.dp, vertical = 8.dp),
        ) {
            Text(
                seg.timeLabel, fontFamily = FontFamily.Monospace, fontSize = 12.5.sp, modifier = Modifier.width(54.dp).padding(top = 1.dp),
                color = if (current) Palette.Accent else Palette.TextDim,
            )
            Text(seg.text, fontSize = 14.sp, fontWeight = if (current) FontWeight.SemiBold else FontWeight.Normal, modifier = Modifier.weight(1f))
        }
        DropdownMenu(menu, onDismissRequest = { menu = false }) {
            DropdownMenuItem(text = { Text("Go to") }, onClick = { menu = false; vm.seekTo(seg.startMs) })
            DropdownMenuItem(text = { Text("Make a note from this line") }, onClick = { menu = false; vm.noteFromSegment(seg) })
            DropdownMenuItem(text = { Text("Copy") }, onClick = { menu = false; onCopy() })
        }
    }
}

private fun shareTranscript(context: Context, title: String, text: String) {
    val send = Intent(Intent.ACTION_SEND).apply {
        type = "text/plain"
        putExtra(Intent.EXTRA_SUBJECT, title)
        putExtra(Intent.EXTRA_TEXT, text)
    }
    context.startActivity(Intent.createChooser(send, "Share transcript"))
}
