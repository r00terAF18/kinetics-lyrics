using Android.App;
using Android.Content;
using Android.Service.Notification;

namespace KineticLyrics.Native.Platforms;

[Service(
    Name = "com.kineticslyrics.app.KineticLyricsNotificationListenerService",
    Permission = "android.permission.BIND_NOTIFICATION_LISTENER_SERVICE",
    Exported = true)]
[IntentFilter(["android.service.notification.NotificationListenerService"])]
public sealed class KineticLyricsNotificationListenerService : NotificationListenerService
{
    public override void OnListenerConnected() => base.OnListenerConnected();
}
