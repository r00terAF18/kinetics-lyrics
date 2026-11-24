package com.kineticlyrics.shared

import platform.Foundation.NSLog

object MacOsNativeSupport {
    fun logReady() {
        NSLog("KineticLyrics KMP macOS Native target initialized.")
    }
}
