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
    public string DirPcToAndroid => T("PC  ➔  Android", "电脑  ➔  手机", "電腦  ➔  手機");
    public string DirAndroidToPc => T("Android  ➔  PC", "手机  ➔  电脑", "手機  ➔  電腦");
    public string MirModeSwitchLabel => T(
        "Mirror Mode — Delete extra files in target",
        "镜像同步模式 — 自动清理目标端多余文件",
        "鏡像同步模式 — 自動清理目標端多餘檔案");
    public string PcDirectoryLabel => T("Windows PC Directory", "Windows 电脑文件夹路径", "Windows 電腦資料夾路徑");
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

    // Settings Tab
    public string LanguageSectionTitle => T("Language / 语言", "界面语言 (Language)", "介面語言 (Language)");
    public string LanguageSectionDesc => T("Select display language for the application interface (instant switch)", "选择应用界面的显示语言（即时生效）", "選擇應用介面的顯示語言（即時生效）");
    public string ThemeSectionTitle => T("Appearance & Material Design 3", "外观与 Material Design 3 主题", "外觀與 Material Design 3 主題");
    public string ThemeSectionDesc => T("Configure light/dark mode and Material You dynamic wallpaper colors", "设置深浅色模式及 Android 12+ 壁纸动态取色 (Material You)", "設定深淺色模式及 Android 12+ 桌布動態取色 (Material You)");
    public string ThemeSystem => T("System", "跟随系统", "跟隨系統");
    public string ThemeLight => T("Light", "浅色", "淺色");
    public string ThemeDark => T("Dark", "深色", "深色");
    public string DynamicColorSwitch => T("Material You Dynamic Color (Android 12+)", "启用 Material You 系统壁纸动态取色", "啟用 Material You 系統桌布動態取色");
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
