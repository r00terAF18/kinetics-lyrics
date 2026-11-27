package com.kineticlyrics.shared

import aman.taglib.TagLib
import android.content.ComponentName
import android.content.Context
import android.graphics.Bitmap
import android.media.MediaMetadata
import android.media.audiofx.Visualizer
import android.media.session.MediaController
import android.media.session.MediaSessionManager
import android.media.session.PlaybackState
import android.os.Environment
import android.os.SystemClock
import android.util.Base64
import java.io.ByteArrayOutputStream
import java.io.File
import java.util.Locale
import java.util.concurrent.CopyOnWriteArrayList
import kotlin.math.ceil
import kotlin.math.floor
import kotlin.math.max
import kotlin.math.min
import kotlin.math.pow
import kotlin.math.sqrt

object AndroidMediaBridge : PlatformMediaSource {
    private val lock = Any()
    private val tracks = CopyOnWriteArrayList<LocalTrack>()
    private val audioExtensions = setOf("flac", "mp3", "m4a", "ogg", "opus", "wav", "wma")

    private var visualizer: Visualizer? = null
    private val fftBins = FloatArray(128)
    private var scanVersion = 0
    private var initializedFolderKey = ""

    private var title = ""
    private var artist = ""
    private var album = ""
    private var isPlaying = false
    private var basePosSec = 0.0
    private var baseNano = System.nanoTime()
    private var durationSec = 0.0
    private var thumbDataUrl: String? = null
    private var localRawLrc: String? = null
    private var localSource: String? = null
    private var lastTrackKey = ""

    fun init(context: Context) {
        val saved = context.getSharedPreferences("kinetic_wallpaper_settings", Context.MODE_PRIVATE)
            .getStringSet("music_folders", null)?.toList().orEmpty()
        val musicDir = Environment.getExternalStoragePublicDirectory(Environment.DIRECTORY_MUSIC)
        val folders = saved.ifEmpty { listOfNotNull(musicDir?.absolutePath) }
        val key = folders.joinToString("|")
        synchronized(lock) {
            if (key == initializedFolderKey) return
            initializedFolderKey = key
        }
        Thread { scanMusicFolders(folders) }.start()
    }

    override fun start() {
        synchronized(lock) {
            if (visualizer != null) return
            try {
                val vis = Visualizer(0)
                val range = Visualizer.getCaptureSizeRange() ?: intArrayOf(128, 1024)
                vis.captureSize = 256.coerceIn(range[0], range[1])
                vis.setDataCaptureListener(object : Visualizer.OnDataCaptureListener {
                    override fun onWaveFormDataCapture(v: Visualizer?, waveform: ByteArray?, samplingRate: Int) {}
                    override fun onFftDataCapture(v: Visualizer?, fft: ByteArray?, samplingRate: Int) {
                        if (fft == null || fft.size < 4) return
                        val half = fft.size / 2
                        synchronized(lock) {
                            for (b in 0 until 64) {
                                val start = max(1, floor(half.toDouble().pow(b / 64.0)).toInt())
                                val end = max(start + 1, min(half, ceil(half.toDouble().pow((b + 1) / 64.0)).toInt()))
                                var sum = 0f
                                for (k in start until end) {
                                    val re = fft[k * 2].toFloat()
                                    val im = fft[k * 2 + 1].toFloat()
                                    sum += sqrt(re * re + im * im) / 128f
                                }
                                val mag = ((sum / (end - start)) * (1.2f + b * 0.12f) * 2.4f).coerceIn(0f, 1.5f)
                                fftBins[b] = mag
                                fftBins[64 + b] = mag
                            }
                        }
                    }
                }, Visualizer.getMaxCaptureRate() / 2, false, true)
                vis.enabled = true
                visualizer = vis
            } catch (_: Throwable) {
                visualizer = null
            }
        }
    }

    override fun stop() {
        synchronized(lock) {
            try {
                visualizer?.enabled = false
                visualizer?.release()
            } catch (_: Throwable) {}
            visualizer = null
        }
    }

    override fun getFftBins(): FloatArray = synchronized(lock) { fftBins.copyOf() }

    override fun scanMusicFolders(folders: List<String>) {
        val version = synchronized(lock) { ++scanVersion }
        val found = mutableListOf<LocalTrack>()
        for (folder in folders) {
            val root = File(folder)
            if (!root.exists()) continue
            root.walkTopDown().onFail { _, _ -> }.forEach { file ->
                if (file.isFile && file.extension.lowercase() in audioExtensions) {
                    val nameWithoutExt = file.nameWithoutExtension
                    val parts = nameWithoutExt.split(" - ", limit = 2)
                    val parsedArtist = if (parts.size == 2) parts[0].trim() else (file.parentFile?.parentFile?.name ?: "")
                    val parsedTitle = if (parts.size == 2) parts[1].trim() else nameWithoutExt.trim()
                    found.add(LocalTrack(file.absolutePath, parsedTitle, parsedArtist, file.parentFile?.name ?: "", 0.0))
                }
            }
        }
        synchronized(lock) {
            if (version != scanVersion) return
            tracks.clear()
            tracks.addAll(found)
            initializedFolderKey = folders.joinToString("|")
        }
    }

