package com.kineticlyrics.shared

import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonElement
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.put

class BridgeDispatcher(private val evaluateJs: (String) -> Unit) {
    private var lastTrackSig = ""
    private var lastLocalLrc: String? = null
    private var lastThumb: String? = null
    private var lastPlaying: Boolean? = null
    private var lastPos = -1.0
    private var lastDur = -1.0
    private var tick = 0

    fun reset() {
        lastTrackSig = ""
        lastLocalLrc = null
        lastThumb = null
        lastPlaying = null
        lastPos = -1.0
        lastDur = -1.0
    }

    fun pushFrame(snap: MediaSnapshot, fftBins: FloatArray) {
        if (snap.isPlaying || (tick++ % 4 == 0)) {
            val arr = JsonArray(fftBins.map { JsonPrimitive(it) })
            send("audio", arr)
        }

        val sig = "${snap.title}|${snap.artist}|${snap.album}"
        if (sig != lastTrackSig) {
            lastTrackSig = sig
            lastLocalLrc = null
            lastPos = -1.0
            lastDur = -1.0
            send("props", buildJsonObject {
                put("title", snap.title)
                put("artist", snap.artist)
                put("albumTitle", snap.album)
            })
        }

        if (snap.localRawLrc != null && snap.localRawLrc != lastLocalLrc) {
            lastLocalLrc = snap.localRawLrc
            send("localLyrics", buildJsonObject {
                put("source", snap.localSource ?: "Local LRC")
                put("lrc", snap.localRawLrc)
            })
        }

        if (snap.thumbnailDataUrl != lastThumb) {
            lastThumb = snap.thumbnailDataUrl
            send("thumb", buildJsonObject {
                put("thumbnail", snap.thumbnailDataUrl ?: "")
            })
        }

        if (lastPlaying != snap.isPlaying) {
            lastPlaying = snap.isPlaying
            send("playback", buildJsonObject {
                put("state", if (snap.isPlaying) 1 else 2)
            })
        }

        if (snap.isPlaying && (kotlin.math.abs(snap.positionSeconds - lastPos) > 0.12 || kotlin.math.abs(snap.durationSeconds - lastDur) > 0.5)) {
            lastPos = snap.positionSeconds
            lastDur = snap.durationSeconds
            send("timeline", buildJsonObject {
                put("position", snap.positionSeconds)
                put("duration", snap.durationSeconds)
            })
        }
    }

    fun pushSettings(s: WallpaperSettings, layoutMode: String = "single", monitors: String = "") {
        fun prop(v: JsonElement) = buildJsonObject { put("value", v) }
        val obj = buildJsonObject {
            put("preset", prop(JsonPrimitive(s.preset)))
            put("layout", prop(JsonPrimitive(s.layout)))
            put("transition", prop(JsonPrimitive(s.transition)))
            put("chaos", prop(JsonPrimitive(s.chaos)))
            put("choreo", prop(JsonPrimitive(s.choreo)))
            put("carry", prop(JsonPrimitive(s.carry)))
            put("camera", prop(JsonPrimitive(s.camera)))
            put("camamount", prop(JsonPrimitive(s.camAmount)))
            put("bgmode", prop(JsonPrimitive(s.bgMode)))
            put("dim", prop(JsonPrimitive(s.dim)))
            put("autoaccent", prop(JsonPrimitive(s.autoAccent)))
            put("accent", prop(JsonPrimitive(s.accent)))
            put("invert", prop(JsonPrimitive(s.invert)))
            put("provider", prop(JsonPrimitive(s.provider)))
            put("offset", prop(JsonPrimitive(s.offsetMs)))
            put("autosync", prop(JsonPrimitive(s.autoSync)))
            put("reactivity", prop(JsonPrimitive(s.reactivity)))
            put("flashes", prop(JsonPrimitive(s.flashes)))
            put("moodon", prop(JsonPrimitive(s.moodOn)))
            put("showinfo", prop(JsonPrimitive(s.showInfo)))
            put("hudlift", prop(JsonPrimitive(s.hudLift)))
            put("idleclock", prop(JsonPrimitive(s.idleClock)))
            put("clock24", prop(JsonPrimitive(s.clock24)))
            put("lowpower", prop(JsonPrimitive(s.lowPower)))
            put("layoutmode", prop(JsonPrimitive(layoutMode)))
            put("monitors", prop(JsonPrimitive(monitors)))
        }
        send("settings", obj)
        send("fps", JsonPrimitive(s.targetFps))
    }

    private fun send(type: String, data: JsonElement) {
        val payload = JsonObject(mapOf("type" to JsonPrimitive(type), "data" to data)).toString()
        evaluateJs("window.__klReceive && window.__klReceive($payload);")
    }
}
