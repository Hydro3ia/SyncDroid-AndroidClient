using System;
using System.Collections.Concurrent;
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
    private readonly HttpClient _probeHttpClient;
    private readonly Handler _mainHandler = new(Looper.MainLooper!);
    private readonly ConcurrentDictionary<string, Task<bool>> _pendingAuthByIp = new(StringComparer.OrdinalIgnoreCase);
    private HttpListener? _httpListener;
    private UdpClient? _udpResponder;
    private CancellationTokenSource? _serverCts;

    public bool IsServerRunning { get; private set; }
    public int ListeningPort { get; private set; } = 45821;
    public CompanionPeer? ConnectedPeer { get; private set; }
    public List<CompanionPeer> DiscoveredPeers { get; } = new();

    public event Action? ConnectionStateChanged;
    public event Action? DiscoveredPeersChanged;
    public Func<string, string, Task<bool>>? ConfirmFirstTimeDeviceAsync { get; set; }

    private CompanionNetworkService()
    {
        string friendlyName = GetDeviceFriendlyName();
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(30)
        };
        _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("X-SyncDroid-DeviceName", friendlyName);

        _probeHttpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(4)
        };
        _probeHttpClient.DefaultRequestHeaders.TryAddWithoutValidation("X-SyncDroid-DeviceName", friendlyName);

        var settings = SettingsService.Load();
        ListeningPort = settings.CompanionPort;

        SettingsService.TrustedDevicesChanged += (_, _) =>
        {
            _mainHandler.Post(() =>
            {
                if (ConnectedPeer != null &&
                    ConnectedPeer.ProtocolType == PeerProtocolType.SyncDroidCompanion &&
                    !SettingsService.IsDeviceTrusted("SyncDroid", null, ConnectedPeer.Endpoint, ConnectedPeer.DeviceName))
                {
                    DisconnectFromPc();
                }
            });
        };
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

    private string ResolvePeerName(string hostIp, string? providedName)
    {
        if (!string.IsNullOrWhiteSpace(providedName))
        {
            return providedName.Trim();
        }

        var discovered = DiscoveredPeers.FirstOrDefault(p =>
            string.Equals(p.IpAddress, hostIp, StringComparison.OrdinalIgnoreCase));
        if (discovered != null && !string.IsNullOrWhiteSpace(discovered.DeviceName))
        {
            return discovered.DeviceName;
        }

        if (ConnectedPeer != null &&
            string.Equals(ConnectedPeer.IpAddress, hostIp, StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(ConnectedPeer.DeviceName))
        {
            return ConnectedPeer.DeviceName;
        }

        return hostIp;
    }

    public async Task<bool> EnsurePeerAuthenticatedAsync(
        string deviceName,
        string ipOrEndpoint,
        bool notifyRemoteOnSuccess = false,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(ipOrEndpoint)) return false;
        string hostIp = SettingsService.NormalizeHostIp(ipOrEndpoint);
        if (string.IsNullOrWhiteSpace(hostIp)) return false;

        string resolvedName = ResolvePeerName(hostIp, deviceName);
        if (SettingsService.IsDeviceTrusted("SyncDroid", null, ipOrEndpoint, resolvedName))
        {
            if (notifyRemoteOnSuccess)
            {
                bool remoteAccepted = await NotifyRemoteConnectAsync(hostIp, ipOrEndpoint, ct);
                if (!remoteAccepted) return false;
            }
            return true;
        }

        var confirmFunc = ConfirmFirstTimeDeviceAsync;
        if (confirmFunc == null)
        {
            SettingsService.AddTrustedDevice("SyncDroid", resolvedName, ipOrEndpoint);
            return true;
        }

        var pendingTask = _pendingAuthByIp.GetOrAdd(hostIp, key => Task.Run(async () =>
        {
            try
            {
                bool confirmed = await confirmFunc(resolvedName, ipOrEndpoint);
                if (confirmed)
                {
                    SettingsService.AddTrustedDevice("SyncDroid", resolvedName, ipOrEndpoint);
                    if (notifyRemoteOnSuccess)
                    {
                        bool remoteAccepted = await NotifyRemoteConnectAsync(hostIp, ipOrEndpoint, ct);
                        if (!remoteAccepted) return false;
                    }
                }
                return confirmed;
            }
            finally
            {
                _pendingAuthByIp.TryRemove(key, out _);
            }
        }));

        return await pendingTask;
    }

    private async Task<bool> NotifyRemoteConnectAsync(string hostIp, string ipOrEndpoint, CancellationToken ct)
    {
        try
        {
            string targetEp = ipOrEndpoint.Contains(':') ? ipOrEndpoint : $"{hostIp}:45821";
            string connectUrl = $"http://{targetEp}/api/v1/connect?deviceName={Uri.EscapeDataString(GetDeviceFriendlyName())}&port={ListeningPort}";
            using var emptyContent = new ByteArrayContent(Array.Empty<byte>());
            using var resp = await _probeHttpClient.PostAsync(connectUrl, emptyContent, ct);
            if (resp.StatusCode == HttpStatusCode.Forbidden || resp.StatusCode == HttpStatusCode.Unauthorized)
            {
                return false;
            }
        }
        catch
        {
        }
        return true;
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
                    string reply = $"{DiscoveryMagicReply}|{GetDeviceFriendlyName()}|Android|0.2.0.0|{tcpPort}";
                    byte[] replyBytes = Encoding.UTF8.GetBytes(reply);
                    await _udpResponder.SendAsync(replyBytes, replyBytes.Length, received.RemoteEndPoint);

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
            string probe = $"{DiscoveryMagicProbe}|{GetDeviceFriendlyName()}|Android|0.2.0.0|{ListeningPort}";
            byte[] data = Encoding.UTF8.GetBytes(probe);

            await udp.SendAsync(data, data.Length, new IPEndPoint(IPAddress.Broadcast, DiscoveryUdpPort));

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
                Version = info?.Version ?? "0.2.0.0",
                IpAddress = host,
                Port = port,
                ProtocolType = PeerProtocolType.SyncDroidCompanion,
                Scheme = "http",
                LastSeenUtc = DateTime.UtcNow
            };

            RegisterDiscoveredPeer(peer);

            bool confirmed = await EnsurePeerAuthenticatedAsync(
                peer.DeviceName,
                peer.Endpoint,
                notifyRemoteOnSuccess: true);
            if (!confirmed)
            {
                var loc = LocalizationService.Instance;
                return (false, loc.T(
                    $"Connection to {peer.DeviceName} ({peer.Endpoint}) was declined.",
                    $"已取消或被拒绝连接至 {peer.DeviceName} ({peer.Endpoint})。",
                    $"已取消或被拒絕連接至 {peer.DeviceName} ({peer.Endpoint})。"));
            }

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
        string remoteIp = req.RemoteEndPoint?.Address?.ToString() ?? "";
        string? headerDevName = req.Headers["X-SyncDroid-DeviceName"];
        string peerName = ResolvePeerName(remoteIp, req.QueryString["deviceName"] ?? headerDevName);

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
                    Version = "0.2.0.0",
                    ListeningPort = ListeningPort,
                    FreeSpaceBytes = freeBytes,
                    DefaultStorageRoot = "/storage/emulated/0"
                };
                await WriteJsonResponseAsync(res, info);
                return;
            }

            if (path.Equals("/api/v1/connect", StringComparison.OrdinalIgnoreCase) && req.HttpMethod == "POST")
            {
                int peerPort = int.TryParse(req.QueryString["port"], out int parsedPort) && parsedPort > 0
                    ? parsedPort
                    : 45821;
                string ep = !string.IsNullOrWhiteSpace(remoteIp) ? $"{remoteIp}:{peerPort}" : remoteIp;
                bool allowed = await EnsurePeerAuthenticatedAsync(peerName, ep, notifyRemoteOnSuccess: false);
                if (!allowed)
                {
                    res.StatusCode = 403;
                    await WriteJsonResponseAsync(res, new { ok = false, error = "Connection declined by user" });
                    return;
                }
                await WriteJsonResponseAsync(res, new { ok = true });
                return;
            }

            if (path.Equals("/api/v1/file/push", StringComparison.OrdinalIgnoreCase) && req.HttpMethod == "POST")
            {
                await HandleServerFilePushAsync(req, res, remoteIp, peerName);
                return;
            }

            // Authenticate all other /api/v1/... endpoints on first connection
            if (!string.IsNullOrWhiteSpace(remoteIp) && !FileHashHelper.IsLoopbackOrDisallowedEndpoint(remoteIp))
            {
                bool allowed = await EnsurePeerAuthenticatedAsync(peerName, remoteIp, notifyRemoteOnSuccess: false);
                if (!allowed)
                {
                    res.StatusCode = 403;
                    await WriteJsonResponseAsync(res, new { ok = false, error = "Device authentication declined by user" });
                    return;
                }
            }

            if (path.Equals("/api/v1/storage/list", StringComparison.OrdinalIgnoreCase))
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
            else if (path.Equals("/api/v1/file/hash", StringComparison.OrdinalIgnoreCase) && req.HttpMethod == "GET")
            {
                string filePath = req.QueryString["path"] ?? "";
                if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                {
                    res.StatusCode = 404;
                    res.Close();
                    return;
                }

                var fi = new FileInfo(filePath);
                string sha256 = await FileHashHelper.ComputeSha256Async(filePath);
                await WriteJsonResponseAsync(res, new { ok = true, sha256, size = fi.Length });
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

                if (SettingsService.Load().VerifyHashAfterTransfer)
                {
                    string sha256 = await FileHashHelper.ComputeSha256Async(filePath);
                    res.AddHeader("X-File-Sha256", sha256);
                }

                await using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, useAsync: true);
                await fs.CopyToAsync(res.OutputStream);
                res.Close();
                SyncStateService.Instance.AddLog($"[PC Pull] Sent file: {filePath}");
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

    private async Task HandleServerFilePushAsync(
        HttpListenerRequest req,
        HttpListenerResponse res,
        string remoteIp,
        string peerName)
    {
        string targetPath = req.QueryString["path"] ?? "";
        long.TryParse(req.QueryString["mtimeTicks"], out long mtimeTicks);
        int.TryParse(req.QueryString["conflict"], out int conflictInt);
        var conflict = Enum.IsDefined(typeof(ConflictPolicy), conflictInt)
            ? (ConflictPolicy)conflictInt
            : ConflictPolicy.Overwrite;
        string? expectedSha256 = !string.IsNullOrWhiteSpace(req.QueryString["sha256"])
            ? req.QueryString["sha256"]!.Trim()
            : req.Headers["X-File-Sha256"]?.Trim();

        if (string.IsNullOrWhiteSpace(targetPath))
        {
            res.StatusCode = 400;
            res.Close();
            return;
        }

        string finalPath = ResolveConflictTargetFilePath(targetPath, req.ContentLength64, conflict, out bool skipFile);
        string parentDir = Path.GetDirectoryName(finalPath) ?? "/storage/emulated/0/Download";
        Directory.CreateDirectory(parentDir);

        string fileId = Guid.NewGuid().ToString("N");
        string sessionId = $"syncdroid_{remoteIp}";
        string displayFileName = Path.GetFileName(finalPath);
        if (string.IsNullOrWhiteSpace(displayFileName)) displayFileName = targetPath;

        var loc = LocalizationService.Instance;
        var state = SyncStateService.Instance;

        bool isTrusted = string.IsNullOrWhiteSpace(remoteIp) ||
                         FileHashHelper.IsLoopbackOrDisallowedEndpoint(remoteIp) ||
                         SettingsService.IsDeviceTrusted("SyncDroid", null, remoteIp, peerName);

        var confirmTcs = !isTrusted
            ? new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously)
            : null;

        var fileItem = new ReceivedFileItem
        {
            Id = fileId,
            FileName = displayFileName,
            SenderName = peerName,
            Protocol = "SyncDroid",
            Size = req.ContentLength64 > 0 ? req.ContentLength64 : 0,
            SizeDisplay = req.ContentLength64 >= 0 ? FileHashHelper.FormatByteSize(req.ContentLength64) : "",
            SavedPath = finalPath,
            StatusText = isTrusted ? loc.ReceiveStatusReceiving : loc.ReceiveStatusPending,
            ProgressPercent = 0
        };

        state.HandleIncomingSession(new IncomingTransferSessionEventArgs
        {
            SessionId = sessionId,
            SenderName = peerName,
            SenderIp = remoteIp,
            Protocol = "SyncDroid",
            SaveDirectory = parentDir,
            RequiresConfirmation = !isTrusted,
            AppendToExistingSession = true,
            Files = new List<ReceivedFileItem> { fileItem },
            ConfirmationTcs = confirmTcs
        });

        if (!isTrusted)
        {
            bool allowed = await EnsurePeerAuthenticatedAsync(peerName, remoteIp, notifyRemoteOnSuccess: false);
            state.SyncConfirmationDecision(allowed);
            if (!allowed)
            {
                res.StatusCode = 403;
                await WriteJsonResponseAsync(res, new { ok = false, error = "Device authentication declined by user" });
                return;
            }
        }

        if (skipFile)
        {
            state.CompleteIncomingFile(sessionId, fileId, finalPath, skipped: true, failed: false);
            state.AddLog($"Skipped (Identical): {displayFileName} | Overall: 100.0% (1/1)");
            await WriteJsonResponseAsync(res, new { ok = true, skipped = true, path = finalPath });
            return;
        }

        string tempPartPath = Path.Combine(parentDir, $".syncdroid_{Guid.NewGuid():N}.part");
        long bytesWritten = 0;
        double elapsedSec = 0;

        try
        {
            await using (var rawFs = new FileStream(tempPartPath, FileMode.Create, FileAccess.Write, FileShare.None, 65536, useAsync: true))
            await using (var trackedFs = new ProgressTrackingStream(rawFs, (inFlightBytes, liveSpeed) =>
            {
                double livePct = req.ContentLength64 > 0
                    ? FileHashHelper.CalculateOverallPercent(inFlightBytes, req.ContentLength64, 0, 1)
                    : 50.0;
                state.UpdateIncomingFileProgress(sessionId, fileId, livePct, liveSpeed);
                state.AddLog($"Transferring: {displayFileName} | Speed: {liveSpeed} | Overall: {livePct:F1}% (1/1)");
            }))
            {
                await req.InputStream.CopyToAsync(trackedFs, 65536);
                await trackedFs.FlushAsync();
                bytesWritten = trackedFs.TotalBytesTransferred;
                elapsedSec = trackedFs.ElapsedSeconds;
            }

            if (req.ContentLength64 >= 0 && bytesWritten != req.ContentLength64)
            {
                throw new IOException($"Incomplete transfer: expected {req.ContentLength64} bytes, got {bytesWritten}.");
            }

            if (SettingsService.Load().VerifyHashAfterTransfer && !string.IsNullOrWhiteSpace(expectedSha256))
            {
                string actualSha256 = await FileHashHelper.ComputeSha256Async(tempPartPath);
                if (!string.Equals(actualSha256, expectedSha256, StringComparison.OrdinalIgnoreCase))
                {
                    try { File.Delete(tempPartPath); } catch { }
                    throw new IOException($"Hash verification failed (SHA-256) for '{finalPath}'.");
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
            string avgSpeed = FileHashHelper.FormatTransferSpeed(bytesWritten, elapsedSec);
            state.CompleteIncomingFile(sessionId, fileId, finalPath, skipped: false, failed: false);
            state.AddLog($"Transferred: {displayFileName} ({FileHashHelper.FormatByteSize(bytesWritten)}) | Speed: {avgSpeed} | Overall: 100.0% (1/1)");
            await WriteJsonResponseAsync(res, new { ok = true, skipped = false, path = finalPath, size = bytesWritten });
        }
        catch
        {
            state.CompleteIncomingFile(sessionId, fileId, finalPath, skipped: false, failed: true);
            throw;
        }
        finally
        {
            if (File.Exists(tempPartPath))
            {
                try { File.Delete(tempPartPath); } catch { }
            }
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

    public async Task<(long BytesTransferred, double ElapsedSeconds, bool Skipped)> PushFileToPcAsync(
        FileInfo localFile,
        string remoteTargetPath,
        ConflictPolicy conflictPolicy,
        CancellationToken token,
        Action<long, string>? onProgressCallback = null)
    {
        string baseUrl = EnsureConnectedPcBaseUrl();
        var settings = SettingsService.Load();
        string? sha256 = settings.VerifyHashAfterTransfer
            ? await FileHashHelper.ComputeSha256Async(localFile.FullName, token)
            : null;

        string url = $"{baseUrl}/api/v1/file/push?path={Uri.EscapeDataString(remoteTargetPath)}&mtimeTicks={localFile.LastWriteTimeUtc.Ticks}&conflict={(int)conflictPolicy}" +
                     (!string.IsNullOrEmpty(sha256) ? $"&sha256={sha256}" : "");

        long bytesSent = 0;
        double elapsedSec = 0;
        bool skippedByRemote = false;

        await using var rawFs = new FileStream(localFile.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 65536, useAsync: true);
        await using var trackedStream = new ProgressTrackingStream(rawFs, onProgressCallback);
        using var content = new StreamContent(trackedStream, 65536);
        content.Headers.ContentLength = localFile.Length;

        if (!string.IsNullOrEmpty(sha256))
        {
            content.Headers.TryAddWithoutValidation("X-File-Sha256", sha256);
        }

        using var resp = await _httpClient.PostAsync(url, content, token);
        resp.EnsureSuccessStatusCode();
        bytesSent = trackedStream.TotalBytesTransferred;
        elapsedSec = trackedStream.ElapsedSeconds;

        try
        {
            string respJson = await resp.Content.ReadAsStringAsync(token);
            if (!string.IsNullOrWhiteSpace(respJson))
            {
                using var doc = JsonDocument.Parse(respJson);
                if (doc.RootElement.TryGetProperty("skipped", out var skipProp) && skipProp.ValueKind == JsonValueKind.True)
                {
                    skippedByRemote = true;
                }
            }
        }
        catch { }

        return (bytesSent, elapsedSec, skippedByRemote);
    }

    public async Task<(long BytesTransferred, double ElapsedSeconds, bool Skipped)> PullFileFromPcAsync(
        string remoteFilePath,
        string localTargetPath,
        long expectedSize,
        long mtimeTicks,
        ConflictPolicy conflictPolicy,
        CancellationToken token,
        Action<long, string>? onProgressCallback = null)
    {
        string baseUrl = EnsureConnectedPcBaseUrl();
        string finalLocalPath = ResolveConflictTargetFilePath(localTargetPath, expectedSize, conflictPolicy, out bool shouldSkip);
        if (shouldSkip) return (0, 0, true);

        string parentDir = Path.GetDirectoryName(finalLocalPath) ?? "/storage/emulated/0/Download";
        Directory.CreateDirectory(parentDir);

        string tempPartPath = Path.Combine(parentDir, $".syncdroid_{Guid.NewGuid():N}.part");
        string url = $"{baseUrl}/api/v1/file/pull?path={Uri.EscapeDataString(remoteFilePath)}";

        long bytesPulled = 0;
        double elapsedSec = 0;

        try
        {
            using var resp = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token);
            resp.EnsureSuccessStatusCode();

            await using (var netStream = await resp.Content.ReadAsStreamAsync(token))
            await using (var rawFs = new FileStream(tempPartPath, FileMode.Create, FileAccess.Write, FileShare.None, 65536, useAsync: true))
            await using (var trackedFs = new ProgressTrackingStream(rawFs, onProgressCallback))
            {
                await netStream.CopyToAsync(trackedFs, 65536, token);
                await trackedFs.FlushAsync(token);
                bytesPulled = trackedFs.TotalBytesTransferred;
                elapsedSec = trackedFs.ElapsedSeconds;
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

            var settings = SettingsService.Load();
            if (settings.VerifyHashAfterTransfer && resp.Headers.TryGetValues("X-File-Sha256", out var shaValues))
            {
                string expectedSha256 = shaValues.FirstOrDefault() ?? "";
                if (!string.IsNullOrWhiteSpace(expectedSha256))
                {
                    string localSha256 = await FileHashHelper.ComputeSha256Async(finalLocalPath, token);
                    if (!string.Equals(expectedSha256, localSha256, StringComparison.OrdinalIgnoreCase))
                    {
                        try { File.Delete(finalLocalPath); } catch { }
                        throw new IOException($"SHA-256 hash mismatch for '{remoteFilePath}'. File discarded.");
                    }
                }
            }

            if (mtimeTicks > 0)
            {
                try
                {
                    File.SetLastWriteTimeUtc(finalLocalPath, new DateTime(mtimeTicks, DateTimeKind.Utc));
                }
                catch { }
            }

            return (bytesPulled, elapsedSec, false);
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
