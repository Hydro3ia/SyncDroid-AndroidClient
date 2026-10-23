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
}
