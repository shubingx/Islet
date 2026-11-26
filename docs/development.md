# 開發指南

從原始碼執行、技術棧、目前的目錄結構與規劃中的架構。

## 從原始碼執行

### 需求

- Windows 10 2004（19041）以上或 Windows 11
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- 編輯器擇一：Visual Studio 2026（勾選「.NET 桌面開發」）、VS Code + C# Dev Kit、或 JetBrains Rider

### 執行

```powershell
git clone https://github.com/shubingx/Islet.git
cd Islet
dotnet run
```

或用 Visual Studio 開啟資料夾後按 `F5`。

### 調整大小

整體尺寸由 `Shell/IsletWindow.xaml.cs` 的 `UiScale` 控制，`1.0` 是原始設計尺寸，改成 `0.85` 可整體縮小。

## 技術棧

| 層 | 選擇 | 用途 |
| --- | --- | --- |
| 語言與執行環境 | C#、.NET 10 | 主程式 |
| UI 框架 | WPF | 無邊框、透明、置頂視窗與彈簧動畫 |
| 媒體控制 | WinRT `GlobalSystemMediaTransportControlsSessionManager` | 讀取與控制 Spotify、YouTube 等播放狀態 |
| 通知監聽 | WinRT `UserNotificationListener` | 定時輪詢通知中心，讀取 LINE、Discord 等社群通知 |
| 效能監控 | Win32 PDH 效能計數器 | CPU、GPU、磁碟、網路使用率 |
| 視窗行為 | Win32 API（P/Invoke） | 點擊穿透、全螢幕偵測、隱藏於工作列 |
| 資料儲存 | JSON 設定檔；SQLite（規劃中） | 設定已存檔；待辦、通知歷史規劃中 |
| 腳本引擎 | Jint（純 .NET 的 JavaScript 直譯器） | 執行使用者腳本（規劃中） |
| 打包 | MSIX／sparse package | 取得通知監聽需要的套件身分 |

## 目前的目錄結構

```text
Islet/
├─ Islet.csproj        專案檔
├─ App.xaml(.cs)       進入點、全域錯誤記錄
├─ Shell/              島視窗、設定視窗、彈簧動畫、Win32 輔助
├─ Services/           媒體、通知（含社群過濾）、App 圖示、效能、設定、開機啟動、首頁圖片
├─ ViewModels/         Main、Settings、Timer、Performance
├─ Controls/           UsageRing（效能頁的環形儀表）
├─ Models/             通知、待辦、曲目資料
├─ Themes/             Dark.xaml（色彩令牌）、Controls.xaml（控制項樣式）
└─ Assets/             App 圖示（Islet.ico）、品牌圖、AppIcons/（通知用的社群 App 圖示）
```

色彩令牌集中在 `Themes/Dark.xaml`，新增淺色主題只需再放一份同鍵名的資源字典。

## 規劃中的架構

正式版會拆成四層，所有 Windows 專屬 API 都收在轉接層，模組之間透過事件匯流排溝通，核心邏輯不依賴 UI 或 Windows，方便測試：

```text
src/
├─ Islet.App/                 WPF 視窗、系統匣、各分頁畫面
├─ Islet.Core/                事件匯流排、模組宿主、模式管理、設定、儲存
├─ Islet.Platform.Windows/    媒體、通知、視窗行為、音訊（Windows 轉接層）
├─ Islet.Modules.*/           時鐘、音樂、通知、待辦
├─ Islet.Scripting/           Jint 腳本宿主、腳本 API、manifest 驗證
└─ Islet.Packaging/           MSIX 打包專案
tests/                         單元測試
samples/hello-card/            範例腳本
```

---

[← 回到文件目錄](README.md)
