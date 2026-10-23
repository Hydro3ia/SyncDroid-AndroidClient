# SyncDroid for Android — Future Update Roadmap (`future_update.md`)

本文档记录计划在下一版本（`> 0.1.2.0`，即 `0.1.3.0`）中合入的新功能需求、确认的设计决策及针对 Android 客户端（`com.syncdroid.client`）的详细实现方案。

---

## 功能 1：在「文件直传 (File Transfer)」与「无线伴侣 (Companion)」中添加对 LocalSend 协议的支持

### 1. 背景与目标
- **背景**：LocalSend 是目前跨平台（Android、iOS/iPadOS、Windows、macOS、Linux）生态中最通用的开源局域网无网文件直传协议（Protocol v2.1）。
- **目标**：在 `SyncDroidAndroid`（包名 `com.syncdroid.client`）的 **「文件直传 (File Transfer)」** 与 **「无线伴侣 (Companion)」** 模块中原生支持 **LocalSend 协议（v2.1）**，使 Android 端不仅能与运行 `SyncDroid WinUI` 的电脑进行双向 `/MIR` 镜像同步与伴侣互传，还能直接与局域网内任何运行 **LocalSend** 的手机、平板（含 iPhone/iPad）或电脑（Windows/macOS/Linux）双向无网互传文件与文件夹：
  1. **作为发送端（Android ➔ LocalSend 设备）**：将「文件直传」队列中的单文件、多文件、文件夹，或从 Android 系统相册/文件管理器通过系统分享面板（`Intent.ActionSend` / `Intent.ActionSendMultiple`）分享进来的文件，直接推送至局域网内的 LocalSend 设备。
  2. **作为接收端（LocalSend 设备 ➔ Android）**：通过 `SyncForegroundService` 在后台监听 LocalSend 协议端口（默认 `53317`），使电脑或其他手机上的 LocalSend 客户端能够直接发现本台 Android 设备并投送文件至手机指定接收目录（默认 `/storage/emulated/0/Download/SyncDroid`），落盘后自动触发 `MediaScannerHelper.ScanFile` 刷新系统相册与媒体库。

---

### 2. LocalSend 通信协议规范（对齐 LocalSend Protocol v2.1）

#### 2.1 局域网发现协议（UDP 多播 + `MulticastLock` + HTTP 注册）
- **默认多播地址与端口**：
  - 多播组 IP：`224.0.0.167`
  - 端口：`53317`（UDP 多播发现 + TCP HTTP/HTTPS 控制与传输）
- **Android 硬件多播锁（`WifiManager.MulticastLock`）**：
  - Android 系统默认会在网卡驱动层过滤 UDP 多播包以节省电量（应用已声明 `android.permission.CHANGE_WIFI_MULTICAST_STATE` 权限）。
  - 在启动 LocalSend 发现与监听服务时，需通过 `WifiManager.CreateMulticastLock("SyncDroid::LocalSendMulticastLock")` 获取并持有 `MulticastLock`（`SetReferenceCounted(false)` + `Acquire()`），确保手机能够稳定接收 `224.0.0.167:53317` 的局域网设备广播。
- **Android 端设备信息模型 (`LocalSendInfoDto`)**：
  ```json
  {
    "alias": "Pixel 9 Pro (SyncDroid)",
    "version": "2.1",
    "deviceModel": "Google Pixel 9 Pro",
    "deviceType": "mobile",
    "fingerprint": "<SHA256_Cert_Or_Random_Fingerprint>",
    "port": 53317,
    "protocol": "http",
    "download": false,
    "announce": true
  }
  ```
- **发现与回应机制**：
  1. **UDP 多播探测**：
     - `LocalSendService` 绑定 `53317` 并加入多播组 `224.0.0.167`。
     - 当用户在「无线伴侣」页点击“扫描局域网 (Scan LAN)”或启动伴侣服务时，同时发送 `SyncDroid` 伴侣广播（UDP `45820`）与 `LocalSend` 多播公告（`224.0.0.167:53317`，`announce: true`）。
     - 当收到局域网内其他 LocalSend 设备发来的 `announce: true` 多播包时，将其加入 `DiscoveredPeers` 列表，并向对端发起 `POST /api/localsend/v2/register`（若失败则回退发送 `announce: false` 的 UDP 包）完成双向发现。
  2. **HTTP 主动探测与手动 IP 连接**：
     - 在「无线伴侣」页手动输入 `IP:53317` 点击连接时，自动通过 `GET /api/localsend/v2/info` 与 `POST /api/localsend/v2/register` 识别并连接对端 LocalSend 节点，解决部分公共 Wi-Fi / 校园网路由器隔离 UDP 多播的问题。

