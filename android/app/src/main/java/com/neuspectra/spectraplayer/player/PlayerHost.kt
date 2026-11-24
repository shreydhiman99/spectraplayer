package com.neuspectra.spectraplayer.player

import android.content.Context
import androidx.media3.common.AudioAttributes
import androidx.media3.common.C
import androidx.media3.common.MediaItem
import androidx.media3.common.PlaybackException
import androidx.media3.common.Player
import androidx.media3.exoplayer.ExoPlayer

/**
 * The video engine: Google's Media3 ExoPlayer. It opens content:// URIs directly, which is what a folder picked through
 * Android's folder picker gives us. Use it from the main thread only.
 */
class PlayerHost(context: Context) {
    val exo: ExoPlayer = ExoPlayer.Builder(context)
        .setAudioAttributes(
            AudioAttributes.Builder().setUsage(C.USAGE_MEDIA).setContentType(C.AUDIO_CONTENT_TYPE_MOVIE).build(),
            /* handleAudioFocus = */ true,
        )
        .setHandleAudioBecomingNoisy(true) // pause when headphones are unplugged
        .setSeekBackIncrementMs(10_000)
        .setSeekForwardIncrementMs(10_000)
        .build()

    /** The video played to its end. */
    var onEnded: (() -> Unit)? = null

    /** The file could not be played (unsupported format, missing file…). */
    var onError: ((String) -> Unit)? = null

    init {
        exo.addListener(object : Player.Listener {
            override fun onPlaybackStateChanged(playbackState: Int) {
                if (playbackState == Player.STATE_ENDED) onEnded?.invoke()
            }

            override fun onPlayerError(error: PlaybackException) {
                onError?.invoke(error.localizedMessage ?: "This file can't be played.")
            }
        })
    }

    val positionMs: Long get() = exo.currentPosition.coerceAtLeast(0)

    /** 0 until the length is known. */
    val durationMs: Long get() = exo.duration.takeIf { it != C.TIME_UNSET && it > 0 } ?: 0

    val isPlaying: Boolean get() = exo.isPlaying

    fun load(uri: String, startMs: Long, play: Boolean) {
        exo.setMediaItem(MediaItem.fromUri(uri), startMs.coerceAtLeast(0))
        exo.prepare()
        exo.playWhenReady = play
    }

    fun play() {
        if (exo.playbackState == Player.STATE_ENDED) exo.seekTo(0)
        exo.play()
    }

    fun pause() = exo.pause()

    fun toggle() = if (exo.isPlaying) pause() else play()

    fun seekTo(ms: Long) = exo.seekTo(ms.coerceAtLeast(0))

    fun seekBy(deltaMs: Long) {
        val target = positionMs + deltaMs
        val max = durationMs
        exo.seekTo(if (max > 0) target.coerceIn(0, max) else target.coerceAtLeast(0))
    }

    var speed: Float
        get() = exo.playbackParameters.speed
        set(value) = exo.setPlaybackSpeed(value.coerceIn(0.25f, 4f))

    fun stop() {
        exo.stop()
        exo.clearMediaItems()
    }

    fun release() = exo.release()
}