    fun updateFromController(controller: MediaController?) {
        if (controller == null) {
            synchronized(lock) { isPlaying = false }
            return
        }
        val meta = controller.metadata
        val pb = controller.playbackState

        val t = meta?.getString(MediaMetadata.METADATA_KEY_TITLE) ?: ""
        val a = meta?.getString(MediaMetadata.METADATA_KEY_ARTIST)
            ?: meta?.getString(MediaMetadata.METADATA_KEY_ALBUM_ARTIST) ?: ""
        val alb = meta?.getString(MediaMetadata.METADATA_KEY_ALBUM) ?: ""
        val durMs = meta?.getLong(MediaMetadata.METADATA_KEY_DURATION) ?: 0L

        val playing = pb?.state == PlaybackState.STATE_PLAYING
        val posMs = pb?.position ?: 0L
        val updateMs = pb?.lastPositionUpdateTime ?: 0L
        val speed = pb?.playbackSpeed ?: 1.0f

        var posSec = posMs / 1000.0
        if (playing && updateMs > 0L) {
            val elapsed = SystemClock.elapsedRealtime() - updateMs
            if (elapsed in 1..30_000) posSec += (elapsed / 1000.0) * max(0.5f, speed)
        }

        val key = "$t|$a|$alb"
        val artBmp = if (key != lastTrackKey && t.isNotBlank()) {
            meta?.getBitmap(MediaMetadata.METADATA_KEY_ALBUM_ART) ?: meta?.getBitmap(MediaMetadata.METADATA_KEY_ART)
        } else null

        synchronized(lock) {
            isPlaying = playing
            basePosSec = max(0.0, posSec)
            baseNano = System.nanoTime()
            if (durMs > 0L) durationSec = durMs / 1000.0

            if (key != lastTrackKey && t.isNotBlank()) {
                lastTrackKey = key
                title = t
                artist = a
                album = alb
                localRawLrc = null
                localSource = null
                thumbDataUrl = encodeBitmap(artBmp)

                val matched = LocalLyricsMatcher.bestMatch(tracks, t, a, alb, durationSec)
                if (matched != null) {
                    val embedded = runCatching { TagLib.getMetadata(matched.path)["LYRICS"] }.getOrNull()
                    val embeddedLrc = normalizeLocalLyrics(embedded, durationSec)
                    if (embeddedLrc != null) {
                        localRawLrc = embeddedLrc
                        localSource = "Embedded Tags" + if (LocalLyricsMatcher.hasTimestamps(embedded.orEmpty())) "" else " · estimated timing"
                    } else {
                        val audio = File(matched.path)
                        for (extension in listOf("lrc", "txt")) {
                            val sidecar = File(audio.parentFile, "${audio.nameWithoutExtension}.$extension")
                            if (sidecar.exists()) {
                                val content = runCatching { sidecar.readText() }.getOrNull()
                                val sidecarLrc = normalizeLocalLyrics(content, durationSec)
                                if (sidecarLrc != null) {
                                    localRawLrc = sidecarLrc
                                    localSource = if (extension == "lrc") "Local .lrc" else "Local .txt"
                                    if (!LocalLyricsMatcher.hasTimestamps(content.orEmpty())) localSource += " · estimated timing"
                                    break
                                }
                            }
                        }
                    }
                }
            }
        }
    }

    override fun getSnapshot(): MediaSnapshot = synchronized(lock) {
        val elapsed = if (isPlaying) (System.nanoTime() - baseNano) / 1_000_000_000.0 else 0.0
        MediaSnapshot(
            title = title,
            artist = artist,
            album = album,
            isPlaying = isPlaying,
            positionSeconds = basePosSec + elapsed,
            durationSeconds = durationSec,
            thumbnailDataUrl = thumbDataUrl,
            localRawLrc = localRawLrc,
            localSource = localSource,
            statusText = localSource ?: if (title.isBlank()) "Idle" else "Synced"
        )
    }

    private fun encodeBitmap(bmp: Bitmap?): String? {
        if (bmp == null) return null
        return runCatching {
            val scaled = Bitmap.createScaledBitmap(bmp, 256, 256, true)
            val out = ByteArrayOutputStream()
            scaled.compress(Bitmap.CompressFormat.JPEG, 82, out)
            "data:image/jpeg;base64," + Base64.encodeToString(out.toByteArray(), Base64.NO_WRAP)
        }.getOrNull()
    }

    private fun normalizeLocalLyrics(raw: String?, durationSeconds: Double): String? {
        if (raw.isNullOrBlank()) return null
        if (LocalLyricsMatcher.hasTimestamps(raw)) return raw
        val lines = raw.lineSequence().map { it.trim() }
            .filter { it.isNotEmpty() && !it.matches(Regex("(?i)^\\[(ar|ti|al|by|offset|re|ve):.*")) }
            .toList()
        if (lines.size < 3) return null

        val span = if (durationSeconds > lines.size * 0.8) durationSeconds else lines.size * 4.0
        val step = span / lines.size
        return lines.mapIndexed { index, line ->
            val centiseconds = kotlin.math.round(index * step * 100).toLong()
            val minutes = centiseconds / 6000
            val seconds = centiseconds % 6000 / 100
            val fraction = centiseconds % 100
            "[%02d:%02d.%02d]%s".format(Locale.US, minutes, seconds, fraction, line)
        }.joinToString("\n")
    }
}
