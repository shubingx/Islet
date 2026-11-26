# 通知用的 App 圖示

Islet 已經附上常見社群 App 的圖示，放在這個資料夾裡，build 時會編譯進 `Islet.exe`，所以打包出來只有一個執行檔也能顯示圖示，不必另外準備。

顯示通知時，Islet 依序找圖示：

1. `Islet.exe` 旁邊 `Assets\AppIcons\` 裡的同名圖檔（可以不重新 build 就換掉內建圖示）
2. 編譯進 `Islet.exe` 的內建圖示
3. 電腦上已安裝 App 的圖示
4. 都找不到才顯示字母

## 已附的圖示

| App | 檔名 | 說明 |
| --- | --- | --- |
| Instagram（網頁） | `instagram.png` | 網頁版通知由 Chrome 發出，電腦上沒有對應的 App 圖示，靠這張顯示 |
| Facebook（網頁） | `facebook.png` | 同上 |
| X（網頁） | `x.png` | 同上 |
| LINE（桌面版） | `line.png` | 優先於電腦上已安裝的 LINE 圖示 |
| Telegram（桌面版或網頁版） | `telegram.png` | 優先於已安裝的圖示；網頁版也靠這張顯示 |
| Discord（桌面版） | `discord.png` | 優先於電腦上已安裝的 Discord 圖示 |

「Telegram Desktop」這類多了字的名稱也會對到 `telegram.png`。

## 圖示規格

為了在深色的島上看起來一致，所有圖示都統一處理成：

- 256×256 的 PNG，透明背景
- 裁成圓形，跟沒有圖示時顯示的圓形字母同一個形狀
- 中間的標誌縮到約 75%，周圍留品牌底色

## 新增或替換圖示

- 檔名用 App 名稱的小寫英數字，副檔名 `.png`，例如 `whatsapp.png`、`messenger.png`。
- 請照上面的規格處理，放進這個資料夾後重新 build，圖示才會編譯進 `Islet.exe`。
- 只想在自己電腦上換圖、不重新 build：在 `Islet.exe` 旁邊建立 `Assets\AppIcons\`，放入同名的 PNG 即可，會優先使用。

## 商標

這些標誌是各公司的商標，屬於各自的擁有者，不在本專案的 MIT 授權範圍內。Islet 只在通知旁邊用它們標示訊息來自哪個 App，與這些公司沒有合作或關聯。
