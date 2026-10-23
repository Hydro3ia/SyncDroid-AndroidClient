using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using SyncDroidAndroid.Models;

namespace SyncDroidAndroid.Services;

public static class SettingsService
{
    private const int MaxHistoryCount = 15;
    private static readonly object FileLock = new();

    public static event EventHandler? TrustedDevicesChanged;

    private static readonly string SettingsFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SyncDroidAndroid",
        "settings.json");

    public static AppSettings Load()
    {
        lock (FileLock)
        {
            AppSettings settings = new();
            try
            {
                if (File.Exists(SettingsFile))
                {
                    var json = File.ReadAllText(SettingsFile);
                    settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
                }
            }
            catch
            {
            }

            settings.PcPathHistory ??= new List<string>();
            settings.AndroidPathHistory ??= new List<string>();
            settings.PcEndpointHistory ??= new List<string>();
            settings.TrustedDevices ??= new List<TrustedDevice>();

            if (string.IsNullOrWhiteSpace(settings.Language))
                settings.Language = "zh-CN";
            if (string.IsNullOrWhiteSpace(settings.ThemeMode))
                settings.ThemeMode = "System";
            if (settings.CompanionPort <= 1024 || settings.CompanionPort > 65535)
                settings.CompanionPort = 45821;
            if (settings.LocalSendPort <= 1024 || settings.LocalSendPort > 65535)
                settings.LocalSendPort = 53317;
            if (string.IsNullOrWhiteSpace(settings.LocalSendReceiveDir))
                settings.LocalSendReceiveDir = "/storage/emulated/0/Download/SyncDroid";

            AddToHistoryList(settings.PcPathHistory, settings.AndroidToPc_PcPath, ignoreCase: true);
            AddToHistoryList(settings.PcPathHistory, settings.PcToAndroid_PcPath, ignoreCase: true);
            AddToHistoryList(settings.PcPathHistory, settings.DirectTransfer_PcTargetDir, ignoreCase: true);

            AddToHistoryList(settings.AndroidPathHistory, settings.AndroidToPc_AndroidPath, ignoreCase: false);
            AddToHistoryList(settings.AndroidPathHistory, settings.PcToAndroid_AndroidPath, ignoreCase: false);
            AddToHistoryList(settings.AndroidPathHistory, settings.DirectTransfer_AndroidTargetDir, ignoreCase: false);

            if (!string.IsNullOrWhiteSpace(settings.LastConnectedPcEndpoint))
            {
                AddToHistoryList(settings.PcEndpointHistory, settings.LastConnectedPcEndpoint, ignoreCase: true);
            }

            return settings;
        }
    }

    public static void Save(AppSettings settings)
    {
        lock (FileLock)
        {
            try
            {
                var dir = Path.GetDirectoryName(SettingsFile);
                if (dir != null) Directory.CreateDirectory(dir);
                var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(SettingsFile, json);
            }
            catch
            {
            }
        }
    }

    public static void AddToHistoryList(List<string> list, string? item, bool ignoreCase)
    {
        if (string.IsNullOrWhiteSpace(item)) return;
        var trimmed = item.Trim();

        var comparer = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        list.RemoveAll(x => string.Equals(x, trimmed, comparer));
        list.Insert(0, trimmed);

        if (list.Count > MaxHistoryCount)
        {
            list.RemoveRange(MaxHistoryCount, list.Count - MaxHistoryCount);
        }
    }

    public static string NormalizeHostIp(string? endpointOrIp)
    {
        if (string.IsNullOrWhiteSpace(endpointOrIp)) return "";
        string trimmed = endpointOrIp.Trim();
        if (trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            trimmed = trimmed.Substring("https://".Length);
        else if (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            trimmed = trimmed.Substring("http://".Length);
        trimmed = trimmed.TrimEnd('/');

        int colonIdx = trimmed.LastIndexOf(':');
        if (colonIdx > 0 && int.TryParse(trimmed.Substring(colonIdx + 1), out _))
        {
            trimmed = trimmed.Substring(0, colonIdx);
        }
        return trimmed;
    }

    public static bool IsDeviceTrusted(string deviceType, string? fingerprint, string? ipOrEndpoint, string? deviceName = null)
    {
        var settings = Load();
        string hostIp = NormalizeHostIp(ipOrEndpoint);

        foreach (var d in settings.TrustedDevices)
        {
            if (!string.Equals(d.DeviceType, deviceType, StringComparison.OrdinalIgnoreCase))
                continue;

            if (!string.IsNullOrWhiteSpace(fingerprint) &&
                !string.IsNullOrWhiteSpace(d.Fingerprint) &&
                string.Equals(d.Fingerprint, fingerprint.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            string savedHostIp = NormalizeHostIp(d.IpAddress);
            if (!string.IsNullOrWhiteSpace(hostIp) &&
                string.Equals(savedHostIp, hostIp, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public static void AddTrustedDevice(string deviceType, string name, string ipOrEndpoint, string? fingerprint = null)
    {
        var settings = Load();
        string hostIp = NormalizeHostIp(ipOrEndpoint);
        string cleanFp = fingerprint?.Trim() ?? "";
        string cleanName = string.IsNullOrWhiteSpace(name)
            ? (string.IsNullOrWhiteSpace(hostIp) ? "Device" : hostIp)
            : name.Trim();

        settings.TrustedDevices.RemoveAll(d =>
            string.Equals(d.DeviceType, deviceType, StringComparison.OrdinalIgnoreCase) &&
            ((!string.IsNullOrWhiteSpace(cleanFp) && string.Equals(d.Fingerprint, cleanFp, StringComparison.OrdinalIgnoreCase)) ||
             (!string.IsNullOrWhiteSpace(hostIp) && string.Equals(NormalizeHostIp(d.IpAddress), hostIp, StringComparison.OrdinalIgnoreCase))));

        string idKey = !string.IsNullOrWhiteSpace(cleanFp) ? cleanFp : hostIp;
        settings.TrustedDevices.Insert(0, new TrustedDevice
        {
            Id = $"{deviceType.ToLowerInvariant()}:{idKey}",
            Name = cleanName,
            IpAddress = string.IsNullOrWhiteSpace(ipOrEndpoint) ? hostIp : ipOrEndpoint.Trim(),
            DeviceType = deviceType,
            Fingerprint = cleanFp,
            AuthenticatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
        });

        Save(settings);
        TrustedDevicesChanged?.Invoke(null, EventArgs.Empty);
    }

    public static void RemoveTrustedDevice(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return;
        var settings = Load();
        int removed = settings.TrustedDevices.RemoveAll(d => string.Equals(d.Id, id, StringComparison.OrdinalIgnoreCase));
        if (removed > 0)
        {
            Save(settings);
            TrustedDevicesChanged?.Invoke(null, EventArgs.Empty);
        }
    }

    public static void ClearTrustedDevices()
    {
        var settings = Load();
        if (settings.TrustedDevices.Count > 0)
        {
            settings.TrustedDevices.Clear();
            Save(settings);
            TrustedDevicesChanged?.Invoke(null, EventArgs.Empty);
        }
    }
}
