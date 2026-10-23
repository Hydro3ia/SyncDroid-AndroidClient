using System;
using System.Collections.Generic;
using Android.OS;
using Android.Views;
using Android.Widget;
using AndroidX.Fragment.App;
using AndroidX.RecyclerView.Widget;
using Google.Android.Material.Button;
using Google.Android.Material.MaterialSwitch;
using Google.Android.Material.TextField;
using SyncDroidAndroid.Models;
using SyncDroidAndroid.Services;

namespace SyncDroidAndroid.Fragments;

public class CompanionFragment : AndroidX.Fragment.App.Fragment
{
    private LinearLayout? _layoutCompanionRoot;
    private LinearLayout? _layoutCompanionColumns;
    private LinearLayout? _paneCompanionLeft;
    private LinearLayout? _paneCompanionRight;

    private TextView? _txtServerTitle;
    private TextView? _txtServerDesc;
    private MaterialSwitch? _switchServer;
    private TextView? _txtDeviceNameInfo;
    private TextView? _txtLocalIps;
    private TextView? _txtPairingCode;

    private TextView? _txtPcConnectTitle;
    private TextView? _txtPcConnectDesc;
    private TextView? _txtConnectedPcInfo;
    private MaterialButton? _btnDisconnectPc;
    private TextView? _txtManualIpLabel;
    private MaterialAutoCompleteTextView? _inputPcEndpoint;
    private MaterialButton? _btnConnectPc;

    private TextView? _txtDiscoveredHeader;
    private MaterialButton? _btnScanLan;
    private TextView? _txtDiscoveredEmpty;
    private RecyclerView? _recyclerDiscovered;
    private DiscoveredPeersAdapter? _peersAdapter;

    public override View OnCreateView(LayoutInflater inflater, ViewGroup? container, Bundle? savedInstanceState)
    {
        var view = inflater.Inflate(Resource.Layout.fragment_companion, container, false)!;

        _layoutCompanionRoot = view.FindViewById<LinearLayout>(Resource.Id.layout_companion_root);
        _layoutCompanionColumns = view.FindViewById<LinearLayout>(Resource.Id.layout_companion_columns);
        _paneCompanionLeft = view.FindViewById<LinearLayout>(Resource.Id.pane_companion_left);
        _paneCompanionRight = view.FindViewById<LinearLayout>(Resource.Id.pane_companion_right);

        _txtServerTitle = view.FindViewById<TextView>(Resource.Id.txt_server_card_title);
        _txtServerDesc = view.FindViewById<TextView>(Resource.Id.txt_server_card_desc);
        _switchServer = view.FindViewById<MaterialSwitch>(Resource.Id.switch_companion_server);
        _txtDeviceNameInfo = view.FindViewById<TextView>(Resource.Id.txt_device_name_info);
        _txtLocalIps = view.FindViewById<TextView>(Resource.Id.txt_local_ips);
        _txtPairingCode = view.FindViewById<TextView>(Resource.Id.txt_pairing_code);

        _txtPcConnectTitle = view.FindViewById<TextView>(Resource.Id.txt_pc_connect_title);
        _txtPcConnectDesc = view.FindViewById<TextView>(Resource.Id.txt_pc_connect_desc);
        _txtConnectedPcInfo = view.FindViewById<TextView>(Resource.Id.txt_connected_pc_info);
        _btnDisconnectPc = view.FindViewById<MaterialButton>(Resource.Id.btn_disconnect_pc);
        _txtManualIpLabel = view.FindViewById<TextView>(Resource.Id.txt_manual_ip_label);
        _inputPcEndpoint = view.FindViewById<MaterialAutoCompleteTextView>(Resource.Id.input_pc_endpoint);
        _btnConnectPc = view.FindViewById<MaterialButton>(Resource.Id.btn_connect_pc);

        _txtDiscoveredHeader = view.FindViewById<TextView>(Resource.Id.txt_discovered_header);
        _btnScanLan = view.FindViewById<MaterialButton>(Resource.Id.btn_scan_lan);
        _txtDiscoveredEmpty = view.FindViewById<TextView>(Resource.Id.txt_discovered_empty);
        _recyclerDiscovered = view.FindViewById<RecyclerView>(Resource.Id.recycler_discovered_pcs);

        _peersAdapter = new DiscoveredPeersAdapter(
            CompanionNetworkService.Instance.DiscoveredPeers,
            async peer =>
            {
                if (_inputPcEndpoint != null) _inputPcEndpoint.Text = peer.Endpoint;
                await ConnectToEndpointAsync(peer.Endpoint);
            });
        _recyclerDiscovered!.SetLayoutManager(new LinearLayoutManager(Context));
        _recyclerDiscovered.SetAdapter(_peersAdapter);

        var settings = SettingsService.Load();
        if (Context != null)
        {
            _inputPcEndpoint!.Adapter = new ArrayAdapter<string>(
                Context, Android.Resource.Layout.SimpleDropDownItem1Line, settings.PcEndpointHistory);
        }
        _inputPcEndpoint!.SetText(
            !string.IsNullOrWhiteSpace(settings.LastConnectedPcEndpoint)
                ? settings.LastConnectedPcEndpoint
                : $"192.168.1.100:{settings.CompanionPort}",
            false);

        WireEvents();
        ApplyLocalization();
        ApplyAdaptiveLayout(ResponsiveWindowService.Instance.CurrentState);
        RefreshServerAndPeerUi();
        RefreshDiscoveredListUi();

        return view;
    }

