using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Android;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Graphics.Drawables;
using Android.OS;
using Android.Provider;
using Android.Views;
using Android.Widget;
using AndroidX.Activity;
using AndroidX.AppCompat.App;
using AndroidX.Core.App;
using AndroidX.Core.Content;
using Google.Android.Material.BottomNavigation;
using Google.Android.Material.Card;
using Google.Android.Material.Color;
using Google.Android.Material.Dialog;
using Google.Android.Material.NavigationRail;
using SyncDroidAndroid.Fragments;
using SyncDroidAndroid.Models;
using SyncDroidAndroid.Services;

namespace SyncDroidAndroid;

[Activity(
    Name = "com.syncdroid.client.MainActivity",
    Label = "@string/app_name",
    MainLauncher = true,
    Exported = true,
    LaunchMode = LaunchMode.SingleTop,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.SmallestScreenSize | ConfigChanges.ScreenLayout | ConfigChanges.KeyboardHidden | ConfigChanges.Density)]
[IntentFilter(
    new[] { Intent.ActionSend, Intent.ActionSendMultiple },
    Categories = new[] { Intent.CategoryDefault },
    DataMimeType = "*/*")]
public class MainActivity : AppCompatActivity
{
    private TextView? _txtAppSubtitle;
    private MaterialCardView? _cardConnectionPill;
    private View? _viewStatusDot;
    private TextView? _txtConnectionPill;
    private BottomNavigationView? _bottomNav;
    private NavigationRailView? _navRail;
    private View? _dividerNavRail;
    private FrameLayout? _fragmentContainer;
    private bool _syncingNavSelection;
    private int _currentTabItemId = Resource.Id.nav_mirror_sync;
    private bool _inManageDevicesSubPage;
    private ManageDevicesBackCallback? _manageDevicesBackCallback;
    private AndroidX.AppCompat.App.AlertDialog? _activeIncomingConfirmDialog;
    private TaskCompletionSource<bool>? _activeSyncDroidConfirmTcs;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        var settings = SettingsService.Load();
        ApplyThemeMode(settings.ThemeMode);
        if (settings.UseDynamicColor)
        {
            DynamicColors.ApplyToActivityIfAvailable(this);
        }

        base.OnCreate(savedInstanceState);
        SetContentView(Resource.Layout.activity_main);

        _txtAppSubtitle = FindViewById<TextView>(Resource.Id.txt_app_subtitle);
        _cardConnectionPill = FindViewById<MaterialCardView>(Resource.Id.card_connection_pill);
        _viewStatusDot = FindViewById<View>(Resource.Id.view_status_dot);
        _txtConnectionPill = FindViewById<TextView>(Resource.Id.txt_connection_pill);
        _bottomNav = FindViewById<BottomNavigationView>(Resource.Id.bottom_navigation);
        _navRail = FindViewById<NavigationRailView>(Resource.Id.navigation_rail);
        _dividerNavRail = FindViewById<View>(Resource.Id.divider_nav_rail);
        _fragmentContainer = FindViewById<FrameLayout>(Resource.Id.fragment_container);

        _manageDevicesBackCallback = new ManageDevicesBackCallback(this);
        OnBackPressedDispatcher.AddCallback(this, _manageDevicesBackCallback);

        _cardConnectionPill!.Click += (_, _) =>
        {
            SelectNavigationItem(Resource.Id.nav_companion);
        };

        _bottomNav!.ItemSelected += (_, e) =>
        {
            if (_syncingNavSelection) return;
            SelectNavigationItem(e.Item.ItemId, forceSwitch: _inManageDevicesSubPage);
        };

        _navRail!.ItemSelected += (_, e) =>
        {
            if (_syncingNavSelection) return;
            SelectNavigationItem(e.Item.ItemId, forceSwitch: _inManageDevicesSubPage);
        };

        CompanionNetworkService.Instance.ConfirmFirstTimeDeviceAsync = ConfirmFirstTimeSyncDroidDeviceOnUiAsync;
        SyncStateService.Instance.IncomingTransferSessionStarted += OnIncomingTransferSessionStarted;
        SyncStateService.Instance.ConfirmationResolvedFromBanner += OnConfirmationResolvedFromBanner;

        RequestNotificationPermissionIfNeeded();

        if (settings.CompanionServerEnabled && !CompanionNetworkService.Instance.IsServerRunning)
        {
            CompanionNetworkService.Instance.StartServer(settings.CompanionPort);
            StartCompanionForegroundService();
        }

