using System.Collections.Generic;

namespace SyncDroidAndroid.Models;

public class AppSettings
{
    public string PcToAndroid_PcPath { get; set; } = @"D:\SyncFolder";
    public string PcToAndroid_AndroidPath { get; set; } = "/storage/emulated/0/Download/SyncDroid";
    public string AndroidToPc_PcPath { get; set; } = @"D:\SyncFolder";
    public string AndroidToPc_AndroidPath { get; set; } = "/storage/emulated/0/DCIM/Camera";

    public string DirectTransfer_PcTargetDir { get; set; } = @"D:\SyncDroidReceived";
    public string DirectTransfer_AndroidTargetDir { get; set; } = "/storage/emulated/0/Download/SyncDroid";

    public List<string> PcPathHistory { get; set; } = new();
    public List<string> AndroidPathHistory { get; set; } = new();
    public List<string> PcEndpointHistory { get; set; } = new();

    public string Language { get; set; } = "zh-CN";
    public string ThemeMode { get; set; } = "System"; // "System", "Light", "Dark"
    public bool UseDynamicColor { get; set; } = true;

    public bool CompanionServerEnabled { get; set; } = true;
    public int CompanionPort { get; set; } = 45821;
    public string LastConnectedPcEndpoint { get; set; } = "";

    public bool LocalSendEnabled { get; set; } = true;
    public int LocalSendPort { get; set; } = 53317;
    public bool LocalSendAutoAccept { get; set; } = true;
    public string LocalSendReceiveDir { get; set; } = "/storage/emulated/0/Download/SyncDroid";

    public bool ConfirmBeforeMirDelete { get; set; } = true;
    public bool CheckLongFileNames { get; set; } = true;
    public bool VerifyHashAfterTransfer { get; set; } = true;

    // Authenticated / Trusted Devices
    public List<TrustedDevice> TrustedDevices { get; set; } = new();
}
