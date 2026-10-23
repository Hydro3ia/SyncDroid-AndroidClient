using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SyncDroidAndroid.Helpers;
using SyncDroidAndroid.Models;

namespace SyncDroidAndroid.Services;

public class MirrorSyncEngine
{
    public static MirrorSyncEngine Instance { get; } = new();

    private MirrorSyncEngine() { }

    /// <summary>
    /// Synchronizes from Windows PC (Source) to Android (Target).
    /// When isMirMode is true, deletes extra files in Android target folder that do not exist on PC.
    /// Comparison rule matches SyncDroidWinUI: relative path (OrdinalIgnoreCase) + exact byte Size.
    /// </summary>
    public async Task SyncPcToAndroidAsync(
        string pcPath,
        string androidPath,
        bool isMirMode,
        bool checkLongFileNames,
        Func<int, Task<bool>>? confirmDeleteCallback,
        CancellationToken token)
    {
        var state = SyncStateService.Instance;
        var net = CompanionNetworkService.Instance;

        pcPath = pcPath.Trim().TrimEnd('/', '\\');
        androidPath = androidPath.Trim().Replace('\\', '/').TrimEnd('/');

        if (isMirMode && LocalStorageScanner.IsDangerousRootPath(androidPath))
        {
            throw new InvalidOperationException(LocalizationService.Instance.T(
                $"Safety Block: Cannot use root storage path '{androidPath}' as /MIR target because extra files would be deleted. Please choose a subfolder.",
                $"安全拦截：禁止将存储根目录 '{androidPath}' 直接作为 /MIR 镜像目标目录（会清空目录内其他文件），请选择具体子文件夹。",
                $"安全攔截：禁止將儲存根目錄 '{androidPath}' 直接作為 /MIR 鏡像目標目錄（會清空目錄內其他檔案），請選擇具體子資料夾。"));
        }

        Directory.CreateDirectory(androidPath);

        state.AddLog($"Scanning PC source directory: {pcPath}");
        var remoteManifest = await net.GetRemotePcTreeManifestAsync(pcPath, token);
        var remoteFiles = remoteManifest.Files;

        state.AddLog($"Scanning Android target directory: {androidPath}");
        var localManifest = LocalStorageScanner.GetDirectoryTreeManifest(androidPath);
        var localByRelPath = localManifest.Files.ToDictionary(
            f => f.RelativePath,
            f => f,
            StringComparer.OrdinalIgnoreCase);

        var touchedLocalPaths = new List<string>();
        int totalFiles = remoteFiles.Count;
        long totalBytes = remoteFiles.Sum(f => f.Size);
        long completedBytes = 0;
        int processed = 0;

        foreach (var remoteFile in remoteFiles)
        {
            token.ThrowIfCancellationRequested();

            string relPath = remoteFile.RelativePath.Replace('\\', '/');
            if (checkLongFileNames && FileNameLengthHelper.IsFileNameTooLong(relPath, out int byteCount))
            {
                processed++;
                completedBytes += remoteFile.Size;
                double skipPct = FileHashHelper.CalculateOverallPercent(completedBytes, totalBytes, processed, totalFiles);
                state.AddLog($"Skipped (Filename > 255 UTF-8 bytes [{byteCount} B]): {relPath} | Overall: {skipPct:F1}% ({processed}/{totalFiles})");
                state.UpdateProgress((int)Math.Round(skipPct));
                continue;
            }

            string targetLocalPath = $"{androidPath}/{relPath}";
            bool shouldPull = true;

            if (localByRelPath.TryGetValue(relPath, out var existingLocal))
            {
                if (existingLocal.Size == remoteFile.Size)
                {
                    shouldPull = false;
                    processed++;
                    completedBytes += remoteFile.Size;
                    double skipPct = FileHashHelper.CalculateOverallPercent(completedBytes, totalBytes, processed, totalFiles);
                    state.AddLog($"Skipped (Identical): {relPath} | Overall: {skipPct:F1}% ({processed}/{totalFiles})");
                    state.UpdateProgress((int)Math.Round(skipPct));
                }
            }

            if (shouldPull)
            {
                long baseCompleted = completedBytes;
                int nextIdx = processed + 1;
                state.AddLog($"Pulling from PC: {relPath} ({FileHashHelper.FormatByteSize(remoteFile.Size)})");
                var (bytesPulled, elapsedSec, _) = await net.PullFileFromPcAsync(
                    remoteFile.FullPath,
                    targetLocalPath,
                    remoteFile.Size,
                    remoteFile.LastWriteTimeUtcTicks,
                    ConflictPolicy.Overwrite,
                    token,
                    (inFlightBytes, liveSpeed) =>
                    {
                        double livePct = FileHashHelper.CalculateOverallPercent(baseCompleted + inFlightBytes, totalBytes, processed, totalFiles);
                        state.UpdateProgress((int)Math.Round(livePct));
                        state.AddLog($"Transferring: {relPath} | Speed: {liveSpeed} | Overall: {livePct:F1}% ({nextIdx}/{totalFiles})");
                    });
                touchedLocalPaths.Add(targetLocalPath);

                processed++;
                completedBytes += remoteFile.Size;
                double donePct = FileHashHelper.CalculateOverallPercent(completedBytes, totalBytes, processed, totalFiles);
                string avgSpeed = FileHashHelper.FormatTransferSpeed(bytesPulled > 0 ? bytesPulled : remoteFile.Size, elapsedSec);
                state.AddLog($"Transferred: {relPath} ({FileHashHelper.FormatByteSize(remoteFile.Size)}) | Speed: {avgSpeed} | Overall: {donePct:F1}% ({processed}/{totalFiles})");
                state.UpdateProgress((int)Math.Round(donePct));
            }
        }

        if (isMirMode)
        {
            var pcRelSet = new HashSet<string>(
                remoteFiles.Select(f => f.RelativePath.Replace('\\', '/')),
                StringComparer.OrdinalIgnoreCase);

            var extraLocalFiles = localManifest.Files
                .Where(f => !pcRelSet.Contains(f.RelativePath.Replace('\\', '/')))
                .ToList();

            if (extraLocalFiles.Count > 0)
            {
                bool proceedDelete = true;
                if (confirmDeleteCallback != null)
                {
                    proceedDelete = await confirmDeleteCallback(extraLocalFiles.Count);
                }

                if (proceedDelete)
                {
                    int delTotal = extraLocalFiles.Count;
                    int delProcessed = 0;
                    foreach (var extra in extraLocalFiles)
                    {
                        token.ThrowIfCancellationRequested();
                        state.AddLog($"Deleting Android extra file (/MIR): {extra.RelativePath}");
                        try
                        {
                            if (File.Exists(extra.FullPath))
                            {
                                File.Delete(extra.FullPath);
                                touchedLocalPaths.Add(extra.FullPath);
                            }
                        }
                        catch (Exception ex)
                        {
                            state.AddLog($"Warning: Failed to delete '{extra.RelativePath}': {ex.Message}");
                        }
                        delProcessed++;
                        state.UpdateProgress(70 + (delProcessed * 30) / (delTotal > 0 ? delTotal : 1));
                    }

                    LocalStorageScanner.CleanEmptySubdirectories(androidPath);
                }
                else
                {
                    state.AddLog($"Skipped deleting {extraLocalFiles.Count} extra file(s) by user choice.");
                }
            }
        }

        if (touchedLocalPaths.Count > 0)
        {
            MediaScannerHelper.NotifyFilesChanged(Android.App.Application.Context, touchedLocalPaths);
        }

        state.AddLog(isMirMode
            ? "Mirror Sync (PC ➔ Android) completed successfully."
            : "Incremental Merge Sync (PC ➔ Android) completed successfully.");
        state.UpdateProgress(100);
    }

