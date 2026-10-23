using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Android.Content;
using Android.OS;
using Android.Views;
using Android.Widget;
using AndroidX.Fragment.App;
using AndroidX.RecyclerView.Widget;
using Google.Android.Material.Button;
using Google.Android.Material.Card;
using Google.Android.Material.Dialog;
using Google.Android.Material.MaterialSwitch;
using Google.Android.Material.ProgressIndicator;
using Google.Android.Material.TextField;
using SyncDroidAndroid.Helpers;
using SyncDroidAndroid.Services;

namespace SyncDroidAndroid.Fragments;

public class MirrorSyncFragment : AndroidX.Fragment.App.Fragment
{
    private bool _isPcToAndroid = true;
    private bool _isMirMode = true;

    private MaterialCardView? _cardPermBanner;
    private TextView? _txtPermTitle;
    private TextView? _txtPermDesc;
    private MaterialButton? _btnGrantPerm;

    private LinearLayout? _layoutMirrorRoot;
    private LinearLayout? _layoutMirrorColumns;
    private LinearLayout? _paneMirrorLeft;
    private LinearLayout? _paneMirrorRight;

    private MaterialButtonToggleGroup? _toggleDirection;
    private MaterialButton? _btnDirPcToAndroid;
    private MaterialButton? _btnDirAndroidToPc;
    private MaterialSwitch? _switchMirMode;

    private LinearLayout? _layoutPathsContainer;
    private LinearLayout? _panelPcPath;
    private LinearLayout? _panelDirectionDivider;
    private LinearLayout? _panelAndroidPath;

    private TextView? _txtPcDirLabel;
    private TextView? _badgePcRole;
    private MaterialAutoCompleteTextView? _inputPcPath;
    private MaterialButton? _btnBrowsePc;

    private TextView? _txtMirrorBanner;

    private TextView? _txtAndroidDirLabel;
    private TextView? _badgeAndroidRole;
    private MaterialAutoCompleteTextView? _inputAndroidPath;
    private MaterialButton? _btnBrowseAndroid;

    private MaterialButton? _btnStartSync;
    private MaterialButton? _btnCancelSync;
    private TextView? _txtSyncStatus;
    private TextView? _txtSyncPercent;
    private LinearProgressIndicator? _progressSync;

    private TextView? _txtLogsHeader;
    private MaterialButton? _btnClearLogs;
    private MaterialButton? _btnCopyLogs;
    private RecyclerView? _recyclerLogs;
    private LogListAdapter? _logAdapter;

