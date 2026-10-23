using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SyncDroidAndroid.Models;

public enum ConflictPolicy
{
    SkipIdenticalSize = 0,
    Overwrite = 1,
    KeepBothRename = 2
}

public enum PeerProtocolType
{
    SyncDroidCompanion = 0,
    LocalSend = 1
}

public class RemoteEntryDto
{
    public string Name { get; set; } = "";
    public string FullPath { get; set; } = "";
    public string RelativePath { get; set; } = "";
    public long Size { get; set; }
    public long LastWriteTimeUtcTicks { get; set; }
    public bool IsDirectory { get; set; }
}

public class DirectoryListingResponse
{
    public string CurrentPath { get; set; } = "";
    public string ParentPath { get; set; } = "";
    public List<RemoteEntryDto> Entries { get; set; } = new();
}

public class TreeManifestResponse
{
    public string RootPath { get; set; } = "";
    public List<RemoteEntryDto> Files { get; set; } = new();
}

public class CompanionInfoResponse
{
    public string DeviceName { get; set; } = "";
    public string Platform { get; set; } = "Android";
    public string Version { get; set; } = "0.2.0.0";
    public int ListeningPort { get; set; } = 45821;
    public long FreeSpaceBytes { get; set; }
    public string DefaultStorageRoot { get; set; } = "/storage/emulated/0";
}

public class CompanionPeer
{
    public string DeviceName { get; set; } = "";
    public string Platform { get; set; } = "Windows";
    public string Version { get; set; } = "0.2.0.0";
    public string IpAddress { get; set; } = "";
    public int Port { get; set; } = 45821;
    public PeerProtocolType ProtocolType { get; set; } = PeerProtocolType.SyncDroidCompanion;
    public string Scheme { get; set; } = "http";
    public DateTime LastSeenUtc { get; set; } = DateTime.UtcNow;

    public string Endpoint => $"{IpAddress}:{Port}";
    public string ProtocolBadge => ProtocolType == PeerProtocolType.LocalSend ? "LocalSend" : "SyncDroid";
}

public class TransferQueueItem
{
    public string FullPath { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public long Size { get; set; }
    public bool IsDirectory { get; set; }
    public bool IsRemotePcItem { get; set; }

    public string FormattedMeta => IsDirectory
        ? (IsRemotePcItem ? $"[Remote Folder] {FullPath}" : $"[Android Folder] {FullPath}")
        : $"{FormatSize(Size)}  •  {FullPath}";

    public static string FormatSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024.0):F2} MB";
        return $"{bytes / (1024.0 * 1024.0 * 1024.0):F2} GB";
    }
}

public class TrustedDevice
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string IpAddress { get; set; } = "";
    public string DeviceType { get; set; } = "";
    public string Fingerprint { get; set; } = "";
    public string AuthenticatedAt { get; set; } = "";

    public string DisplayEndpoint => string.IsNullOrWhiteSpace(IpAddress) ? "-" : IpAddress;
}

public class ReceivedFileItem
{
    public string Id { get; set; } = "";
    public string FileName { get; set; } = "";
    public string SenderName { get; set; } = "";
    public string Protocol { get; set; } = "";
    public long Size { get; set; }
    public string SizeDisplay { get; set; } = "";
    public string SavedPath { get; set; } = "";
    public string StatusText { get; set; } = "";
    public double ProgressPercent { get; set; }
}

public class IncomingTransferSessionEventArgs : EventArgs
{
    public string SessionId { get; set; } = "";
    public string SenderName { get; set; } = "";
    public string SenderIp { get; set; } = "";
    public string Protocol { get; set; } = "";
    public string SaveDirectory { get; set; } = "";
    public bool RequiresConfirmation { get; set; }
    public bool AppendToExistingSession { get; set; }
    public List<ReceivedFileItem> Files { get; set; } = new();
    public TaskCompletionSource<bool>? ConfirmationTcs { get; set; }
}
