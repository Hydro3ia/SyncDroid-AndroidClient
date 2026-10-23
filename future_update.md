# SyncDroid for Android — Future Update Roadmap (`future_update.md`)

本文档记录计划在下一版本（`> 0.1.2.0`，即 `0.1.3.0`）中合入的新功能需求、确认的设计决策及针对 Android 客户端（`com.syncdroid.client`）的详细实现方案。

---

## 附：与 Windows 端 (`SyncDroidWinUI v0.1.5.0`) 对齐的新增实现逻辑与功能需求

以下为 Windows 端在 `v0.1.5.0` 中已落地的核心实现逻辑与 UI 变更，供 Android 端（`SyncDroid-on-Android`）同步开发对齐：

### 1. 文件传输时在活动日志输出「实时传输速度」与「总体传输完成百分比」
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

### 2. 接收文件管理页面、设备初次连接确认与「管理认证设备」
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