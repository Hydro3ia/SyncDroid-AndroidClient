# SyncDroid for Android (`SyncDroid-on-Android`)

**SyncDroid for Android** is the official **Material Design 3 (MD3)** wireless companion and folder mirror-synchronization (`/MIR`) & direct file transfer client for **SyncDroid WinUI**, built with **.NET 10 for Android** (`net10.0-android`).

- **Current Version**: `0.1.2.0 Release`
- **Target Framework**: `net10.0-android` (API 26 ~ 36)
- **UI Design**: Material Design 3 (`Theme.Material3.DayNight.NoActionBar`, Material You Dynamic Color, BottomNavigationView)

## Key Features
1. **Two-Way Mirror (`/MIR`) & Incremental Merge Folder Sync**:
   - **PC ➔ Android (Push)** & **Android ➔ PC (Pull)**.
   - Size-based idempotent comparison (`StringComparer.OrdinalIgnoreCase` + exact byte `Size`) matching `SyncDroidWinUI`.
   - Switch between **`/MIR` Mirror Mode** (removes extra files in target) and **Incremental Merge Mode** (preserves existing extra files in target).
   - Built-in safety guard against wiping root storage paths (`/storage/emulated/0`, `C:\`, etc.) and optional confirmation dialog before deleting extra files.
2. **Direct File & Folder Transfer (Non-`/MIR` Mode + LocalSend Protocol v2.1)**:
   - Transfer single files, multiple files, or whole folders (`手机 ➔ 设备` or `设备 ➔ 手机`) without touching other files in the destination.
   - Native **LocalSend Protocol v2.1** support (`224.0.0.167:53317` UDP multicast discovery with `WifiManager.MulticastLock` + HTTP/HTTPS `53317` sender & receiver) to send and receive files with any LocalSend device on LAN (Android, iOS/iPadOS, Windows, macOS, Linux).
   - Configurable conflict resolution: **Skip Same Size**, **Overwrite**, or **Keep Both (`file (1).ext`)**.
   - Supports Android system **Share (`ACTION_SEND` / `ACTION_SEND_MULTIPLE`)** to send files directly from Gallery or File Managers.
3. **Wireless Companion Server & Auto-Discovery**:
   - Built-in HTTP/REST streaming server (`TCP 45821`) and UDP LAN Broadcast Auto-Discovery (`UDP 45820`).
   - Atomic `.syncdroid_<guid>.part` file writing and automatic `MediaScannerConnection` refresh.
   - Android 14+ compliant `ForegroundService` (`dataSync|connectedDevice`) with `WakeLock`, `WifiLock`, live progress notification, and notification cancel action.
4. **Multi-Language & UTF-8 / CJK Filename Safety**:
   - Instant runtime switching between **English (`en-US`)**, **Simplified Chinese (`zh-CN`)**, and **Traditional Chinese (`zh-TW`)**.
   - Automatic detection & Grapheme-safe truncation for filenames exceeding Android's `255`-byte UTF-8 limit (`<= 254` bytes) and Windows NTFS invalid character sanitization.

## Building from Source
```powershell
dotnet build "C:\Users\Yiren\SyncDroid-on-Android\SyncDroidAndroid.csproj" -c Release
```
