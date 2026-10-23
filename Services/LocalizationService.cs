using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SyncDroidAndroid.Services;

public class LocalizationService : ObservableObject
{
    public static LocalizationService Instance { get; } = new();

    public event EventHandler? LanguageChanged;

    private string _currentLanguage = "zh-CN";

    private LocalizationService()
    {
        var settings = SettingsService.Load();
        _currentLanguage = string.IsNullOrWhiteSpace(settings.Language) ? "zh-CN" : settings.Language;
    }

    public string CurrentLanguage
    {
        get => _currentLanguage;
        set
        {
            if (string.IsNullOrWhiteSpace(value) || _currentLanguage == value) return;
            _currentLanguage = value;

            var settings = SettingsService.Load();
            settings.Language = _currentLanguage;
            SettingsService.Save(settings);

            OnPropertyChanged(string.Empty);
            LanguageChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public string T(string en, string zhCn, string zhTw)
    {
        return _currentLanguage switch
        {
            "zh-CN" => zhCn,
            "zh-TW" => zhTw,
            _ => en
        };
    }

    // Top Bar & Bottom Navigation
    public string AppSubtitle => T("Wireless Companion & Mirror Sync", "无线伴侣与文件夹镜像同步", "無線伴侶與資料夾鏡像同步");
    public string NoPcConnectedPill => T("No Device Connected", "未连接设备", "未連接裝置");
    public string ConnectedToPcPrefix => T("Connected: ", "已连接: ", "已連接: ");
    public string ServerListeningPill => T("Companion Ready", "伴侣服务就绪", "伴侶服務就緒");

    public string NavMirrorSync => T("Mirror Sync", "镜像同步", "鏡像同步");
    public string NavFileTransfer => T("File Transfer", "文件直传", "檔案直傳");
    public string NavReceive => T("Receive Files", "接收文件", "接收檔案");
    public string NavCompanion => T("Companion", "无线伴侣", "無線伴侶");
    public string NavSettings => T("Settings", "设置", "設定");

    // Permission Banner
    public string PermBannerTitle => T("All Files Access Required", "需要「所有文件访问权限」", "需要「所有檔案存取權限」");
    public string PermBannerDesc => T(
        "Grant MANAGE_EXTERNAL_STORAGE to mirror sync and manage files across /storage/emulated/0.",
        "请授予所有文件访问权限，以便在 /storage/emulated/0 各目录间执行同步与文件传输。",
        "請授予所有檔案存取權限，以便在 /storage/emulated/0 各目錄間執行同步與檔案傳輸。");
    public string GrantButton => T("Grant", "去授权", "去授權");

    // Mirror Sync Tab
    public string DirPcToAndroid => T("Pull (/MIR mode)", "拉取同步 (/MIR mode)", "拉取同步 (/MIR mode)");
    public string DirAndroidToPc => T("Push (/MIR mode)", "推送同步 (/MIR mode)", "推送同步 (/MIR mode)");
    public string MirModeSwitchLabel => T(
        "Mirror Mode — Delete extra files in target",
        "镜像同步模式 — 自动清理目标端多余文件",
        "鏡像同步模式 — 自動清理目標端多餘檔案");
    public string PcDirectoryLabel => T("Target Device Directory", "目标设备文件夹路径", "目標裝置資料夾路徑");
    public string AndroidDirectoryLabel => T("Android Device Directory", "Android 手机文件夹路径", "Android 手機資料夾路徑");
    public string SourceBadge => T("SOURCE", "源目录 (SOURCE)", "來源目錄 (SOURCE)");
    public string TargetBadge => T("TARGET", "目标目录 (TARGET)", "目標目錄 (TARGET)");
    public string MirrorBannerMir => T("▼  MIRROR SYNC (/MIR)  ▼", "▼  镜像同步模式 (/MIR)  ▼", "▼  鏡像同步模式 (/MIR)  ▼");
    public string MirrorBannerMerge => T("▼  INCREMENTAL MERGE (NO DELETE)  ▼", "▼  增量合并同步 (保留目标端多余文件)  ▼", "▼  增量合併同步 (保留目標端多餘檔案)  ▼");
    public string BrowseButton => T("Browse...", "浏览...", "瀏覽...");
    public string StartSyncButton => T("Start Sync", "开始同步", "開始同步");
    public string CancelButton => T("Cancel", "取消", "取消");
    public string ActivityLogsHeader => T("Activity Logs", "同步活动日志", "同步活動日誌");
    public string ClearButton => T("Clear", "清空", "清除");
    public string CopyAllButton => T("Copy All", "复制日志", "複製日誌");

    // Status Texts
    public string StatusReady => T("Ready", "就绪", "就緒");
    public string StatusSyncing => T("Syncing...", "正在同步...", "正在同步...");
    public string StatusTransferring => T("Transferring...", "正在传输...", "正在傳輸...");
    public string StatusCompleted => T("Completed", "已完成", "已完成");
    public string StatusTransferCompleted => T("Transfer Completed", "传输完成", "傳輸完成");
    public string StatusCancelled => T("Cancelled", "已取消", "已取消");
    public string StatusFailed => T("Failed", "失败", "失敗");

    // Direct File Transfer Tab (Non-MIR & LocalSend)
    public string TxModeTitle => T("Direct File & Folder Transfer", "单文件 / 文件夹自由直传", "單檔案 / 資料夾自由直傳");
    public string TxModeDesc => T(
        "Transfer individual files or whole folders to any SyncDroid or LocalSend device without deleting existing files.",
        "自由发送单个、多个文件或整个文件夹（支持 SyncDroid 伴侣与 LocalSend 协议），不会删除目标目录中的已有文件。",
        "自由發送單個、多個檔案或整個資料夾（支援 SyncDroid 伴侶與 LocalSend 協議），不會刪除目標目錄中的已有檔案。");
    public string TxDirAndroidToPc => T("Android  ➔  Device", "手机  ➔  设备", "手機  ➔  裝置");
    public string TxDirPcToAndroid => T("Device  ➔  Android", "设备  ➔  手机", "裝置  ➔  手機");
    public string ConflictLabel => T("When file already exists in destination:", "遇到同名文件时的处理策略：", "遇到同名檔案時的處理策略：");
    public string ConflictSkip => T("Skip Same Size", "大小相同跳过", "大小相同略過");
    public string ConflictOverwrite => T("Overwrite", "强制覆盖", "強制覆蓋");
    public string ConflictRename => T("Keep Both", "保留两者", "保留兩者");
    public string QueueHeader => T("Transfer Queue", "待传输队列", "待傳輸佇列");
    public string ClearQueueButton => T("Clear Queue", "清空队列", "清空佇列");
    public string AddFileButton => T("+ Add File", "+ 添加文件", "+ 新增檔案");
    public string AddFolderButton => T("+ Add Folder", "+ 添加文件夹", "+ 新增資料夾");
    public string QueueEmptyHint => T(
        "No items in queue. Tap '+ Add File' or '+ Add Folder' above, or share files from other apps.",
        "队列为空。请点击上方「+ 添加文件」或「+ 添加文件夹」，或从系统相册/文件管理器分享文件到此应用。",
        "佇列為空。請點擊上方「+ 新增檔案」或「+ 新增資料夾」，或從系統相簿/檔案管理員分享檔案到此應用。");
    public string TargetPcFolderLabel => T("Target Destination Folder (Device)", "目标设备接收文件夹", "目標裝置接收資料夾");
    public string TargetAndroidFolderLabel => T("Target Destination Folder (Android)", "手机端目标接收文件夹", "手機端目標接收資料夾");
    public string LocalSendAutoSaveTargetHint => T(
        "Saved automatically to receiver's LocalSend download folder",
        "由接收端设备 LocalSend 自动保存至其下载目录",
        "由接收端裝置 LocalSend 自動儲存至其下載目錄");
    public string LocalSendMirrorNotSupportedHint => T(
        "Currently connected to a [LocalSend] device. Folder Mirror (/MIR) requires a SyncDroid Companion connection on PC; please use the 'File Transfer' tab for LocalSend transfers.",
        "当前已连接 [LocalSend] 设备。文件夹镜像同步 (/MIR) 需要连接电脑端 SyncDroid 伴侣服务；若要通过 LocalSend 传输文件，请切换至「文件直传」页面。",
        "目前已連接 [LocalSend] 裝置。資料夾鏡像同步 (/MIR) 需要連接電腦端 SyncDroid 伴侶服務；若要透過 LocalSend 傳輸檔案，請切換至「檔案直傳」頁面。");
    public string StartTransferButton => T("Start Transfer", "开始传输", "開始傳輸");

    // Receive Files Tab
    public string ReceivePageTitle => T("Incoming File Receiver", "文件接收中心", "檔案接收中心");
    public string ReceiveSenderLabel => T("Sender Device:", "发送方设备：", "傳送方裝置：");
    public string ReceiveNoActiveSender => T("Waiting for incoming transfers...", "等待其他设备发送文件...", "等待其他裝置傳送檔案...");
    public string ReceiveSaveDirLabel => T("Save Directory:", "保存目录：", "儲存目錄：");
    public string OpenReceiveFolderButton => T("Open Folder", "打开目录", "打開目錄");
    public string ReceiveFilesHeader => T("Incoming Files (File Names)", "接收文件列表（文件档名）", "接收檔案清單（檔案檔名）");
    public string ReceiveEmptyFilesHint => T(
        "No incoming files yet. When another LocalSend or SyncDroid device sends files to this phone, it will automatically switch to this page and display the file names here.",
        "暂无接收文件。当其他 LocalSend 或 SyncDroid 设备向本机发送数据时，将自动切换至此页面并显示文件档名与接收进度。",
        "暫無接收檔案。當其他 LocalSend 或 SyncDroid 裝置向本機傳送資料時，將自動切換至此頁面並顯示檔案檔名與接收進度。");
    public string ClearReceiveListButton => T("Clear List", "清空列表", "清空清單");
    public string ReceivePendingConfirmBannerFormat => T(
        "Incoming transfer request from \"{0}\" ({1} file(s)). Confirm to accept and authenticate this device:",
        "收到来自「{0}」的文件传输请求（共 {1} 个文件），请确认是否接受并认证该设备：",
        "收到來自「{0}」的檔案傳輸請求（共 {1} 個檔案），請確認是否接受並認證該裝置：");
    public string AcceptTransferButton => T("Accept & Authenticate", "接受并认证", "接受並認證");
    public string DeclineTransferButton => T("Decline", "拒绝", "拒絕");
    public string ReceiveStatusPending => T("Pending Confirmation", "等待确认", "等待確認");
    public string ReceiveStatusWaitingUpload => T("Accepted, waiting...", "已接受，等待传输...", "已接受，等待傳輸...");
    public string ReceiveStatusReceiving => T("Receiving...", "正在接收...", "正在接收...");
    public string ReceiveStatusCompleted => T("Completed", "接收完成", "接收完成");
    public string ReceiveStatusSkipped => T("Skipped (Identical)", "已跳过 (相同文件)", "已略過 (相同檔案)");
    public string ReceiveStatusDeclined => T("Declined", "已拒绝", "已拒絕");
    public string ReceiveStatusFailed => T("Failed", "接收失败", "接收失敗");

    // First-Time Connection & LocalSend Receive Confirmation Dialogs
    public string FirstTimeConnectDialogTitle => T("First-Time Device Connection Confirmation", "设备初次连接确认", "裝置初次連接確認");
    public string FirstTimeSyncDroidConnectMessage => T(
        "Connecting with SyncDroid device \"{0}\" ({1}) for the first time.\n\nDo you want to confirm this connection and add it to your authenticated devices?",
        "检测到与 SyncDroid 设备「{0} ({1})」初次连接。\n\n是否确认连接并将该设备加入已认证设备列表？",
        "偵測到與 SyncDroid 裝置「{0} ({1})」初次連接。\n\n是否確認連接並將該裝置加入已認證裝置清單？");
    public string FirstTimeLocalSendReceiveTitle => T("LocalSend Transfer Request Confirmation", "LocalSend 接收请求确认", "LocalSend 接收請求確認");
    public string FirstTimeLocalSendReceiveMessage => T(
        "LocalSend device \"{0}\" ({1}) is requesting to send {2} file(s) to this phone:\n\n{3}\n\nDo you want to accept this transfer request and authenticate the device?",
        "LocalSend 设备「{0} ({1})」请求向本机发送 {2} 个文件：\n\n{3}\n\n是否接受此次传输请求并认证该设备？",
        "LocalSend 裝置「{0} ({1})」請求向本機傳送 {2} 個檔案：\n\n{3}\n\n是否接受此次傳輸請求並認證該裝置？");
    public string ConfirmAcceptButton => T("Confirm & Accept", "确认接受", "確認接受");
    public string ConfirmRejectButton => T("Decline", "拒绝", "拒絕");

    // Companion Tab
    public string ServerCardTitle => T("Android Wireless Companion & LocalSend Server", "手机无线伴侣与 LocalSend 服务端", "手機無線伴侶與 LocalSend 服務端");
    public string ServerCardDesc => T(
        "Allow SyncDroid WinUI (TCP 45821) and LocalSend devices (TCP/UDP 53317) on LAN to discover and transfer files with this phone",
        "开启后，局域网内的电脑端 SyncDroid WinUI 与任意 LocalSend 设备均可自动发现本机并进行同步或文件互传",
        "開啟後，區域網路內的電腦端 SyncDroid WinUI 與任意 LocalSend 裝置均可自動探索本機並進行同步或檔案互傳");
    public string PcConnectTitle => T("Connect to Remote Device (SyncDroid / LocalSend)", "连接远端设备 (SyncDroid / LocalSend)", "連接遠端裝置 (SyncDroid / LocalSend)");
    public string PcConnectDesc => T(
        "Connect to SyncDroid WinUI on PC (45821) or any LocalSend device (53317) to initiate sync or direct file transfer",
        "连接电脑端 SyncDroid WinUI (端口 45821) 或局域网内任意 LocalSend 设备 (端口 53317) 以主动发起同步或文件直传",
        "連接電腦端 SyncDroid WinUI (連接埠 45821) 或區域網路內任意 LocalSend 裝置 (連接埠 53317) 以主動發起同步或檔案直傳");
    public string ManualIpLabel => T("Device IP & Port (e.g. 192.168.1.100:45821 or :53317):", "设备 IP 地址与端口 (例如 192.168.1.100:45821 或 :53317)：", "裝置 IP 位址與連接埠 (例如 192.168.1.100:45821 或 :53317)：");
    public string ConnectButton => T("Connect", "连接", "連接");
    public string DisconnectButton => T("Disconnect", "断开连接", "中斷連接");
    public string DiscoveredHeader => T("Discovered Devices on LAN", "局域网内发现的设备", "區域網路內發現的裝置");
    public string ScanLanButton => T("Scan LAN", "扫描局域网", "掃描區域網路");
    public string DiscoveredEmptyHint => T(
        "Tap 'Scan LAN' to search for SyncDroid & LocalSend devices on your Wi-Fi network, or enter IP:Port above.",
        "点击「扫描局域网」搜索同一 Wi-Fi 下的 SyncDroid 或 LocalSend 设备，或在上方直接输入 IP:端口 连接。",
        "點擊「掃描區域網路」搜尋同一 Wi-Fi 下的 SyncDroid 或 LocalSend 裝置，或在上方直接輸入 IP:連接埠 連接。");

    // Settings Tab & Manage Authenticated Devices Page
    public string LanguageSectionTitle => T("Language / 语言", "界面语言 (Language)", "介面語言 (Language)");
    public string LanguageSectionDesc => T("Select display language for the application interface (instant switch)", "选择应用界面的显示语言（即时生效）", "選擇應用介面的顯示語言（即時生效）");
    public string ThemeSectionTitle => T("Appearance & Material Design 3", "外观与 Material Design 3 主题", "外觀與 Material Design 3 主題");
    public string ThemeSectionDesc => T("Configure light/dark mode and Material You dynamic wallpaper colors", "设置深浅色模式及 Android 12+ 壁纸动态取色 (Material You)", "設定深淺色模式及 Android 12+ 桌布動態取色 (Material You)");
    public string ThemeSystem => T("System", "跟随系统", "跟隨系統");
    public string ThemeLight => T("Light", "浅色", "淺色");
    public string ThemeDark => T("Dark", "深色", "深色");
    public string DynamicColorSwitch => T("Material You Dynamic Color (Android 12+)", "启用 Material You 系统壁纸动态取色", "啟用 Material You 系統桌布動態取色");
    public string TrustedDevicesSectionTitle => T("Authenticated Devices Management", "已认证设备管理", "已認證裝置管理");
    public string TrustedDevicesSectionDesc => T(
        "View and manage SyncDroid and LocalSend devices that have been confirmed and authenticated on first connection",
        "查看并管理在初次连接或接收时已通过确认的 SyncDroid 与 LocalSend 认证设备",
        "查看並管理在初次連接或接收時已通過確認的 SyncDroid 與 LocalSend 認證裝置");
    public string ManageTrustedDevicesButton => T("Manage Authenticated Devices", "管理认证设备", "管理認證裝置");
    public string ManageDevicesPageTitle => T("Manage Authenticated Devices", "管理认证设备", "管理認證裝置");
    public string ManageDevicesPageDesc => T(
        "Devices listed below have been confirmed by you. Deleting a device will require user confirmation again the next time it connects or sends files.",
        "以下设备已通过您的初次连接或接收确认。删除已认证设备后，下次与该设备连接或接收其发送的数据时将重新需要您进行确认。",
        "以下裝置已通過您的初次連接或接收確認。刪除已認證裝置後，下次與該裝置連接或接收其傳送的資料時將重新需要您進行確認。");
    public string BackToSettingsButton => T("Back to Settings", "返回设置", "返回設定");
    public string RemoveAllDevicesButton => T("Remove All", "全部删除", "全部刪除");
    public string DeleteDeviceButton => T("Delete", "删除", "刪除");
    public string NoTrustedDevicesHint => T(
        "No authenticated devices yet. Devices confirmed during first-time connection or file reception will appear here.",
        "暂无已认证的设备。在两台设备初次连接或接收文件时确认通过的设备将显示在此处。",
        "暫無已認證的裝置。在兩台裝置初次連接或接收檔案時確認通過的裝置將顯示在此處。");
    public string TrustedDeviceTimePrefix => T("Authenticated: ", "认证时间：", "認證時間：");
    public string PermSectionTitle => T("Storage Access, Port & Safety", "存储权限、通信端口与安全防护", "儲存權限、通訊埠與安全防護");
    public string ManagePermButton => T("Manage Permission", "管理权限", "管理權限");
    public string PortLabel => T("Companion Listening TCP Port (Default: 45821):", "伴侣服务监听 TCP 端口 (默认 45821)：", "伴侶服務監聽 TCP 連接埠 (預設 45821)：");
    public string ApplyPortButton => T("Apply Port", "应用端口", "套用連接埠");
    public string LocalSendSwitchLabel => T(
        "Enable LocalSend Protocol v2.1 Receiver & Discovery (Port 53317)",
        "启用 LocalSend 协议 v2.1 发现与接收服务 (端口 53317)",
        "啟用 LocalSend 協議 v2.1 探索與接收服務 (連接埠 53317)");
    public string ConfirmDeleteSwitch => T("Confirm before /MIR deletes extra target files", "/MIR 镜像同步删除目标端多余文件前弹窗确认", "/MIR 鏡像同步刪除目標端多餘檔案前彈窗確認");
    public string CheckLongNamesSwitch => T("Detect & handle filenames > 255 UTF-8 bytes", "自动检测并处理超过 255 字节 (UTF-8) 的超长文件名", "自動偵測並處理超過 255 位元組 (UTF-8) 的超長檔名");
    public string AboutSectionTitle => T("About This App", "关于此应用", "關於此應用程式");
    public string AboutAppDescription => T(
        "SyncDroid for Android is a Material Design 3 wireless companion, CJK-safe folder mirror (/MIR), and LocalSend-compatible file transfer client.",
        "SyncDroid Android 端是基于 Material Design 3 打造的无线伴侣应用，支持双向 /MIR 镜像同步、单文件/文件夹自由直传及 LocalSend 协议互通。",
        "SyncDroid Android 端是基於 Material Design 3 打造的無線伴侶應用，支援雙向 /MIR 鏡像同步、單檔案/資料夾自由直傳及 LocalSend 協議互通。");
    public string AboutDeveloperInfo => T(
        "Developer: Yiren Li  •  Open Source under MIT License",
        "开发者：Yiren Li  •  基于 MIT 协议开源",
        "開發者：Yiren Li  •  基於 MIT 協議開源");
    public string GithubButton => T("GitHub Repository", "GitHub 源代码仓库", "GitHub 原始碼倉庫");
    public string PrivacyButton => T("Privacy Policy", "隐私政策", "隱私權政策");

    // Storage Browser Dialog
    public string BrowserLocalTitle => T("Browse Android Storage (/storage)", "浏览手机存储目录 (/storage)", "瀏覽手機儲存目錄 (/storage)");
    public string BrowserRemotePcTitle => T("Browse Remote Device Storage", "浏览远端设备目录", "瀏覽遠端裝置目錄");
    public string NewFolderButton => T("New Folder", "新建文件夹", "新增資料夾");
    public string NewFolderPlaceholder => T("Enter new folder name...", "输入新文件夹名称...", "輸入新資料夾名稱...");
    public string CreateButton => T("Create", "创建", "建立");
    public string SelectedPathLabel => T("Selected Path:", "当前选中路径：", "目前選取路徑：");
    public string SelectFolderButton => T("Select Folder", "选择此文件夹", "選取此資料夾");
    public string SelectFileButton => T("Select File", "选择此文件", "選取此檔案");
}
