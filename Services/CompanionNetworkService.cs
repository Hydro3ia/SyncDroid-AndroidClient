using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Android.OS;
using SyncDroidAndroid.Helpers;
using SyncDroidAndroid.Models;

namespace SyncDroidAndroid.Services;

public class CompanionNetworkService
{
    public const int DiscoveryUdpPort = 45820;
    public const string DiscoveryMagicProbe = "SYNCDROID_DISCOVER_V1";
    public const string DiscoveryMagicReply = "SYNCDROID_HERE_V1";

    public static CompanionNetworkService Instance { get; } = new();

    private readonly HttpClient _httpClient;
    private readonly Handler _mainHandler = new(Looper.MainLooper!);
    private HttpListener? _httpListener;
    private UdpClient? _udpResponder;
    private CancellationTokenSource? _serverCts;

    public bool IsServerRunning { get; private set; }
    public int ListeningPort { get; private set; } = 45821;
    public CompanionPeer? ConnectedPeer { get; private set; }
    public List<CompanionPeer> DiscoveredPeers { get; } = new();

    public event Action? ConnectionStateChanged;
    public event Action? DiscoveredPeersChanged;

    private CompanionNetworkService()
    {
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(30)
        };
        var settings = SettingsService.Load();
        ListeningPort = settings.CompanionPort;
    }

    public static string GetDeviceFriendlyName()
    {
        string manufacturer = Build.Manufacturer ?? "";
        string model = Build.Model ?? "Android";
        if (model.StartsWith(manufacturer, StringComparison.OrdinalIgnoreCase))
            return model;
        return $"{manufacturer} {model}".Trim();
    }

    public static List<string> GetLocalIpv4Addresses()
    {
        var result = new List<string>();
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up ||
                    ni.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                    continue;

                foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                {
                    if (ua.Address.AddressFamily == AddressFamily.InterNetwork &&
                        !IPAddress.IsLoopback(ua.Address))
                    {
                        result.Add(ua.Address.ToString());
                    }
                }
            }
        }
        catch
        {
        }

        if (result.Count == 0)
        {
            result.Add("127.0.0.1");
        }
        return result;
    }

    public void StartServer(int port)
    {
        StopServer();

        ListeningPort = port;
        _serverCts = new CancellationTokenSource();
        var token = _serverCts.Token;

        try
        {
            _httpListener = new HttpListener();
            _httpListener.Prefixes.Add($"http://*:{port}/");
            _httpListener.Start();
            IsServerRunning = true;

            _ = Task.Run(() => AcceptHttpRequestsLoopAsync(_httpListener, token), token);
            _ = Task.Run(() => RunUdpDiscoveryResponderLoopAsync(port, token), token);

            SyncStateService.Instance.AddLog($"Companion Server listening on TCP port {port} (UDP discovery {DiscoveryUdpPort})");
        }
        catch (Exception ex)
        {
            IsServerRunning = false;
            SyncStateService.Instance.AddLog($"Failed to start Companion Server on port {port}: {ex.Message}");
        }

        var settings = SettingsService.Load();
        if (settings.LocalSendEnabled)
        {
            LocalSendService.Instance.Start(settings.LocalSendPort);
        }

        NotifyConnectionStateChanged();
    }

    public void StopServer()
    {
        LocalSendService.Instance.Stop();

        try
        {
            _serverCts?.Cancel();
            _serverCts?.Dispose();
            _serverCts = null;
        }
        catch { }

        try
        {
            _httpListener?.Stop();
            _httpListener?.Close();
            _httpListener = null;
        }
        catch { }

        try
        {
            _udpResponder?.Close();
            _udpResponder = null;
        }
        catch { }

        if (IsServerRunning)
        {
            IsServerRunning = false;
            SyncStateService.Instance.AddLog("Companion Server stopped.");
            NotifyConnectionStateChanged();
        }
    }

    private async Task RunUdpDiscoveryResponderLoopAsync(int tcpPort, CancellationToken token)
    {
        try
        {
            _udpResponder = new UdpClient();
            _udpResponder.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _udpResponder.Client.Bind(new IPEndPoint(IPAddress.Any, DiscoveryUdpPort));

            while (!token.IsCancellationRequested)
            {
                var received = await _udpResponder.ReceiveAsync(token);
                string msg = Encoding.UTF8.GetString(received.Buffer).Trim();

                if (msg.StartsWith(DiscoveryMagicProbe, StringComparison.Ordinal))
                {
                    string reply = $"{DiscoveryMagicReply}|{GetDeviceFriendlyName()}|Android|0.1.2.0|{tcpPort}";
                    byte[] replyBytes = Encoding.UTF8.GetBytes(reply);
                    await _udpResponder.SendAsync(replyBytes, replyBytes.Length, received.RemoteEndPoint);

                    // Also parse if sender included its own peer info
                    ParseAndRegisterPeer(msg, received.RemoteEndPoint.Address.ToString());
                }
                else if (msg.StartsWith(DiscoveryMagicReply, StringComparison.Ordinal))
                {
                    ParseAndRegisterPeer(msg, received.RemoteEndPoint.Address.ToString());
                }
            }
        }
        catch
        {
        }
    }

    private void ParseAndRegisterPeer(string packet, string remoteIp)
    {
        var localIps = GetLocalIpv4Addresses();
        if (localIps.Contains(remoteIp)) return;

        var parts = packet.Split('|');
        if (parts.Length >= 5 && int.TryParse(parts[4], out int peerPort))
        {
            var peer = new CompanionPeer
            {
                DeviceName = parts[1],
                Platform = parts[2],
                Version = parts[3],
                IpAddress = remoteIp,
                Port = peerPort,
                ProtocolType = PeerProtocolType.SyncDroidCompanion,
                Scheme = "http",
                LastSeenUtc = DateTime.UtcNow
            };

            RegisterDiscoveredPeer(peer);
        }
    }

    public void RegisterDiscoveredPeer(CompanionPeer peer)
    {
        _mainHandler.Post(() =>
        {
            DiscoveredPeers.RemoveAll(p => string.Equals(p.Endpoint, peer.Endpoint, StringComparison.OrdinalIgnoreCase));
            DiscoveredPeers.Insert(0, peer);
            DiscoveredPeersChanged?.Invoke();
        });
    }

    public async Task ScanLanForPeersAsync()
    {
        _ = LocalSendService.Instance.SendMulticastAnnouncementAsync(announce: true);

        try
        {
            using var udp = new UdpClient();
            udp.EnableBroadcast = true;
            string probe = $"{DiscoveryMagicProbe}|{GetDeviceFriendlyName()}|Android|0.1.2.0|{ListeningPort}";
            byte[] data = Encoding.UTF8.GetBytes(probe);

            await udp.SendAsync(data, data.Length, new IPEndPoint(IPAddress.Broadcast, DiscoveryUdpPort));

            // Also send to subnet broadcast addresses
            foreach (var ip in GetLocalIpv4Addresses())
            {
                var segments = ip.Split('.');
                if (segments.Length == 4)
                {
                    string subnetBroadcast = $"{segments[0]}.{segments[1]}.{segments[2]}.255";
                    if (IPAddress.TryParse(subnetBroadcast, out var bcastAddr))
                    {
                        await udp.SendAsync(data, data.Length, new IPEndPoint(bcastAddr, DiscoveryUdpPort));
                    }
                }
            }

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1.8));
            while (!cts.IsCancellationRequested)
            {
                var result = await udp.ReceiveAsync(cts.Token);
                string reply = Encoding.UTF8.GetString(result.Buffer).Trim();
                if (reply.StartsWith(DiscoveryMagicReply, StringComparison.Ordinal))
                {
                    ParseAndRegisterPeer(reply, result.RemoteEndPoint.Address.ToString());
                }
            }
        }
        catch
        {
        }
    }

    public async Task<(bool Success, string Message)> ConnectToPcAsync(string rawEndpoint)
    {
        if (string.IsNullOrWhiteSpace(rawEndpoint))
            return (false, "Endpoint cannot be empty.");

        string clean = rawEndpoint.Trim()
            .Replace("http://", "", StringComparison.OrdinalIgnoreCase)
            .Replace("https://", "", StringComparison.OrdinalIgnoreCase)
            .TrimEnd('/');

        string host = clean;
        int port = 45821;
        int colonIdx = clean.LastIndexOf(':');
        if (colonIdx > 0 && int.TryParse(clean.Substring(colonIdx + 1), out int parsedPort))
        {
            host = clean.Substring(0, colonIdx);
            port = parsedPort;
        }

        // If a discovered LocalSend peer matches this endpoint or port is 53317, try LocalSend first
        var knownLocalSend = DiscoveredPeers.FirstOrDefault(p =>
            p.ProtocolType == PeerProtocolType.LocalSend &&
            string.Equals(p.IpAddress, host, StringComparison.OrdinalIgnoreCase) &&
            p.Port == port);

        if (knownLocalSend != null || port == LocalSendService.DefaultPort)
        {
            var lsPeer = await LocalSendService.Instance.ProbeLocalSendPeerAsync(host, port) ?? knownLocalSend;
            if (lsPeer != null)
            {
                SetConnectedPeer(lsPeer);
                return (true, $"Connected to [LocalSend] {lsPeer.DeviceName} ({lsPeer.Endpoint})");
            }
        }

        string url = $"http://{host}:{port}/api/v1/info";
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(4));
            var resp = await _httpClient.GetAsync(url, cts.Token);
            resp.EnsureSuccessStatusCode();
            var json = await resp.Content.ReadAsStringAsync(cts.Token);
            var info = JsonSerializer.Deserialize<CompanionInfoResponse>(json, JsonOptions);

            var peer = new CompanionPeer
            {
                DeviceName = string.IsNullOrWhiteSpace(info?.DeviceName) ? host : info.DeviceName,
                Platform = info?.Platform ?? "Windows",
                Version = info?.Version ?? "0.1.3.0",
                IpAddress = host,
                Port = port,
                ProtocolType = PeerProtocolType.SyncDroidCompanion,
                Scheme = "http",
                LastSeenUtc = DateTime.UtcNow
            };

            SetConnectedPeer(peer);
            return (true, $"Connected to {peer.DeviceName} ({peer.Endpoint})");
        }
        catch (Exception ex)
        {
            // Fallback: probe whether host:port (or host:53317) is a LocalSend node
            var fallbackLs = await LocalSendService.Instance.ProbeLocalSendPeerAsync(host, port);
            if (fallbackLs != null)
            {
                SetConnectedPeer(fallbackLs);
                return (true, $"Connected to [LocalSend] {fallbackLs.DeviceName} ({fallbackLs.Endpoint})");
            }

            return (false, $"Connection to {host}:{port} failed: {ex.Message}");
        }
    }

    private void SetConnectedPeer(CompanionPeer peer)
    {
        ConnectedPeer = peer;
        RegisterDiscoveredPeer(peer);

        var settings = SettingsService.Load();
        settings.LastConnectedPcEndpoint = peer.Endpoint;
        SettingsService.AddToHistoryList(settings.PcEndpointHistory, peer.Endpoint, ignoreCase: true);
        SettingsService.Save(settings);

        SyncStateService.Instance.AddLog($"Connected to [{peer.ProtocolBadge}] {peer.DeviceName} ({peer.Endpoint})");
        NotifyConnectionStateChanged();
    }

    public void DisconnectFromPc()
    {
        if (ConnectedPeer != null)
        {
            SyncStateService.Instance.AddLog($"Disconnected from PC ({ConnectedPeer.Endpoint}).");
            ConnectedPeer = null;
            NotifyConnectionStateChanged();
        }
    }

    // =========================================================================
    // HTTP Server Request Handling (When PC SyncDroidWinUI connects to Android)
    // =========================================================================

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private async Task AcceptHttpRequestsLoopAsync(HttpListener listener, CancellationToken token)
    {
        while (!token.IsCancellationRequested && listener.IsListening)
        {
            try
            {
                var context = await listener.GetContextAsync();
                _ = Task.Run(() => HandleHttpContextAsync(context), token);
            }
            catch
            {
                if (!listener.IsListening) break;
            }
        }
    }

    private async Task HandleHttpContextAsync(HttpListenerContext ctx)
    {
        var req = ctx.Request;
        var res = ctx.Response;
        string path = req.Url?.AbsolutePath ?? "/";

        try
        {
            if (path.Equals("/api/v1/info", StringComparison.OrdinalIgnoreCase))
            {
                long freeBytes = 0;
                try
                {
                    var stat = new StatFs("/storage/emulated/0");
                    freeBytes = stat.AvailableBytes;
                }
                catch { }

                var info = new CompanionInfoResponse
                {
                    DeviceName = GetDeviceFriendlyName(),
                    Platform = "Android",
                    Version = "0.1.2.0",
                    ListeningPort = ListeningPort,
                    FreeSpaceBytes = freeBytes,
                    DefaultStorageRoot = "/storage/emulated/0"
                };
                await WriteJsonResponseAsync(res, info);
            }
            else if (path.Equals("/api/v1/storage/list", StringComparison.OrdinalIgnoreCase))
            {
                string dirPath = req.QueryString["path"] ?? "/storage/emulated/0";
                bool includeFiles = string.Equals(req.QueryString["includeFiles"], "true", StringComparison.OrdinalIgnoreCase);
                var listing = LocalStorageScanner.GetDirectoryListing(dirPath, includeFiles);
                await WriteJsonResponseAsync(res, listing);
            }
            else if (path.Equals("/api/v1/storage/tree", StringComparison.OrdinalIgnoreCase))
            {
                string rootPath = req.QueryString["path"] ?? "/storage/emulated/0";
                var manifest = LocalStorageScanner.GetDirectoryTreeManifest(rootPath);
                await WriteJsonResponseAsync(res, manifest);
            }
            else if (path.Equals("/api/v1/storage/mkdir", StringComparison.OrdinalIgnoreCase) && req.HttpMethod == "POST")
            {
                string targetDir = req.QueryString["path"] ?? "";
                if (string.IsNullOrWhiteSpace(targetDir))
                {
                    res.StatusCode = 400;
                    res.Close();
                    return;
                }
                Directory.CreateDirectory(targetDir);
                await WriteJsonResponseAsync(res, new { ok = true, path = targetDir });
            }
            else if (path.Equals("/api/v1/file/pull", StringComparison.OrdinalIgnoreCase) && req.HttpMethod == "GET")
            {
                string filePath = req.QueryString["path"] ?? "";
                if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                {
                    res.StatusCode = 404;
                    res.Close();
                    return;
                }

                var fi = new FileInfo(filePath);
                res.ContentType = "application/octet-stream";
                res.ContentLength64 = fi.Length;
                res.AddHeader("X-LastWriteTimeUtcTicks", fi.LastWriteTimeUtc.Ticks.ToString());

                await using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, useAsync: true);
                await fs.CopyToAsync(res.OutputStream);
                res.Close();
                SyncStateService.Instance.AddLog($"[PC Pull] Sent file: {filePath}");
            }
            else if (path.Equals("/api/v1/file/push", StringComparison.OrdinalIgnoreCase) && req.HttpMethod == "POST")
            {
                string targetPath = req.QueryString["path"] ?? "";
                long.TryParse(req.QueryString["mtimeTicks"], out long mtimeTicks);
                int.TryParse(req.QueryString["conflict"], out int conflictInt);
                var conflict = (ConflictPolicy)conflictInt;

                if (string.IsNullOrWhiteSpace(targetPath))
                {
                    res.StatusCode = 400;
                    res.Close();
                    return;
                }

                string finalPath = ResolveConflictTargetFilePath(targetPath, req.ContentLength64, conflict, out bool skipFile);
                if (skipFile)
                {
                    await WriteJsonResponseAsync(res, new { ok = true, skipped = true, path = finalPath });
                    return;
                }

                string parentDir = Path.GetDirectoryName(finalPath) ?? "/storage/emulated/0/Download";
                Directory.CreateDirectory(parentDir);

                string tempPartPath = Path.Combine(parentDir, $".syncdroid_{Guid.NewGuid():N}.part");
                try
                {
                    await using (var fs = new FileStream(tempPartPath, FileMode.Create, FileAccess.Write, FileShare.None, 65536, useAsync: true))
                    {
                        await req.InputStream.CopyToAsync(fs);
                    }

                    if (req.ContentLength64 >= 0)
                    {
                        var partInfo = new FileInfo(tempPartPath);
                        if (partInfo.Length != req.ContentLength64)
                        {
                            throw new IOException($"Incomplete transfer: expected {req.ContentLength64} bytes, got {partInfo.Length}.");
                        }
                    }

                    File.Move(tempPartPath, finalPath, overwrite: true);

                    if (mtimeTicks > 0)
                    {
                        try
                        {
                            File.SetLastWriteTimeUtc(finalPath, new DateTime(mtimeTicks, DateTimeKind.Utc));
                        }
                        catch { }
                    }

                    MediaScannerHelper.NotifyFilesChanged(Android.App.Application.Context, new[] { finalPath });
                    SyncStateService.Instance.AddLog($"[PC Push] Received: {finalPath}");
                    await WriteJsonResponseAsync(res, new { ok = true, skipped = false, path = finalPath });
                }
                finally
                {
                    if (File.Exists(tempPartPath))
                    {
                        try { File.Delete(tempPartPath); } catch { }
                    }
                }
            }
            else if (path.Equals("/api/v1/file/delete", StringComparison.OrdinalIgnoreCase) && req.HttpMethod == "POST")
            {
                string filePath = req.QueryString["path"] ?? "";
                if (string.IsNullOrWhiteSpace(filePath) || LocalStorageScanner.IsDangerousRootPath(filePath))
                {
                    res.StatusCode = 400;
                    res.Close();
                    return;
                }

                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                    MediaScannerHelper.NotifyFilesChanged(Android.App.Application.Context, new[] { filePath });
                    SyncStateService.Instance.AddLog($"[PC MIR Delete] Removed: {filePath}");
                }
                await WriteJsonResponseAsync(res, new { ok = true });
            }
            else
            {
                res.StatusCode = 404;
                res.Close();
            }
        }
        catch (Exception ex)
        {
            try
            {
                res.StatusCode = 500;
                await WriteJsonResponseAsync(res, new { ok = false, error = ex.Message });
            }
            catch { }
        }
    }

    public static string ResolveConflictTargetFilePath(string targetPath, long incomingSize, ConflictPolicy policy, out bool shouldSkip)
    {
        shouldSkip = false;
        if (!File.Exists(targetPath)) return targetPath;

        if (policy == ConflictPolicy.SkipIdenticalSize)
        {
            var existing = new FileInfo(targetPath);
            if (incomingSize >= 0 && existing.Length == incomingSize)
            {
                shouldSkip = true;
                return targetPath;
            }
            return targetPath;
        }

        if (policy == ConflictPolicy.Overwrite)
        {
            return targetPath;
        }

        // KeepBothRename
        string dir = Path.GetDirectoryName(targetPath) ?? "";
        string stem = Path.GetFileNameWithoutExtension(targetPath);
        string ext = Path.GetExtension(targetPath);
        int index = 1;
        while (true)
        {
            string candidate = Path.Combine(dir, $"{stem} ({index}){ext}").Replace('\\', '/');
            if (!File.Exists(candidate))
            {
                return candidate;
            }
            index++;
        }
    }

    private static async Task WriteJsonResponseAsync<T>(HttpListenerResponse res, T payload)
    {
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions);
        res.ContentType = "application/json; charset=utf-8";
        res.ContentLength64 = bytes.Length;
        await res.OutputStream.WriteAsync(bytes, 0, bytes.Length);
        res.Close();
    }

    // =========================================================================
    // HTTP Client Methods (When Android initiates operations with Windows PC)
    // =========================================================================

    private string EnsureConnectedPcBaseUrl()
    {
        if (ConnectedPeer == null)
        {
            throw new InvalidOperationException(LocalizationService.Instance.T(
                "No remote device connected. Please connect to a device in the Companion tab first.",
                "尚未连接远端设备。请先在「无线伴侣」标签页连接设备。",
                "尚未連接遠端裝置。請先在「無線伴侶」標籤頁連接裝置。"));
        }
        if (ConnectedPeer.ProtocolType == PeerProtocolType.LocalSend)
        {
            throw new InvalidOperationException(LocalizationService.Instance.LocalSendMirrorNotSupportedHint);
        }
        return $"http://{ConnectedPeer.IpAddress}:{ConnectedPeer.Port}";
    }

    public async Task<DirectoryListingResponse> GetRemotePcDirectoryListingAsync(string remotePath, bool includeFiles, CancellationToken token = default)
    {
        string baseUrl = EnsureConnectedPcBaseUrl();
        string url = $"{baseUrl}/api/v1/storage/list?path={Uri.EscapeDataString(remotePath ?? "")}&includeFiles={(includeFiles ? "true" : "false")}";
        var resp = await _httpClient.GetAsync(url, token);
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadAsStringAsync(token);
        return JsonSerializer.Deserialize<DirectoryListingResponse>(json, JsonOptions) ?? new DirectoryListingResponse();
    }

    public async Task<TreeManifestResponse> GetRemotePcTreeManifestAsync(string remoteRootPath, CancellationToken token)
    {
        string baseUrl = EnsureConnectedPcBaseUrl();
        string url = $"{baseUrl}/api/v1/storage/tree?path={Uri.EscapeDataString(remoteRootPath)}";
        var resp = await _httpClient.GetAsync(url, token);
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadAsStringAsync(token);
        return JsonSerializer.Deserialize<TreeManifestResponse>(json, JsonOptions) ?? new TreeManifestResponse();
    }

    public async Task CreateRemotePcDirectoryAsync(string remotePath, CancellationToken token = default)
    {
        string baseUrl = EnsureConnectedPcBaseUrl();
        string url = $"{baseUrl}/api/v1/storage/mkdir?path={Uri.EscapeDataString(remotePath)}";
        var resp = await _httpClient.PostAsync(url, null, token);
        resp.EnsureSuccessStatusCode();
    }

    public async Task PushFileToPcAsync(FileInfo localFile, string remoteTargetPath, ConflictPolicy conflictPolicy, CancellationToken token)
    {
        string baseUrl = EnsureConnectedPcBaseUrl();
        string url = $"{baseUrl}/api/v1/file/push?path={Uri.EscapeDataString(remoteTargetPath)}&mtimeTicks={localFile.LastWriteTimeUtc.Ticks}&conflict={(int)conflictPolicy}";

        await using var fs = new FileStream(localFile.FullName, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, useAsync: true);
        using var content = new StreamContent(fs, 65536);
        content.Headers.ContentLength = localFile.Length;

        var resp = await _httpClient.PostAsync(url, content, token);
        resp.EnsureSuccessStatusCode();
    }

    public async Task PullFileFromPcAsync(string remoteFilePath, string localTargetPath, long expectedSize, long mtimeTicks, ConflictPolicy conflictPolicy, CancellationToken token)
    {
        string baseUrl = EnsureConnectedPcBaseUrl();
        string finalLocalPath = ResolveConflictTargetFilePath(localTargetPath, expectedSize, conflictPolicy, out bool shouldSkip);
        if (shouldSkip) return;

        string parentDir = Path.GetDirectoryName(finalLocalPath) ?? "/storage/emulated/0/Download";
        Directory.CreateDirectory(parentDir);

        string tempPartPath = Path.Combine(parentDir, $".syncdroid_{Guid.NewGuid():N}.part");
        string url = $"{baseUrl}/api/v1/file/pull?path={Uri.EscapeDataString(remoteFilePath)}";

        try
        {
            using var resp = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token);
            resp.EnsureSuccessStatusCode();

            await using (var netStream = await resp.Content.ReadAsStreamAsync(token))
            await using (var fs = new FileStream(tempPartPath, FileMode.Create, FileAccess.Write, FileShare.None, 65536, useAsync: true))
            {
                await netStream.CopyToAsync(fs, 65536, token);
            }

            if (expectedSize >= 0)
            {
                var partInfo = new FileInfo(tempPartPath);
                if (partInfo.Length != expectedSize)
                {
                    throw new IOException($"Size mismatch for '{remoteFilePath}': expected {expectedSize} bytes, received {partInfo.Length} bytes.");
                }
            }

            File.Move(tempPartPath, finalLocalPath, overwrite: true);

            if (mtimeTicks > 0)
            {
                try
                {
                    File.SetLastWriteTimeUtc(finalLocalPath, new DateTime(mtimeTicks, DateTimeKind.Utc));
                }
                catch { }
            }
        }
        finally
        {
            if (File.Exists(tempPartPath))
            {
                try { File.Delete(tempPartPath); } catch { }
            }
        }
    }

    public async Task DeleteRemotePcFileAsync(string remoteFilePath, CancellationToken token)
    {
        string baseUrl = EnsureConnectedPcBaseUrl();
        string url = $"{baseUrl}/api/v1/file/delete?path={Uri.EscapeDataString(remoteFilePath)}";
        var resp = await _httpClient.PostAsync(url, null, token);
        resp.EnsureSuccessStatusCode();
    }

    private void NotifyConnectionStateChanged()
    {
        _mainHandler.Post(() => ConnectionStateChanged?.Invoke());
    }
}
