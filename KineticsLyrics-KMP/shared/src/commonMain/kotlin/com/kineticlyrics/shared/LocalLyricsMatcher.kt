package com.kineticlyrics.shared

import kotlin.math.abs

object LocalLyricsMatcher {
    private val nonWordRegex = Regex("[^\\p{L}\\p{N}]+")
    private val lrcStampRegex = Regex("\\[(\\d+):(\\d{1,2}(?:\\.\\d+)?)\\]")

    fun normalize(s: String): String =
        nonWordRegex.replace(s.lowercase(), " ").trim()

    fun hasTimestamps(text: String): Boolean = lrcStampRegex.containsMatchIn(text)

    fun bestMatch(
        tracks: List<LocalTrack>,
        title: String,
        artist: String,
        album: String,
        durationSeconds: Double
    ): LocalTrack? {
        val normTitle = normalize(title)
        val normArtist = normalize(artist)
        if (normTitle.isEmpty() || normArtist.isEmpty()) return null

        val scored = tracks.mapNotNull { track ->
            if (normalize(track.title) != normTitle || normalize(track.artist) != normArtist) return@mapNotNull null
            val diff = abs(durationSeconds - track.durationSeconds)
            if (durationSeconds > 0 && track.durationSeconds > 0 && diff > 10.0) return@mapNotNull null
            val score = (if (durationSeconds > 0 && track.durationSeconds > 0 && diff <= 3.0) 2 else 0) +
                (if (album.isNotEmpty() && normalize(track.album) == normalize(album)) 1 else 0)
            track to score
        }.sortedByDescending { it.second }

        if (scored.isEmpty()) return null
        if (scored.size > 1 && scored[0].second == scored[1].second) return null
        return scored[0].first
    }
}
