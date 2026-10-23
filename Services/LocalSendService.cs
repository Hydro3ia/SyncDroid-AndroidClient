using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Android.Content;
using Android.Net.Wifi;
using SyncDroidAndroid.Helpers;
using SyncDroidAndroid.Models;

namespace SyncDroidAndroid.Services;

/// <summary>
/// Implements LocalSend Protocol v2.1 on Android:
/// - UDP Multicast discovery on 224.0.0.167:53317 with Android WifiManager.MulticastLock
/// - HTTP Server for receiving files/folders (/api/localsend/v2/info, /register, /prepare-upload, /upload, /cancel)
/// - HTTP/HTTPS Client for sending files/folders from the Transfer Queue to any LocalSend peer on LAN
/// </summary>
public sealed class LocalSendService
{
    public const string MulticastGroupIp = "224.0.0.167";
    public const int DefaultPort = 53317;

    public static LocalSendService Instance { get; } = new();

    private readonly HttpClient _httpClient;
    private readonly string _fingerprint;
    private readonly ConcurrentDictionary<string, LocalSendUploadSession> _uploadSessions = new();

    private HttpListener? _httpListener;
    private UdpClient? _multicastClient;
    private CancellationTokenSource? _serverCts;
    private WifiManager.MulticastLock? _multicastLock;

