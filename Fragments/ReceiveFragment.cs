using System;
using System.Collections.Generic;
using System.IO;
using Android.Content;
using Android.OS;
using Android.Views;
using Android.Widget;
using AndroidX.Fragment.App;
using AndroidX.RecyclerView.Widget;
using Google.Android.Material.Button;
using Google.Android.Material.Card;
using Google.Android.Material.ProgressIndicator;
using SyncDroidAndroid.Models;
using SyncDroidAndroid.Services;

namespace SyncDroidAndroid.Fragments;

public class ReceiveFragment : AndroidX.Fragment.App.Fragment
{
    private LinearLayout? _layoutReceiveRoot;
    private LinearLayout? _layoutReceiveColumns;
    private LinearLayout? _paneReceiveLeft;
    private LinearLayout? _paneReceiveRight;

    private TextView? _txtSenderLabel;
    private TextView? _txtSenderDevice;
    private TextView? _badgeProtocol;
    private MaterialButton? _btnOpenFolder;

    private TextView? _txtSaveDirLabel;
    private TextView? _txtSaveDir;

    private MaterialCardView? _cardPendingConfirmBanner;
    private TextView? _txtPendingConfirmBanner;
    private MaterialButton? _btnAcceptBanner;
    private MaterialButton? _btnDeclineBanner;

    private TextView? _txtFilesHeader;
    private TextView? _txtFileCount;
    private MaterialButton? _btnClearList;
    private TextView? _txtEmptyHint;
    private RecyclerView? _recyclerReceivedFiles;
    private ReceivedFilesAdapter? _filesAdapter;

    private TextView? _txtReceiveStatus;
    private TextView? _txtReceivePercent;
    private LinearProgressIndicator? _progressReceive;

    private TextView? _txtLogsHeader;
    private MaterialButton? _btnClearLogs;
    private MaterialButton? _btnCopyLogs;
    private RecyclerView? _recyclerLogs;
    private LogListAdapter? _logAdapter;

    public override View OnCreateView(LayoutInflater inflater, ViewGroup? container, Bundle? savedInstanceState)
    {
        var view = inflater.Inflate(Resource.Layout.fragment_receive, container, false)!;

        _layoutReceiveRoot = view.FindViewById<LinearLayout>(Resource.Id.layout_receive_root);
        _layoutReceiveColumns = view.FindViewById<LinearLayout>(Resource.Id.layout_receive_columns);
        _paneReceiveLeft = view.FindViewById<LinearLayout>(Resource.Id.pane_receive_left);
        _paneReceiveRight = view.FindViewById<LinearLayout>(Resource.Id.pane_receive_right);

        _txtSenderLabel = view.FindViewById<TextView>(Resource.Id.txt_receive_sender_label);
        _txtSenderDevice = view.FindViewById<TextView>(Resource.Id.txt_receive_sender_device);
        _badgeProtocol = view.FindViewById<TextView>(Resource.Id.badge_receive_protocol);
        _btnOpenFolder = view.FindViewById<MaterialButton>(Resource.Id.btn_open_receive_folder);

        _txtSaveDirLabel = view.FindViewById<TextView>(Resource.Id.txt_receive_save_dir_label);
        _txtSaveDir = view.FindViewById<TextView>(Resource.Id.txt_receive_save_dir);

        _cardPendingConfirmBanner = view.FindViewById<MaterialCardView>(Resource.Id.card_pending_confirm_banner);
        _txtPendingConfirmBanner = view.FindViewById<TextView>(Resource.Id.txt_pending_confirm_banner);
        _btnAcceptBanner = view.FindViewById<MaterialButton>(Resource.Id.btn_accept_transfer_banner);
        _btnDeclineBanner = view.FindViewById<MaterialButton>(Resource.Id.btn_decline_transfer_banner);

        _txtFilesHeader = view.FindViewById<TextView>(Resource.Id.txt_receive_files_header);
        _txtFileCount = view.FindViewById<TextView>(Resource.Id.txt_receive_file_count);
        _btnClearList = view.FindViewById<MaterialButton>(Resource.Id.btn_clear_receive_list);
        _txtEmptyHint = view.FindViewById<TextView>(Resource.Id.txt_receive_empty_hint);
        _recyclerReceivedFiles = view.FindViewById<RecyclerView>(Resource.Id.recycler_received_files);

        _txtReceiveStatus = view.FindViewById<TextView>(Resource.Id.txt_receive_status);
        _txtReceivePercent = view.FindViewById<TextView>(Resource.Id.txt_receive_percent);
        _progressReceive = view.FindViewById<LinearProgressIndicator>(Resource.Id.progress_receive);

        _txtLogsHeader = view.FindViewById<TextView>(Resource.Id.txt_receive_logs_header);
        _btnClearLogs = view.FindViewById<MaterialButton>(Resource.Id.btn_receive_clear_logs);
        _btnCopyLogs = view.FindViewById<MaterialButton>(Resource.Id.btn_receive_copy_logs);
        _recyclerLogs = view.FindViewById<RecyclerView>(Resource.Id.recycler_receive_logs);

        _filesAdapter = new ReceivedFilesAdapter(SyncStateService.Instance.ReceivedFiles);
        _recyclerReceivedFiles!.SetLayoutManager(new LinearLayoutManager(Context));
        _recyclerReceivedFiles.SetAdapter(_filesAdapter);

        _logAdapter = new LogListAdapter(SyncStateService.Instance.Logs);
        _recyclerLogs!.SetLayoutManager(new LinearLayoutManager(Context));
        _recyclerLogs.SetAdapter(_logAdapter);

        WireEvents();
        ApplyLocalization();
        ApplyAdaptiveLayout(ResponsiveWindowService.Instance.CurrentState);
        RefreshReceiveUi();

        return view;
    }

