# 打包與發佈（維護者）

這一節是給專案維護者的發佈流程，目標是讓 **不懂電腦的人也能雙擊安裝**：不用裝 .NET、不用開終端機。

## 發佈產物

每個版本產出三樣東西，上傳到 GitHub Releases：

| 產物 | 說明 |
| --- | --- |
| `Islet-Setup-x.y.z.exe` | 安裝精靈，含開始功能表捷徑、開機自動啟動選項、解除安裝；會註冊套件身分，讓通知功能可用 |
| `Islet-x.y.z-portable.zip` | 免安裝版，解壓即用（無通知功能） |
| `SHA256SUMS.txt` | 以上檔案的校驗值 |

## 步驟 1：發佈成獨立執行檔

使用 self-contained 發佈，把 .NET 執行環境一起打包，使用者電腦就不必另外安裝 .NET：

```powershell
dotnet publish -c Release -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:Version=1.0.0 `
  -o .\publish\win-x64
```

輸出在 `publish\win-x64\`，把這個資料夾壓縮就是免安裝版。

> `.csproj` 已設定 `ApplicationIcon`；發佈前再補上 `Product`、`Company`、`Copyright`、`Version` 等資訊，檔案總管的「內容」裡才會顯示正確名稱。

## 步驟 2：製作安裝精靈

兩種做法擇一（第一次發佈前定案）：

- **方案 A：MSIX 套件**（`Islet.Packaging` 專案）
  原生取得套件身分，通知監聽直接可用，也能上架 Microsoft Store 與自動更新。缺點是側載安裝需要簽章，否則使用者裝不起來。
- **方案 B：Inno Setup 安裝檔 + sparse package**
  傳統 `Setup.exe` 體驗，對一般使用者最熟悉；安裝時註冊一個 sparse package 讓程式取得套件身分。sparse package 同樣需要簽章。

安裝檔應包含：

- [ ] 預設安裝到 `%LOCALAPPDATA%\Programs\Islet`（不需要系統管理員權限）
- [ ] 開始功能表捷徑，桌面捷徑為選項
- [ ] 「開機時自動啟動」選項
- [ ] 中文介面
- [ ] 解除安裝時詢問是否一併刪除 `%APPDATA%\Islet\`
- [ ] 升級安裝時保留使用者設定

## 步驟 3：程式碼簽章

未簽章的程式會觸發 SmartScreen 警告，大幅降低一般使用者的信任感；MSIX 與 sparse package 也需要簽章才能安裝。正式發佈前評估：

- 開源專案的免費簽章計畫（例如 SignPath Foundation）
- Azure Trusted Signing 等雲端簽章服務
- 購買程式碼簽章憑證
- 上架 Microsoft Store（由商店簽章）

在取得簽章之前，README 的安裝步驟需保留「仍要執行」的說明。

## 步驟 4：產生校驗值並發佈

```powershell
Get-FileHash .\dist\* -Algorithm SHA256 |
  ForEach-Object { "$($_.Hash.ToLower())  $(Split-Path $_.Path -Leaf)" } |
  Set-Content .\dist\SHA256SUMS.txt
```

1. 更新 `CHANGELOG.md`
2. 建立 Git tag：`git tag v1.0.0 && git push origin v1.0.0`
3. 在 GitHub 建立 Release，上傳三個檔案，貼上這版的變更摘要

之後可改由 GitHub Actions 在推送 tag 時自動完成步驟 1–4，並評估加入 winget 與 Microsoft Store。

## 發佈前檢查清單

- [ ] 在乾淨的 Windows 10 與 Windows 11（沒裝 .NET）上安裝測試
- [ ] 100%、125%、150% 縮放與多螢幕下版面正確
- [ ] 通知授權流程可以順利完成
- [ ] 解除安裝乾淨，升級後設定仍在
- [ ] `THIRD-PARTY-NOTICES.md` 已更新

---

[← 回到文件目錄](README.md)