    public override View OnCreateView(LayoutInflater inflater, ViewGroup? container, Bundle? savedInstanceState)
    {
        var view = inflater.Inflate(Resource.Layout.fragment_mirror_sync, container, false)!;

        _cardPermBanner = view.FindViewById<MaterialCardView>(Resource.Id.card_permission_banner);
        _txtPermTitle = view.FindViewById<TextView>(Resource.Id.txt_perm_banner_title);
        _txtPermDesc = view.FindViewById<TextView>(Resource.Id.txt_perm_banner_desc);
        _btnGrantPerm = view.FindViewById<MaterialButton>(Resource.Id.btn_grant_perm_banner);

        _layoutMirrorRoot = view.FindViewById<LinearLayout>(Resource.Id.layout_mirror_root);
        _layoutMirrorColumns = view.FindViewById<LinearLayout>(Resource.Id.layout_mirror_columns);
        _paneMirrorLeft = view.FindViewById<LinearLayout>(Resource.Id.pane_mirror_left);
        _paneMirrorRight = view.FindViewById<LinearLayout>(Resource.Id.pane_mirror_right);

        _toggleDirection = view.FindViewById<MaterialButtonToggleGroup>(Resource.Id.toggle_direction);
        _btnDirPcToAndroid = view.FindViewById<MaterialButton>(Resource.Id.btn_dir_pc_to_android);
        _btnDirAndroidToPc = view.FindViewById<MaterialButton>(Resource.Id.btn_dir_android_to_pc);
        _switchMirMode = view.FindViewById<MaterialSwitch>(Resource.Id.switch_mir_mode);

        _layoutPathsContainer = view.FindViewById<LinearLayout>(Resource.Id.layout_paths_container);
        _panelPcPath = view.FindViewById<LinearLayout>(Resource.Id.panel_pc_path);
        _panelDirectionDivider = view.FindViewById<LinearLayout>(Resource.Id.panel_direction_divider);
        _panelAndroidPath = view.FindViewById<LinearLayout>(Resource.Id.panel_android_path);

        _txtPcDirLabel = view.FindViewById<TextView>(Resource.Id.txt_pc_dir_label);
        _badgePcRole = view.FindViewById<TextView>(Resource.Id.badge_pc_role);
        _inputPcPath = view.FindViewById<MaterialAutoCompleteTextView>(Resource.Id.input_pc_path);
        _btnBrowsePc = view.FindViewById<MaterialButton>(Resource.Id.btn_browse_pc);

        _txtMirrorBanner = view.FindViewById<TextView>(Resource.Id.txt_mirror_banner);

        _txtAndroidDirLabel = view.FindViewById<TextView>(Resource.Id.txt_android_dir_label);
        _badgeAndroidRole = view.FindViewById<TextView>(Resource.Id.badge_android_role);
        _inputAndroidPath = view.FindViewById<MaterialAutoCompleteTextView>(Resource.Id.input_android_path);
        _btnBrowseAndroid = view.FindViewById<MaterialButton>(Resource.Id.btn_browse_android);

        _btnStartSync = view.FindViewById<MaterialButton>(Resource.Id.btn_start_sync);
        _btnCancelSync = view.FindViewById<MaterialButton>(Resource.Id.btn_cancel_sync);
        _txtSyncStatus = view.FindViewById<TextView>(Resource.Id.txt_sync_status);
        _txtSyncPercent = view.FindViewById<TextView>(Resource.Id.txt_sync_percent);
        _progressSync = view.FindViewById<LinearProgressIndicator>(Resource.Id.progress_sync);

        _txtLogsHeader = view.FindViewById<TextView>(Resource.Id.txt_logs_header);
        _btnClearLogs = view.FindViewById<MaterialButton>(Resource.Id.btn_clear_logs);
        _btnCopyLogs = view.FindViewById<MaterialButton>(Resource.Id.btn_copy_logs);
        _recyclerLogs = view.FindViewById<RecyclerView>(Resource.Id.recycler_logs);

        _logAdapter = new LogListAdapter(SyncStateService.Instance.Logs);
        _recyclerLogs!.SetLayoutManager(new LinearLayoutManager(Context));
        _recyclerLogs.SetAdapter(_logAdapter);

        WireEvents();
        LoadSavedPathsForDirection();
        ApplyLocalization();
        UpdateRoleAndOrderLayout();
        ApplyAdaptiveLayout(ResponsiveWindowService.Instance.CurrentState);
        RefreshSyncStateUi();

        return view;
    }

    public override void OnResume()
    {
        base.OnResume();
        UpdatePermissionBannerVisibility();
        ApplyAdaptiveLayout(ResponsiveWindowService.Instance.CurrentState);
        ResponsiveWindowService.Instance.WindowStateChanged += ApplyAdaptiveLayout;
        SyncStateService.Instance.StateChanged += RefreshSyncStateUi;
        SyncStateService.Instance.LogAdded += OnLogAdded;
        LocalizationService.Instance.LanguageChanged += OnLanguageChanged;
    }

    public override void OnPause()
    {
        SaveCurrentPaths();
        ResponsiveWindowService.Instance.WindowStateChanged -= ApplyAdaptiveLayout;
        SyncStateService.Instance.StateChanged -= RefreshSyncStateUi;
        SyncStateService.Instance.LogAdded -= OnLogAdded;
        LocalizationService.Instance.LanguageChanged -= OnLanguageChanged;
        base.OnPause();
    }