    public override void OnResume()
    {
        base.OnResume();
        ApplyAdaptiveLayout(ResponsiveWindowService.Instance.CurrentState);
        ResponsiveWindowService.Instance.WindowStateChanged += ApplyAdaptiveLayout;
        CompanionNetworkService.Instance.ConnectionStateChanged += RefreshServerAndPeerUi;
        CompanionNetworkService.Instance.DiscoveredPeersChanged += RefreshDiscoveredListUi;
        LocalizationService.Instance.LanguageChanged += OnLanguageChanged;
        RefreshServerAndPeerUi();
        RefreshDiscoveredListUi();
    }

    public override void OnPause()
    {
        ResponsiveWindowService.Instance.WindowStateChanged -= ApplyAdaptiveLayout;
        CompanionNetworkService.Instance.ConnectionStateChanged -= RefreshServerAndPeerUi;
        CompanionNetworkService.Instance.DiscoveredPeersChanged -= RefreshDiscoveredListUi;
        LocalizationService.Instance.LanguageChanged -= OnLanguageChanged;
        base.OnPause();
    }

    private void ApplyAdaptiveLayout(AdaptiveWindowState state)
    {
        ResponsiveWindowService.ApplyTwoPaneLayout(
            Context,
            _layoutCompanionRoot,
            _layoutCompanionColumns,
            _paneCompanionLeft,
            _paneCompanionRight);
    }

    private void WireEvents()
    {
        _switchServer!.CheckedChange += (_, e) =>
        {
            var settings = SettingsService.Load();
            settings.CompanionServerEnabled = e.IsChecked;
            SettingsService.Save(settings);

            if (e.IsChecked)
            {
                CompanionNetworkService.Instance.StartServer(settings.CompanionPort);
                (Activity as MainActivity)?.StartCompanionForegroundService();
            }
            else
            {
                CompanionNetworkService.Instance.StopServer();
                (Activity as MainActivity)?.StopCompanionForegroundService();
            }
            RefreshServerAndPeerUi();
        };

        _btnConnectPc!.Click += async (_, _) =>
        {
            string ep = _inputPcEndpoint?.Text?.Trim() ?? "";
            await ConnectToEndpointAsync(ep);
        };

        _btnDisconnectPc!.Click += (_, _) =>
        {
            CompanionNetworkService.Instance.DisconnectFromPc();
            RefreshServerAndPeerUi();
        };

        _btnScanLan!.Click += async (_, _) =>
        {
            _btnScanLan.Enabled = false;
            await CompanionNetworkService.Instance.ScanLanForPeersAsync();
            _btnScanLan.Enabled = true;
            RefreshDiscoveredListUi();
        };
    }

    private async System.Threading.Tasks.Task ConnectToEndpointAsync(string endpoint)
    {
        if (Context == null) return;
        _btnConnectPc!.Enabled = false;
        var (success, msg) = await CompanionNetworkService.Instance.ConnectToPcAsync(endpoint);
        _btnConnectPc.Enabled = true;

        Toast.MakeText(Context, msg, ToastLength.Short)?.Show();
        RefreshServerAndPeerUi();
    }

    private void RefreshServerAndPeerUi()
    {
        var net = CompanionNetworkService.Instance;
        var loc = LocalizationService.Instance;

        if (_switchServer != null && _switchServer.Checked != net.IsServerRunning)
        {
            _switchServer.Checked = net.IsServerRunning;
        }

        string ips = string.Join(", ", CompanionNetworkService.GetLocalIpv4Addresses());
        string lsStatus = LocalSendService.Instance.IsRunning
            ? $"LocalSend: :{LocalSendService.Instance.ListeningPort}"
            : "LocalSend: Off";
        if (_txtDeviceNameInfo != null)
            _txtDeviceNameInfo.Text = $"Device: {CompanionNetworkService.GetDeviceFriendlyName()}  •  Status: {(net.IsServerRunning ? "Running" : "Stopped")}";
        if (_txtLocalIps != null)
            _txtLocalIps.Text = $"LAN IP: {ips}  |  SyncDroid: :{net.ListeningPort}  |  {lsStatus}";
        if (_txtPairingCode != null)
            _txtPairingCode.Text = $"UDP Discovery: {CompanionNetworkService.DiscoveryUdpPort} & {LocalSendService.MulticastGroupIp}:{LocalSendService.DefaultPort}";

        if (net.ConnectedPeer != null)
        {
            if (_txtConnectedPcInfo != null)
                _txtConnectedPcInfo.Text = $"{loc.ConnectedToPcPrefix}[{net.ConnectedPeer.ProtocolBadge}] {net.ConnectedPeer.DeviceName} ({net.ConnectedPeer.Endpoint})";
            if (_btnDisconnectPc != null)
                _btnDisconnectPc.Visibility = ViewStates.Visible;
        }
        else
        {
            if (_txtConnectedPcInfo != null)
                _txtConnectedPcInfo.Text = loc.NoPcConnectedPill;
            if (_btnDisconnectPc != null)
                _btnDisconnectPc.Visibility = ViewStates.Gone;
        }
    }

