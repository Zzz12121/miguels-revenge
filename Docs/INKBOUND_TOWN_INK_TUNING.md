# 河鎮穿透黑墨與重組調整

## 目前穿透效果（取代下方舊版局部墨絲）

最新多點散墨：居民在頭、胸、腹部、雙手、雙腳共 7 個位置同時散墨，依目前動畫骨架取點。家具依尺寸增加到最多 12 個位置，從實際網格挑出彼此分開的部位，涵蓋桌面、桌腳、櫃子／布架等；小物件維持少量點。每點預設 12 條黑墨絲，整件物件上限 160 條，僅使用黑色墨絲，避免多點疊加後白色點綴蓋過黑墨。

玩家身體穿過居民／可穿透生活物件後，該物件完全隱藏，在它周圍呼叫與 `CalligraphyInkProjectile` 命中時相同的 `CalligraphyInkField.Burst(...,1.1)` 黑墨爆散及 `M_InkFluidRibbon` 材質。沒有呼叫傷害函式，也沒有伴隨的白色 nova／impact／cone 閃光。原本的 Returning ink filaments 不再產生。

黑墨全部自然散去後，物件才開始由下而上、以不規則墨斑聚回；不是透明度直接漸入。居民行走、骨架動畫保持運作，因此在目前的位置及姿勢回復。特效跟隨被穿過的物件，沒有跟在玩家身上。實體樹幹保持可見，只有可穿透枝葉使用此效果。

場景根物件 **Inkbound Town — a preserved afternoon → Inkbound Town Ink Settings**：

| 欄位 | 調整方式 |
|---|---|
| Ink Playback Speed | 散開／流動／聚回的共同速度。保留使用者最新設定 `0.7`；`0.5` 是半速，`1` 是原速。越小越慢。 |
| Pass Through Effect Scale | 黑墨範圍／尺寸倍率，預設 `1`。 |
| Pass Through Point Spacing | 大物件散墨點間距，預設 `0.7`；越小，散布的點越多（最多 12 點）。居民固定 7 點。 |
| Pass Through Ink Per Point | 每點墨絲數量，預設 `12`；增加會更濃密，整件物件上限 160。 |
| Reassemble Seconds | 原速下的聚回時間，預設 `0.65` 秒；實際歷時＝此值 ÷ 場景速度 ÷ 個別速度。 |
| Maximum Pass Through Effects | 同時播放的物件上限，預設 `8`；達上限時保留其他物件可見。 |
| Pass Through Ink Material | 直接引用老者黑墨材質，不要改成白色命中閃光材質。 |

黑墨原始生命週期為約 1.4–3.2 秒，再除以播放速度；`0.162` 時散去約需 9–20 秒，之後聚回約需 4 秒。這不會放慢玩家或居民動畫。要縮短消失時間，增加 Ink Playback Speed；只想加快聚回，降低 Reassemble Seconds。

個別居民／道具的 **Inkbound Echo → Playback Speed** 仍會乘上場景速度。舊版 Return Seconds、Flow Strength、Maximum Displacement、Sample Count 不控制這個新模式。Use Projectile Hit Effect 預設勾選；取消才回到舊版效果。

店鋪桌、椅、茶盤、杯、器皿、布架等以完整物件保存為獨立組，不會因同材質而一起消失。多材質物件等各個接觸部分都離開後才觸發。更新既有店鋪可使用選單 **GeminiGrowUp → Inkbound Town V3 → Update individual passable furnishings**；這只更新家具，不重建街道／居民／地形。完整 Rebuild town 仍使用生成器預設速度 `0.5`；手動調速後應另存場景。

`Docs/Validation/InkboundPassThroughChecks.cs` 僅檢查穿透流程；結果及原生 Unity 畫面保存在 `output/inkbound-town/pass-through-impact/`。舊單點檢查曾暫時使用速度 `1` 以縮短等待，再回復當時的 `0.162`。最新 `MultiPointPreview` 保持使用者的 `0.7`，確認實際墨絲在頭／腹／雙腳起始、大桌六點覆蓋與完整復原，見 `multiple-points.json`、圖片 09／10；主要實玩交給使用者。

## 前次尺寸修正與舊版局部墨絲（歷史記錄）

本輪將居民（含船夫）放大為原先 **1.1 倍**，修正柳樹／闊葉樹以樹幹接地，兩位修網漁夫從錯誤的 Y=-0.65 改到符合蹲姿鞋底的 Y=0.08，讓鞋底落在岸面。

可穿透的生活道具按組與實體建築分開合併。桌、凳、櫃、布、器皿、籃、漁具、棋具、船與槳、盆栽、藤蔓、燈籠、植栽的可穿透部分皆接入局部散墨。牆、地面、橋及樹幹既有實體碰撞保留；水面、天空、遠山不當作生活道具。

## 整座場景一起調

在 Hierarchy 選最上層 **Inkbound Town — a preserved afternoon**，於 Inspector 找 **Inkbound Town Ink Settings**，修改 **Ink Playback Speed**：

| 數值 | 效果 |
|---|---|
| `1` | 原本速度 |
| `0.5` | 目前設定：半速，散開、流動及回流歷時約兩倍 |
| `0.25` | 四倍慢動作 |
| `0.75` | 介於原速與半速之間 |

這只控制散墨的局部時間，不會放慢玩家、居民生活動畫、樹葉風動或戰鬥。Play 中可即時試調；要永久保留，結束 Play 後在 Edit 模式再設定並儲存場景。

## 個別物件微調

居民選角色根物件；道具選其網格子物件，於 **Inkbound Echo** 調整：

- **Playback Speed**：個別速度倍率，預設 `1`。最後速度＝場景速度 × 個別速度。
- **Return Seconds**：預設 `1.3`，越大回流越慢；這是原速下的衰減時間參數，不是精準的倒數秒數。
- **Touch Radius**：接觸擾動範圍，越大影響越寬。
- **Flow Strength**：墨汁捲動強度。
- **Maximum Displacement**：墨汁最多能離開原形多遠。

例如場景設 `0.5`，某個物件的 Playback Speed 設 `0.5`，該物件會以原速的 `0.25` 播放。若只想延長回流而維持散開速度，保留速度倍率，增加該物件的 Return Seconds。

生成器已包含這些變更，重建仍會使用 1.1 倍居民、半速散墨與可穿透道具組；手動調好的場景數值會被完整重建流程的預設值取代。

## 必要確認

`output/inkbound-town/visual-fixes/` 保存修正圖與結果：102 名居民／船夫均為 1.1 倍、304 個可穿透物件組未漏接散墨、匯入樹的木幹最低點為地面高度、修網漁夫蹲姿鞋底誤差約 2mm。相同接觸下，原速回流約 1.69 秒，半速約 3.46 秒；六次 32／176 樣本切換與再次接觸後 Console 無錯誤。主要實玩仍交給使用者。
