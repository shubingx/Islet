# 授權與開源規範

## 授權

本專案以 [MIT 授權](../LICENSE) 釋出，© shubingx。範例腳本與腳本 API 文件同樣採 MIT，讓社群可以自由改寫。

> 例外：專案附帶的 `Assets/AppIcons/` 社群 App 標誌（Instagram、Facebook、X、LINE、Telegram、Discord）屬於各公司的商標，不在 MIT 授權範圍內，僅用來在通知旁標示來源 App。

貢獻者提交的程式碼，將以與本專案相同的授權釋出。

## 第三方套件

- 所有相依套件與其授權列在 [`THIRD-PARTY-NOTICES.md`](../THIRD-PARTY-NOTICES.md)（待補）。
- 新增相依套件前需確認授權相容；**不得引入 GPL 類授權的套件**，否則整個專案必須跟著改用 GPL。

## 商標與名稱

- 「Dynamic Island／動態島」是 Apple 的產品名稱，本專案與 Apple 無任何關係。
- Spotify、YouTube、LINE、Discord、Instagram、Facebook、X、Telegram 等名稱與商標屬於各自的擁有者，本專案僅透過 Windows 公開 API 讀取它們的播放狀態與系統通知，未與這些公司合作，也不使用非官方方式存取其服務。
- 專案附帶這些 App 的標誌（`Assets/AppIcons/`），只為了在通知旁標示訊息來源；為了配合介面，圖示經過裁成圓形與縮放處理。

## 行為準則

參與本專案的每個人都應互相尊重、友善溝通。詳見 [`CODE_OF_CONDUCT.md`](../CODE_OF_CONDUCT.md)（待補，預計採用 Contributor Covenant）。

## 安全性問題

發現安全漏洞（特別是腳本沙盒、通知資料外洩相關）**請不要公開開 Issue**，請依 [`SECURITY.md`](../SECURITY.md)（待補）的方式私下回報，或使用 GitHub 的 [Private vulnerability reporting](https://github.com/shubingx/Islet/security/advisories/new)。

## 公開前倉庫檔案清單

- [x] `README.md`
- [x] `LICENSE`（MIT）
- [ ] `CONTRIBUTING.md`
- [ ] `CODE_OF_CONDUCT.md`
- [ ] `SECURITY.md`
- [ ] `CHANGELOG.md`
- [ ] `THIRD-PARTY-NOTICES.md`
- [ ] `.github/`：Issue 與 PR 範本、CI 設定

---

[← 回到文件目錄](README.md)