    public override void OnResume()
    {
        base.OnResume();
        ApplyAdaptiveLayout(ResponsiveWindowService.Instance.CurrentState);
        ResponsiveWindowService.Instance.WindowStateChanged += ApplyAdaptiveLayout;
        SyncStateService.Instance.ReceiveStateChanged += RefreshReceiveUi;
        SyncStateService.Instance.StateChanged += RefreshReceiveUi;
        SyncStateService.Instance.LogAdded += OnLogAdded;
        LocalizationService.Instance.LanguageChanged += OnLanguageChanged;
        RefreshReceiveUi();
    }

    public override void OnPause()
    {
        ResponsiveWindowService.Instance.WindowStateChanged -= ApplyAdaptiveLayout;
        SyncStateService.Instance.ReceiveStateChanged -= RefreshReceiveUi;
        SyncStateService.Instance.StateChanged -= RefreshReceiveUi;
        SyncStateService.Instance.LogAdded -= OnLogAdded;
        LocalizationService.Instance.LanguageChanged -= OnLanguageChanged;
        base.OnPause();
    }

    private void ApplyAdaptiveLayout(AdaptiveWindowState state)
    {
        ResponsiveWindowService.ApplyTwoPaneLayout(
            Context,
            _layoutReceiveRoot,
            _layoutReceiveColumns,
            _paneReceiveLeft,
            _paneReceiveRight);

        if (Context != null)
        {
            float density = Context.Resources?.DisplayMetrics?.Density ?? 1f;
            if (_recyclerReceivedFiles?.LayoutParameters is LinearLayout.LayoutParams flp)
            {
                flp.Height = (int)Math.Round((state.UseTwoPaneLayout ? 230 : 180) * density);
                _recyclerReceivedFiles.LayoutParameters = flp;
            }
            if (_recyclerLogs?.LayoutParameters is LinearLayout.LayoutParams llp)
            {
                llp.Height = (int)Math.Round((state.UseTwoPaneLayout ? 340 : 200) * density);
                _recyclerLogs.LayoutParameters = llp;
            }
        }
    }