    private void RefreshDiscoveredListUi()
    {
        var peers = CompanionNetworkService.Instance.DiscoveredPeers;
        _peersAdapter?.NotifyDataSetChanged();
        if (_txtDiscoveredEmpty != null)
            _txtDiscoveredEmpty.Visibility = peers.Count == 0 ? ViewStates.Visible : ViewStates.Gone;
        if (_recyclerDiscovered != null)
            _recyclerDiscovered.Visibility = peers.Count == 0 ? ViewStates.Gone : ViewStates.Visible;
    }

    private void OnLanguageChanged(object? sender, EventArgs e) => ApplyLocalization();

    private void ApplyLocalization()
    {
        var loc = LocalizationService.Instance;
        if (_txtServerTitle != null) _txtServerTitle.Text = loc.ServerCardTitle;
        if (_txtServerDesc != null) _txtServerDesc.Text = loc.ServerCardDesc;
        if (_txtPcConnectTitle != null) _txtPcConnectTitle.Text = loc.PcConnectTitle;
        if (_txtPcConnectDesc != null) _txtPcConnectDesc.Text = loc.PcConnectDesc;
        if (_txtManualIpLabel != null) _txtManualIpLabel.Text = loc.ManualIpLabel;
        if (_btnConnectPc != null) _btnConnectPc.Text = loc.ConnectButton;
        if (_btnDisconnectPc != null) _btnDisconnectPc.Text = loc.DisconnectButton;
        if (_txtDiscoveredHeader != null) _txtDiscoveredHeader.Text = loc.DiscoveredHeader;
        if (_btnScanLan != null) _btnScanLan.Text = loc.ScanLanButton;
        if (_txtDiscoveredEmpty != null) _txtDiscoveredEmpty.Text = loc.DiscoveredEmptyHint;
        RefreshServerAndPeerUi();
    }

    private sealed class DiscoveredPeersAdapter : RecyclerView.Adapter
    {
        private readonly List<CompanionPeer> _peers;
        private readonly Action<CompanionPeer> _onConnect;

        public DiscoveredPeersAdapter(List<CompanionPeer> peers, Action<CompanionPeer> onConnect)
        {
            _peers = peers;
            _onConnect = onConnect;
        }

        public override int ItemCount => _peers.Count;

        public override RecyclerView.ViewHolder OnCreateViewHolder(ViewGroup parent, int viewType)
        {
            var v = LayoutInflater.From(parent.Context)!.Inflate(Resource.Layout.item_discovered_peer, parent, false)!;
            return new PeerViewHolder(v);
        }

        public override void OnBindViewHolder(RecyclerView.ViewHolder holder, int position)
        {
            var vh = (PeerViewHolder)holder;
            var peer = _peers[position];
            vh.Name.Text = $"[{peer.ProtocolBadge}] {peer.DeviceName} ({peer.Platform})";
            vh.Endpoint.Text = $"{peer.Endpoint}  •  v{peer.Version}";
            vh.ConnectBtn.Text = LocalizationService.Instance.ConnectButton;

            vh.ConnectBtn.Click -= vh.CurrentClick;
            vh.CurrentClick = (_, _) => _onConnect(peer);
            vh.ConnectBtn.Click += vh.CurrentClick;
        }

        private sealed class PeerViewHolder : RecyclerView.ViewHolder
        {
            public TextView Name { get; }
            public TextView Endpoint { get; }
            public MaterialButton ConnectBtn { get; }
            public EventHandler? CurrentClick { get; set; }

            public PeerViewHolder(View itemView) : base(itemView)
            {
                Name = itemView.FindViewById<TextView>(Resource.Id.txt_peer_name)!;
                Endpoint = itemView.FindViewById<TextView>(Resource.Id.txt_peer_endpoint)!;
                ConnectBtn = itemView.FindViewById<MaterialButton>(Resource.Id.btn_connect_peer)!;
            }
        }
    }
}