#### 2.2 HTTP / HTTPS REST 传输协议（默认 TCP 端口：`53317`）
LocalSend 客户端默认启用自签名 TLS 证书（`protocol: "https"`）或明文 HTTP（`protocol: "http"`）。`SyncDroidAndroid` 作为客户端发起请求时配置 `HttpClientHandler.ServerCertificateCustomValidationCallback = (_, _, _, _) => true` 以兼容对端自签名证书；作为服务端监听时支持 HTTP/HTTPS 接入。

| 接口路径 | 方法 | 查询参数 (`QueryString`) | 功能说明 |
| :--- | :--- | :--- | :--- |
| `/api/localsend/v2/info` | `GET` | 无 | 获取本机设备基本信息（`alias`、`version`、`deviceModel`、`deviceType: "mobile"`、`fingerprint`、`port`、`protocol`） |
| `/api/localsend/v2/register` | `POST` | 无 | 接收对端设备注册信息，将其加入已发现设备列表，并返回本机设备信息 |
| `/api/localsend/v2/prepare-upload` | `POST` | `pin=<可选PIN>` | 发送文件前的握手请求，提交发送方 `info` 与待传文件字典 `files`（含 `id`、`fileName`、`size`、`fileType`、`metadata`），返回 `sessionId` 与每个文件的上传 `token` |
| `/api/localsend/v2/upload` | `POST` | `sessionId=<sid>&fileId=<fid>&token=<tok>` | 以 `application/octet-stream` 流式上传单个文件二进制内容 |
| `/api/localsend/v2/cancel` | `POST` | `sessionId=<sid>` | 取消当前传输会话 |

#### 2.3 核心 DTO 数据结构（新增 `Models/LocalSendModels.cs`）
```csharp
using System.Collections.Generic;

namespace SyncDroidAndroid.Models;

public class LocalSendInfoDto
{
    public string Alias { get; set; } = "";
    public string Version { get; set; } = "2.1";
    public string DeviceModel { get; set; } = "Android";
    public string DeviceType { get; set; } = "mobile";
    public string Fingerprint { get; set; } = "";
    public int Port { get; set; } = 53317;
    public string Protocol { get; set; } = "http";
    public bool Download { get; set; } = false;
    public bool? Announce { get; set; }
}

public class LocalSendFileDto
{
    public string Id { get; set; } = "";
    public string FileName { get; set; } = ""; // 支持相对路径如 "DCIM/Camera/IMG_001.jpg"
    public long Size { get; set; }
    public string FileType { get; set; } = "application/octet-stream";
    public string? Sha256 { get; set; }
    public string? Preview { get; set; }
    public LocalSendFileMetadataDto? Metadata { get; set; }
}

public class LocalSendFileMetadataDto
{
    public string? Modified { get; set; } // ISO-8601 UTC 时间字符串
}

public class LocalSendPrepareUploadRequestDto
{
    public LocalSendInfoDto Info { get; set; } = new();
    public Dictionary<string, LocalSendFileDto> Files { get; set; } = new();
}

public class LocalSendPrepareUploadResponseDto
{
    public string SessionId { get; set; } = "";
    public Dictionary<string, string> Files { get; set; } = new(); // fileId -> token
}
```

---

### 3. Android 端交互与业务流程

#### 3.1 设备协议类型区分与模式边界
1. **`CompanionPeer` 协议标识扩展（`Models/SyncModels.cs`）**：
   - 新增枚举 `PeerProtocolType { SyncDroidCompanion = 0, LocalSend = 1 }`，并在 `CompanionPeer` 中增加 `ProtocolType` 与 `HttpsProtocol` 属性：
     - `PeerProtocolType.SyncDroidCompanion` ➔ 在列表与顶部连接胶囊中显示 `[SyncDroid] DESKTOP-PC (192.168.1.x:45821)`
     - `PeerProtocolType.LocalSend` ➔ 在列表与顶部连接胶囊中显示 `[LocalSend] MacBook / iPhone / PC (192.168.1.x:53317)`
