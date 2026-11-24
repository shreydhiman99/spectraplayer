package com.neuspectra.spectraplayer.ui

import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.darkColorScheme
import androidx.compose.runtime.Composable
import androidx.compose.ui.graphics.Color

/**
 * The same palette as the Windows app: a deep blue-black chrome and one blue accent. Each kind of thing you can mark has
 * its own colour so it is recognisable at a glance: amber = bookmarks, teal = notes, violet = the marked section,
 * blue = progress and actions, coral = problems, green = the focus timer.
 */
object Palette {
    val Bg = Color(0xFF0B0C10)
    val Panel = Color(0xFF12141B)
    val PanelAlt = Color(0xFF1A1D27)
    val Hover = Color(0xFF272C3A)
    val Line = Color(0xFF232736)
    val Text = Color(0xFFE9ECF5)
    val TextDim = Color(0xFF8A92A8)
    val Accent = Color(0xFF4F7DF3)
    val AccentHover = Color(0xFF6F93F7)
    val Marker = Color(0xFFFFC857)
    val Note = Color(0xFF4DD0C8)
    val Selection = Color(0xFFA78BFA)
    val Danger = Color(0xFFF0736B)
    val Focus = Color(0xFF6FCF97)
}

@Composable
fun SpectraTheme(content: @Composable () -> Unit) {
    MaterialTheme(
        colorScheme = darkColorScheme(
            primary = Palette.Accent,
            onPrimary = Color.White,
            secondary = Palette.AccentHover,
            background = Palette.Bg,
            onBackground = Palette.Text,
            surface = Palette.Panel,
            onSurface = Palette.Text,
            surfaceVariant = Palette.PanelAlt,
            onSurfaceVariant = Palette.TextDim,
            outline = Palette.Line,
            error = Palette.Danger,
        ),
        content = content,
    )
}
