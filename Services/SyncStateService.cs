using System;
using System.Collections.Generic;
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

    public int Progress { get; private set; }
    public string StatusText { get; private set; } = LocalizationService.Instance.StatusReady;
    public bool IsBusy { get; private set; }

    private CancellationTokenSource? _activeCts;

    public event Action? StateChanged;
    public event Action<string>? LogAdded;
    public event Action? QueueChanged;

    private SyncStateService()
    {
        LocalizationService.Instance.LanguageChanged += (_, _) =>
        {
            if (!IsBusy)
            {
                StatusText = LocalizationService.Instance.StatusReady;
                NotifyStateChanged();
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

    private void NotifyStateChanged()
    {
        _mainHandler.Post(() => StateChanged?.Invoke());
    }
}
