using System;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace SyncDroidAndroid.Helpers;

public static class FileHashHelper
{
    public static async Task<string> ComputeSha256Async(string filePath, CancellationToken ct = default)
    {
        await using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 65536, useAsync: true);
        byte[] hash = await SHA256.HashDataAsync(fs, ct).ConfigureAwait(false);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static bool IsLoopbackOrDisallowedEndpoint(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;

        string trimmed = value.Trim();
        bool hadScheme = false;
        if (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed.Substring("http://".Length);
            hadScheme = true;
        }
        else if (trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed.Substring("https://".Length);
            hadScheme = true;
        }
        trimmed = trimmed.TrimEnd('/');

        string host = trimmed;
        bool hasPort = false;
        int colonIdx = trimmed.LastIndexOf(':');
        if (colonIdx > 0 && int.TryParse(trimmed.Substring(colonIdx + 1), out _))
        {
            host = trimmed.Substring(0, colonIdx).Trim('[', ']');
            hasPort = true;
        }

        if (IPAddress.TryParse(host, out var ip))
        {
            if (IPAddress.IsLoopback(ip) || ip.Equals(IPAddress.Any) || ip.Equals(IPAddress.IPv6Any))
            {
                return true;
            }
            return false;
        }

        if (hasPort || hadScheme)
        {
            return true;
        }

        return false;
    }

    public static string FormatByteSize(long bytes)
    {
        if (bytes < 0) bytes = 0;
        if (bytes < 1024) return $"{bytes} B";
        double kb = bytes / 1024.0;
        if (kb < 1024) return $"{kb:F1} KB";
        double mb = kb / 1024.0;
        if (mb < 1024) return $"{mb:F2} MB";
        double gb = mb / 1024.0;
        return $"{gb:F2} GB";
    }

    public static string FormatTransferSpeed(long bytesTransferred, double elapsedSeconds)
    {
        if (bytesTransferred <= 0) return "0 B/s";
        if (elapsedSeconds < 0.001) elapsedSeconds = 0.001;
        double bytesPerSec = bytesTransferred / elapsedSeconds;
        if (bytesPerSec < 1024) return $"{bytesPerSec:F0} B/s";
        double kbPerSec = bytesPerSec / 1024.0;
        if (kbPerSec < 1024) return $"{kbPerSec:F1} KB/s";
        double mbPerSec = kbPerSec / 1024.0;
        if (mbPerSec < 1024) return $"{mbPerSec:F2} MB/s";
        double gbPerSec = mbPerSec / 1024.0;
        return $"{gbPerSec:F2} GB/s";
    }

    public static double CalculateOverallPercent(long completedBytes, long totalBytes, int processedFiles, int totalFiles)
    {
        if (totalBytes > 0)
        {
            return Math.Clamp((completedBytes * 100.0) / totalBytes, 0.0, 100.0);
        }
        if (totalFiles > 0)
        {
            return Math.Clamp((processedFiles * 100.0) / totalFiles, 0.0, 100.0);
        }
        return 100.0;
    }
}