2. **不同协议下的功能可用性处理**：
   - 当已连接的对端为 **`[LocalSend]`** 设备时：
     - **Tab 2「文件直传 (File Transfer)」**：完全支持发送队列至对端设备。由于 LocalSend 协议由接收端自行管理落盘路径，此时方向自动固定为 `手机 ➔ 设备`，目标文件夹输入框置灰并显示提示“由接收端 LocalSend 自动保存至其下载目录”，隐藏“浏览...”按钮；点击「开始传输」即刻执行 LocalSend `prepare-upload` + `upload` 流式发送。
     - **Tab 1「镜像同步 (Mirror Sync)」**：由于 LocalSend 协议不包含远端目录树遍历（`/api/v1/storage/tree`）与多余文件清理（`/api/v1/file/delete`）能力，若用户在连接 `[LocalSend]` 节点时点击「开始同步」，在日志与弹窗中给出友好三语提示，引导用户使用「文件直传」页面或连接 `SyncDroid WinUI` 无线伴侣服务。

#### 3.2 发送队列至 LocalSend 设备流程（`Android ➔ LocalSend 设备`）
1. 遍历 `SyncStateService.Instance.TransferQueue` 中的文件与文件夹（若队列项为文件夹，则通过 `LocalStorageScanner` 递归展开其内部文件，并以 `<文件夹名>/<相对路径>` 作为 `FileName`，使接收端 LocalSend 原样还原目录层级）。
2. 根据设置项 `CheckLongFileNames` 执行文件名长度与非法字符预检。
3. 调用对端 `POST /api/localsend/v2/prepare-upload`：
   - 在同步活动日志中记录：`[LocalSend] Waiting for target device ({peer.DeviceName}) to accept transfer...`
   - 若返回 `204 No Content`：目标端无需传输或全部跳过，直接标记为 `100% Completed`。
   - 若返回 `403 Forbidden`：提示目标设备拒绝了接收请求。
   - 若返回 `200 OK`：解析返回的 `sessionId` 与每个 `fileId` 对应的 `token`。
4. 依次通过 `POST /api/localsend/v2/upload?sessionId=...&fileId=...&token=...` 流式上传每个文件，通过 `ProgressStreamContent` 实时更新 `SyncStateService` 进度条、前台服务通知栏进度（`SyncForegroundService`）及活动日志。
5. 若用户在界面或通知栏点击「取消 (Cancel)」，立即中断上传流并调用 `POST /api/localsend/v2/cancel?sessionId=...`。

#### 3.3 从 LocalSend 设备接收文件流程（`LocalSend 设备 ➔ Android`）
1. 在「设置 (Settings)」页支持开启/关闭 **LocalSend 局域网接收服务**（默认开启，端口 `53317`）及设置手机端默认接收目录（默认 `/storage/emulated/0/Download/SyncDroid`）。
2. 当局域网内其他设备向本机发起 `POST /api/localsend/v2/prepare-upload` 时：
   - 若开启“自动接收 (Auto-Accept)”，直接生成 `sessionId` 与文件 `token` 返回 `200 OK`；若未开启自动接收且应用处于前台，则弹出 Material 3 确认对话框询问是否接收来自 `{info.Alias}` 的 `{files.Count}` 个文件。
3. 在处理 `POST /api/localsend/v2/upload` 时：
   - 自动对接收文件名执行 UTF-8 `<= 254` 字节安全截断（`FileNameLengthHelper`），防止外源设备发来超长中文文件名导致 Android `ENAMETOOLONG` 异常；
   - 先流式写入目标目录下的 `.syncdroid_<guid>.part` 临时文件，完成后按当前设定的同名冲突策略（大小相同跳过 / 强制覆盖 / 保留两者）原子移动至最终路径；
   - 调用 `MediaScannerHelper.ScanFile(context, finalPath)` 通知 Android `MediaStore`，确保接收到的图片、视频、音频立即在系统相册中可见。

---

## 功能 2：在「文件直传 (File Transfer)」与连接状态中将写死为“电脑 / PC”的字符串统一调整为“设备 (Device)”

