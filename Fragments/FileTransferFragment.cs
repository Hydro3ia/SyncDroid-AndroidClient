using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Android.OS;
using Android.Views;
using Android.Widget;
using AndroidX.Fragment.App;
using AndroidX.RecyclerView.Widget;
using Google.Android.Material.Button;
using Google.Android.Material.ProgressIndicator;
using Google.Android.Material.TextField;
using SyncDroidAndroid.Models;
using SyncDroidAndroid.Services;

namespace SyncDroidAndroid.Fragments;

public class FileTransferFragment : AndroidX.Fragment.App.Fragment
{
    private bool _isAndroidToPc = true;
    private ConflictPolicy _conflictPolicy = ConflictPolicy.SkipIdenticalSize;

    private LinearLayout? _layoutTxRoot;
    private LinearLayout? _layoutTxColumns;
    private LinearLayout? _paneTxLeft;
    private LinearLayout? _paneTxRight;

    private TextView? _txtModeTitle;
    private TextView? _txtModeDesc;
    private MaterialButtonToggleGroup? _toggleTransferDir;
    private MaterialButton? _btnTxAndroidToPc;
    private MaterialButton? _btnTxPcToAndroid;

    private TextView? _txtConflictLabel;
    private MaterialButtonToggleGroup? _toggleConflictPolicy;
    private MaterialButton? _btnConflictSkip;
    private MaterialButton? _btnConflictOverwrite;
    private MaterialButton? _btnConflictRename;

    private TextView? _txtQueueHeader;
    private MaterialButton? _btnClearQueue;
    private MaterialButton? _btnAddFile;
    private MaterialButton? _btnAddFolder;
    private TextView? _txtQueueEmpty;
    private RecyclerView? _recyclerQueue;
    private QueueListAdapter? _queueAdapter;

    private TextView? _txtTargetLabel;
    private MaterialAutoCompleteTextView? _inputTargetPath;
    private MaterialButton? _btnBrowseTarget;

    private MaterialButton? _btnStartTransfer;
    private MaterialButton? _btnCancelTransfer;
    private TextView? _txtTxStatus;
    private TextView? _txtTxPercent;
    private LinearProgressIndicator? _progressTransfer;

