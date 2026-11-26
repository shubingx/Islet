# 通知用的 App 圖標

Islet 會先找這個資料夾裡的圖檔，找不到才用電腦上已安裝 App 的圖標，再找不到才顯示字母。

- 檔名 = App 名稱的小寫英數字，副檔名 `.png`。目前認得的檔名：

  | App | 檔名 | 不放檔案時 |
  | --- | --- | --- |
  | Instagram（網頁） | `instagram.png` | 字母 |
  | Facebook（網頁） | `facebook.png` | 字母 |
  | X（網頁） | `x.png` | 字母 |
  | LINE（桌面版） | `line.png` | 用已安裝的 LINE 圖標 |
  | Telegram（桌面版或網頁版） | `telegram.png` | 桌面版用已安裝的圖標；網頁版顯示字母 |
  | Discord（桌面版） | `discord.png` | 用已安裝的 Discord 圖標 |

  「Telegram Desktop」這類多了字的名稱也會對到 `telegram.png`。
- 建議 128×128 以上、透明背景的正方形 PNG。
- 網頁版 IG／FB／X 的通知是 Chrome 發的，電腦上沒有對應的 App 圖標，一定要放檔案才會顯示。
- Discord、LINE 等桌面 App 不放檔案也會顯示它們自己的圖標；放了檔案則以檔案為準。

這些標誌是各公司的商標，不屬於本專案的 MIT 授權，請使用官方品牌資源提供的檔案。