### 1. 背景与原因
- 引入 LocalSend 协议支持后，Android 客户端在「文件直传 (File Transfer)」页面不再局限于仅与 Windows 电脑（PC）互传文件，而是可以与局域网内的任意手机（Android / iPhone）、平板（iPad / Android Tablet）或电脑（Windows / Mac / Linux）自由互传。
- 因此，需要将「文件直传」页面（以及通用设备发现列表）中原本写死为 `"电脑"` / `"PC"` 的 UI 文案统一调整为中立、准确的 **“设备 / 裝置 / Device”**（注：Tab 1「镜像同步」因专为 PC 与手机双向文件夹镜像设计，保留原有的 `电脑 ➔ 手机` / `PC ➔ Android` 表述）。

### 2. 具体三语词条调整对照表 (`Services/LocalizationService.cs`)

| 属性名 (`LocalizationService`) | 原文案 (`0.1.2.0`) | 调整后新文案 (`0.1.3.0`) |
| :--- | :--- | :--- |
| **`TxDirAndroidToPc`**<br>*(建议重命名或保留兼容属性名 `TxDirAndroidToDevice`)* | • `en-US`: `Android  ➔  PC`<br>• `zh-CN`: `手机  ➔  电脑`<br>• `zh-TW`: `手機  ➔  電腦` | • `en-US`: `Android  ➔  Device`<br>• `zh-CN`: `手机  ➔  设备`<br>• `zh-TW`: `手機  ➔  裝置` |
| **`TxDirPcToAndroid`**<br>*(或 `TxDirDeviceToAndroid`)* | • `en-US`: `PC  ➔  Android`<br>• `zh-CN`: `电脑  ➔  手机`<br>• `zh-TW`: `電腦  ➔  手機` | • `en-US`: `Device  ➔  Android`<br>• `zh-CN`: `设备  ➔  手机`<br>• `zh-TW`: `裝置  ➔  手機` |
| **`TargetPcFolderLabel`**<br>*(或 `TargetDeviceFolderLabel`)* | • `en-US`: `Target Destination Folder (PC)`<br>• `zh-CN`: `电脑端目标接收文件夹`<br>• `zh-TW`: `電腦端目標接收資料夾` | • `en-US`: `Target Destination Folder (Device)`<br>• `zh-CN`: `目标设备接收文件夹`<br>• `zh-TW`: `目標裝置接收資料夾` |
| **`NoPcConnectedPill`** | • `en-US`: `No PC Connected`<br>• `zh-CN`: `未连接电脑端`<br>• `zh-TW`: `未連接電腦端` | • `en-US`: `No Device Connected`<br>• `zh-CN`: `未连接设备`<br>• `zh-TW`: `未連接裝置` |
| **`ConnectedToPcPrefix`** | • `en-US`: `PC: `<br>• `zh-CN`: `已连电脑: `<br>• `zh-TW`: `已連電腦: ` | • `en-US`: `Connected: `<br>• `zh-CN`: `已连接: `<br>• `zh-TW`: `已連接: ` |
| **`DiscoveredHeader`** | • `en-US`: `Discovered PCs on LAN`<br>• `zh-CN`: `局域网内发现的电脑`<br>• `zh-TW`: `區域網路內發現的電腦` | • `en-US`: `Discovered Devices on LAN`<br>• `zh-CN`: `局域网内发现的设备`<br>• `zh-TW`: `區域網路內發現的裝置` |

### 3. 「文件直传」页内布局与代码调整 (`Fragments/FileTransferFragment.cs` & `fragment_file_transfer.xml`)
1. **`Resources/layout/fragment_file_transfer.xml`**：
   - 将方向切换按钮默认文本更新为 `Android ➔ Device` 与 `Device ➔ Android`，目标目录标签默认文本更新为 `Target Destination Folder (Device)`。