    public override View OnCreateView(LayoutInflater inflater, ViewGroup? container, Bundle? savedInstanceState)
    {
        var view = inflater.Inflate(Resource.Layout.fragment_file_transfer, container, false)!;

        _layoutTxRoot = view.FindViewById<LinearLayout>(Resource.Id.layout_tx_root);
        _layoutTxColumns = view.FindViewById<LinearLayout>(Resource.Id.layout_tx_columns);
        _paneTxLeft = view.FindViewById<LinearLayout>(Resource.Id.pane_tx_left);
        _paneTxRight = view.FindViewById<LinearLayout>(Resource.Id.pane_tx_right);

        _txtModeTitle = view.FindViewById<TextView>(Resource.Id.txt_tx_mode_title);
        _txtModeDesc = view.FindViewById<TextView>(Resource.Id.txt_tx_mode_desc);
        _toggleTransferDir = view.FindViewById<MaterialButtonToggleGroup>(Resource.Id.toggle_transfer_dir);
        _btnTxAndroidToPc = view.FindViewById<MaterialButton>(Resource.Id.btn_tx_android_to_pc);
        _btnTxPcToAndroid = view.FindViewById<MaterialButton>(Resource.Id.btn_tx_pc_to_android);

        _txtConflictLabel = view.FindViewById<TextView>(Resource.Id.txt_conflict_label);
        _toggleConflictPolicy = view.FindViewById<MaterialButtonToggleGroup>(Resource.Id.toggle_conflict_policy);
        _btnConflictSkip = view.FindViewById<MaterialButton>(Resource.Id.btn_conflict_skip);
        _btnConflictOverwrite = view.FindViewById<MaterialButton>(Resource.Id.btn_conflict_overwrite);
        _btnConflictRename = view.FindViewById<MaterialButton>(Resource.Id.btn_conflict_rename);

        _txtQueueHeader = view.FindViewById<TextView>(Resource.Id.txt_queue_header);
        _btnClearQueue = view.FindViewById<MaterialButton>(Resource.Id.btn_clear_queue);
        _btnAddFile = view.FindViewById<MaterialButton>(Resource.Id.btn_add_file);
        _btnAddFolder = view.FindViewById<MaterialButton>(Resource.Id.btn_add_folder);
        _txtQueueEmpty = view.FindViewById<TextView>(Resource.Id.txt_queue_empty);
        _recyclerQueue = view.FindViewById<RecyclerView>(Resource.Id.recycler_transfer_queue);

        _txtTargetLabel = view.FindViewById<TextView>(Resource.Id.txt_tx_target_label);
        _inputTargetPath = view.FindViewById<MaterialAutoCompleteTextView>(Resource.Id.input_tx_target_path);
        _btnBrowseTarget = view.FindViewById<MaterialButton>(Resource.Id.btn_browse_tx_target);

        _btnStartTransfer = view.FindViewById<MaterialButton>(Resource.Id.btn_start_transfer);
        _btnCancelTransfer = view.FindViewById<MaterialButton>(Resource.Id.btn_cancel_transfer);
        _txtTxStatus = view.FindViewById<TextView>(Resource.Id.txt_tx_status);
        _txtTxPercent = view.FindViewById<TextView>(Resource.Id.txt_tx_percent);
        _progressTransfer = view.FindViewById<LinearProgressIndicator>(Resource.Id.progress_transfer);

        _queueAdapter = new QueueListAdapter(
            SyncStateService.Instance.TransferQueue,
            idx => SyncStateService.Instance.RemoveFromQueueAt(idx));
        _recyclerQueue!.SetLayoutManager(new LinearLayoutManager(Context));
        _recyclerQueue.SetAdapter(_queueAdapter);

        WireEvents();
        ApplyLocalization();
        LoadTargetPathForDirection();
        ApplyAdaptiveLayout(ResponsiveWindowService.Instance.CurrentState);
        RefreshQueueUi();
        RefreshTransferStateUi();

        return view;
    }

    public override void OnResume()
    {
        base.OnResume();
        ApplyAdaptiveLayout(ResponsiveWindowService.Instance.CurrentState);
        ResponsiveWindowService.Instance.WindowStateChanged += ApplyAdaptiveLayout;
        CompanionNetworkService.Instance.ConnectionStateChanged += LoadTargetPathForDirection;
        SyncStateService.Instance.QueueChanged += RefreshQueueUi;
        SyncStateService.Instance.StateChanged += RefreshTransferStateUi;
        LocalizationService.Instance.LanguageChanged += OnLanguageChanged;
        LoadTargetPathForDirection();
        RefreshQueueUi();
    }

    public override void OnPause()
    {
        SaveTargetPathForDirection();
        ResponsiveWindowService.Instance.WindowStateChanged -= ApplyAdaptiveLayout;
        CompanionNetworkService.Instance.ConnectionStateChanged -= LoadTargetPathForDirection;
        SyncStateService.Instance.QueueChanged -= RefreshQueueUi;
        SyncStateService.Instance.StateChanged -= RefreshTransferStateUi;
        LocalizationService.Instance.LanguageChanged -= OnLanguageChanged;
        base.OnPause();
    }

    private void ApplyAdaptiveLayout(AdaptiveWindowState state)
    {
        ResponsiveWindowService.ApplyTwoPaneLayout(
            Context,
            _layoutTxRoot,
            _layoutTxColumns,
            _paneTxLeft,
            _paneTxRight);

        if (_recyclerQueue != null && Context != null)
        {
            float density = Context.Resources?.DisplayMetrics?.Density ?? 1f;
            int queueHeightDp = state.UseTwoPaneLayout ? 230 : 150;
            if (_recyclerQueue.LayoutParameters is LinearLayout.LayoutParams lp)
            {
                lp.Height = (int)Math.Round(queueHeightDp * density);
                _recyclerQueue.LayoutParameters = lp;
            }
        }
    }

