using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Android.OS;
using SyncDroidAndroid.Models;

namespace SyncDroidAndroid.Services;

public class SyncStateService
{
    public static SyncStateService Instance { get; } = new();

    private readonly Handler _mainHandler = new(Looper.MainLooper!);
    private readonly object _lock = new();

    public List<string> Logs { get; } = new();
    public List<TransferQueueItem> TransferQueue { get; } = new();
    public List<ReceivedFileItem> ReceivedFiles { get; } = new();

    public int Progress { get; private set; }
    public string StatusText { get; private set; } = LocalizationService.Instance.StatusReady;
    public bool IsBusy { get; private set; }

    // Receive Session State
    public string ActiveReceiveSessionId { get; private set; } = "";
    public string ReceiveSenderDisplay { get; private set; } = "";
    public string ReceiveProtocol { get; private set; } = "";
    public string ReceiveSaveDirectory { get; private set; } = "";
    public string ReceiveStatusText { get; private set; } = LocalizationService.Instance.StatusReady;
    public int ReceiveProgress { get; private set; }
    public IncomingTransferSessionEventArgs? PendingConfirmArgs { get; private set; }

    private CancellationTokenSource? _activeCts;

    public event Action? StateChanged;
    public event Action<string>? LogAdded;
    public event Action? QueueChanged;
    public event Action? ReceiveStateChanged;
    public event Action<IncomingTransferSessionEventArgs>? IncomingTransferSessionStarted;
    public event Action? ConfirmationResolvedFromBanner;

    private SyncStateService()
    {
        var settings = SettingsService.Load();
        ReceiveSaveDirectory = settings.LocalSendReceiveDir;

        LocalizationService.Instance.LanguageChanged += (_, _) =>
        {
            if (!IsBusy)
            {
                StatusText = LocalizationService.Instance.StatusReady;
                NotifyStateChanged();
            }
            if (ReceivedFiles.Count == 0 && PendingConfirmArgs == null)
            {
                ReceiveStatusText = LocalizationService.Instance.StatusReady;
                NotifyReceiveStateChanged();
            }
        };
    }

    public CancellationToken BeginOperation(string initialStatus)
    {
        lock (_lock)
        {
            _activeCts?.Cancel();
            _activeCts?.Dispose();
            _activeCts = new CancellationTokenSource();
            IsBusy = true;
            Progress = 0;
            StatusText = initialStatus;
        }
        NotifyStateChanged();
        return _activeCts.Token;
    }

    public void EndOperation(string finalStatus, int finalProgress = 100)
    {
        lock (_lock)
        {
            _activeCts?.Dispose();
            _activeCts = null;
            IsBusy = false;
            Progress = finalProgress;
            StatusText = finalStatus;
        }
        NotifyStateChanged();
    }

    public void CancelCurrentOperation()
    {
        lock (_lock)
        {
            _activeCts?.Cancel();
        }
        AddLog("Cancellation requested by user...");
    }

    public void UpdateProgress(int progress, string? status = null)
    {
        int clamped = Math.Clamp(progress, 0, 100);
        _mainHandler.Post(() =>
        {
            Progress = clamped;
            if (!string.IsNullOrEmpty(status))
            {
                StatusText = status;
            }
            StateChanged?.Invoke();
        });
    }

    public void AddLog(string message)
    {
        string line = $"[{DateTime.Now:HH:mm:ss}] {message}";
        _mainHandler.Post(() =>
        {
            Logs.Add(line);
            if (Logs.Count > 1000)
            {
                Logs.RemoveAt(0);
            }
            LogAdded?.Invoke(line);
        });
    }

    public void ClearLogs()
    {
        _mainHandler.Post(() =>
        {
            Logs.Clear();
            StateChanged?.Invoke();
        });
    }

    public void AddToQueue(TransferQueueItem item)
    {
        _mainHandler.Post(() =>
        {
            if (!TransferQueue.Exists(x => string.Equals(x.FullPath, item.FullPath, StringComparison.Ordinal)))
            {
                TransferQueue.Add(item);
                QueueChanged?.Invoke();
            }
        });
    }

    public void RemoveFromQueueAt(int index)
    {
        _mainHandler.Post(() =>
        {
            if (index >= 0 && index < TransferQueue.Count)
            {
                TransferQueue.RemoveAt(index);
                QueueChanged?.Invoke();
            }
        });
    }

    public void ClearQueue()
    {
        _mainHandler.Post(() =>
        {
            TransferQueue.Clear();
            QueueChanged?.Invoke();
        });
    }

    // =========================================================================
    // Incoming Receive Session State Management
    // =========================================================================

    public void HandleIncomingSession(IncomingTransferSessionEventArgs args)
    {
        _mainHandler.Post(() =>
        {
            var loc = LocalizationService.Instance;
            bool isSameSession = string.Equals(ActiveReceiveSessionId, args.SessionId, StringComparison.Ordinal);
            ActiveReceiveSessionId = args.SessionId;

            ReceiveSenderDisplay = string.IsNullOrWhiteSpace(args.SenderIp) ||
                                   args.SenderName.Contains(args.SenderIp, StringComparison.OrdinalIgnoreCase)
                ? args.SenderName
                : $"{args.SenderName} ({args.SenderIp})";
            ReceiveProtocol = args.Protocol;

            if (!string.IsNullOrWhiteSpace(args.SaveDirectory))
            {
                ReceiveSaveDirectory = args.SaveDirectory;
            }

            if (!args.AppendToExistingSession && !isSameSession)
            {
                ReceivedFiles.Clear();
            }
            else if (!args.AppendToExistingSession)
            {
                ReceivedFiles.Clear();
            }

            foreach (var f in args.Files)
            {
                if (!ReceivedFiles.Any(existing => string.Equals(existing.Id, f.Id, StringComparison.Ordinal)))
                {
                    ReceivedFiles.Add(f);
                }
            }

            if (args.RequiresConfirmation && args.ConfirmationTcs != null)
            {
                PendingConfirmArgs = args;
                ReceiveStatusText = loc.ReceiveStatusPending;
                ReceiveProgress = 0;
            }
            else
            {
                PendingConfirmArgs = null;
                ReceiveStatusText = loc.ReceiveStatusReceiving;
            }

            ReceiveStateChanged?.Invoke();
            IncomingTransferSessionStarted?.Invoke(args);
        });
    }