2. **`Fragments/FileTransferFragment.cs`**：
   - 当已连接对端为 `PeerProtocolType.LocalSend` 时：
     - 目标目录输入框自动显示只读提示文案：
       - `en-US`: `Saved automatically to receiver's LocalSend download folder`
       - `zh-CN`: `由接收端设备 LocalSend 自动保存至其下载目录`
       - `zh-TW`: `由接收端裝置 LocalSend 自動儲存至其下載目錄`
     - 自动禁用目标路径下拉框与「浏览...」按钮，点击「开始传输」时直接调用 `LocalSendService.Instance.SendQueueToPeerAsync(...)`。
   - 同时保持与 `ResponsiveWindowService`（Jetpack WindowManager）的自适应双栏（Two-Pane）布局联动，在平板、横屏与折叠屏展开态下左栏展示方向/冲突策略与目标操作、右栏展示待传输队列。

---

## 待新增与修改的代码文件清单（`0.1.3.0` 实施阶段）

1. **`Models/LocalSendModels.cs`（新增）**：
   - 定义 LocalSend Protocol v2.1 所需的 DTO（`LocalSendInfoDto`、`LocalSendFileDto`、`LocalSendFileMetadataDto`、`LocalSendPrepareUploadRequestDto`、`LocalSendPrepareUploadResponseDto`）。
2. **`Models/SyncModels.cs` & `Models/AppSettings.cs`（修改）**：
   - 在 `CompanionPeer` 中新增 `PeerProtocolType ProtocolType` 属性；
   - 在 `AppSettings` 中新增 `LocalSendEnabled`（默认 `true`）、`LocalSendPort`（默认 `53317`）、`LocalSendAutoAccept`（默认 `true`）、`LocalSendReceiveDir`（默认 `"/storage/emulated/0/Download/SyncDroid"`）。
3. **`Services/LocalSendService.cs`（新增）**：
   - 管理 Android `WifiManager.MulticastLock`；
   - 实现 UDP 多播 `224.0.0.167:53317` 设备发现与公告回应；
   - 实现 HTTP/HTTPS `53317` LocalSend 客户端（`info`、`register`、`prepare-upload`、`upload`、`cancel`）与服务端接收监听（含 `.syncdroid_<guid>.part` 原子写入、UTF-8 254 字节安全文件名截断、`MediaScannerHelper` 媒体库刷新）。
4. **`Services/CompanionNetworkService.cs` & `Services/SyncForegroundService.cs`（修改）**：
   - 在扫描局域网和启动伴侣前台服务时协同启停 `LocalSendService`，合并展示 `[SyncDroid]` 与 `[LocalSend]` 发现节点。
5. **`Services/LocalizationService.cs`（修改）**：
   - 将「文件直传」与通用连接状态中的 `"电脑"` / `"PC"` 标识统一调整为 `"设备"` / `"裝置"` / `"Device"`；
   - 新增 LocalSend 服务配置、等待对端确认、接收提示等三语词条。
6. **`Fragments/FileTransferFragment.cs` & `Resources/layout/fragment_file_transfer.xml`（修改）**：
   - 应用新的“设备 (Device)”文案；
   - 接入 `LocalSendService` 队列发送逻辑，并根据当前连接设备的协议类型（`SyncDroidCompanion` vs `LocalSend`）自适应切换目标路径栏状态。
7. **`Fragments/CompanionFragment.cs` & `Fragments/SettingsFragment.cs`（修改）**：
   - 「无线伴侣」页支持同时发现、展示并连接 `[SyncDroid]` 与 `[LocalSend]` 节点；
   - 「设置」页支持配置本机 LocalSend 接收服务开关、端口（`53317`）与默认接收文件夹。

---

## 附：与 Windows 端 (`SyncDroidWinUI v0.1.5.0`) 对齐的新增实现逻辑与功能需求

以下为 Windows 端在 `v0.1.5.0` 中已落地的核心实现逻辑与 UI 变更，供 Android 端（`SyncDroid-on-Android`）同步开发对齐：

### 1. LocalSend 协议完整落地实现逻辑（含双协议探测、SHA-256 校验与端口占用提示）
1. **双协议（HTTPS / HTTP）自适应监听与客户端探测**：
   - **服务端监听（TCP `53317`）**：使用内存动态生成的 2048-bit RSA 自签名 `X509Certificate2`（计算证书 `RawData` 的 SHA-256 Hex 作为 `fingerprint`）。在 `TcpListener` 接收连接后读取首字节：若首字节为 `0x16`（TLS ClientHello），自动升级为 `SslStream`；否则按明文 HTTP 解析，从而单端口同时兼容开启或关闭加密的 LocalSend 客户端。
   - **客户端请求回退**：调用对端 `/api/localsend/v2/prepare-upload` 或 `/info` 时，优先使用对端声明的 `protocol`（`https` 或 `http`），若连接异常则自动切换另一协议重试并更新 `peer.Protocol`。
