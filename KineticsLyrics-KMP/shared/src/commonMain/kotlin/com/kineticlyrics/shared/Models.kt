package com.kineticlyrics.shared

import kotlinx.serialization.Serializable

@Serializable
data class MediaSnapshot(
    val title: String = "",
    val artist: String = "",
    val album: String = "",
    val isPlaying: Boolean = false,
    val positionSeconds: Double = 0.0,
    val durationSeconds: Double = 0.0,
    val thumbnailDataUrl: String? = null,
    val localRawLrc: String? = null,
    val localSource: String? = null,
    val statusText: String = "Waiting for music…"
)

@Serializable
data class WallpaperSettings(
    val preset: String = "custom",
    val layout: String = "auto",
    val transition: String = "mixed",
    val chaos: Int = 35,
    val choreo: Boolean = true,
    val carry: Boolean = true,
    val camera: String = "auto",
    val camAmount: Int = 50,
    val bgMode: String = "art",
    val dim: Int = 55,
    val autoAccent: Boolean = true,
    val accent: String = "1 0.3 0.43",
    val invert: Boolean = false,
    val provider: String = "auto",
    val offsetMs: Int = 0,
    val autoSync: Boolean = false,
    val reactivity: Int = 60,
    val flashes: Boolean = true,
    val moodOn: Boolean = true,
    val showInfo: Boolean = true,
    val hudLift: Int = 4,
    val idleClock: Boolean = true,
    val clock24: Boolean = true,
    val lowPower: Boolean = false,
    val targetMonitor: String = "PRIMARY",
    val targetFps: Int = 60,
    val musicFolders: List<String> = emptyList()
)

@Serializable
data class LocalTrack(
    val path: String,
    val title: String,
    val artist: String,
    val album: String,
    val durationSeconds: Double
)

interface PlatformMediaSource {
    fun start()
    fun stop()
    fun getSnapshot(): MediaSnapshot
    fun getFftBins(): FloatArray
    fun scanMusicFolders(folders: List<String>)
}
