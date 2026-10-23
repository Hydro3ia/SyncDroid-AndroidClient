# SyncDroid for Android — Agent Handover & Developer Guide (`agent-readme.md`)

## 1. Project Overview
- **Project Path**: `C:\Users\Yiren\SyncDroid-on-Android`
- **Target Framework**: `net10.0-android` (`SupportedOSPlatformVersion=26.0`, Target SDK 36)
- **UI Framework**: Native Android XML + `Xamarin.Google.Android.Material` (`1.12.0.2`, Material Design 3 + Dynamic Color) + Jetpack WindowManager (`Xamarin.AndroidX.Window` `1.3.0.3`, adaptive `BottomNavigationView`/`NavigationRailView` & responsive two-pane layouts)
- **Package Name (`ApplicationId`)**: `com.syncdroid.client`
- **Companion Desktop Project**: `C:\Users\Yiren\SyncDroidWinUI`

## 2. Directory & File Structure
```text
C:\Users\Yiren\SyncDroid-on-Android\
├── SyncDroidAndroid.csproj              # .NET 10 Android project file
├── AndroidManifest.xml                  # Permissions (MANAGE_EXTERNAL_STORAGE, FOREGROUND_SERVICE_DATA_SYNC, etc.)
├── MainActivity.cs                      # TopAppBar, BottomNavigationView, Share Intent receiver, DynamicColor
├── Models/
│   ├── AppSettings.cs                   # Persistent settings & 15-item histories
│   ├── LocalSendModels.cs               # LocalSend Protocol v2.1 DTOs & upload session models
│   └── SyncModels.cs                    # DTOs for Companion API, directory listings, tree manifests & transfer queue
├── Helpers/
│   ├── FileNameLengthHelper.cs          # UTF-8 <= 254 byte truncation & Windows invalid char sanitization
│   └── MediaScannerHelper.cs            # MediaScannerConnection refresh for Android Gallery/MediaStore
├── Services/
│   ├── CompanionNetworkService.cs       # UDP Discovery (45820) + HTTP REST Server/Client (45821)
│   ├── LocalSendService.cs              # LocalSend Protocol v2.1 Multicast Discovery (224.0.0.167:53317) + Server/Client
│   ├── LocalStorageScanner.cs           # Android /storage scanner, root safety guard, empty folder cleaner
│   ├── MirrorSyncEngine.cs              # /MIR Mirror Sync, Incremental Merge Sync & Non-MIR Direct Transfer
│   ├── SyncForegroundService.cs         # ForegroundService + WakeLock + WifiLock + Progress Notification
│   ├── SyncStateService.cs              # Singleton progress, logs, cancellation & transfer queue state
│   ├── SettingsService.cs               # JSON persistence (settings.json)
│   ├── ResponsiveWindowService.cs       # Jetpack WindowManager WindowMetrics & foldable/multi-pane responsive layout service
│   └── LocalizationService.cs           # Instant en-US / zh-CN / zh-TW localization
├── Fragments/
│   ├── MirrorSyncFragment.cs            # Tab 1: /MIR & Merge folder synchronization UI
│   ├── FileTransferFragment.cs          # Tab 2: Non-MIR single/multi file & folder transfer UI
│   ├── CompanionFragment.cs             # Tab 3: Wireless Companion Server & PC discovery/connect UI
│   ├── SettingsFragment.cs              # Tab 4: Language, MD3 Theme/Dynamic Color, Permissions, Port & About
│   └── StorageBrowserDialog.cs          # Modal directory/file browser for both local Android and remote PC
└── Resources/                           # MD3 layouts, drawables, menus, and DayNight themes
```
