package com.kineticlyrics.wallpaper

import android.content.ComponentName
import android.media.MediaMetadata
import android.media.session.MediaController
import android.media.session.MediaSessionManager
import android.media.session.PlaybackState
import android.service.notification.NotificationListenerService
import com.kineticlyrics.shared.AndroidMediaBridge

class MediaNotificationListener : NotificationListenerService() {
    private var sessionManager: MediaSessionManager? = null
    private var activeController: MediaController? = null
    private var callback: MediaController.Callback? = null
    private var sessionListener: MediaSessionManager.OnActiveSessionsChangedListener? = null

    override fun onListenerConnected() {
        super.onListenerConnected()
        AndroidMediaBridge.init(applicationContext)
        val mgr = getSystemService(MEDIA_SESSION_SERVICE) as? MediaSessionManager ?: return
        sessionManager = mgr

        val component = ComponentName(this, MediaNotificationListener::class.java)
        val listener = MediaSessionManager.OnActiveSessionsChangedListener { controllers ->
            bindControllers(controllers)
        }
        sessionListener = listener
        mgr.addOnActiveSessionsChangedListener(listener, component)
        bindControllers(mgr.getActiveSessions(component))
    }

    override fun onListenerDisconnected() {
        unbindCurrent()
        val mgr = sessionManager
        val listener = sessionListener
        if (mgr != null && listener != null) {
            runCatching { mgr.removeOnActiveSessionsChangedListener(listener) }
        }
        super.onListenerDisconnected()
    }

    private fun bindControllers(controllers: List<MediaController>?) {
        if (controllers.isNullOrEmpty()) {
            unbindCurrent()
            AndroidMediaBridge.updateFromController(null)
            return
        }

        val chosen = controllers.firstOrNull { it.playbackState?.state == PlaybackState.STATE_PLAYING }
            ?: controllers.first()

        if (activeController?.sessionToken == chosen.sessionToken) {
            AndroidMediaBridge.updateFromController(chosen)
            return
        }

        unbindCurrent()
        activeController = chosen
        val cb = object : MediaController.Callback() {
            override fun onMetadataChanged(metadata: MediaMetadata?) = AndroidMediaBridge.updateFromController(chosen)
            override fun onPlaybackStateChanged(state: PlaybackState?) = AndroidMediaBridge.updateFromController(chosen)
        }
        callback = cb
        chosen.registerCallback(cb)
        AndroidMediaBridge.updateFromController(chosen)
    }

    private fun unbindCurrent() {
        val ctrl = activeController
        val cb = callback
        if (ctrl != null && cb != null) {
            runCatching { ctrl.unregisterCallback(cb) }
        }
        activeController = null
        callback = null
    }
}