    private void ApplyAdaptiveLayout(AdaptiveWindowState state)
    {
        ResponsiveWindowService.ApplyTwoPaneLayout(
            Context,
            _layoutMirrorRoot,
            _layoutMirrorColumns,
            _paneMirrorLeft,
            _paneMirrorRight);

        if (_recyclerLogs != null && Context != null)
        {
            float density = Context.Resources?.DisplayMetrics?.Density ?? 1f;
            int logHeightDp = state.UseTwoPaneLayout ? 360 : 220;
            if (_recyclerLogs.LayoutParameters is LinearLayout.LayoutParams lp)
            {
                lp.Height = (int)Math.Round(logHeightDp * density);
                _recyclerLogs.LayoutParameters = lp;
            }
        }
    }

    private void WireEvents()
    {
        _btnGrantPerm!.Click += (_, _) =>
        {
            (Activity as MainActivity)?.RequestAllFilesAccessPermission();
        };

        _toggleDirection!.ButtonChecked += (_, _) =>
        {
            int checkedId = _toggleDirection.CheckedButtonId;
            bool newDir = checkedId == Resource.Id.btn_dir_pc_to_android;
            if (newDir == _isPcToAndroid) return;
            SaveCurrentPaths();
            _isPcToAndroid = newDir;
            LoadSavedPathsForDirection();
            UpdateRoleAndOrderLayout();
        };

        _switchMirMode!.CheckedChange += (_, e) =>
        {
            _isMirMode = e.IsChecked;
            UpdateRoleAndOrderLayout();
        };

        _btnBrowsePc!.Click += (_, _) =>
        {
            if (Context == null) return;
            StorageBrowserDialog.Show(
                Context,
                isRemotePc: true,
                allowSelectFile: false,
                initialPath: _inputPcPath?.Text ?? @"C:\",
                onSelected: (selectedPath, _, _) =>
                {
                    if (_inputPcPath != null) _inputPcPath.Text = selectedPath;
                    SaveCurrentPaths();
                });
        };

        _btnBrowseAndroid!.Click += (_, _) =>
        {
            if (Context == null) return;
            StorageBrowserDialog.Show(
                Context,
                isRemotePc: false,
                allowSelectFile: false,
                initialPath: _inputAndroidPath?.Text ?? "/storage/emulated/0",
                onSelected: (selectedPath, _, _) =>
                {
                    if (_inputAndroidPath != null) _inputAndroidPath.Text = selectedPath;
                    SaveCurrentPaths();
                    CheckAndroidLongFileNamesIfSource(selectedPath);
                });
        };

        _btnStartSync!.Click += async (_, _) => await StartSyncAsync();

        _btnCancelSync!.Click += (_, _) => SyncStateService.Instance.CancelCurrentOperation();

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

    private void LoadSavedPathsForDirection()
    {
        var settings = SettingsService.Load();
        RefreshHistoryDropdowns(settings);

        if (_isPcToAndroid)
        {
            _inputPcPath?.SetText(settings.PcToAndroid_PcPath, false);
            _inputAndroidPath?.SetText(settings.PcToAndroid_AndroidPath, false);
        }
        else
        {
            _inputPcPath?.SetText(settings.AndroidToPc_PcPath, false);
            _inputAndroidPath?.SetText(settings.AndroidToPc_AndroidPath, false);
        }
    }

    private void SaveCurrentPaths()
    {
        string pc = _inputPcPath?.Text?.Trim() ?? "";
        string android = _inputAndroidPath?.Text?.Trim() ?? "";

        var settings = SettingsService.Load();
        if (_isPcToAndroid)
        {
            if (!string.IsNullOrEmpty(pc)) settings.PcToAndroid_PcPath = pc;
            if (!string.IsNullOrEmpty(android)) settings.PcToAndroid_AndroidPath = android;
        }
        else
        {
            if (!string.IsNullOrEmpty(pc)) settings.AndroidToPc_PcPath = pc;
            if (!string.IsNullOrEmpty(android)) settings.AndroidToPc_AndroidPath = android;
        }

        SettingsService.AddToHistoryList(settings.PcPathHistory, pc, ignoreCase: true);
        SettingsService.AddToHistoryList(settings.AndroidPathHistory, android, ignoreCase: false);
        SettingsService.Save(settings);
        RefreshHistoryDropdowns(settings);
    }

    private void RefreshHistoryDropdowns(Models.AppSettings settings)
    {
        if (Context == null) return;
        if (_inputPcPath != null)
        {
            _inputPcPath.Adapter = new ArrayAdapter<string>(
                Context, Android.Resource.Layout.SimpleDropDownItem1Line, settings.PcPathHistory);
        }
        if (_inputAndroidPath != null)
        {
            _inputAndroidPath.Adapter = new ArrayAdapter<string>(
                Context, Android.Resource.Layout.SimpleDropDownItem1Line, settings.AndroidPathHistory);
        }
    }

    private void UpdateRoleAndOrderLayout()
    {
        var loc = LocalizationService.Instance;
        if (_badgePcRole == null || _badgeAndroidRole == null || _layoutPathsContainer == null) return;

        _badgePcRole.Text = _isPcToAndroid ? loc.SourceBadge : loc.TargetBadge;
        _badgeAndroidRole.Text = _isPcToAndroid ? loc.TargetBadge : loc.SourceBadge;
        _txtMirrorBanner!.Text = _isMirMode ? loc.MirrorBannerMir : loc.MirrorBannerMerge;

        // Dynamically reorder Source on top, Divider in middle, Target on bottom (matching SyncDroidWinUI)
        _layoutPathsContainer.RemoveAllViews();
        if (_isPcToAndroid)
        {
            _layoutPathsContainer.AddView(_panelPcPath);
            _layoutPathsContainer.AddView(_panelDirectionDivider);
            _layoutPathsContainer.AddView(_panelAndroidPath);
        }
        else
        {
            _layoutPathsContainer.AddView(_panelAndroidPath);
            _layoutPathsContainer.AddView(_panelDirectionDivider);
            _layoutPathsContainer.AddView(_panelPcPath);
        }
    }

    private void UpdatePermissionBannerVisibility()
    {
        if (_cardPermBanner == null) return;
        bool granted = MainActivity.HasAllFilesAccessPermission();
        _cardPermBanner.Visibility = granted ? ViewStates.Gone : ViewStates.Visible;
    }

    private void CheckAndroidLongFileNamesIfSource(string androidDir)
    {
        if (_isPcToAndroid || Context == null) return;
        var settings = SettingsService.Load();
        if (!settings.CheckLongFileNames || !Directory.Exists(androidDir)) return;

        var manifest = LocalStorageScanner.GetDirectoryTreeManifest(androidDir);
        var tooLongFiles = manifest.Files
            .Where(f => FileNameLengthHelper.IsFileNameTooLong(f.Name, out _))
            .ToList();

        if (tooLongFiles.Count > 0)
        {
            ShowLongFileNameHandlingDialog(tooLongFiles);
        }
    }

    private void ShowLongFileNameHandlingDialog(List<Models.RemoteEntryDto> tooLongFiles)
    {
        if (Context == null) return;
        var loc = LocalizationService.Instance;

        string preview = string.Join("\n", tooLongFiles.Take(5).Select(f => $"• {f.RelativePath}"));
        if (tooLongFiles.Count > 5) preview += $"\n... (+{tooLongFiles.Count - 5} more)";

        var builder = new MaterialAlertDialogBuilder(Context);
        builder.SetTitle(loc.T("Filenames Exceeding 255 Bytes Detected", "检测到超过 255 字节的超长文件名", "偵測到超過 255 位元組的超長檔名"));
        builder.SetMessage(loc.T(
            $"Found {tooLongFiles.Count} file(s) with UTF-8 names > 255 bytes:\n{preview}\n\nHow would you like to handle them?",
            $"共发现 {tooLongFiles.Count} 个文件名超过 255 字节（含扩展名）：\n{preview}\n\n请选择处理方式：",
            $"共發現 {tooLongFiles.Count} 個檔名超過 255 位元組（含副檔名）：\n{preview}\n\n請選擇處理方式："));
        builder.SetPositiveButton(loc.T("Rename (< 255 B)", "修改文件名 (< 255 字节)", "修改檔名 (< 255 位元組)"), (_, _) =>
        {
            foreach (var item in tooLongFiles)
            {
                try
                {
                    string dir = Path.GetDirectoryName(item.FullPath) ?? "";
                    string newName = FileNameLengthHelper.TruncateFileNameStrictlyUnder255Bytes(
                        item.Name,
                        candidate => File.Exists(Path.Combine(dir, candidate)));
                    string newFullPath = Path.Combine(dir, newName);
                    File.Move(item.FullPath, newFullPath);
                    SyncStateService.Instance.AddLog($"Renamed long filename: '{item.Name}' ➔ '{newName}'");
                }
                catch (Exception ex)
                {
                    SyncStateService.Instance.AddLog($"Failed to rename '{item.Name}': {ex.Message}");
                }
            }
        });
        builder.SetNeutralButton(loc.T("Skip Long Files", "略过超长文件", "略過超長檔案"), (_, _) =>
        {
            SyncStateService.Instance.AddLog($"Will automatically skip {tooLongFiles.Count} file(s) with names > 255 bytes.");
        });
        builder.SetNegativeButton(loc.CancelButton, (_, _) => { });
        builder.Show();
    }

    private async Task StartSyncAsync()
    {
        var state = SyncStateService.Instance;
        var loc = LocalizationService.Instance;

        if (state.IsBusy)
        {
            state.AddLog("Sync or transfer is already running.");
            return;
        }

        string pcPath = _inputPcPath?.Text?.Trim() ?? "";
        string androidPath = _inputAndroidPath?.Text?.Trim() ?? "";

        if (string.IsNullOrWhiteSpace(pcPath) || string.IsNullOrWhiteSpace(androidPath))
        {
            state.AddLog("Error: PC path and Android path cannot be empty.");
            return;
        }

        SaveCurrentPaths();
        var settings = SettingsService.Load();
        var token = state.BeginOperation(loc.StatusSyncing);

        try
        {
            Func<int, Task<bool>>? confirmDelete = null;
            if (settings.ConfirmBeforeMirDelete && Context != null)
            {
                confirmDelete = count => AskConfirmDeleteAsync(Context, count);
            }

            if (_isPcToAndroid)
            {
                await MirrorSyncEngine.Instance.SyncPcToAndroidAsync(
                    pcPath,
                    androidPath,
                    _isMirMode,
                    settings.CheckLongFileNames,
                    confirmDelete,
                    token);
            }
            else
            {
                await MirrorSyncEngine.Instance.SyncAndroidToPcAsync(
                    androidPath,
                    pcPath,
                    _isMirMode,
                    settings.CheckLongFileNames,
                    confirmDelete,
                    token);
            }

            state.EndOperation(loc.StatusCompleted, 100);
        }
        catch (System.OperationCanceledException)
        {
            state.AddLog("Sync was cancelled.");
            state.EndOperation(loc.StatusCancelled, state.Progress);
        }
        catch (Exception ex)
        {
            state.AddLog($"Error: {ex.Message}");
            state.EndOperation(loc.StatusFailed, state.Progress);
        }
    }

    private static Task<bool> AskConfirmDeleteAsync(Context context, int deleteCount)
    {
        var tcs = new TaskCompletionSource<bool>();
        var loc = LocalizationService.Instance;

        new Handler(Looper.MainLooper!).Post(() =>
        {
            var builder = new MaterialAlertDialogBuilder(context);
            builder.SetTitle(loc.T("Confirm /MIR File Deletion", "确认 /MIR 镜像清理删除", "確認 /MIR 鏡像清理刪除"));
            builder.SetMessage(loc.T(
                $"Mirror Sync (/MIR) found {deleteCount} extra file(s) in the target directory that do not exist in the source.\n\nDelete these {deleteCount} extra file(s)?",
                $"镜像同步 (/MIR) 检测到目标目录中存在 {deleteCount} 个源目录没有的多余文件。\n\n是否确认删除这 {deleteCount} 个多余文件？",
                $"鏡像同步 (/MIR) 偵測到目標目錄中存在 {deleteCount} 個來源目錄沒有的多餘檔案。\n\n是否確認刪除這 {deleteCount} 個多餘檔案？"));
            builder.SetCancelable(false);
            builder.SetPositiveButton(loc.T("Delete Extra Files", "确认删除", "確認刪除"), (_, _) => tcs.TrySetResult(true));
            builder.SetNegativeButton(loc.T("Keep Files", "保留不删", "保留不刪"), (_, _) => tcs.TrySetResult(false));
            builder.Show();
        });

        return tcs.Task;
    }

    private void OnLogAdded(string line)
    {
        if (_logAdapter == null || _recyclerLogs == null) return;
        _logAdapter.NotifyItemInserted(SyncStateService.Instance.Logs.Count - 1);
        _recyclerLogs.ScrollToPosition(Math.Max(0, SyncStateService.Instance.Logs.Count - 1));
    }

    private void RefreshSyncStateUi()
    {
        var state = SyncStateService.Instance;
        if (_txtSyncStatus != null) _txtSyncStatus.Text = state.StatusText;
        if (_txtSyncPercent != null) _txtSyncPercent.Text = $"{state.Progress}%";
        if (_progressSync != null) _progressSync.Progress = state.Progress;
        _logAdapter?.NotifyDataSetChanged();
    }

    private void OnLanguageChanged(object? sender, EventArgs e) => ApplyLocalization();

    private void ApplyLocalization()
    {
        var loc = LocalizationService.Instance;
        if (_txtPermTitle != null) _txtPermTitle.Text = loc.PermBannerTitle;
        if (_txtPermDesc != null) _txtPermDesc.Text = loc.PermBannerDesc;
        if (_btnGrantPerm != null) _btnGrantPerm.Text = loc.GrantButton;

        if (_btnDirPcToAndroid != null) _btnDirPcToAndroid.Text = loc.DirPcToAndroid;
        if (_btnDirAndroidToPc != null) _btnDirAndroidToPc.Text = loc.DirAndroidToPc;
        if (_switchMirMode != null) _switchMirMode.Text = loc.MirModeSwitchLabel;

        if (_txtPcDirLabel != null) _txtPcDirLabel.Text = loc.PcDirectoryLabel;
        if (_txtAndroidDirLabel != null) _txtAndroidDirLabel.Text = loc.AndroidDirectoryLabel;
        if (_btnBrowsePc != null) _btnBrowsePc.Text = loc.BrowseButton;
        if (_btnBrowseAndroid != null) _btnBrowseAndroid.Text = loc.BrowseButton;

        if (_btnStartSync != null) _btnStartSync.Text = loc.StartSyncButton;
        if (_btnCancelSync != null) _btnCancelSync.Text = loc.CancelButton;
        if (_txtLogsHeader != null) _txtLogsHeader.Text = loc.ActivityLogsHeader;
        if (_btnClearLogs != null) _btnClearLogs.Text = loc.ClearButton;
        if (_btnCopyLogs != null) _btnCopyLogs.Text = loc.CopyAllButton;

        UpdateRoleAndOrderLayout();
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
