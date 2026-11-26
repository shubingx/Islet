# 自訂腳本（規劃中）

你可以用 JavaScript 替島加功能，例如顯示天氣卡片、每小時提醒喝水、換歌時自動記錄歌單。

腳本放在 `%APPDATA%\Islet\extensions\<腳本 ID>\`，每個腳本包含：

```text
manifest.json   腳本資訊與權限宣告
main.js         腳本本體
```

`manifest.json` 範例：

```json
{
  "id": "com.example.hello-card",
  "name": "Hello Card",
  "version": "1.0.0",
  "main": "main.js",
  "permissions": ["network", "notifications.write"],
  "triggers": ["interval:15m", "event:music.changed"]
}
```

- **permissions**：腳本需要的能力，第一次載入時會列給你確認；沒列出的能力一律拒絕。
- **triggers**：腳本何時被喚醒；沒有觸發時不佔資源。

腳本能做的事：在島上新增卡片、訂閱事件（換歌、通知到達、模式切換、定時器）、發出島上通知、開啟連結或程式、儲存自己的設定。腳本在沙盒內執行，超過時間限制會被中止。

> [!CAUTION]
> 官方不審核社群腳本。只安裝你信任來源的腳本，並仔細看清楚它要求的權限。

---

[← 回到文件目錄](README.md)
