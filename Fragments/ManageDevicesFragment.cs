using System;
using System.Collections.Generic;
using Android.OS;
using Android.Views;
using Android.Widget;
using AndroidX.Fragment.App;
using AndroidX.RecyclerView.Widget;
using Google.Android.Material.Button;
using SyncDroidAndroid.Models;
using SyncDroidAndroid.Services;

namespace SyncDroidAndroid.Fragments;

public class ManageDevicesFragment : AndroidX.Fragment.App.Fragment
{
    private readonly List<TrustedDevice> _devices = new();

    private LinearLayout? _layoutManageRoot;
    private MaterialButton? _btnBackToSettings;
    private TextView? _txtTitle;
    private TextView? _txtDeviceCount;
    private MaterialButton? _btnRemoveAll;
    private TextView? _txtDesc;
    private TextView? _txtEmptyHint;
    private RecyclerView? _recyclerDevices;
    private TrustedDevicesAdapter? _adapter;

    public override View OnCreateView(LayoutInflater inflater, ViewGroup? container, Bundle? savedInstanceState)
    {
        var view = inflater.Inflate(Resource.Layout.fragment_manage_devices, container, false)!;

        _layoutManageRoot = view.FindViewById<LinearLayout>(Resource.Id.layout_manage_devices_root);
        _btnBackToSettings = view.FindViewById<MaterialButton>(Resource.Id.btn_back_to_settings);
        _txtTitle = view.FindViewById<TextView>(Resource.Id.txt_manage_devices_title);
        _txtDeviceCount = view.FindViewById<TextView>(Resource.Id.txt_trusted_device_count);
        _btnRemoveAll = view.FindViewById<MaterialButton>(Resource.Id.btn_remove_all_devices);
        _txtDesc = view.FindViewById<TextView>(Resource.Id.txt_manage_devices_desc);
        _txtEmptyHint = view.FindViewById<TextView>(Resource.Id.txt_trusted_devices_empty);
        _recyclerDevices = view.FindViewById<RecyclerView>(Resource.Id.recycler_trusted_devices);

        _adapter = new TrustedDevicesAdapter(_devices, id =>
        {
            SettingsService.RemoveTrustedDevice(id);
            LoadDevicesToUi();
        });
        _recyclerDevices!.SetLayoutManager(new LinearLayoutManager(Context));
        _recyclerDevices.SetAdapter(_adapter);

        _btnBackToSettings!.Click += (_, _) =>
        {
            (Activity as MainActivity)?.ShowSettingsFragment();
        };

        _btnRemoveAll!.Click += (_, _) =>
        {
            SettingsService.ClearTrustedDevices();
            LoadDevicesToUi();
        };

        ApplyLocalization();
        LoadDevicesToUi();

        return view;
    }

    public override void OnResume()
    {
        base.OnResume();
        SettingsService.TrustedDevicesChanged += OnTrustedDevicesChanged;
        LocalizationService.Instance.LanguageChanged += OnLanguageChanged;
        LoadDevicesToUi();
    }

    public override void OnPause()
    {
        SettingsService.TrustedDevicesChanged -= OnTrustedDevicesChanged;
        LocalizationService.Instance.LanguageChanged -= OnLanguageChanged;
        base.OnPause();
    }

    private void OnTrustedDevicesChanged(object? sender, EventArgs e)
    {
        Activity?.RunOnUiThread(LoadDevicesToUi);
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        ApplyLocalization();
        LoadDevicesToUi();
    }

    private void ApplyLocalization()
    {
        var loc = LocalizationService.Instance;
        if (_btnBackToSettings != null) _btnBackToSettings.Text = $"← {loc.BackToSettingsButton}";
        if (_txtTitle != null) _txtTitle.Text = loc.ManageDevicesPageTitle;
        if (_btnRemoveAll != null) _btnRemoveAll.Text = loc.RemoveAllDevicesButton;
        if (_txtDesc != null) _txtDesc.Text = loc.ManageDevicesPageDesc;
        if (_txtEmptyHint != null) _txtEmptyHint.Text = loc.NoTrustedDevicesHint;
    }

    public void LoadDevicesToUi()
    {
        var settings = SettingsService.Load();
        _devices.Clear();
        _devices.AddRange(settings.TrustedDevices);
        _adapter?.NotifyDataSetChanged();

        int count = _devices.Count;
        if (_txtDeviceCount != null) _txtDeviceCount.Text = count.ToString();
        if (_txtEmptyHint != null) _txtEmptyHint.Visibility = count == 0 ? ViewStates.Visible : ViewStates.Gone;
        if (_recyclerDevices != null) _recyclerDevices.Visibility = count == 0 ? ViewStates.Gone : ViewStates.Visible;
        if (_btnRemoveAll != null) _btnRemoveAll.Enabled = count > 0;
    }

    private sealed class TrustedDevicesAdapter : RecyclerView.Adapter
    {
        private readonly List<TrustedDevice> _items;
        private readonly Action<string> _onDelete;

        public TrustedDevicesAdapter(List<TrustedDevice> items, Action<string> onDelete)
        {
            _items = items;
            _onDelete = onDelete;
        }

        public override int ItemCount => _items.Count;

        public override RecyclerView.ViewHolder OnCreateViewHolder(ViewGroup parent, int viewType)
        {
            var v = LayoutInflater.From(parent.Context)!.Inflate(Resource.Layout.item_trusted_device, parent, false)!;
            return new DeviceViewHolder(v);
        }

        public override void OnBindViewHolder(RecyclerView.ViewHolder holder, int position)
        {
            var vh = (DeviceViewHolder)holder;
            var item = _items[position];
            var loc = LocalizationService.Instance;

            bool isLocalSend = string.Equals(item.DeviceType, "LocalSend", StringComparison.OrdinalIgnoreCase);
            vh.Icon.SetImageResource(isLocalSend ? Resource.Drawable.ic_swap_horiz : Resource.Drawable.ic_devices);
            vh.Name.Text = item.Name;
            vh.DeviceType.Text = item.DeviceType;
            vh.Meta.Text = $"{item.DisplayEndpoint}  •  {loc.TrustedDeviceTimePrefix}{item.AuthenticatedAt}";
            vh.DeleteBtn.Text = loc.DeleteDeviceButton;

            vh.DeleteBtn.Click -= vh.CurrentClick;
            vh.CurrentClick = (_, _) => _onDelete(item.Id);
            vh.DeleteBtn.Click += vh.CurrentClick;
        }

        private sealed class DeviceViewHolder : RecyclerView.ViewHolder
        {
            public ImageView Icon { get; }
            public TextView Name { get; }
            public TextView DeviceType { get; }
            public TextView Meta { get; }
            public MaterialButton DeleteBtn { get; }
            public EventHandler? CurrentClick { get; set; }

            public DeviceViewHolder(View itemView) : base(itemView)
            {
                Icon = itemView.FindViewById<ImageView>(Resource.Id.img_trusted_device_icon)!;
                Name = itemView.FindViewById<TextView>(Resource.Id.txt_trusted_device_name)!;
                DeviceType = itemView.FindViewById<TextView>(Resource.Id.txt_trusted_device_type)!;
                Meta = itemView.FindViewById<TextView>(Resource.Id.txt_trusted_device_meta)!;
                DeleteBtn = itemView.FindViewById<MaterialButton>(Resource.Id.btn_delete_trusted_device)!;
            }
        }
    }
}