    private void WireEvents()
    {
        _btnOpenFolder!.Click += (_, _) =>
        {
            if (Context == null) return;
            var state = SyncStateService.Instance;
            var settings = SettingsService.Load();
            string dir = !string.IsNullOrWhiteSpace(state.ReceiveSaveDirectory)
                ? state.ReceiveSaveDirectory
                : settings.LocalSendReceiveDir;

            try
            {
                if (!string.IsNullOrWhiteSpace(dir))
                {
                    Directory.CreateDirectory(dir);
                }
            }
            catch { }

            StorageBrowserDialog.Show(
                Context,
                isRemotePc: false,
                allowSelectFile: true,
                initialPath: dir,
                onSelected: (selectedPath, isDir, _) =>
                {
                    string targetDir = isDir
                        ? selectedPath
                        : (Path.GetDirectoryName(selectedPath)?.Replace('\\', '/') ?? dir);
                    if (!string.IsNullOrWhiteSpace(targetDir))
                    {
                        var s = SettingsService.Load();
                        s.LocalSendReceiveDir = targetDir;
                        SettingsService.Save(s);
                        if (_txtSaveDir != null) _txtSaveDir.Text = targetDir;
                    }
                });
        };

        _btnAcceptBanner!.Click += (_, _) =>
        {
            SyncStateService.Instance.SyncConfirmationDecision(accepted: true, fromBanner: true);
        };

        _btnDeclineBanner!.Click += (_, _) =>
        {
            SyncStateService.Instance.SyncConfirmationDecision(accepted: false, fromBanner: true);
        };

        _btnClearList!.Click += (_, _) =>
        {
            SyncStateService.Instance.ClearReceivedFiles();
        };

        _btnClearLogs!.Click += (_, _) =>
        {
            SyncStateService.Instance.ClearLogs();
            _logAdapter?.NotifyDataSetChanged();
        };

        _btnCopyLogs!.Click += (_, _) =>
        {
            if (Context == null) return;
            var allLogs = string.Join(System.Environment.NewLine, SyncStateService.Instance.Logs);
            var clipboard = (ClipboardManager?)Context.GetSystemService(Context.ClipboardService);
            if (clipboard != null)
            {
                clipboard.PrimaryClip = ClipData.NewPlainText("SyncDroid Logs", allLogs);
                SyncStateService.Instance.AddLog("Logs copied to clipboard.");
            }
        };
    }

    private void RefreshReceiveUi()
    {
        var state = SyncStateService.Instance;
        var loc = LocalizationService.Instance;

        if (_txtSenderDevice != null)
        {
            _txtSenderDevice.Text = string.IsNullOrWhiteSpace(state.ReceiveSenderDisplay)
                ? loc.ReceiveNoActiveSender
                : state.ReceiveSenderDisplay;
        }

        if (_badgeProtocol != null)
        {
            if (!string.IsNullOrWhiteSpace(state.ReceiveProtocol))
            {
                _badgeProtocol.Text = state.ReceiveProtocol;
                _badgeProtocol.Visibility = ViewStates.Visible;
            }
            else
            {
                _badgeProtocol.Visibility = ViewStates.Gone;
            }
        }

        if (_txtSaveDir != null)
        {
            _txtSaveDir.Text = !string.IsNullOrWhiteSpace(state.ReceiveSaveDirectory)
                ? state.ReceiveSaveDirectory
                : SettingsService.Load().LocalSendReceiveDir;
        }

        var pending = state.PendingConfirmArgs;
        if (_cardPendingConfirmBanner != null)
        {
            if (pending != null && pending.RequiresConfirmation)
            {
                if (_txtPendingConfirmBanner != null)
                {
                    _txtPendingConfirmBanner.Text = string.Format(
                        loc.ReceivePendingConfirmBannerFormat,
                        state.ReceiveSenderDisplay,
                        pending.Files.Count);
                }
                _cardPendingConfirmBanner.Visibility = ViewStates.Visible;
            }
            else
            {
                _cardPendingConfirmBanner.Visibility = ViewStates.Gone;
            }
        }

        int count = state.ReceivedFiles.Count;
        if (_txtFileCount != null) _txtFileCount.Text = count.ToString();
        if (_txtEmptyHint != null) _txtEmptyHint.Visibility = count == 0 ? ViewStates.Visible : ViewStates.Gone;
        if (_recyclerReceivedFiles != null) _recyclerReceivedFiles.Visibility = count == 0 ? ViewStates.Gone : ViewStates.Visible;
        _filesAdapter?.NotifyDataSetChanged();
        if (count > 0)
        {
            _recyclerReceivedFiles?.ScrollToPosition(count - 1);
        }

        if (_txtReceiveStatus != null) _txtReceiveStatus.Text = state.ReceiveStatusText;
        if (_txtReceivePercent != null) _txtReceivePercent.Text = $"{state.ReceiveProgress}%";
        if (_progressReceive != null) _progressReceive.Progress = state.ReceiveProgress;

        _logAdapter?.NotifyDataSetChanged();
    }