    private void WireEvents()
    {
        _toggleTransferDir!.ButtonChecked += (_, _) =>
        {
            int checkedId = _toggleTransferDir.CheckedButtonId;
            bool newDir = checkedId == Resource.Id.btn_tx_android_to_pc;
            if (newDir == _isAndroidToPc) return;
            SaveTargetPathForDirection();
            _isAndroidToPc = newDir;
            SyncStateService.Instance.ClearQueue();
            LoadTargetPathForDirection();
            ApplyLocalization();
        };

        _toggleConflictPolicy!.ButtonChecked += (_, _) =>
        {
            int checkedId = _toggleConflictPolicy.CheckedButtonId;
            if (checkedId == Resource.Id.btn_conflict_skip)
                _conflictPolicy = ConflictPolicy.SkipIdenticalSize;
            else if (checkedId == Resource.Id.btn_conflict_overwrite)
                _conflictPolicy = ConflictPolicy.Overwrite;
            else if (checkedId == Resource.Id.btn_conflict_rename)
                _conflictPolicy = ConflictPolicy.KeepBothRename;
        };

        _btnClearQueue!.Click += (_, _) => SyncStateService.Instance.ClearQueue();

        _btnAddFile!.Click += (_, _) =>
        {
            if (Context == null) return;
            bool browseRemotePc = !_isAndroidToPc;
            StorageBrowserDialog.Show(
                Context,
                isRemotePc: browseRemotePc,
                allowSelectFile: true,
                initialPath: browseRemotePc ? @"C:\" : "/storage/emulated/0",
                onSelected: (selectedPath, isDir, size) =>
                {
                    long finalSize = size;
                    if (!browseRemotePc && !isDir && File.Exists(selectedPath))
                    {
                        finalSize = new FileInfo(selectedPath).Length;
                    }

                    SyncStateService.Instance.AddToQueue(new TransferQueueItem
                    {
                        FullPath = selectedPath,
                        DisplayName = Path.GetFileName(selectedPath.TrimEnd('/', '\\')),
                        Size = finalSize,
                        IsDirectory = isDir,
                        IsRemotePcItem = browseRemotePc
                    });
                });
        };

        _btnAddFolder!.Click += (_, _) =>
        {
            if (Context == null) return;
            bool browseRemotePc = !_isAndroidToPc;
            StorageBrowserDialog.Show(
                Context,
                isRemotePc: browseRemotePc,
                allowSelectFile: false,
                initialPath: browseRemotePc ? @"C:\" : "/storage/emulated/0",
                onSelected: (selectedPath, _, _) =>
                {
                    SyncStateService.Instance.AddToQueue(new TransferQueueItem
                    {
                        FullPath = selectedPath,
                        DisplayName = Path.GetFileName(selectedPath.TrimEnd('/', '\\')),
                        Size = 0,
                        IsDirectory = true,
                        IsRemotePcItem = browseRemotePc
                    });
                });
        };

        _btnBrowseTarget!.Click += (_, _) =>
        {
            if (Context == null) return;
            bool targetIsRemotePc = _isAndroidToPc;
            StorageBrowserDialog.Show(
                Context,
                isRemotePc: targetIsRemotePc,
                allowSelectFile: false,
                initialPath: _inputTargetPath?.Text ?? (targetIsRemotePc ? @"C:\" : "/storage/emulated/0/Download"),
                onSelected: (selectedPath, _, _) =>
                {
                    if (_inputTargetPath != null) _inputTargetPath.Text = selectedPath;
                    SaveTargetPathForDirection();
                });
        };

        _btnStartTransfer!.Click += async (_, _) => await StartDirectTransferAsync();
        _btnCancelTransfer!.Click += (_, _) => SyncStateService.Instance.CancelCurrentOperation();
    }

    private bool IsLocalSendSendMode =>
        _isAndroidToPc &&
        CompanionNetworkService.Instance.ConnectedPeer?.ProtocolType == PeerProtocolType.LocalSend;

    private void LoadTargetPathForDirection()
    {
        if (IsLocalSendSendMode)
        {
            if (_inputTargetPath != null)
            {
                _inputTargetPath.Enabled = false;
                _inputTargetPath.SetText(LocalizationService.Instance.LocalSendAutoSaveTargetHint, false);
            }
            if (_btnBrowseTarget != null)
            {
                _btnBrowseTarget.Enabled = false;
            }
            return;
        }

        if (_inputTargetPath != null) _inputTargetPath.Enabled = true;
        if (_btnBrowseTarget != null) _btnBrowseTarget.Enabled = true;

        var settings = SettingsService.Load();
        if (Context != null && _inputTargetPath != null)
        {
            var history = _isAndroidToPc ? settings.PcPathHistory : settings.AndroidPathHistory;
            _inputTargetPath.Adapter = new ArrayAdapter<string>(
                Context, Android.Resource.Layout.SimpleDropDownItem1Line, history);
        }

        _inputTargetPath?.SetText(
            _isAndroidToPc ? settings.DirectTransfer_PcTargetDir : settings.DirectTransfer_AndroidTargetDir,
            false);
    }

    private void SaveTargetPathForDirection()
    {
        if (IsLocalSendSendMode) return;

        string target = _inputTargetPath?.Text?.Trim() ?? "";
        if (string.IsNullOrEmpty(target) ||
            string.Equals(target, LocalizationService.Instance.LocalSendAutoSaveTargetHint, StringComparison.Ordinal))
            return;

        var settings = SettingsService.Load();
        if (_isAndroidToPc)
        {
            settings.DirectTransfer_PcTargetDir = target;
            SettingsService.AddToHistoryList(settings.PcPathHistory, target, ignoreCase: true);
        }
        else
        {
            settings.DirectTransfer_AndroidTargetDir = target;
            SettingsService.AddToHistoryList(settings.AndroidPathHistory, target, ignoreCase: false);
        }
        SettingsService.Save(settings);
    }

    private async Task StartDirectTransferAsync()
    {
        var state = SyncStateService.Instance;
        var loc = LocalizationService.Instance;

        if (state.IsBusy)
        {
            state.AddLog("Another sync or transfer operation is currently active.");
            return;
        }

        if (state.TransferQueue.Count == 0)
        {
            state.AddLog("Error: Transfer queue is empty. Please add files or folders first.");
            return;
        }

        string targetDir = _inputTargetPath?.Text?.Trim() ?? "";
        if (!IsLocalSendSendMode && string.IsNullOrWhiteSpace(targetDir))
        {
            state.AddLog("Error: Target destination directory cannot be empty.");
            return;
        }

        SaveTargetPathForDirection();
        var snapshot = state.TransferQueue.ToList();
        var token = state.BeginOperation(loc.StatusTransferring);

        try
        {
            await MirrorSyncEngine.Instance.ExecuteDirectTransferAsync(
                snapshot,
                targetDir,
                _isAndroidToPc,
                _conflictPolicy,
                token);
            state.EndOperation(loc.StatusCompleted, 100);
        }
        catch (System.OperationCanceledException)
        {
            state.AddLog("Direct transfer was cancelled.");
            state.EndOperation(loc.StatusCancelled, state.Progress);
        }
        catch (Exception ex)
        {
            state.AddLog($"Transfer Error: {ex.Message}");
            state.EndOperation(loc.StatusFailed, state.Progress);
        }
    }

    private void RefreshQueueUi()
    {
        var queue = SyncStateService.Instance.TransferQueue;
        _queueAdapter?.NotifyDataSetChanged();
        if (_txtQueueEmpty != null)
            _txtQueueEmpty.Visibility = queue.Count == 0 ? ViewStates.Visible : ViewStates.Gone;
        if (_recyclerQueue != null)
            _recyclerQueue.Visibility = queue.Count == 0 ? ViewStates.Gone : ViewStates.Visible;
        if (_txtQueueHeader != null)
            _txtQueueHeader.Text = $"{LocalizationService.Instance.QueueHeader} ({queue.Count})";
    }

    private void RefreshTransferStateUi()
    {
        var state = SyncStateService.Instance;
        if (_txtTxStatus != null) _txtTxStatus.Text = state.StatusText;
        if (_txtTxPercent != null) _txtTxPercent.Text = $"{state.Progress}%";
        if (_progressTransfer != null) _progressTransfer.Progress = state.Progress;
    }

    private void OnLanguageChanged(object? sender, EventArgs e) => ApplyLocalization();

    private void ApplyLocalization()
    {
        var loc = LocalizationService.Instance;
        if (_txtModeTitle != null) _txtModeTitle.Text = loc.TxModeTitle;
        if (_txtModeDesc != null) _txtModeDesc.Text = loc.TxModeDesc;
        if (_btnTxAndroidToPc != null) _btnTxAndroidToPc.Text = loc.TxDirAndroidToPc;
        if (_btnTxPcToAndroid != null) _btnTxPcToAndroid.Text = loc.TxDirPcToAndroid;

        if (_txtConflictLabel != null) _txtConflictLabel.Text = loc.ConflictLabel;
        if (_btnConflictSkip != null) _btnConflictSkip.Text = loc.ConflictSkip;
        if (_btnConflictOverwrite != null) _btnConflictOverwrite.Text = loc.ConflictOverwrite;
        if (_btnConflictRename != null) _btnConflictRename.Text = loc.ConflictRename;

        if (_btnClearQueue != null) _btnClearQueue.Text = loc.ClearQueueButton;
        if (_btnAddFile != null) _btnAddFile.Text = loc.AddFileButton;
        if (_btnAddFolder != null) _btnAddFolder.Text = loc.AddFolderButton;
        if (_txtQueueEmpty != null) _txtQueueEmpty.Text = loc.QueueEmptyHint;

        if (_txtTargetLabel != null)
            _txtTargetLabel.Text = _isAndroidToPc ? loc.TargetPcFolderLabel : loc.TargetAndroidFolderLabel;
        if (_btnBrowseTarget != null) _btnBrowseTarget.Text = loc.BrowseButton;
        if (_btnStartTransfer != null) _btnStartTransfer.Text = loc.StartTransferButton;
        if (_btnCancelTransfer != null) _btnCancelTransfer.Text = loc.CancelButton;

        if (IsLocalSendSendMode && _inputTargetPath != null)
        {
            _inputTargetPath.SetText(loc.LocalSendAutoSaveTargetHint, false);
        }

        RefreshQueueUi();
    }

    private sealed class QueueListAdapter : RecyclerView.Adapter
    {
        private readonly List<TransferQueueItem> _items;
        private readonly Action<int> _onRemove;

        public QueueListAdapter(List<TransferQueueItem> items, Action<int> onRemove)
        {
            _items = items;
            _onRemove = onRemove;
        }

        public override int ItemCount => _items.Count;

        public override RecyclerView.ViewHolder OnCreateViewHolder(ViewGroup parent, int viewType)
        {
            var v = LayoutInflater.From(parent.Context)!.Inflate(Resource.Layout.item_transfer_queue, parent, false)!;
            return new QueueViewHolder(v);
        }

        public override void OnBindViewHolder(RecyclerView.ViewHolder holder, int position)
        {
            var vh = (QueueViewHolder)holder;
            var item = _items[position];
            vh.Icon.SetImageResource(item.IsDirectory ? Resource.Drawable.ic_folder : Resource.Drawable.ic_file);
            vh.Name.Text = item.DisplayName;
            vh.Meta.Text = item.FormattedMeta;

            vh.RemoveBtn.Click -= vh.CurrentClick;
            vh.CurrentClick = (_, _) => _onRemove(vh.BindingAdapterPosition);
            vh.RemoveBtn.Click += vh.CurrentClick;
        }

        private sealed class QueueViewHolder : RecyclerView.ViewHolder
        {
            public ImageView Icon { get; }
            public TextView Name { get; }
            public TextView Meta { get; }
            public MaterialButton RemoveBtn { get; }
            public EventHandler? CurrentClick { get; set; }

            public QueueViewHolder(View itemView) : base(itemView)
            {
                Icon = itemView.FindViewById<ImageView>(Resource.Id.img_queue_icon)!;
                Name = itemView.FindViewById<TextView>(Resource.Id.txt_queue_name)!;
                Meta = itemView.FindViewById<TextView>(Resource.Id.txt_queue_meta)!;
                RemoveBtn = itemView.FindViewById<MaterialButton>(Resource.Id.btn_remove_queue_item)!;
            }
        }
    }
}