    public bool IsRunning { get; private set; }
    public int ListeningPort { get; private set; } = DefaultPort;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private LocalSendService()
    {
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true
        };
        _httpClient = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromMinutes(30)
        };

        var rawId = $"{CompanionNetworkService.GetDeviceFriendlyName()}_{Environment.MachineName}_SyncDroid";
        _fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawId))).ToLowerInvariant();
    }

    public LocalSendInfoDto BuildLocalInfo(bool? announce = null)
    {
        return new LocalSendInfoDto
        {
            Alias = $"{CompanionNetworkService.GetDeviceFriendlyName()} (SyncDroid)",
            Version = "2.1",
            DeviceModel = CompanionNetworkService.GetDeviceFriendlyName(),
            DeviceType = "mobile",
            Fingerprint = _fingerprint,
            Port = ListeningPort,
            Protocol = "http",
            Download = false,
            Announce = announce
        };
    }

    public bool IsAccessForbiddenError { get; private set; }

    public void Start(int port = DefaultPort)
    {
        Stop();

        var settings = SettingsService.Load();
        if (!settings.LocalSendEnabled)
            return;

        ListeningPort = port > 1024 && port <= 65535 ? port : DefaultPort;
        _serverCts = new CancellationTokenSource();
        var token = _serverCts.Token;

        AcquireMulticastLock();
        IsAccessForbiddenError = false;

        try
        {
            _httpListener = new HttpListener();
            _httpListener.Prefixes.Add($"http://*:{ListeningPort}/");
            _httpListener.Start();
            IsRunning = true;

            _ = Task.Run(() => AcceptHttpLoopAsync(_httpListener, token), token);
            _ = Task.Run(() => RunMulticastListenerLoopAsync(ListeningPort, token), token);
            _ = Task.Run(() => SendMulticastAnnouncementAsync(announce: true), token);

            SyncStateService.Instance.AddLog($"[LocalSend] Receiver ready on TCP/UDP port {ListeningPort} (Multicast {MulticastGroupIp})");
        }
        catch (Exception ex)
        {
            IsRunning = false;
            string msg = ex.Message.ToLowerInvariant();
            if (ex is HttpListenerException hlex && (hlex.ErrorCode == 5 || hlex.ErrorCode == 32 || hlex.ErrorCode == 183))
            {
                IsAccessForbiddenError = true;
            }
            else if (msg.Contains("forbidden") || msg.Contains("access") || msg.Contains("访问权限") || msg.Contains("already in use"))
            {
                IsAccessForbiddenError = true;
            }

            SyncStateService.Instance.AddLog($"[LocalSend] Could not bind port {ListeningPort}: {ex.Message}");
        }
    }

    public void Stop()
    {
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
            _multicastClient?.Close();
            _multicastClient = null;
        }
        catch { }

        ReleaseMulticastLock();
        IsRunning = false;
    }

    private void AcquireMulticastLock()
    {
        try
        {
            var wm = (WifiManager?)Android.App.Application.Context.GetSystemService(Context.WifiService);
            if (wm != null)
            {
                _multicastLock = wm.CreateMulticastLock("SyncDroid::LocalSendMulticastLock");
                _multicastLock?.SetReferenceCounted(false);
                _multicastLock?.Acquire();
            }
        }
        catch { }
    }

    private void ReleaseMulticastLock()
    {
        try
        {
            if (_multicastLock?.IsHeld == true)
            {
                _multicastLock.Release();
            }
        }
        catch { }
    }

    // =========================================================================
    // UDP Multicast Discovery & Announcement (224.0.0.167:53317)
    // =========================================================================

    private async Task RunMulticastListenerLoopAsync(int udpPort, CancellationToken token)
    {
        try
        {
            _multicastClient = new UdpClient();
            _multicastClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _multicastClient.Client.Bind(new IPEndPoint(IPAddress.Any, udpPort));

            var multicastAddr = IPAddress.Parse(MulticastGroupIp);
            try
            {
                _multicastClient.JoinMulticastGroup(multicastAddr);
            }
            catch { }

            while (!token.IsCancellationRequested)
            {
                var received = await _multicastClient.ReceiveAsync(token);
                string remoteIp = received.RemoteEndPoint.Address.ToString();
                if (CompanionNetworkService.GetLocalIpv4Addresses().Contains(remoteIp))
                    continue;

                string json = Encoding.UTF8.GetString(received.Buffer).Trim();
                if (!json.StartsWith("{", StringComparison.Ordinal))
                    continue;

                var dto = JsonSerializer.Deserialize<LocalSendInfoDto>(json, JsonOptions);
                if (dto == null || string.Equals(dto.Fingerprint, _fingerprint, StringComparison.OrdinalIgnoreCase))
                    continue;

                RegisterLocalSendPeer(dto, remoteIp);

                if (dto.Announce == true)
                {
                    _ = Task.Run(() => RespondToAnnouncementAsync(dto, remoteIp), token);
                }
            }
        }
        catch { }
    }

    private async Task RespondToAnnouncementAsync(LocalSendInfoDto remoteDto, string remoteIp)
    {
        int port = remoteDto.Port > 0 ? remoteDto.Port : DefaultPort;
        string scheme = string.Equals(remoteDto.Protocol, "https", StringComparison.OrdinalIgnoreCase) ? "https" : "http";
        var selfInfo = BuildLocalInfo(announce: false);

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2.5));
            var json = JsonSerializer.Serialize(selfInfo, JsonOptions);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            var resp = await _httpClient.PostAsync($"{scheme}://{remoteIp}:{port}/api/localsend/v2/register", content, cts.Token);
            if (resp.IsSuccessStatusCode)
                return;
        }
        catch { }

        // Fallback UDP unicast/multicast response per LocalSend protocol
        try
        {
            using var udp = new UdpClient();
            byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(selfInfo, JsonOptions);
            await udp.SendAsync(bytes, bytes.Length, new IPEndPoint(IPAddress.Parse(MulticastGroupIp), DefaultPort));
        }
        catch { }
    }

    public async Task SendMulticastAnnouncementAsync(bool announce = true)
    {
        try
        {
            using var udp = new UdpClient();
            udp.EnableBroadcast = true;
            var payload = BuildLocalInfo(announce: announce);
            byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions);

            var mcastEndpoint = new IPEndPoint(IPAddress.Parse(MulticastGroupIp), DefaultPort);
            await udp.SendAsync(bytes, bytes.Length, mcastEndpoint);

            foreach (var ip in CompanionNetworkService.GetLocalIpv4Addresses())
            {
                var segments = ip.Split('.');
                if (segments.Length == 4)
                {
                    string subnetBroadcast = $"{segments[0]}.{segments[1]}.{segments[2]}.255";
                    if (IPAddress.TryParse(subnetBroadcast, out var bcastAddr))
                    {
                        await udp.SendAsync(bytes, bytes.Length, new IPEndPoint(bcastAddr, DefaultPort));
                    }
                }
            }
        }
        catch { }
    }

    private void RegisterLocalSendPeer(LocalSendInfoDto dto, string remoteIp, int? overridePort = null, string? overrideScheme = null)
    {
        int port = overridePort ?? (dto.Port > 0 ? dto.Port : DefaultPort);
        string scheme = overrideScheme ?? (string.Equals(dto.Protocol, "https", StringComparison.OrdinalIgnoreCase) ? "https" : "http");
        string alias = !string.IsNullOrWhiteSpace(dto.Alias)
            ? dto.Alias
            : (!string.IsNullOrWhiteSpace(dto.DeviceModel) ? dto.DeviceModel : remoteIp);
        string model = !string.IsNullOrWhiteSpace(dto.DeviceModel) ? dto.DeviceModel : "LocalSend";

        var peer = new CompanionPeer
        {
            DeviceName = alias,
            Platform = $"LocalSend • {model}",
            Version = string.IsNullOrWhiteSpace(dto.Version) ? "2.1" : dto.Version,
            IpAddress = remoteIp,
            Port = port,
            ProtocolType = PeerProtocolType.LocalSend,
            Scheme = scheme,
            LastSeenUtc = DateTime.UtcNow
        };

        CompanionNetworkService.Instance.RegisterDiscoveredPeer(peer);
    }

    // =========================================================================
    // HTTP Server Request Handling (LocalSend Device ➔ Android)
    // =========================================================================

    private async Task AcceptHttpLoopAsync(HttpListener listener, CancellationToken token)
    {
        while (!token.IsCancellationRequested && listener.IsListening)
        {
            try
            {
                var ctx = await listener.GetContextAsync();
                _ = Task.Run(() => HandleHttpContextAsync(ctx), token);
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
            if ((path.Equals("/api/localsend/v2/info", StringComparison.OrdinalIgnoreCase) ||
                 path.Equals("/api/localsend/v1/info", StringComparison.OrdinalIgnoreCase)) &&
                req.HttpMethod == "GET")
            {
                await WriteJsonAsync(res, BuildLocalInfo());
            }
            else if ((path.Equals("/api/localsend/v2/register", StringComparison.OrdinalIgnoreCase) ||
                      path.Equals("/api/localsend/v1/register", StringComparison.OrdinalIgnoreCase)) &&
                     req.HttpMethod == "POST")
            {
                using var sr = new StreamReader(req.InputStream, Encoding.UTF8);
                string body = await sr.ReadToEndAsync();
                var remoteDto = JsonSerializer.Deserialize<LocalSendInfoDto>(body, JsonOptions);
                string remoteIp = req.RemoteEndPoint?.Address?.ToString() ?? "";
                if (remoteDto != null && !string.IsNullOrWhiteSpace(remoteIp))
                {
                    RegisterLocalSendPeer(remoteDto, remoteIp);
                }
                await WriteJsonAsync(res, BuildLocalInfo());
            }
            else if ((path.Equals("/api/localsend/v2/prepare-upload", StringComparison.OrdinalIgnoreCase) ||
                      path.Equals("/api/localsend/v1/prepare-upload", StringComparison.OrdinalIgnoreCase)) &&
                     req.HttpMethod == "POST")
            {
                using var sr = new StreamReader(req.InputStream, Encoding.UTF8);
                string body = await sr.ReadToEndAsync();
                var prepReq = JsonSerializer.Deserialize<LocalSendPrepareUploadRequestDto>(body, JsonOptions);
                if (prepReq == null || prepReq.Files == null || prepReq.Files.Count == 0)
                {
                    res.StatusCode = 400;
                    res.Close();
                    return;
                }

                string remoteIp = req.RemoteEndPoint?.Address?.ToString() ?? "";
                if (!string.IsNullOrWhiteSpace(remoteIp))
                {
                    RegisterLocalSendPeer(prepReq.Info, remoteIp);
                }

                var settings = SettingsService.Load();
                var loc = LocalizationService.Instance;
                var state = SyncStateService.Instance;

                string receiveDir = !string.IsNullOrWhiteSpace(settings.LocalSendReceiveDir)
                    ? settings.LocalSendReceiveDir
                    : (!string.IsNullOrWhiteSpace(settings.DirectTransfer_AndroidTargetDir)
                        ? settings.DirectTransfer_AndroidTargetDir
                        : "/storage/emulated/0/Download/SyncDroid");

                string senderAlias = string.IsNullOrWhiteSpace(prepReq.Info?.Alias) ? remoteIp : prepReq.Info!.Alias;
                string senderFp = prepReq.Info?.Fingerprint ?? "";
                int senderPort = prepReq.Info?.Port > 0 ? prepReq.Info.Port : DefaultPort;
                bool isTrusted = SettingsService.IsDeviceTrusted("LocalSend", senderFp, remoteIp, senderAlias);

                string sessionId = Guid.NewGuid().ToString("N");
                var receivedItems = new List<ReceivedFileItem>();
                foreach (var kvp in prepReq.Files)
                {
                    string safeRel = SanitizeIncomingRelativePath(kvp.Value.FileName);
                    string targetPath = $"{receiveDir.TrimEnd('/', '\\')}/{safeRel}";
                    receivedItems.Add(new ReceivedFileItem
                    {
                        Id = kvp.Key,
                        FileName = kvp.Value.FileName,
                        SenderName = senderAlias,
                        Protocol = "LocalSend",
                        Size = kvp.Value.Size,
                        SizeDisplay = FileHashHelper.FormatByteSize(kvp.Value.Size),
                        SavedPath = targetPath,
                        StatusText = isTrusted ? loc.ReceiveStatusWaitingUpload : loc.ReceiveStatusPending,
                        ProgressPercent = 0
                    });
                }

                var tcs = !isTrusted
                    ? new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously)
                    : null;

                var sessionArgs = new IncomingTransferSessionEventArgs
                {
                    SessionId = sessionId,
                    SenderName = senderAlias,
                    SenderIp = remoteIp,
                    Protocol = "LocalSend",
                    SaveDirectory = receiveDir,
                    RequiresConfirmation = !isTrusted,
                    AppendToExistingSession = false,
                    Files = receivedItems,
                    ConfirmationTcs = tcs
                };

                state.HandleIncomingSession(sessionArgs);

                if (!isTrusted && tcs != null)
                {
                    state.AddLog($"[LocalSend] Incoming transfer request from '{senderAlias}' ({prepReq.Files.Count} file(s)) — waiting for user confirmation...");
                    bool accepted = false;
                    using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMinutes(3));
                    try
                    {
                        using (timeoutCts.Token.Register(() => tcs.TrySetResult(false)))
                        {
                            accepted = await tcs.Task;
                        }
                    }
                    catch
                    {
                        accepted = false;
                    }

                    if (!accepted)
                    {
                        state.AddLog($"[LocalSend] Declined incoming transfer request from '{senderAlias}'.");
                        res.StatusCode = 403;
                        await WriteJsonAsync(res, new { error = "Declined by user" });
                        return;
                    }

                    SettingsService.AddTrustedDevice("LocalSend", senderAlias, $"{remoteIp}:{senderPort}", senderFp);
                }

                var tokens = new Dictionary<string, string>();
                foreach (var kvp in prepReq.Files)
                {
                    tokens[kvp.Key] = Guid.NewGuid().ToString("N");
                }

                var session = new LocalSendUploadSession
                {
                    SessionId = sessionId,
                    SenderInfo = prepReq.Info ?? new LocalSendInfoDto(),
                    Files = prepReq.Files,
                    Tokens = tokens,
                    ReceiveDirectory = receiveDir,
                    CreatedUtc = DateTime.UtcNow,
                    TotalFiles = prepReq.Files.Count,
                    TotalBytes = prepReq.Files.Values.Sum(f => f.Size),
                    ProcessedFiles = 0,
                    CompletedBytes = 0
                };
                _uploadSessions[sessionId] = session;

                state.AddLog($"[LocalSend] Accepted incoming transfer session from '{senderAlias}' ({prepReq.Files.Count} file(s)) ➔ {receiveDir}");

                var prepResp = new LocalSendPrepareUploadResponseDto
                {
                    SessionId = sessionId,
                    Files = tokens
                };
                await WriteJsonAsync(res, prepResp);
            }
            else if ((path.Equals("/api/localsend/v2/upload", StringComparison.OrdinalIgnoreCase) ||
                      path.Equals("/api/localsend/v1/upload", StringComparison.OrdinalIgnoreCase)) &&
                     req.HttpMethod == "POST")
            {
                string sessionId = req.QueryString["sessionId"] ?? "";
                string fileId = req.QueryString["fileId"] ?? "";
                string token = req.QueryString["token"] ?? "";

                if (!_uploadSessions.TryGetValue(sessionId, out var session) ||
                    !session.Files.TryGetValue(fileId, out var fileDto) ||
                    !session.Tokens.TryGetValue(fileId, out var expectedToken) ||
                    !string.Equals(token, expectedToken, StringComparison.Ordinal))
                {
                    res.StatusCode = 403;
                    res.Close();
                    return;
                }

                var settings = SettingsService.Load();
                var state = SyncStateService.Instance;

                string safeRelPath = SanitizeIncomingRelativePath(fileDto.FileName);
                string rawTargetPath = $"{session.ReceiveDirectory.TrimEnd('/', '\\')}/{safeRelPath}";
                string finalPath = CompanionNetworkService.ResolveConflictTargetFilePath(
                    rawTargetPath,
                    fileDto.Size,
                    ConflictPolicy.KeepBothRename,
                    out _);

                string parentDir = Path.GetDirectoryName(finalPath) ?? session.ReceiveDirectory;
                Directory.CreateDirectory(parentDir);

                string tempPartPath = Path.Combine(parentDir, $".syncdroid_{Guid.NewGuid():N}.part");
                long bytesReceived = 0;
                double elapsedSec = 0;
                long baseCompleted = Interlocked.Read(ref session.CompletedBytes);
                int currentIdx = Math.Min(session.TotalFiles, session.ProcessedFiles + 1);
                int totalFiles = Math.Max(1, session.TotalFiles);

                try
                {
                    state.UpdateIncomingFileProgress(sessionId, fileId, 0, "");
                    await using (var rawFs = new FileStream(tempPartPath, FileMode.Create, FileAccess.Write, FileShare.None, 65536, useAsync: true))
                    await using (var trackedFs = new ProgressTrackingStream(rawFs, (inFlightBytes, liveSpeed) =>
                    {
                        double filePct = FileHashHelper.CalculateOverallPercent(inFlightBytes, fileDto.Size, 0, 1);
                        double overallPct = FileHashHelper.CalculateOverallPercent(baseCompleted + inFlightBytes, session.TotalBytes, session.ProcessedFiles, totalFiles);
                        state.UpdateIncomingFileProgress(sessionId, fileId, filePct, liveSpeed);
                        state.AddLog($"Transferring: {safeRelPath} | Speed: {liveSpeed} | Overall: {overallPct:F1}% ({currentIdx}/{totalFiles})");
                    }))
                    {
                        await req.InputStream.CopyToAsync(trackedFs, 65536);
                        await trackedFs.FlushAsync();
                        bytesReceived = trackedFs.TotalBytesTransferred;
                        elapsedSec = trackedFs.ElapsedSeconds;
                    }

                    File.Move(tempPartPath, finalPath, overwrite: true);

                    if (settings.VerifyHashAfterTransfer && !string.IsNullOrWhiteSpace(fileDto.Sha256))
                    {
                        string localSha256 = await FileHashHelper.ComputeSha256Async(finalPath);
                        if (!string.Equals(fileDto.Sha256, localSha256, StringComparison.OrdinalIgnoreCase))
                        {
                            try { File.Delete(finalPath); } catch { }
                            state.CompleteIncomingFile(sessionId, fileId, finalPath, skipped: false, failed: true);
                            state.AddLog($"[LocalSend] Error: SHA-256 hash mismatch for {finalPath}. File discarded.");
                            res.StatusCode = 400;
                            res.Close();
                            return;
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(fileDto.Metadata?.Modified) &&
                        DateTimeOffset.TryParse(fileDto.Metadata.Modified, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dtoTime))
                    {
                        try
                        {
                            File.SetLastWriteTimeUtc(finalPath, dtoTime.UtcDateTime);
                        }
                        catch { }
                    }

                    int doneFiles = Interlocked.Increment(ref session.ProcessedFiles);
                    long doneBytes = Interlocked.Add(ref session.CompletedBytes, fileDto.Size > 0 ? fileDto.Size : bytesReceived);
                    double donePct = FileHashHelper.CalculateOverallPercent(doneBytes, session.TotalBytes, doneFiles, totalFiles);
                    string avgSpeed = FileHashHelper.FormatTransferSpeed(bytesReceived, elapsedSec);

                    MediaScannerHelper.NotifyFilesChanged(Android.App.Application.Context, new[] { finalPath });
                    state.CompleteIncomingFile(sessionId, fileId, finalPath, skipped: false, failed: false);
                    state.AddLog($"Transferred: {safeRelPath} ({FileHashHelper.FormatByteSize(bytesReceived)}) | Speed: {avgSpeed} | Overall: {donePct:F1}% ({doneFiles}/{totalFiles})");

                    res.StatusCode = 200;
                    res.Close();
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
            else if ((path.Equals("/api/localsend/v2/cancel", StringComparison.OrdinalIgnoreCase) ||
                      path.Equals("/api/localsend/v1/cancel", StringComparison.OrdinalIgnoreCase)) &&
                     req.HttpMethod == "POST")
            {
                string sessionId = req.QueryString["sessionId"] ?? "";
                if (!string.IsNullOrWhiteSpace(sessionId) && _uploadSessions.TryRemove(sessionId, out _))
                {
                    SyncStateService.Instance.AddLog($"[LocalSend] Transfer session {sessionId} cancelled by sender.");
                }
                res.StatusCode = 200;
                res.Close();
            }
            else
            {
                res.StatusCode = 404;
                res.Close();
            }
        }
        catch (Exception ex)
        {
            SyncStateService.Instance.AddLog($"[LocalSend] Server error: {ex.Message}");
            try
            {
                res.StatusCode = 500;
                res.Close();
            }
            catch { }
        }
    }

    private static string SanitizeIncomingRelativePath(string rawFileName)
    {
        if (string.IsNullOrWhiteSpace(rawFileName))
            return $"localsend_{DateTime.Now:yyyyMMdd_HHmmss}";

        var segments = rawFileName
            .Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Where(s => s != "." && s != "..")
            .Select(s =>
            {
                if (FileNameLengthHelper.IsFileNameTooLong(s, out _))
                {
                    return FileNameLengthHelper.TruncateFileNameStrictlyUnder255Bytes(s);
                }
                return s;
            })
            .ToArray();

        return segments.Length > 0 ? string.Join('/', segments) : $"localsend_{DateTime.Now:yyyyMMdd_HHmmss}";
    }

    private static async Task WriteJsonAsync<T>(HttpListenerResponse res, T payload)
    {
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions);
        res.ContentType = "application/json; charset=utf-8";
        res.ContentLength64 = bytes.Length;
        await res.OutputStream.WriteAsync(bytes, 0, bytes.Length);
        res.Close();
    }

    // =========================================================================
    // HTTP / HTTPS Client Operations (Android ➔ LocalSend Device)
    // =========================================================================

    public async Task<CompanionPeer?> ProbeLocalSendPeerAsync(string host, int port)
    {
        foreach (string scheme in new[] { "https", "http" })
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3.5));
                string infoUrl = $"{scheme}://{host}:{port}/api/localsend/v2/info";
                var resp = await _httpClient.GetAsync(infoUrl, cts.Token);
                if (!resp.IsSuccessStatusCode)
                    continue;

                var json = await resp.Content.ReadAsStringAsync(cts.Token);
                var dto = JsonSerializer.Deserialize<LocalSendInfoDto>(json, JsonOptions);
                if (dto == null)
                    continue;

                try
                {
                    var selfJson = JsonSerializer.Serialize(BuildLocalInfo(announce: false), JsonOptions);
                    using var regContent = new StringContent(selfJson, Encoding.UTF8, "application/json");
                    await _httpClient.PostAsync($"{scheme}://{host}:{port}/api/localsend/v2/register", regContent, cts.Token);
                }
                catch { }

                string alias = !string.IsNullOrWhiteSpace(dto.Alias)
                    ? dto.Alias
                    : (!string.IsNullOrWhiteSpace(dto.DeviceModel) ? dto.DeviceModel : host);
                string model = !string.IsNullOrWhiteSpace(dto.DeviceModel) ? dto.DeviceModel : "LocalSend";

                var peer = new CompanionPeer
                {
                    DeviceName = alias,
                    Platform = $"LocalSend • {model}",
                    Version = string.IsNullOrWhiteSpace(dto.Version) ? "2.1" : dto.Version,
                    IpAddress = host,
                    Port = port,
                    ProtocolType = PeerProtocolType.LocalSend,
                    Scheme = scheme,
                    LastSeenUtc = DateTime.UtcNow
                };

                CompanionNetworkService.Instance.RegisterDiscoveredPeer(peer);
                return peer;
            }
            catch { }
        }

        return null;
    }

    public async Task SendQueueToPeerAsync(
        CompanionPeer peer,
        IReadOnlyList<TransferQueueItem> queue,
        bool checkLongFileNames,
        CancellationToken token)
    {
        var state = SyncStateService.Instance;
        var loc = LocalizationService.Instance;

        var expandedFiles = new List<(string FileId, FileInfo LocalFile, LocalSendFileDto Dto)>();
        int idx = 0;

        var settings = SettingsService.Load();

        foreach (var item in queue)
        {
            token.ThrowIfCancellationRequested();

            if (!item.IsDirectory)
            {
                var fi = new FileInfo(item.FullPath);
                if (!fi.Exists)
                {
                    state.AddLog($"[LocalSend] Warning: Local file not found: {item.FullPath}");
                    continue;
                }

                string safeName = FileNameLengthHelper.SanitizeRelativePathForWindows(fi.Name, out _);
                if (checkLongFileNames && FileNameLengthHelper.IsFileNameTooLong(safeName, out _))
                {
                    safeName = FileNameLengthHelper.TruncateFileNameStrictlyUnder255Bytes(safeName);
                }

                string fid = $"file_{++idx}";
                expandedFiles.Add((fid, fi, new LocalSendFileDto
                {
                    Id = fid,
                    FileName = safeName,
                    Size = fi.Length,
                    FileType = "application/octet-stream",
                    Sha256 = settings.VerifyHashAfterTransfer ? (await FileHashHelper.ComputeSha256Async(fi.FullName, token)) : null,
                    Metadata = new LocalSendFileMetadataDto
                    {
                        Modified = fi.LastWriteTimeUtc.ToString("O")
                    }
                }));
            }
            else
            {
                var di = new DirectoryInfo(item.FullPath);
                if (!di.Exists)
                {
                    state.AddLog($"[LocalSend] Warning: Local folder not found: {item.FullPath}");
                    continue;
                }

                string folderName = FileNameLengthHelper.SanitizeRelativePathForWindows(di.Name, out _);
                var manifest = LocalStorageScanner.GetDirectoryTreeManifest(di.FullName);

                foreach (var sub in manifest.Files)
                {
                    var fi = new FileInfo(sub.FullPath);
                    if (!fi.Exists) continue;

                    string safeRel = FileNameLengthHelper.SanitizeRelativePathForWindows(sub.RelativePath, out _);
                    string combinedRel = $"{folderName}/{safeRel.Replace('\\', '/')}";
                    string fid = $"file_{++idx}";

                    expandedFiles.Add((fid, fi, new LocalSendFileDto
                    {
                        Id = fid,
                        FileName = combinedRel,
                        Size = fi.Length,
                        FileType = "application/octet-stream",
                        Sha256 = settings.VerifyHashAfterTransfer ? (await FileHashHelper.ComputeSha256Async(fi.FullName, token)) : null,
                        Metadata = new LocalSendFileMetadataDto
                        {
                            Modified = fi.LastWriteTimeUtc.ToString("O")
                        }
                    }));
                }
            }
        }

        if (expandedFiles.Count == 0)
        {
            throw new InvalidOperationException("No valid local files found in transfer queue.");
        }

        string baseUrl = $"{peer.Scheme}://{peer.IpAddress}:{peer.Port}";
        var prepReq = new LocalSendPrepareUploadRequestDto
        {
            Info = BuildLocalInfo(announce: false),
            Files = expandedFiles.ToDictionary(x => x.FileId, x => x.Dto)
        };

        state.AddLog(loc.T(
            $"[LocalSend] Waiting for target device ({peer.DeviceName}) to accept {expandedFiles.Count} file(s)...",
            $"[LocalSend] 正在等待目标设备 ({peer.DeviceName}) 确认接收 {expandedFiles.Count} 个文件...",
            $"[LocalSend] 正在等待目標裝置 ({peer.DeviceName}) 確認接收 {expandedFiles.Count} 個檔案..."));

        string reqJson = JsonSerializer.Serialize(prepReq, JsonOptions);
        using var reqContent = new StringContent(reqJson, Encoding.UTF8, "application/json");
        using var prepResp = await _httpClient.PostAsync($"{baseUrl}/api/localsend/v2/prepare-upload", reqContent, token);

        if (prepResp.StatusCode == HttpStatusCode.NoContent)
        {
            state.AddLog("[LocalSend] Target device reported all files skipped (204 No Content).");
            state.UpdateProgress(100);
            return;
        }

        if (prepResp.StatusCode == HttpStatusCode.Forbidden || prepResp.StatusCode == HttpStatusCode.Unauthorized)
        {
            throw new InvalidOperationException(loc.T(
                $"Target device '{peer.DeviceName}' rejected the transfer request.",
                $"目标设备“{peer.DeviceName}”拒绝了接收请求。",
                $"目標裝置「{peer.DeviceName}」拒絕了接收請求。"));
        }

        prepResp.EnsureSuccessStatusCode();
        var prepRespJson = await prepResp.Content.ReadAsStringAsync(token);
        var sessionDto = JsonSerializer.Deserialize<LocalSendPrepareUploadResponseDto>(prepRespJson, JsonOptions);
        if (sessionDto == null || string.IsNullOrWhiteSpace(sessionDto.SessionId))
        {
            throw new InvalidOperationException("Invalid prepare-upload response from LocalSend device.");
        }

        string sessionId = sessionDto.SessionId;
        int totalFiles = expandedFiles.Count;
        long totalBytes = expandedFiles.Sum(x => x.LocalFile.Length);
        long completedBytes = 0;
        int completed = 0;

        try
        {
            foreach (var entry in expandedFiles)
            {
                token.ThrowIfCancellationRequested();

                if (!sessionDto.Files.TryGetValue(entry.FileId, out var fileToken) || string.IsNullOrWhiteSpace(fileToken))
                {
                    completed++;
                    completedBytes += entry.LocalFile.Length;
                    double skipPct = FileHashHelper.CalculateOverallPercent(completedBytes, totalBytes, completed, totalFiles);
                    state.AddLog($"Skipped (Identical): {entry.Dto.FileName} | Overall: {skipPct:F1}% ({completed}/{totalFiles})");
                    state.UpdateProgress((int)Math.Round(skipPct));
                    continue;
                }

                long baseCompleted = completedBytes;
                int nextIdx = completed + 1;
                state.AddLog($"[LocalSend] Sending: {entry.Dto.FileName} ({FileHashHelper.FormatByteSize(entry.LocalFile.Length)})");
                string uploadUrl = $"{baseUrl}/api/localsend/v2/upload?sessionId={Uri.EscapeDataString(sessionId)}&fileId={Uri.EscapeDataString(entry.FileId)}&token={Uri.EscapeDataString(fileToken)}";

                long bytesSent = 0;
                double elapsedSec = 0;
                await using (var rawFs = new FileStream(entry.LocalFile.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 65536, useAsync: true))
                await using (var trackedStream = new ProgressTrackingStream(rawFs, (inFlightBytes, liveSpeed) =>
                {
                    double livePct = FileHashHelper.CalculateOverallPercent(baseCompleted + inFlightBytes, totalBytes, completed, totalFiles);
                    state.UpdateProgress((int)Math.Round(livePct));
                    state.AddLog($"Transferring: {entry.Dto.FileName} | Speed: {liveSpeed} | Overall: {livePct:F1}% ({nextIdx}/{totalFiles})");
                }))
                using (var streamContent = new StreamContent(trackedStream, 65536))
                {
                    streamContent.Headers.ContentLength = entry.LocalFile.Length;

                    using var upResp = await _httpClient.PostAsync(uploadUrl, streamContent, token);
                    upResp.EnsureSuccessStatusCode();
                    bytesSent = trackedStream.TotalBytesTransferred;
                    elapsedSec = trackedStream.ElapsedSeconds;
                }

                completed++;
                completedBytes += entry.LocalFile.Length;
                double donePct = FileHashHelper.CalculateOverallPercent(completedBytes, totalBytes, completed, totalFiles);
                string avgSpeed = FileHashHelper.FormatTransferSpeed(bytesSent > 0 ? bytesSent : entry.LocalFile.Length, elapsedSec);
                state.AddLog($"Transferred: {entry.Dto.FileName} ({FileHashHelper.FormatByteSize(entry.LocalFile.Length)}) | Speed: {avgSpeed} | Overall: {donePct:F1}% ({completed}/{totalFiles})");
                state.UpdateProgress((int)Math.Round(donePct));
            }
        }
        catch (OperationCanceledException)
        {
            try
            {
                using var cancelCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await _httpClient.PostAsync($"{baseUrl}/api/localsend/v2/cancel?sessionId={Uri.EscapeDataString(sessionId)}", null, cancelCts.Token);
            }
            catch { }
            throw;
        }

        state.AddLog($"[LocalSend] Direct transfer to '{peer.DeviceName}' completed ({completed} file(s)).");
        state.UpdateProgress(100);
    }
}