        ResponsiveWindowService.Instance.RecomputeWindowMetrics(this);
        ApplyAdaptiveShellLayout(ResponsiveWindowService.Instance.CurrentState);
        ApplyTopAndBottomBarLocalization();
        UpdateConnectionPillUi();

        if (savedInstanceState == null)
        {
            SelectNavigationItem(Resource.Id.nav_mirror_sync, forceSwitch: true);
        }

        HandleIncomingShareIntent(Intent);
    }

    protected override void OnDestroy()
    {
        SyncStateService.Instance.IncomingTransferSessionStarted -= OnIncomingTransferSessionStarted;
        SyncStateService.Instance.ConfirmationResolvedFromBanner -= OnConfirmationResolvedFromBanner;
        if (CompanionNetworkService.Instance.ConfirmFirstTimeDeviceAsync == ConfirmFirstTimeSyncDroidDeviceOnUiAsync)
        {
            CompanionNetworkService.Instance.ConfirmFirstTimeDeviceAsync = null;
        }
        base.OnDestroy();
    }

    protected override void OnStart()
    {
        base.OnStart();
        ResponsiveWindowService.Instance.WindowStateChanged += ApplyAdaptiveShellLayout;
        ResponsiveWindowService.Instance.StartTracking(this);
        ApplyAdaptiveShellLayout(ResponsiveWindowService.Instance.CurrentState);
    }

    protected override void OnStop()
    {
        ResponsiveWindowService.Instance.WindowStateChanged -= ApplyAdaptiveShellLayout;
        ResponsiveWindowService.Instance.StopTracking();
        base.OnStop();
    }

    public override void OnConfigurationChanged(Android.Content.Res.Configuration newConfig)
    {
        base.OnConfigurationChanged(newConfig);
        var state = ResponsiveWindowService.Instance.RecomputeWindowMetrics(this);
        ApplyAdaptiveShellLayout(state);
    }

    protected override void OnNewIntent(Intent? intent)
    {
        base.OnNewIntent(intent);
        HandleIncomingShareIntent(intent);
    }

    protected override void OnResume()
    {
        base.OnResume();
        CompanionNetworkService.Instance.ConnectionStateChanged += UpdateConnectionPillUi;
        LocalizationService.Instance.LanguageChanged += OnLanguageChanged;
        CompanionNetworkService.Instance.ConfirmFirstTimeDeviceAsync = ConfirmFirstTimeSyncDroidDeviceOnUiAsync;
        UpdateConnectionPillUi();
    }

    protected override void OnPause()
    {
        CompanionNetworkService.Instance.ConnectionStateChanged -= UpdateConnectionPillUi;
        LocalizationService.Instance.LanguageChanged -= OnLanguageChanged;
        base.OnPause();
    }

    private void OnIncomingTransferSessionStarted(IncomingTransferSessionEventArgs args)
    {
        RunOnUiThread(() =>
        {
            SelectNavigationItem(Resource.Id.nav_receive, forceSwitch: true);

            if (args.RequiresConfirmation &&
                args.ConfirmationTcs != null &&
                string.Equals(args.Protocol, "LocalSend", StringComparison.OrdinalIgnoreCase))
            {
                ShowLocalSendReceiveConfirmDialog(args);
            }
        });
    }

    private void OnConfirmationResolvedFromBanner()
    {
        RunOnUiThread(() =>
        {
            try
            {
                _activeIncomingConfirmDialog?.Dismiss();
                _activeIncomingConfirmDialog = null;
            }
            catch { }

            if (_activeSyncDroidConfirmTcs != null && !_activeSyncDroidConfirmTcs.Task.IsCompleted)
            {
                var status = SyncStateService.Instance.ReceiveStatusText;
                bool accepted = status != LocalizationService.Instance.ReceiveStatusDeclined;
                _activeSyncDroidConfirmTcs.TrySetResult(accepted);
            }
        });
    }

    private void ShowLocalSendReceiveConfirmDialog(IncomingTransferSessionEventArgs args)
    {
        if (IsFinishing || IsDestroyed || args.ConfirmationTcs == null || args.ConfirmationTcs.Task.IsCompleted)
            return;

        try
        {
            _activeIncomingConfirmDialog?.Dismiss();
        }
        catch { }

        var loc = LocalizationService.Instance;
        var previewLines = args.Files
            .Take(12)
            .Select(f => $"• {f.FileName} ({f.SizeDisplay})")
            .ToList();
        if (args.Files.Count > 12)
        {
            previewLines.Add($"... (+{args.Files.Count - 12} more)");
        }
        string filePreviewText = string.Join("\n", previewLines);

        string msg = string.Format(
            loc.FirstTimeLocalSendReceiveMessage,
            args.SenderName,
            args.SenderIp,
            args.Files.Count,
            filePreviewText);

        var builder = new MaterialAlertDialogBuilder(this);
        builder.SetTitle(loc.FirstTimeLocalSendReceiveTitle);
        builder.SetMessage(msg);
        builder.SetCancelable(true);
        builder.SetPositiveButton(loc.ConfirmAcceptButton, (_, _) =>
        {
            if (!args.ConfirmationTcs.Task.IsCompleted)
            {
                SyncStateService.Instance.SyncConfirmationDecision(accepted: true);
            }
        });
        builder.SetNegativeButton(loc.ConfirmRejectButton, (_, _) =>
        {
            if (!args.ConfirmationTcs.Task.IsCompleted)
            {
                SyncStateService.Instance.SyncConfirmationDecision(accepted: false);
            }
        });

        _activeIncomingConfirmDialog = builder.Show();
    }

    private Task<bool> ConfirmFirstTimeSyncDroidDeviceOnUiAsync(string deviceName, string ipOrEndpoint)
    {
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _activeSyncDroidConfirmTcs = tcs;

        RunOnUiThread(() =>
        {
            if (IsFinishing || IsDestroyed)
            {
                tcs.TrySetResult(true);
                return;
            }

            try
            {
                _activeIncomingConfirmDialog?.Dismiss();
            }
            catch { }

            var loc = LocalizationService.Instance;
            var builder = new MaterialAlertDialogBuilder(this);
            builder.SetTitle(loc.FirstTimeConnectDialogTitle);
            builder.SetMessage(string.Format(loc.FirstTimeSyncDroidConnectMessage, deviceName, ipOrEndpoint));
            builder.SetCancelable(false);
            builder.SetPositiveButton(loc.ConfirmAcceptButton, (_, _) =>
            {
                tcs.TrySetResult(true);
            });
            builder.SetNegativeButton(loc.ConfirmRejectButton, (_, _) =>
            {
                tcs.TrySetResult(false);
            });

            _activeIncomingConfirmDialog = builder.Show();
        });

        return tcs.Task;
    }

    public void ShowManageDevicesFragment()
    {
        _inManageDevicesSubPage = true;
        if (_manageDevicesBackCallback != null) _manageDevicesBackCallback.Enabled = true;

        SupportFragmentManager
            .BeginTransaction()
            .Replace(Resource.Id.fragment_container, new ManageDevicesFragment())
            .CommitAllowingStateLoss();
    }

    public void ShowSettingsFragment()
    {
        _inManageDevicesSubPage = false;
        if (_manageDevicesBackCallback != null) _manageDevicesBackCallback.Enabled = false;
        SelectNavigationItem(Resource.Id.nav_settings, forceSwitch: true);
    }

    public void SelectNavigationItem(int itemId, bool forceSwitch = false)
    {
        bool changed = _currentTabItemId != itemId || forceSwitch || _inManageDevicesSubPage;
        _currentTabItemId = itemId;
        _inManageDevicesSubPage = false;
        if (_manageDevicesBackCallback != null) _manageDevicesBackCallback.Enabled = false;

        _syncingNavSelection = true;
        if (_bottomNav != null && _bottomNav.SelectedItemId != itemId)
        {
            _bottomNav.SelectedItemId = itemId;
        }
        if (_navRail != null && _navRail.SelectedItemId != itemId)
        {
            _navRail.SelectedItemId = itemId;
        }
        _syncingNavSelection = false;

        if (changed)
        {
            SwitchTab(itemId);
        }
    }

    private void ApplyAdaptiveShellLayout(AdaptiveWindowState state)
    {
        if (_bottomNav == null || _navRail == null || _fragmentContainer == null) return;

        float density = Resources?.DisplayMetrics?.Density ?? 1f;
        bool useRail = state.UseNavigationRail;

        _navRail.Visibility = useRail ? ViewStates.Visible : ViewStates.Gone;
        if (_dividerNavRail != null)
        {
            _dividerNavRail.Visibility = useRail ? ViewStates.Visible : ViewStates.Gone;
        }
        _bottomNav.Visibility = useRail ? ViewStates.Gone : ViewStates.Visible;

        if (_fragmentContainer.LayoutParameters is LinearLayout.LayoutParams lp)
        {
            lp.BottomMargin = useRail ? 0 : (int)Math.Round(80 * density);
            _fragmentContainer.LayoutParameters = lp;
        }

        if (_txtConnectionPill != null)
        {
            int maxPillWidthDp = state.WidthSizeClass switch
            {
                WindowWidthSizeClass.Expanded => 320,
                WindowWidthSizeClass.Medium => 240,
                _ => 145
            };
            _txtConnectionPill.SetMaxWidth((int)Math.Round(maxPillWidthDp * density));
        }
    }

    private void SwitchTab(int itemId)
    {
        AndroidX.Fragment.App.Fragment fragment = itemId switch
        {
            Resource.Id.nav_file_transfer => new FileTransferFragment(),
            Resource.Id.nav_receive => new ReceiveFragment(),
            Resource.Id.nav_companion => new CompanionFragment(),
            Resource.Id.nav_settings => new SettingsFragment(),
            _ => new MirrorSyncFragment()
        };

        SupportFragmentManager
            .BeginTransaction()
            .Replace(Resource.Id.fragment_container, fragment)
            .CommitAllowingStateLoss();
    }

    public static void ApplyThemeMode(string themeMode)
    {
        int nightMode = themeMode switch
        {
            "Light" => AppCompatDelegate.ModeNightNo,
            "Dark" => AppCompatDelegate.ModeNightYes,
            _ => AppCompatDelegate.ModeNightFollowSystem
        };
        AppCompatDelegate.DefaultNightMode = nightMode;
    }

    public static bool HasAllFilesAccessPermission()
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(30))
        {
            return Android.OS.Environment.IsExternalStorageManager;
        }
        return true;
    }

    public void RequestAllFilesAccessPermission()
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(30))
        {
            try
            {
                var intent = new Intent(Settings.ActionManageAppAllFilesAccessPermission);
                intent.SetData(Android.Net.Uri.Parse($"package:{PackageName}"));
                StartActivity(intent);
            }
            catch
            {
                var intent = new Intent(Settings.ActionManageAllFilesAccessPermission);
                StartActivity(intent);
            }
        }
        else
        {
            ActivityCompat.RequestPermissions(
                this,
                new[] { Manifest.Permission.ReadExternalStorage, Manifest.Permission.WriteExternalStorage },
                1001);
        }
    }

    private void RequestNotificationPermissionIfNeeded()
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(33))
        {
            if (ContextCompat.CheckSelfPermission(this, Manifest.Permission.PostNotifications) != Permission.Granted)
            {
                ActivityCompat.RequestPermissions(this, new[] { Manifest.Permission.PostNotifications }, 1002);
            }
        }
    }

    public void StartCompanionForegroundService()
    {
        try
        {
            var intent = new Intent(this, typeof(SyncForegroundService));
            intent.SetAction(SyncForegroundService.ActionStartService);
            if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
            {
                StartForegroundService(intent);
            }
            else
            {
                StartService(intent);
            }
        }
        catch { }
    }

    public void StopCompanionForegroundService()
    {
        try
        {
            var intent = new Intent(this, typeof(SyncForegroundService));
            intent.SetAction(SyncForegroundService.ActionStopService);
            StartService(intent);
        }
        catch { }
    }

    private void HandleIncomingShareIntent(Intent? intent)
    {
        if (intent == null) return;
        string? action = intent.Action;
        if (action != Intent.ActionSend && action != Intent.ActionSendMultiple) return;

        try
        {
            var uris = new System.Collections.Generic.List<Android.Net.Uri>();

            if (action == Intent.ActionSend)
            {
                var uri = (Android.Net.Uri?)intent.GetParcelableExtra(Intent.ExtraStream);
                if (uri != null) uris.Add(uri);
            }
            else if (action == Intent.ActionSendMultiple)
            {
                var uriList = intent.GetParcelableArrayListExtra(Intent.ExtraStream);
                if (uriList != null)
                {
                    foreach (var item in uriList)
                    {
                        if (item is Android.Net.Uri uri) uris.Add(uri);
                    }
                }
            }

            var clipData = intent.ClipData;
            if (clipData != null)
            {
                for (int i = 0; i < clipData.ItemCount; i++)
                {
                    var uri = clipData.GetItemAt(i)?.Uri;
                    if (uri != null && !uris.Contains(uri)) uris.Add(uri);
                }
            }

            if (uris.Count == 0 && intent.Data != null)
            {
                uris.Add(intent.Data);
            }

            foreach (var uri in uris)
            {
                ImportSharedUriToQueue(uri);
            }

            SelectNavigationItem(Resource.Id.nav_file_transfer);
        }
        catch (Exception ex)
        {
            SyncStateService.Instance.AddLog($"Failed to import shared file: {ex.Message}");
        }
    }

    private void ImportSharedUriToQueue(Android.Net.Uri uri)
    {
        if (uri.Scheme == "file" && !string.IsNullOrEmpty(uri.Path))
        {
            var fi = new FileInfo(uri.Path);
            if (fi.Exists)
            {
                SyncStateService.Instance.AddToQueue(new TransferQueueItem
                {
                    FullPath = fi.FullName,
                    DisplayName = fi.Name,
                    Size = fi.Length,
                    IsDirectory = false
                });
                return;
            }
        }

        string fileName = $"shared_{DateTime.Now:yyyyMMdd_HHmmss}";
        var cursor = ContentResolver?.Query(uri, null, null, null, null);
        if (cursor != null)
        {
            using (cursor)
            {
                int nameIndex = cursor.GetColumnIndex(IOpenableColumns.DisplayName);
                if (nameIndex >= 0 && cursor.MoveToFirst())
                {
                    fileName = cursor.GetString(nameIndex) ?? fileName;
                }
            }
        }

        string cacheDir = Path.Combine(CacheDir!.AbsolutePath, "shared_outgoing");
        Directory.CreateDirectory(cacheDir);
        string localCopyPath = Path.Combine(cacheDir, fileName);

        using (var inStream = ContentResolver!.OpenInputStream(uri))
        using (var outStream = File.Create(localCopyPath))
        {
            inStream?.CopyTo(outStream);
        }

        var copyInfo = new FileInfo(localCopyPath);
        SyncStateService.Instance.AddToQueue(new TransferQueueItem
        {
            FullPath = copyInfo.FullName,
            DisplayName = fileName,
            Size = copyInfo.Length,
            IsDirectory = false
        });
    }

    private void UpdateConnectionPillUi()
    {
        var net = CompanionNetworkService.Instance;
        var loc = LocalizationService.Instance;

        if (_txtConnectionPill == null || _viewStatusDot == null) return;

        if (_viewStatusDot.Background is GradientDrawable gd)
        {
            int colorRes = net.ConnectedPeer != null
                ? Resource.Color.status_online
                : (net.IsServerRunning ? Resource.Color.folder_amber : Resource.Color.status_offline);
            gd.SetColor(ContextCompat.GetColor(this, colorRes));
        }

        if (net.ConnectedPeer != null)
        {
            string badge = net.ConnectedPeer.ProtocolType == PeerProtocolType.LocalSend ? "[LocalSend] " : "";
            _txtConnectionPill.Text = $"{loc.ConnectedToPcPrefix}{badge}{net.ConnectedPeer.DeviceName}";
        }
        else if (net.IsServerRunning)
        {
            _txtConnectionPill.Text = $"{loc.ServerListeningPill} :{net.ListeningPort}";
        }
        else
        {
            _txtConnectionPill.Text = loc.NoPcConnectedPill;
        }
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        ApplyTopAndBottomBarLocalization();
        UpdateConnectionPillUi();
    }

    private void ApplyTopAndBottomBarLocalization()
    {
        var loc = LocalizationService.Instance;
        if (_txtAppSubtitle != null) _txtAppSubtitle.Text = loc.AppSubtitle;

        if (_bottomNav != null)
        {
            var menu = _bottomNav.Menu;
            menu.FindItem(Resource.Id.nav_mirror_sync)?.SetTitle(loc.NavMirrorSync);
            menu.FindItem(Resource.Id.nav_file_transfer)?.SetTitle(loc.NavFileTransfer);
            menu.FindItem(Resource.Id.nav_receive)?.SetTitle(loc.NavReceive);
            menu.FindItem(Resource.Id.nav_companion)?.SetTitle(loc.NavCompanion);
            menu.FindItem(Resource.Id.nav_settings)?.SetTitle(loc.NavSettings);
        }

        if (_navRail != null)
        {
            var railMenu = _navRail.Menu;
            railMenu.FindItem(Resource.Id.nav_mirror_sync)?.SetTitle(loc.NavMirrorSync);
            railMenu.FindItem(Resource.Id.nav_file_transfer)?.SetTitle(loc.NavFileTransfer);
            railMenu.FindItem(Resource.Id.nav_receive)?.SetTitle(loc.NavReceive);
            railMenu.FindItem(Resource.Id.nav_companion)?.SetTitle(loc.NavCompanion);
            railMenu.FindItem(Resource.Id.nav_settings)?.SetTitle(loc.NavSettings);
        }
    }

    private sealed class ManageDevicesBackCallback : OnBackPressedCallback
    {
        private readonly MainActivity _activity;

        public ManageDevicesBackCallback(MainActivity activity) : base(false)
        {
            _activity = activity;
        }

        public override void HandleOnBackPressed()
        {
            _activity.ShowSettingsFragment();
        }
    }
}