    /// <summary>
    /// Synchronizes from Android (Source) to Windows PC (Target).
    /// When isMirMode is true, deletes extra files in PC target folder that do not exist on Android.
    /// </summary>
    public async Task SyncAndroidToPcAsync(
        string androidPath,
        string pcPath,
        bool isMirMode,
        bool checkLongFileNames,
        Func<int, Task<bool>>? confirmDeleteCallback,
        CancellationToken token)
    {
        var state = SyncStateService.Instance;
        var net = CompanionNetworkService.Instance;

        androidPath = androidPath.Trim().Replace('\\', '/').TrimEnd('/');
        pcPath = pcPath.Trim().TrimEnd('/', '\\');

        if (!Directory.Exists(androidPath))
        {
            throw new DirectoryNotFoundException($"Android source folder does not exist: {androidPath}");
        }

        if (isMirMode && LocalStorageScanner.IsDangerousRootPath(pcPath))
        {
            throw new InvalidOperationException(LocalizationService.Instance.T(
                $"Safety Block: Cannot use root drive '{pcPath}' as /MIR target.",
                $"安全拦截：禁止将磁盘根目录 '{pcPath}' 直接作为 /MIR 镜像目标目录。",
                $"安全攔截：禁止將磁碟根目錄 '{pcPath}' 直接作為 /MIR 鏡像目標目錄。"));
        }

        state.AddLog($"Scanning Android source directory: {androidPath}");
        var localManifest = LocalStorageScanner.GetDirectoryTreeManifest(androidPath);
        var localFiles = localManifest.Files;

        state.AddLog($"Scanning PC target directory: {pcPath}");
        var remoteManifest = await net.GetRemotePcTreeManifestAsync(pcPath, token);
        var remoteByRelPath = remoteManifest.Files.ToDictionary(
            f => f.RelativePath.Replace('\\', '/'),
            f => f,
            StringComparer.OrdinalIgnoreCase);

        int totalFiles = localFiles.Count;
        long totalBytes = localFiles.Sum(f => f.Size);
        long completedBytes = 0;
        int processed = 0;
        var syncedRelPathsForMir = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var localEntry in localFiles)
        {
            token.ThrowIfCancellationRequested();

            string relPath = FileNameLengthHelper.SanitizeRelativePathForWindows(localEntry.RelativePath, out bool sanitized);
            if (sanitized)
            {
                state.AddLog($"Sanitized Windows filename: '{localEntry.RelativePath}' ➔ '{relPath}'");
            }

            if (checkLongFileNames && FileNameLengthHelper.IsFileNameTooLong(relPath, out int byteCount))
            {
                processed++;
                completedBytes += localEntry.Size;
                double skipPct = FileHashHelper.CalculateOverallPercent(completedBytes, totalBytes, processed, totalFiles);
                state.AddLog($"Skipped (Filename > 255 UTF-8 bytes [{byteCount} B]): {relPath} | Overall: {skipPct:F1}% ({processed}/{totalFiles})");
                state.UpdateProgress((int)Math.Round(skipPct));
                continue;
            }

            syncedRelPathsForMir.Add(relPath);

            bool shouldPush = true;
            if (remoteByRelPath.TryGetValue(relPath, out var existingRemote))
            {
                if (existingRemote.Size == localEntry.Size)
                {
                    shouldPush = false;
                    processed++;
                    completedBytes += localEntry.Size;
                    double skipPct = FileHashHelper.CalculateOverallPercent(completedBytes, totalBytes, processed, totalFiles);
                    state.AddLog($"Skipped (Identical): {relPath} | Overall: {skipPct:F1}% ({processed}/{totalFiles})");
                    state.UpdateProgress((int)Math.Round(skipPct));
                }
            }

            if (shouldPush)
            {
                long baseCompleted = completedBytes;
                int nextIdx = processed + 1;
                string targetRemotePath = $"{pcPath.Replace('/', '\\')}\\{relPath.Replace('/', '\\')}";
                state.AddLog($"Pushing to PC: {relPath} ({FileHashHelper.FormatByteSize(localEntry.Size)})");
                var fi = new FileInfo(localEntry.FullPath);
                var (bytesSent, elapsedSec, _) = await net.PushFileToPcAsync(
                    fi,
                    targetRemotePath,
                    ConflictPolicy.Overwrite,
                    token,
                    (inFlightBytes, liveSpeed) =>
                    {
                        double livePct = FileHashHelper.CalculateOverallPercent(baseCompleted + inFlightBytes, totalBytes, processed, totalFiles);
                        state.UpdateProgress((int)Math.Round(livePct));
                        state.AddLog($"Transferring: {relPath} | Speed: {liveSpeed} | Overall: {livePct:F1}% ({nextIdx}/{totalFiles})");
                    });

                processed++;
                completedBytes += localEntry.Size;
                double donePct = FileHashHelper.CalculateOverallPercent(completedBytes, totalBytes, processed, totalFiles);
                string avgSpeed = FileHashHelper.FormatTransferSpeed(bytesSent > 0 ? bytesSent : localEntry.Size, elapsedSec);
                state.AddLog($"Transferred: {relPath} ({FileHashHelper.FormatByteSize(localEntry.Size)}) | Speed: {avgSpeed} | Overall: {donePct:F1}% ({processed}/{totalFiles})");
                state.UpdateProgress((int)Math.Round(donePct));
            }
        }

