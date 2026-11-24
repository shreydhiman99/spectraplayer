package com.neuspectra.spectraplayer.ui

import androidx.compose.foundation.Canvas
import androidx.compose.foundation.gestures.detectHorizontalDragGestures
import androidx.compose.foundation.gestures.detectTapGestures
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.StrokeCap
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.unit.dp
import kotlin.math.min

/** A ring that fills clockwise from the top: a faint full circle with a coloured arc over it, [fraction] of the way round. */
@Composable
fun RingProgress(fraction: Float, color: Color, modifier: Modifier = Modifier, track: Color = Palette.Line, strokeWidth: Float = 9f) {
    Canvas(modifier) {
        val stroke = strokeWidth.dp.toPx()
        val diameter = min(size.width, size.height) - stroke
        if (diameter <= 0) return@Canvas
        val topLeft = Offset((size.width - diameter) / 2, (size.height - diameter) / 2)
        val arcSize = Size(diameter, diameter)
        drawArc(track, 0f, 360f, false, topLeft, arcSize, style = Stroke(stroke))
        val sweep = 360f * fraction.coerceIn(0f, 1f)
        if (sweep > 0.2f) drawArc(color, -90f, sweep, false, topLeft, arcSize, style = Stroke(stroke, cap = StrokeCap.Round))
    }
}

/**
 * The seek bar, with a tick for every bookmark (amber) and a dot for every note (teal). Drag or tap to seek; the video
 * only jumps when the finger lifts.
 */
@Composable
fun SeekBar(
    positionMs: Long,
    durationMs: Long,
    bookmarks: List<Long>,
    notes: List<Long>,
    onSeek: (Long) -> Unit,
    modifier: Modifier = Modifier,
    /** The marked section (for transcripts): a violet band, or just a tick while only the start is set. */
    selectionStartMs: Long? = null,
    selectionEndMs: Long? = null,
) {
    var dragFraction by remember { mutableStateOf<Float?>(null) }
    val fraction = dragFraction ?: if (durationMs > 0) (positionMs.toFloat() / durationMs).coerceIn(0f, 1f) else 0f

    Box(
        modifier
            .height(36.dp)
            .pointerInput(durationMs) {
                detectTapGestures { offset -> if (durationMs > 0) onSeek((offset.x / size.width * durationMs).toLong().coerceIn(0, durationMs)) }
            }
            .pointerInput(durationMs) {
                detectHorizontalDragGestures(
                    onDragStart = { o -> dragFraction = (o.x / size.width).coerceIn(0f, 1f) },
                    onDragEnd = {
                        dragFraction?.let { if (durationMs > 0) onSeek((it * durationMs).toLong()) }
                        dragFraction = null
                    },
                    onDragCancel = { dragFraction = null },
                    onHorizontalDrag = { change, _ -> dragFraction = (change.position.x / size.width).coerceIn(0f, 1f) },
                )
            },
    ) {
        Canvas(Modifier.fillMaxSize()) {
            val pad = 10.dp.toPx()
            val width = size.width - 2 * pad
            val cy = size.height / 2
            val trackHeight = 4.dp.toPx()

            drawLine(Palette.Hover, Offset(pad, cy), Offset(pad + width, cy), trackHeight, StrokeCap.Round)

            if (durationMs > 0 && selectionStartMs != null) {
                val x1 = pad + width * (selectionStartMs.toFloat() / durationMs).coerceIn(0f, 1f)
                if (selectionEndMs != null && selectionEndMs > selectionStartMs) {
                    val x2 = pad + width * (selectionEndMs.toFloat() / durationMs).coerceIn(0f, 1f)
                    drawRect(Palette.Selection.copy(alpha = 0.45f), Offset(x1, cy - 6.dp.toPx()), Size(x2 - x1, 12.dp.toPx()))
                } else {
                    drawLine(Palette.Selection, Offset(x1, cy - 8.dp.toPx()), Offset(x1, cy + 8.dp.toPx()), 2.dp.toPx(), StrokeCap.Round)
                }
            }
            if (fraction > 0) drawLine(Palette.Accent, Offset(pad, cy), Offset(pad + width * fraction, cy), trackHeight, StrokeCap.Round)

            if (durationMs > 0) {
                for (ms in bookmarks) {
                    val x = pad + width * (ms.toFloat() / durationMs).coerceIn(0f, 1f)
                    drawLine(Palette.Marker, Offset(x, cy - 9.dp.toPx()), Offset(x, cy - 3.dp.toPx()), 2.dp.toPx(), StrokeCap.Round)
                }
                for (ms in notes) {
                    val x = pad + width * (ms.toFloat() / durationMs).coerceIn(0f, 1f)
                    drawCircle(Palette.Note, 2.5.dp.toPx(), Offset(x, cy + 9.dp.toPx()))
                }
            }

            val thumb = if (dragFraction != null) 9.dp.toPx() else 7.dp.toPx()
            drawCircle(Color.White, thumb, Offset(pad + width * fraction, cy))
        }
    }
}

/** A one-line text prompt (rename, edit a note…). */
@Composable
fun TextPromptDialog(
    title: String,
    initial: String,
    confirmLabel: String,
    singleLine: Boolean = true,
    onDismiss: () -> Unit,
    onConfirm: (String) -> Unit,
) {
    var text by remember { mutableStateOf(initial) }
    AlertDialog(
        onDismissRequest = onDismiss,
        containerColor = Palette.Panel,
        title = { Text(title) },
        text = {
            OutlinedTextField(value = text, onValueChange = { text = it }, singleLine = singleLine, modifier = Modifier.fillMaxWidth().height(if (singleLine) 64.dp else 140.dp))
        },
        confirmButton = { TextButton(onClick = { onConfirm(text) }) { Text(confirmLabel) } },
        dismissButton = { TextButton(onClick = onDismiss) { Text("Cancel") } },
    )
}