2. **端口冲突（`Access is forbidden` / `AddressAlreadyInUse`）检测与提示**：
   - 当设备已运行独立版 LocalSend 应用占用 `53317` 端口时，绑定端口会触发 `SocketError.AccessDenied` / `SocketError.AddressAlreadyInUse`（或异常信息包含 `forbidden` / `access` / `访问权限`）。
   - 记录 `IsAccessForbiddenError = true`，在设置页 LocalSend 配置卡片状态栏直接显示多语言提示並弹窗提醒用户：
     - `zh-CN`：`端口访问被拒绝 (Access is forbidden)：请关闭已存在的 LocalSend 软件以使用此功能`
     - `zh-TW`：`連接埠存取被拒絕 (Access is forbidden)：請關閉已存在的 LocalSend 軟體以使用此功能`
     - `en-US`：`Access is forbidden (Port 53317 in use): Please close the existing LocalSend software to use this feature`
3. **LocalSend 传输与接收后的 Hash 比对校验**：
   - 当设置项 `VerifyHashAfterTransfer`（默认开启 `true`）启用时：
     - 作为发送端：在 `prepare-upload` 的 `LocalSendFileDto.Sha256` 中附带本地文件计算的 SHA-256 值；
     - 作为接收端：在 `/upload` 接收写入 `.syncdroid_<guid>.part` 临时文件后，若 `fileDto.Sha256` 非空，计算临时文件的 SHA-256 并比对，不一致则立即删除残损临时文件并报错。

---

### 2. 文件传输时在活动日志输出「实时传输速度」与「总体传输完成百分比」
1. **`ProgressTrackingStream` 流包装器与速率/进度计算（参考 `Helpers/FileHashHelper.cs`）**：
   - 预先统计本次任务待传输文件的总字节数 `totalBytes` 与总文件数 `totalFiles`，并维护已完成字节数 `completedBytes` 与已完成文件数 `processedFiles`。
   - **总体传输完成百分比计算公式**：
     - 当 `totalBytes > 0` 时：`overallPercent = Clamp((completedBytes + currentFileInFlightBytes) * 100.0 / totalBytes, 0.0, 100.0)`；
     - 当 `totalBytes == 0` 时：`overallPercent = Clamp(processedFiles * 100.0 / totalFiles, 0.0, 100.0)`。
   - **日志输出规范**：
     - **大文件传输途中（每 `1500 ms` 节流输出一次）**：
       `Transferring: <相对路径> | Speed: <实时速率如 32.45 MB/s> | Overall: <XX.X>% (<当前序号>/<总文件数>)`
     - **单文件传输完成时**：
       `Transferred: <相对路径> (<文件大小如 48.20 MB>) | Speed: <平均速率如 31.80 MB/s> | Overall: <XX.X>% (<已完成数>/<总文件数>)`
     - **跳过相同文件时**：
       `Skipped (Identical): <相对路径> | Overall: <XX.X>% (<已完成数>/<总文件数>)`
   - 同时将实时 `overallPercent` 同步驱动 UI 进度条与前台通知栏进度。

---