        if (isMirMode)
        {
            var extraRemoteFiles = remoteManifest.Files
                .Where(r => !syncedRelPathsForMir.Contains(r.RelativePath.Replace('\\', '/')))
                .ToList();

            if (extraRemoteFiles.Count > 0)
            {
                bool proceedDelete = true;
                if (confirmDeleteCallback != null)
                {
                    proceedDelete = await confirmDeleteCallback(extraRemoteFiles.Count);
                }

                if (proceedDelete)
                {
                    int delTotal = extraRemoteFiles.Count;
                    int delProcessed = 0;
                    foreach (var extra in extraRemoteFiles)
                    {
                        token.ThrowIfCancellationRequested();
                        state.AddLog($"Deleting PC extra file (/MIR): {extra.RelativePath}");
                        await net.DeleteRemotePcFileAsync(extra.FullPath, token);
                        delProcessed++;
                        state.UpdateProgress(70 + (delProcessed * 30) / (delTotal > 0 ? delTotal : 1));
                    }
                }
                else
                {
                    state.AddLog($"Skipped deleting {extraRemoteFiles.Count} extra PC file(s) by user choice.");
                }
            }
        }

        state.AddLog(isMirMode
            ? "Mirror Sync (Android ➔ PC) completed successfully."
            : "Incremental Merge Sync (Android ➔ PC) completed successfully.");
        state.UpdateProgress(100);
    }

    /// <summary>
    /// Executes Non-MIR direct file and folder transfer in either direction (Android ➔ PC or PC ➔ Android).
    /// Never deletes existing files in the destination folder.
    /// </summary>
    public async Task ExecuteDirectTransferAsync(
        IReadOnlyList<TransferQueueItem> queue,
        string targetDir,
        bool isAndroidToPc,
        ConflictPolicy conflictPolicy,
        CancellationToken token)
    {
        var state = SyncStateService.Instance;
        var net = CompanionNetworkService.Instance;

        if (queue.Count == 0)
            throw new InvalidOperationException("Transfer queue is empty.");

        if (net.ConnectedPeer?.ProtocolType == PeerProtocolType.LocalSend)
        {
            if (!isAndroidToPc)
            {
                throw new InvalidOperationException(LocalizationService.Instance.T(
                    "LocalSend protocol does not support browsing/pulling remote folders from phone. Please send files directly from the other device's LocalSend client to this phone.",
                    "LocalSend 协议不支持从手机主动拉取对端目录。请直接从对端设备的 LocalSend 客户端选择文件发送至本手机。",
                    "LocalSend 協議不支援從手機主動拉取對端目錄。請直接從對端裝置的 LocalSend 客戶端選擇檔案發送至本手機。"));
            }

            var settings = SettingsService.Load();
            await LocalSendService.Instance.SendQueueToPeerAsync(
                net.ConnectedPeer,
                queue,
                settings.CheckLongFileNames,
                token);
            return;
        }

        if (isAndroidToPc)
        {
            string cleanPcDir = targetDir.Trim().TrimEnd('/', '\\').Replace('/', '\\');
            var pushPlan = new List<(FileInfo LocalFile, string RelativeDestPath, string RemoteTargetPath)>();

            foreach (var item in queue)
            {
                token.ThrowIfCancellationRequested();
                if (!item.IsDirectory)
                {
                    var fi = new FileInfo(item.FullPath);
                    if (!fi.Exists)
                    {
                        state.AddLog($"Warning: Local file not found: {item.FullPath}");
                        continue;
                    }
                    string safeName = FileNameLengthHelper.SanitizeRelativePathForWindows(fi.Name, out _);
                    string remoteTarget = $"{cleanPcDir}\\{safeName}";
                    pushPlan.Add((fi, safeName, remoteTarget));
                }
                else
                {
                    var di = new DirectoryInfo(item.FullPath);
                    if (!di.Exists)
                    {
                        state.AddLog($"Warning: Local folder not found: {item.FullPath}");
                        continue;
                    }
                    string folderName = FileNameLengthHelper.SanitizeRelativePathForWindows(di.Name, out _);
                    var manifest = LocalStorageScanner.GetDirectoryTreeManifest(di.FullName);
                    foreach (var subFile in manifest.Files)
                    {
                        var fi = new FileInfo(subFile.FullPath);
                        if (!fi.Exists) continue;
                        string safeRel = FileNameLengthHelper.SanitizeRelativePathForWindows(subFile.RelativePath, out _);
                        string displayRel = $"{folderName}/{safeRel.Replace('\\', '/')}";
                        string remoteTarget = $"{cleanPcDir}\\{folderName}\\{safeRel.Replace('/', '\\')}";
                        pushPlan.Add((fi, displayRel, remoteTarget));
                    }
                }
            }

            int totalFiles = pushPlan.Count;
            long totalBytes = pushPlan.Sum(x => x.LocalFile.Length);
            long completedBytes = 0;
            int processed = 0;

            foreach (var (fi, relPath, remoteTarget) in pushPlan)
            {
                token.ThrowIfCancellationRequested();
                long baseCompleted = completedBytes;
                int nextIdx = processed + 1;

                state.AddLog($"Sending file to device: {relPath} ({FileHashHelper.FormatByteSize(fi.Length)})");
                var (bytesSent, elapsedSec, skipped) = await net.PushFileToPcAsync(
                    fi,
                    remoteTarget,
                    conflictPolicy,
                    token,
                    (inFlightBytes, liveSpeed) =>
                    {
                        double livePct = FileHashHelper.CalculateOverallPercent(baseCompleted + inFlightBytes, totalBytes, processed, totalFiles);
                        state.UpdateProgress((int)Math.Round(livePct));
                        state.AddLog($"Transferring: {relPath} | Speed: {liveSpeed} | Overall: {livePct:F1}% ({nextIdx}/{totalFiles})");
                    });

                processed++;
                completedBytes += fi.Length;
                double donePct = FileHashHelper.CalculateOverallPercent(completedBytes, totalBytes, processed, totalFiles);
                if (skipped)
                {
                    state.AddLog($"Skipped (Identical): {relPath} | Overall: {donePct:F1}% ({processed}/{totalFiles})");
                }
                else
                {
                    string avgSpeed = FileHashHelper.FormatTransferSpeed(bytesSent > 0 ? bytesSent : fi.Length, elapsedSec);
                    state.AddLog($"Transferred: {relPath} ({FileHashHelper.FormatByteSize(fi.Length)}) | Speed: {avgSpeed} | Overall: {donePct:F1}% ({processed}/{totalFiles})");
                }
                state.UpdateProgress((int)Math.Round(donePct));
            }
        }
        else
        {
            // Device ➔ Android Direct Transfer
            string cleanAndroidDir = targetDir.Trim().Replace('\\', '/').TrimEnd('/');
            Directory.CreateDirectory(cleanAndroidDir);

            var touchedLocalFiles = new List<string>();
            var pullPlan = new List<(string RemoteFullPath, string RelativeDestPath, string LocalTargetPath, long Size, long MtimeTicks)>();

            foreach (var item in queue)
            {
                token.ThrowIfCancellationRequested();
                if (!item.IsDirectory)
                {
                    string fileName = Path.GetFileName(item.FullPath.Replace('\\', '/'));
                    string localTarget = $"{cleanAndroidDir}/{fileName}";
                    pullPlan.Add((item.FullPath, fileName, localTarget, item.Size > 0 ? item.Size : 0, 0));
                }
                else
                {
                    string folderName = Path.GetFileName(item.FullPath.TrimEnd('/', '\\').Replace('\\', '/'));
                    var remoteTree = await net.GetRemotePcTreeManifestAsync(item.FullPath, token);
                    foreach (var remoteFile in remoteTree.Files)
                    {
                        string rel = remoteFile.RelativePath.Replace('\\', '/');
                        string displayRel = $"{folderName}/{rel}";
                        string localTarget = $"{cleanAndroidDir}/{folderName}/{rel}";
                        pullPlan.Add((remoteFile.FullPath, displayRel, localTarget, remoteFile.Size, remoteFile.LastWriteTimeUtcTicks));
                    }
                }
            }

            int totalFiles = pullPlan.Count;
            long totalBytes = pullPlan.Sum(x => x.Size > 0 ? x.Size : 0);
            long completedBytes = 0;
            int processed = 0;

            foreach (var (remoteFull, relPath, localTarget, expectedSize, mtimeTicks) in pullPlan)
            {
                token.ThrowIfCancellationRequested();
                long baseCompleted = completedBytes;
                int nextIdx = processed + 1;

                state.AddLog($"Pulling from device: {relPath}");
                var (bytesPulled, elapsedSec, skipped) = await net.PullFileFromPcAsync(
                    remoteFull,
                    localTarget,
                    expectedSize > 0 ? expectedSize : -1,
                    mtimeTicks,
                    conflictPolicy,
                    token,
                    (inFlightBytes, liveSpeed) =>
                    {
                        double livePct = FileHashHelper.CalculateOverallPercent(baseCompleted + inFlightBytes, totalBytes, processed, totalFiles);
                        state.UpdateProgress((int)Math.Round(livePct));
                        state.AddLog($"Transferring: {relPath} | Speed: {liveSpeed} | Overall: {livePct:F1}% ({nextIdx}/{totalFiles})");
                    });

                processed++;
                long effectiveSize = expectedSize > 0 ? expectedSize : bytesPulled;
                completedBytes += effectiveSize;
                double donePct = FileHashHelper.CalculateOverallPercent(completedBytes, totalBytes, processed, totalFiles);

                if (skipped)
                {
                    state.AddLog($"Skipped (Identical): {relPath} | Overall: {donePct:F1}% ({processed}/{totalFiles})");
                }
                else
                {
                    touchedLocalFiles.Add(localTarget);
                    string avgSpeed = FileHashHelper.FormatTransferSpeed(bytesPulled > 0 ? bytesPulled : effectiveSize, elapsedSec);
                    state.AddLog($"Transferred: {relPath} ({FileHashHelper.FormatByteSize(effectiveSize)}) | Speed: {avgSpeed} | Overall: {donePct:F1}% ({processed}/{totalFiles})");
                }
                state.UpdateProgress((int)Math.Round(donePct));
            }

            if (touchedLocalFiles.Count > 0)
            {
                MediaScannerHelper.NotifyFilesChanged(Android.App.Application.Context, touchedLocalFiles);
            }
        }

        state.AddLog(isAndroidToPc
            ? "Direct transfer to device completed successfully."
            : "Direct pull from device completed successfully.");
        state.UpdateProgress(100);
    }
}
