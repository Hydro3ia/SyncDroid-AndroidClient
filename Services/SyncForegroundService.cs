using System;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Net.Wifi;
using Android.OS;
using AndroidX.Core.App;

namespace SyncDroidAndroid.Services;

[Service(
    Name = "com.syncdroid.client.SyncForegroundService",
    Exported = false,
    ForegroundServiceType = ForegroundService.TypeDataSync | ForegroundService.TypeConnectedDevice)]
public class SyncForegroundService : Service
{
    public const string ChannelId = "syncdroid_companion_channel";
    public const int NotificationId = 45821;
    public const string ActionStartService = "com.syncdroid.client.ACTION_START";
    public const string ActionStopService = "com.syncdroid.client.ACTION_STOP";
    public const string ActionCancelSync = "com.syncdroid.client.ACTION_CANCEL_SYNC";

    private PowerManager.WakeLock? _wakeLock;
    private WifiManager.WifiLock? _wifiLock;

    public override IBinder? OnBind(Intent? intent) => null;

    public override void OnCreate()
    {
        base.OnCreate();
        CreateNotificationChannel();
        AcquireLocks();

        SyncStateService.Instance.StateChanged += OnSyncStateChanged;
        CompanionNetworkService.Instance.ConnectionStateChanged += OnSyncStateChanged;
    }

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        string? action = intent?.Action;
        if (action == ActionCancelSync)
        {
            SyncStateService.Instance.CancelCurrentOperation();
            return StartCommandResult.Sticky;
        }

        if (action == ActionStopService)
        {
            StopForeground(StopForegroundFlags.Remove);
            StopSelf();
            return StartCommandResult.NotSticky;
        }

        var notification = BuildNotification();
        if (OperatingSystem.IsAndroidVersionAtLeast(34))
        {
            StartForeground(
                NotificationId,
                notification,
                ForegroundService.TypeDataSync | ForegroundService.TypeConnectedDevice);
        }
        else
        {
            StartForeground(NotificationId, notification);
        }

        return StartCommandResult.Sticky;
    }

    private void OnSyncStateChanged()
    {
        try
        {
            var nm = NotificationManagerCompat.From(this);
            nm.Notify(NotificationId, BuildNotification());
        }
        catch { }
    }

    private Notification BuildNotification()
    {
        var loc = LocalizationService.Instance;
        var state = SyncStateService.Instance;
        var net = CompanionNetworkService.Instance;

        var openIntent = new Intent(this, typeof(MainActivity));
        openIntent.SetFlags(ActivityFlags.SingleTop | ActivityFlags.ClearTop);
        var pendingOpen = PendingIntent.GetActivity(
            this, 0, openIntent, PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);

        string title = state.IsBusy
            ? $"SyncDroid — {state.StatusText} ({state.Progress}%)"
            : $"SyncDroid — {loc.ServerListeningPill}";

        string ips = string.Join(", ", CompanionNetworkService.GetLocalIpv4Addresses());
        string subtitle = net.ConnectedPeer != null
            ? $"{loc.ConnectedToPcPrefix}{net.ConnectedPeer.DeviceName} ({net.ConnectedPeer.Endpoint})"
            : $"LAN: {ips} : {net.ListeningPort}";

        var builder = new NotificationCompat.Builder(this, ChannelId)
            .SetSmallIcon(Resource.Drawable.ic_sync)
            .SetContentTitle(title)
            .SetContentText(subtitle)
            .SetContentIntent(pendingOpen)
            .SetOngoing(true)
            .SetOnlyAlertOnce(true)
            .SetPriority(NotificationCompat.PriorityLow);

        if (state.IsBusy)
        {
            builder.SetProgress(100, state.Progress, false);

            var cancelIntent = new Intent(this, typeof(SyncForegroundService));
            cancelIntent.SetAction(ActionCancelSync);
            var pendingCancel = PendingIntent.GetService(
                this, 1, cancelIntent, PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);

            builder.AddAction(Resource.Drawable.ic_delete, loc.CancelButton, pendingCancel);
        }

        return builder.Build();
    }

    private void CreateNotificationChannel()
    {
        if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
        {
            var channel = new NotificationChannel(
                ChannelId,
                "SyncDroid Wireless Companion & Sync",
                NotificationImportance.Low)
            {
                Description = "Keeps wireless companion server and folder synchronization active in the background."
            };
            var manager = (NotificationManager?)GetSystemService(NotificationService);
            manager?.CreateNotificationChannel(channel);
        }
    }

    private void AcquireLocks()
    {
        try
        {
            var pm = (PowerManager?)GetSystemService(PowerService);
            _wakeLock = pm?.NewWakeLock(WakeLockFlags.Partial, "SyncDroid::CompanionWakeLock");
            _wakeLock?.SetReferenceCounted(false);
            _wakeLock?.Acquire();
        }
        catch { }

        try
        {
            var wm = (WifiManager?)ApplicationContext?.GetSystemService(WifiService);
            var wifiMode = OperatingSystem.IsAndroidVersionAtLeast(29)
                ? Android.Net.WifiMode.FullLowLatency
                : Android.Net.WifiMode.FullHighPerf;
            _wifiLock = wm?.CreateWifiLock(wifiMode, "SyncDroid::CompanionWifiLock");
            _wifiLock?.SetReferenceCounted(false);
            _wifiLock?.Acquire();
        }
        catch { }
    }

    private void ReleaseLocks()
    {
        try
        {
            if (_wakeLock?.IsHeld == true) _wakeLock.Release();
        }
        catch { }

        try
        {
            if (_wifiLock?.IsHeld == true) _wifiLock.Release();
        }
        catch { }
    }

    public override void OnDestroy()
    {
        SyncStateService.Instance.StateChanged -= OnSyncStateChanged;
        CompanionNetworkService.Instance.ConnectionStateChanged -= OnSyncStateChanged;
        ReleaseLocks();
        base.OnDestroy();
    }
}