### 3. 在三个模式的 Target Path 选择器中支持 Windows 盘符（跨平台 Target 设备目录浏览）
1. **背景**：
   - 现行版本下，连接的目标设备（Target Device）可以是任意平台（例如通过无线伴侣连接 Windows 电脑或另一台 Android 设备）。
   - 因此在 **Push (`/MIR mode`)**、**Pull (`/MIR mode`)** 与 **Direct Transfer（文件直传）** 三个模式的目标路径选择器（`StorageBrowserDialog` / `AndroidFolderPickerDialog`）中，均需支持 **Windows 盘符（`C:\`、`D:\`、`E:\` 等）** 与 **Android `/storage`**。
2. **协议与选择器实现要点**：
   - 当向 Windows 端无线伴侣服务请求 `GET /api/v1/storage/list?path=/` 时，Windows 端返回所有就绪盘符列表（`C:\`、`D:\` 等，`IsDirectory = true`）。
   - 在目录选择器顶部提供 **盘符 / 根目录快速切换下拉框（`RootDriveComboBox`）**，并在目录树根节点同时支持展示 Windows 盘符节点（`C:\`、`D:\` 等，使用硬盘图标）与 `/storage` 节点。
   - **路径规范化与拼接（`IsWindowsDrivePath` / `NormalizeRemoteTargetPath` / `CombineRemotePath`）**：
     - 识别以 `[A-Za-z]:` 开头的 Windows 盘符路径；
     - 对盘符根目录保留末尾反斜杠（如 `C:\`），子目录使用 `\` 拼接（如 `D:\Backup\SubFolder`），避免将 `C:\` 错误截断为 `C:` 或混用正反斜杠。
   - **无线伴侣服务端新增 Hash 校验接口对齐**：
     - 在无线伴侣 HTTP 服务端新增 `GET /api/v1/file/hash?path=<path>`（返回 `{"ok":true,"sha256":"...","size":123}`），并在 `GET /api/v1/file/pull` 与 `POST /api/v1/file/push` 中支持 `X-File-Sha256` 请求/响应头，供开启「传输后 Hash 比对校验」（默认开启）时进行端到端 SHA-256 校验。

---

### 4. 更改同步标签页 / 模式名称为 `Push (/MIR mode)` 与 `Pull (/MIR mode)`
- 由于目标设备不再局限于特定单一平台，将原本写死为 `PC ➔ Android` 与 `Android ➔ PC`（或 `手机 ➔ 电脑` / `电脑 ➔ 手机`）的同步标签页/方向标签统一修改为：
  - **Tab 1 / 推送同步方向**：**`Push (/MIR mode)`**
  - **Tab 2 / 拉取同步方向**：**`Pull (/MIR mode)`**
- 将同步页内的目标文件夹路径标签统一为中立的 **`Target Device Directory` / `目标设备文件夹路径` / `目標裝置資料夾路徑`**，输入框占位提示更新为包含双平台示例：`/storage/emulated/0/MyFolder or D:\MyFolder`。

### 5. 接收文件管理页面、设备初次连接确认与「管理认证设备」
1. **新增接收页面 (`ReceivePage` / `ReceiveFragment`) 与自动切换逻辑**
   - 增加独立的 **「接收文件 (Receive Files)」** 页面。
   - 当收到对端 `LocalSend` 的 `/api/localsend/v2/prepare-upload` 请求或 `SyncDroid Companion` 的 `/api/v1/file/push` 请求时，如果不在白名单设备中，自动跳转到接收页面并展示确认提示；若已在信任列表中，同样自动跳转到该页面展示每个接收文件的进度与状态（`ReceiveFilesHeader`）。
   - 页面展示发送方设备名称与协议类型、本机保存目录（支持一键打开）、包含每个文件的保存路径、大小与实时状态列表，以及整体接收进度条。
2. **初次连接与接收时的用户确认认证机制 (`TrustedDevices`)**
   - **SyncDroid 伴侣连接**：在两台设备初次通信（主动连接或被动接收数据）时，拦截请求并弹出确认对话框。确认后将其加入已认证设备列表持久化保存，后续不再弹窗；若拒绝则中止连接并返回 `403 Forbidden`。
   - **LocalSend 连接**：接收 LocalSend 传输请求（`prepare-upload`）时，拦截并展示包含待接收文件预览的确认弹窗（或在 Receive 页面通过横幅确认）。接受后开始传输，并将该设备持久化至认证设备列表。
3. **设置页「管理认证设备」按钮与设备管理页**
   - 在「设置」中新增 **「已认证设备管理」** 卡片与入口按钮。
   - 进入设备管理列表页后，列出所有已成功认证的 SyncDroid 与 LocalSend 设备（名称、协议类型、IP/端口地址、认证时间），支持逐个 **「删除」** 或 **「全部删除」**。被删除的设备下次连接时将重新触发确认流程。

### 6. 更换图标并启用自适应图标
- 更换图标为 "C:\Users\Yiren\Downloads\Quick Share\IMG_20261023_125130.png"，并启用自适应图标。

### 7. 在分享页面（直传模式）新增入口
- 在系统分享页面新增入口（直传模式）。