    public void SyncConfirmationDecision(bool accepted, bool fromBanner = false)
    {
        _mainHandler.Post(() =>
        {
            var loc = LocalizationService.Instance;
            var pending = PendingConfirmArgs;
            PendingConfirmArgs = null;

            foreach (var item in ReceivedFiles)
            {
                if (item.StatusText == loc.ReceiveStatusPending)
                {
                    item.StatusText = accepted ? loc.ReceiveStatusWaitingUpload : loc.ReceiveStatusDeclined;
                }
            }

            ReceiveStatusText = accepted ? loc.ReceiveStatusReceiving : loc.ReceiveStatusDeclined;
            if (!accepted)
            {
                ReceiveProgress = 0;
            }

            pending?.ConfirmationTcs?.TrySetResult(accepted);

            ReceiveStateChanged?.Invoke();
            if (fromBanner)
            {
                ConfirmationResolvedFromBanner?.Invoke();
            }
        });
    }

    public void UpdateIncomingFileProgress(string sessionId, string fileId, double percent, string speed)
    {
        _mainHandler.Post(() =>
        {
            var loc = LocalizationService.Instance;
            var item = ReceivedFiles.FirstOrDefault(f => string.Equals(f.Id, fileId, StringComparison.Ordinal));
            if (item != null)
            {
                item.ProgressPercent = percent;
                item.StatusText = string.IsNullOrWhiteSpace(speed)
                    ? $"{loc.ReceiveStatusReceiving} ({percent:F0}%)"
                    : $"{loc.ReceiveStatusReceiving} ({percent:F0}% • {speed})";
            }

            ReceiveStatusText = loc.ReceiveStatusReceiving;
            RecomputeOverallReceiveProgress();
            ReceiveStateChanged?.Invoke();
        });
    }

    public void CompleteIncomingFile(string sessionId, string fileId, string savedPath, bool skipped, bool failed)
    {
        _mainHandler.Post(() =>
        {
            var loc = LocalizationService.Instance;
            var item = ReceivedFiles.FirstOrDefault(f => string.Equals(f.Id, fileId, StringComparison.Ordinal));
            if (item != null)
            {
                if (!string.IsNullOrWhiteSpace(savedPath))
                {
                    item.SavedPath = savedPath;
                }

                if (failed)
                {
                    item.StatusText = loc.ReceiveStatusFailed;
                }
                else if (skipped)
                {
                    item.ProgressPercent = 100;
                    item.StatusText = loc.ReceiveStatusSkipped;
                }
                else
                {
                    item.ProgressPercent = 100;
                    item.StatusText = loc.ReceiveStatusCompleted;
                }
            }

            RecomputeOverallReceiveProgress();
            ReceiveStateChanged?.Invoke();
        });
    }

    private void RecomputeOverallReceiveProgress()
    {
        var loc = LocalizationService.Instance;
        if (ReceivedFiles.Count == 0)
        {
            ReceiveProgress = 0;
            return;
        }

        double avg = ReceivedFiles.Average(f => f.ProgressPercent);
        ReceiveProgress = (int)Math.Round(Math.Clamp(avg, 0, 100));
        Progress = ReceiveProgress;
        StatusText = ReceiveStatusText;

        bool allFinished = ReceivedFiles.All(f =>
            f.ProgressPercent >= 100 ||
            f.StatusText == loc.ReceiveStatusCompleted ||
            f.StatusText == loc.ReceiveStatusSkipped ||
            f.StatusText == loc.ReceiveStatusFailed ||
            f.StatusText == loc.ReceiveStatusDeclined);

        if (allFinished && ReceivedFiles.Count > 0)
        {
            bool anyFailed = ReceivedFiles.Any(f => f.StatusText == loc.ReceiveStatusFailed);
            ReceiveStatusText = anyFailed ? loc.ReceiveStatusFailed : loc.StatusTransferCompleted;
            if (!anyFailed)
            {
                ReceiveProgress = 100;
                Progress = 100;
            }
            StatusText = ReceiveStatusText;
        }

        StateChanged?.Invoke();
    }

    public void ClearReceivedFiles()
    {
        _mainHandler.Post(() =>
        {
            ReceivedFiles.Clear();
            ReceiveProgress = 0;
            ReceiveStatusText = LocalizationService.Instance.StatusReady;
            ReceiveStateChanged?.Invoke();
        });
    }

    private void NotifyStateChanged()
    {
        _mainHandler.Post(() => StateChanged?.Invoke());
    }

    private void NotifyReceiveStateChanged()
    {
        _mainHandler.Post(() => ReceiveStateChanged?.Invoke());
    }
}
