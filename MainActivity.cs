using System;
using System.IO;
using Android;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Graphics.Drawables;
using Android.OS;
using Android.Provider;
using Android.Views;
using Android.Widget;
using AndroidX.AppCompat.App;
using AndroidX.Core.App;
using AndroidX.Core.Content;
using Google.Android.Material.BottomNavigation;
using Google.Android.Material.Card;
using Google.Android.Material.Color;
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

        _cardConnectionPill!.Click += (_, _) =>
        {
            SelectNavigationItem(Resource.Id.nav_companion);
        };

        _bottomNav!.ItemSelected += (_, e) =>
        {
            if (_syncingNavSelection) return;
            SelectNavigationItem(e.Item.ItemId);
        };

        _navRail!.ItemSelected += (_, e) =>
        {
            if (_syncingNavSelection) return;
            SelectNavigationItem(e.Item.ItemId);
        };

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
        UpdateConnectionPillUi();
    }

    protected override void OnPause()
    {
        CompanionNetworkService.Instance.ConnectionStateChanged -= UpdateConnectionPillUi;
        LocalizationService.Instance.LanguageChanged -= OnLanguageChanged;
        base.OnPause();
    }

    private void SelectNavigationItem(int itemId, bool forceSwitch = false)
    {
        bool changed = _currentTabItemId != itemId || forceSwitch;
        _currentTabItemId = itemId;

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
            Resource.Id.nav_companion => new CompanionFragment(),
            Resource.Id.nav_settings => new SettingsFragment(),
            _ => new MirrorSyncFragment()
        };

        SupportFragmentManager
            .BeginTransaction()
            .Replace(Resource.Id.fragment_container, fragment)
            .Commit();
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
            var clipData = intent.ClipData;
            if (clipData != null && clipData.ItemCount > 0)
            {
                for (int i = 0; i < clipData.ItemCount; i++)
                {
                    var uri = clipData.GetItemAt(i)?.Uri;
                    if (uri != null) ImportSharedUriToQueue(uri);
                }
            }
            else if (intent.Data != null)
            {
                ImportSharedUriToQueue(intent.Data);
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

        // Copy content:// stream to temp cache so it can be sent reliably over Companion network
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

        bool isOnline = net.ConnectedPeer != null || net.IsServerRunning;
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
            menu.FindItem(Resource.Id.nav_companion)?.SetTitle(loc.NavCompanion);
            menu.FindItem(Resource.Id.nav_settings)?.SetTitle(loc.NavSettings);
        }

        if (_navRail != null)
        {
            var railMenu = _navRail.Menu;
            railMenu.FindItem(Resource.Id.nav_mirror_sync)?.SetTitle(loc.NavMirrorSync);
            railMenu.FindItem(Resource.Id.nav_file_transfer)?.SetTitle(loc.NavFileTransfer);
            railMenu.FindItem(Resource.Id.nav_companion)?.SetTitle(loc.NavCompanion);
            railMenu.FindItem(Resource.Id.nav_settings)?.SetTitle(loc.NavSettings);
        }
    }
}