    private void OnLogAdded(string line)
    {
        if (_logAdapter == null || _recyclerLogs == null) return;
        _logAdapter.NotifyItemInserted(SyncStateService.Instance.Logs.Count - 1);
        _recyclerLogs.ScrollToPosition(Math.Max(0, SyncStateService.Instance.Logs.Count - 1));
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        ApplyLocalization();
        RefreshReceiveUi();
    }

    private void ApplyLocalization()
    {
        var loc = LocalizationService.Instance;
        if (_txtSenderLabel != null) _txtSenderLabel.Text = loc.ReceiveSenderLabel;
        if (_btnOpenFolder != null) _btnOpenFolder.Text = loc.OpenReceiveFolderButton;
        if (_txtSaveDirLabel != null) _txtSaveDirLabel.Text = loc.ReceiveSaveDirLabel;
        if (_btnAcceptBanner != null) _btnAcceptBanner.Text = loc.AcceptTransferButton;
        if (_btnDeclineBanner != null) _btnDeclineBanner.Text = loc.DeclineTransferButton;
        if (_txtFilesHeader != null) _txtFilesHeader.Text = loc.ReceiveFilesHeader;
        if (_btnClearList != null) _btnClearList.Text = loc.ClearReceiveListButton;
        if (_txtEmptyHint != null) _txtEmptyHint.Text = loc.ReceiveEmptyFilesHint;
        if (_txtLogsHeader != null) _txtLogsHeader.Text = loc.ActivityLogsHeader;
        if (_btnClearLogs != null) _btnClearLogs.Text = loc.ClearButton;
        if (_btnCopyLogs != null) _btnCopyLogs.Text = loc.CopyAllButton;
    }

    private sealed class ReceivedFilesAdapter : RecyclerView.Adapter
    {
        private readonly List<ReceivedFileItem> _items;

        public ReceivedFilesAdapter(List<ReceivedFileItem> items) => _items = items;

        public override int ItemCount => _items.Count;

        public override RecyclerView.ViewHolder OnCreateViewHolder(ViewGroup parent, int viewType)
        {
            var v = LayoutInflater.From(parent.Context)!.Inflate(Resource.Layout.item_received_file, parent, false)!;
            return new FileViewHolder(v);
        }

        public override void OnBindViewHolder(RecyclerView.ViewHolder holder, int position)
        {
            var vh = (FileViewHolder)holder;
            var item = _items[position];
            vh.Protocol.Text = item.Protocol;
            vh.FileName.Text = item.FileName;
            vh.SavedPath.Text = item.SavedPath;
            vh.Size.Text = item.SizeDisplay;
            vh.Status.Text = item.StatusText;
        }

        private sealed class FileViewHolder : RecyclerView.ViewHolder
        {
            public TextView Protocol { get; }
            public TextView FileName { get; }
            public TextView SavedPath { get; }
            public TextView Size { get; }
            public TextView Status { get; }

            public FileViewHolder(View itemView) : base(itemView)
            {
                Protocol = itemView.FindViewById<TextView>(Resource.Id.txt_received_protocol)!;
                FileName = itemView.FindViewById<TextView>(Resource.Id.txt_received_filename)!;
                SavedPath = itemView.FindViewById<TextView>(Resource.Id.txt_received_saved_path)!;
                Size = itemView.FindViewById<TextView>(Resource.Id.txt_received_size)!;
                Status = itemView.FindViewById<TextView>(Resource.Id.txt_received_status)!;
            }
        }
    }

    private sealed class LogListAdapter : RecyclerView.Adapter
    {
        private readonly List<string> _logs;

        public LogListAdapter(List<string> logs) => _logs = logs;

        public override int ItemCount => _logs.Count;

        public override RecyclerView.ViewHolder OnCreateViewHolder(ViewGroup parent, int viewType)
        {
            var tv = (TextView)LayoutInflater.From(parent.Context)!.Inflate(Resource.Layout.item_log_line, parent, false)!;
            return new LogViewHolder(tv);
        }

        public override void OnBindViewHolder(RecyclerView.ViewHolder holder, int position)
        {
            ((LogViewHolder)holder).TextView.Text = _logs[position];
        }

        private sealed class LogViewHolder : RecyclerView.ViewHolder
        {
            public TextView TextView { get; }
            public LogViewHolder(TextView tv) : base(tv) => TextView = tv;
        }
    }
}
