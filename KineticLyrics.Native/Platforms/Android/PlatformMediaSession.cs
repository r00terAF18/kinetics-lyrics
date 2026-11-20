using Android.Content;
using Android.Graphics;
using Android.Media;
using Android.Media.Session;
using Android.OS;
using Android.Provider;
using KineticLyrics.Native.Models;
using KineticLyrics.Native.Services;

namespace KineticLyrics.Native.Platforms;

public sealed class PlatformMediaSession : IPlatformMediaSession
{
    private CancellationTokenSource? _pollCancellation;
    private string? _lastIdentity;
    private byte[]? _lastArtwork;

    public event EventHandler<MediaTrackSnapshot?>? TrackChanged;

    public string AccessStatus
    {
        get
        {
            var service = new ComponentName(Android.App.Application.Context, Java.Lang.Class.FromType(typeof(KineticLyricsNotificationListenerService)));
            var context = Android.App.Application.Context;
            var manager = (Android.App.NotificationManager?)context.GetSystemService(Context.NotificationService);
            var granted = Build.VERSION.SdkInt >= (BuildVersionCodes)27
                ? manager?.IsNotificationListenerAccessGranted(service) == true
                : Settings.Secure.GetString(context.ContentResolver, "enabled_notification_listeners")?.Contains(context.PackageName, StringComparison.OrdinalIgnoreCase) == true;
            return granted
                ? "Android notification access is enabled"
                : "Enable notification access to read media sessions";
        }
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_pollCancellation is not null)
            return Task.CompletedTask;
        _pollCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _ = PollAsync(_pollCancellation.Token);
        return Task.CompletedTask;
    }

    public void Stop()
    {
        var cancellation = Interlocked.Exchange(ref _pollCancellation, null);
        if (cancellation is null)
            return;
        cancellation.Cancel();
        cancellation.Dispose();
    }

    public Task RequestAccessAsync(CancellationToken cancellationToken = default)
    {
        var activity = Platform.CurrentActivity;
        activity?.StartActivity(new Intent(Settings.ActionNotificationListenerSettings));
        return Task.CompletedTask;
    }

    private async Task PollAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(400));
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
                await PublishCurrentAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (System.OperationCanceledException) { }
    }

    private Task PublishCurrentAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var context = Android.App.Application.Context;
            var service = new ComponentName(context, Java.Lang.Class.FromType(typeof(KineticLyricsNotificationListenerService)));
            var manager = (MediaSessionManager?)context.GetSystemService(Context.MediaSessionService);
            var controllers = manager?.GetActiveSessions(service);
            if (controllers is null || controllers.Count == 0)
            {
                if (_lastIdentity is not null)
                {
                    _lastIdentity = null;
                    TrackChanged?.Invoke(this, null);
                }
                return Task.CompletedTask;
            }

            var selected = controllers.FirstOrDefault(controller => controller.PlaybackState?.State == PlaybackStateCode.Playing)
                ?? controllers.FirstOrDefault(controller => !string.IsNullOrWhiteSpace(controller.Metadata?.GetString(MediaMetadata.MetadataKeyTitle)));
            if (selected is null)
                return Task.CompletedTask;

            var metadata = selected.Metadata;
            var playback = selected.PlaybackState;
            var title = metadata?.GetString(MediaMetadata.MetadataKeyTitle)?.Trim() ?? string.Empty;
            if (title.Length == 0)
                return Task.CompletedTask;

            var artist = metadata?.GetString(MediaMetadata.MetadataKeyArtist)
                ?? metadata?.GetString(MediaMetadata.MetadataKeyAlbumArtist)
                ?? string.Empty;
            var album = metadata?.GetString(MediaMetadata.MetadataKeyAlbum) ?? string.Empty;
            var duration = Math.Max(0, (metadata?.GetLong(MediaMetadata.MetadataKeyDuration) ?? 0) / 1000d);
            var playing = playback?.State == PlaybackStateCode.Playing;
            var position = Math.Max(0, (playback?.Position ?? 0) / 1000d);
            var updatedAt = playback?.LastPositionUpdateTime ?? SystemClock.ElapsedRealtime();
            if (playing && playback is not null)
                position += Math.Max(0, (SystemClock.ElapsedRealtime() - updatedAt) / 1000d * playback.PlaybackSpeed);

            var source = selected.PackageName ?? string.Empty;
            var identity = source + "|" + artist + "|" + title;
            if (!string.Equals(identity, _lastIdentity, StringComparison.Ordinal))
            {
                _lastIdentity = identity;
                _lastArtwork = ReadArtwork(metadata);
            }

            var mediaUri = metadata?.GetString(MediaMetadata.MetadataKeyMediaUri);
            TrackChanged?.Invoke(this, new MediaTrackSnapshot(
                title, artist, album, source, playing, position, duration, DateTimeOffset.UtcNow,
                _lastArtwork, mediaUri));
        }
        catch (Java.Lang.SecurityException)
        {
            _lastIdentity = null;
        }
        catch (Exception)
        {
            // A media app may end its session between the active-session query and metadata read.
        }
        return Task.CompletedTask;
    }

    private static byte[]? ReadArtwork(MediaMetadata? metadata)
    {
        var bitmap = metadata?.GetBitmap(MediaMetadata.MetadataKeyAlbumArt)
            ?? metadata?.GetBitmap(MediaMetadata.MetadataKeyArt)
            ?? metadata?.GetBitmap(MediaMetadata.MetadataKeyDisplayIcon);
        if (bitmap is null)
            return null;
        try
        {
            using var stream = new MemoryStream();
            bitmap.Compress(Bitmap.CompressFormat.Jpeg, 82, stream);
            return stream.ToArray();
        }
        catch { return null; }
        finally { bitmap.Dispose(); }
    }
}
