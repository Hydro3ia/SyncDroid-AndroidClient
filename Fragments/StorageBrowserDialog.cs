using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Android.Content;
using Android.Views;
using Android.Widget;
using AndroidX.RecyclerView.Widget;
using Google.Android.Material.Button;
using Google.Android.Material.Dialog;
using SyncDroidAndroid.Models;
using SyncDroidAndroid.Services;

namespace SyncDroidAndroid.Fragments;

public static class StorageBrowserDialog
{
    public static void Show(
        Context context,
        bool isRemotePc,
        bool allowSelectFile,
        string initialPath,
        Action<string, bool, long> onSelected)
    {
        var loc = LocalizationService.Instance;
        var view = LayoutInflater.From(context)!.Inflate(Resource.Layout.dialog_storage_browser, null)!;

        var txtTitle = view.FindViewById<TextView>(Resource.Id.txt_browser_title)!;
        var btnNewFolder = view.FindViewById<MaterialButton>(Resource.Id.btn_browser_new_folder)!;
        var btnRefresh = view.FindViewById<MaterialButton>(Resource.Id.btn_browser_refresh)!;
        var layoutNewFolderBar = view.FindViewById<LinearLayout>(Resource.Id.layout_new_folder_bar)!;
        var inputNewFolderName = view.FindViewById<EditText>(Resource.Id.input_new_folder_name)!;
        var btnConfirmNewFolder = view.FindViewById<MaterialButton>(Resource.Id.btn_confirm_new_folder)!;
        var btnUp = view.FindViewById<MaterialButton>(Resource.Id.btn_browser_up)!;
        var txtCurrentDir = view.FindViewById<TextView>(Resource.Id.txt_browser_current_dir)!;
        var recyclerEntries = view.FindViewById<RecyclerView>(Resource.Id.recycler_browser_entries)!;
        var txtSelectedLabel = view.FindViewById<TextView>(Resource.Id.txt_selected_path_label)!;
        var inputSelectedPath = view.FindViewById<EditText>(Resource.Id.input_browser_selected_path)!;
        var txtError = view.FindViewById<TextView>(Resource.Id.txt_browser_error)!;
        var btnCancel = view.FindViewById<MaterialButton>(Resource.Id.btn_browser_cancel)!;
        var btnSelect = view.FindViewById<MaterialButton>(Resource.Id.btn_browser_select)!;
        var spinnerRoot = view.FindViewById<Spinner>(Resource.Id.spinner_root_drive)!;

        txtTitle.Text = isRemotePc ? loc.BrowserRemotePcTitle : loc.BrowserLocalTitle;
        btnNewFolder.Text = loc.NewFolderButton;
        btnRefresh.Text = loc.T("Refresh", "刷新", "重新整理");
        inputNewFolderName.Hint = loc.NewFolderPlaceholder;
        btnConfirmNewFolder.Text = loc.CreateButton;
        txtSelectedLabel.Text = loc.SelectedPathLabel;
        btnCancel.Text = loc.CancelButton;
        btnSelect.Text = allowSelectFile ? loc.SelectFileButton : loc.SelectFolderButton;

        BrowserEntriesAdapter? adapter = null;

        if (isRemotePc)
        {
            spinnerRoot.Visibility = ViewStates.Visible;
            var adapterRoot = new ArrayAdapter<string>(context, Android.Resource.Layout.SimpleSpinnerItem, new[] { "Root (/)", "C:\\", "D:\\", "E:\\", "F:\\" });
            adapterRoot.SetDropDownViewResource(Android.Resource.Layout.SimpleSpinnerDropDownItem);
            spinnerRoot.Adapter = adapterRoot;
            spinnerRoot.ItemSelected += (sender, e) =>
            {
                string? selected = spinnerRoot.SelectedItem?.ToString();
                if (!string.IsNullOrEmpty(selected))
                {
                    string target = selected == "Root (/)" ? "/" : selected;
                    _ = LoadDirectoryAsync(target);
                }
            };
        }

        string currentDir = string.IsNullOrWhiteSpace(initialPath)
            ? (isRemotePc ? @"C:\" : "/storage/emulated/0")
            : initialPath.Trim();

        // If initialPath points to a file, start in its parent directory
        if (!isRemotePc && File.Exists(currentDir))
        {
            currentDir = Path.GetDirectoryName(currentDir)?.Replace('\\', '/') ?? "/storage/emulated/0";
        }
        else if (isRemotePc && currentDir == "/")
        {
            currentDir = "/";
        }

        string parentDir = currentDir;
        bool selectedIsDir = true;
        long selectedSize = 0;

        inputSelectedPath.Text = currentDir;

        var dialogBuilder = new MaterialAlertDialogBuilder(context);
        dialogBuilder.SetView(view);
        var dialog = dialogBuilder.Create()!;

        async Task LoadDirectoryAsync(string path)
        {
            txtError.Visibility = ViewStates.Gone;
            try
            {
                DirectoryListingResponse resp;
                if (isRemotePc)
                {
                    resp = await CompanionNetworkService.Instance.GetRemotePcDirectoryListingAsync(path, allowSelectFile);
                }
                else
                {
                    resp = await Task.Run(() => LocalStorageScanner.GetDirectoryListing(path, allowSelectFile));
                }

                currentDir = resp.CurrentPath;
                parentDir = resp.ParentPath;
                txtCurrentDir.Text = currentDir;
                inputSelectedPath.Text = currentDir;
                selectedIsDir = true;
                selectedSize = 0;
                adapter?.UpdateEntries(resp.Entries);
            }
            catch (Exception ex)
            {
                txtError.Text = ex.Message;
                txtError.Visibility = ViewStates.Visible;
            }
        }

        adapter = new BrowserEntriesAdapter(entry =>
        {
            if (entry.IsDirectory)
            {
                selectedIsDir = true;
                selectedSize = 0;
                _ = LoadDirectoryAsync(entry.FullPath);
            }
            else if (allowSelectFile)
            {
                selectedIsDir = false;
                selectedSize = entry.Size;
                inputSelectedPath.Text = entry.FullPath;
            }
        });

        recyclerEntries.SetLayoutManager(new LinearLayoutManager(context));
        recyclerEntries.SetAdapter(adapter);

        btnUp.Click += (_, _) =>
        {
            if (!string.IsNullOrWhiteSpace(parentDir))
            {
                _ = LoadDirectoryAsync(parentDir);
            }
        };

        btnRefresh.Click += (_, _) =>
        {
            _ = LoadDirectoryAsync(currentDir);
        };

        btnNewFolder.Click += (_, _) =>
        {
            layoutNewFolderBar.Visibility = layoutNewFolderBar.Visibility == ViewStates.Visible
                ? ViewStates.Gone
                : ViewStates.Visible;
        };

        btnConfirmNewFolder.Click += async (_, _) =>
        {
            string folderName = inputNewFolderName.Text?.Trim() ?? "";
            if (string.IsNullOrEmpty(folderName)) return;

            try
            {
                string targetNewDir = isRemotePc
                    ? $"{currentDir.TrimEnd('\\', '/')}\\{folderName}"
                    : $"{currentDir.TrimEnd('/')}/{folderName}";

                if (isRemotePc)
                {
                    await CompanionNetworkService.Instance.CreateRemotePcDirectoryAsync(targetNewDir);
                }
                else
                {
                    Directory.CreateDirectory(targetNewDir);
                }

                inputNewFolderName.Text = "";
                layoutNewFolderBar.Visibility = ViewStates.Gone;
                await LoadDirectoryAsync(targetNewDir);
            }
            catch (Exception ex)
            {
                txtError.Text = ex.Message;
                txtError.Visibility = ViewStates.Visible;
            }
        };

        btnCancel.Click += (_, _) => dialog.Dismiss();

        btnSelect.Click += (_, _) =>
        {
            string chosen = inputSelectedPath.Text?.Trim() ?? currentDir;
            if (!string.IsNullOrEmpty(chosen))
            {
                onSelected(chosen, selectedIsDir, selectedSize);
                dialog.Dismiss();
            }
        };

        dialog.Show();
        ResponsiveWindowService.ApplyAdaptiveDialogSizing(dialog, context);
        _ = LoadDirectoryAsync(currentDir);
    }

    private sealed class BrowserEntriesAdapter : RecyclerView.Adapter
    {
        private readonly List<RemoteEntryDto> _items = new();
        private readonly Action<RemoteEntryDto> _onClick;

        public BrowserEntriesAdapter(Action<RemoteEntryDto> onClick)
        {
            _onClick = onClick;
        }

        public void UpdateEntries(IEnumerable<RemoteEntryDto> entries)
        {
            _items.Clear();
            _items.AddRange(entries);
            NotifyDataSetChanged();
        }

        public override int ItemCount => _items.Count;

        public override RecyclerView.ViewHolder OnCreateViewHolder(ViewGroup parent, int viewType)
        {
            var v = LayoutInflater.From(parent.Context)!.Inflate(Resource.Layout.item_browser_entry, parent, false)!;
            return new ViewHolder(v);
        }

        public override void OnBindViewHolder(RecyclerView.ViewHolder holder, int position)
        {
            var vh = (ViewHolder)holder;
            var item = _items[position];
            vh.Icon.SetImageResource(item.IsDirectory ? Resource.Drawable.ic_folder : Resource.Drawable.ic_file);
            vh.Name.Text = item.Name;
            vh.Meta.Text = item.IsDirectory ? "DIR" : TransferQueueItem.FormatSize(item.Size);
            vh.ItemView.Click -= vh.CurrentClickHandler;
            vh.CurrentClickHandler = (_, _) => _onClick(item);
            vh.ItemView.Click += vh.CurrentClickHandler;
        }

        private sealed class ViewHolder : RecyclerView.ViewHolder
        {
            public ImageView Icon { get; }
            public TextView Name { get; }
            public TextView Meta { get; }
            public EventHandler? CurrentClickHandler { get; set; }

            public ViewHolder(View itemView) : base(itemView)
            {
                Icon = itemView.FindViewById<ImageView>(Resource.Id.img_browser_icon)!;
                Name = itemView.FindViewById<TextView>(Resource.Id.txt_browser_entry_name)!;
                Meta = itemView.FindViewById<TextView>(Resource.Id.txt_browser_entry_meta)!;
            }
        }
    }
}
