# 參與貢獻

非常歡迎任何形式的參與：回報問題、提出點子、改善文件、寫範例腳本、送 PR 都好！

1. **回報問題或提建議**：到 [Issues](https://github.com/shubingx/Islet/issues) 開一則新的，附上 Windows 版本、重現步驟與截圖。
2. **想改程式**：
   1. Fork 本專案並建立分支：`git checkout -b feature/你的功能`
   2. 修改並確認 `dotnet build` 沒有錯誤
   3. 提交時寫清楚做了什麼：`git commit -m "新增：待辦到期提醒"`
   4. 推送後開 Pull Request，說明改了什麼、為什麼
3. **重大設計變更**（例如新模組、改架構）請先開 Issue 或 Discussion 討論，避免做完才發現方向不同。
4. 新手可以從標記 `good first issue` 的 Issue 開始。

**協作規則**

- 正式版發佈前，維護者直接在 `main` 提交；發佈後改為功能在分支開發、透過 PR 合併，`main` 隨時保持可發佈狀態。
- PR 需通過 CI（GitHub Actions 在 Windows 上自動建置與測試）才能合併。
- 程式風格遵循 `.editorconfig` 與 .NET 慣例；Windows 專屬 API 一律放在轉接層。
- 參考其他專案時只參考設計，**不要直接複製程式碼**，以免授權衝突（例如部分同類專案採 GPL-3.0，與本專案授權不相容）。

開發環境的設定方式請見[開發指南](development.md)。

---

[← 回到文件目錄](README.md)
