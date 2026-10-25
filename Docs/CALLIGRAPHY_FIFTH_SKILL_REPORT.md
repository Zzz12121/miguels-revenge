# 第五次技能：墨相鎮海・浮墨天階

## 入口與調整位置

- 靶場：`Assets/_Project/Scenes/VFX_VoidRend_Bocchi.unity`。
- 場景中的 `CalligraphyElder` → `CalligraphyBossCombat` → **9 墨相鎮海／浮墨天階（全部參數）** → `Ink Avatar Ultimate`。
- 共用 Prefab：`Assets/_Project/Prefabs/Boss/PF_CalligraphyElder_Battle.prefab`，同一個元件與參數群。
- F8 開始靶場老者測試，↓ 切換至第 10 招（index 9）；↑ 重播。也可在 Play Mode 的 Boss Inspector 直接點選 `9 · 墨相鎮海・浮墨天階`。
- Play Mode 中的參數調整不會永久儲存。場景與共用 Prefab 都已接上新材質與原包 Flaming Pillar；只改場景實例，不會自動覆寫 Prefab 的其他設定。

## 實際流程

1. 本體與法像仰身張臂長嘯。多組原包 `ARPG_White_FlamingPillar` 分批出現，法像同步暈染顯現。
2. 法像使用原老者的蒙皮網格、骨架及原貼圖，保留五官、鬍鬚與衣袍細節；新的透明流墨材質使濃淡與邊緣持續變化。完整模型預設世界高度 **100m**，下半身埋入地面並在材質中裁去，只顯現上半身。不是縮放值 100 導致 250m 高的實心雕像。
3. 巨筆先由老者的右側掃向左側，再反向掃回。預設 60°。筆尖位置與判定使用同一個弧線範圍，兩掃分別只可命中一次；兩次傷害、是否擊退可分開調整。
4. 法像雙手舉筆、向下插地。實際筆尖在地面接觸點觸發衝擊，不在前搖期間預先命中；落點對準 `Slam / Forward Distance`。
5. 插地點向外暈染成墨海。玩家被高速擊飛，原本施放中的玩家技能、牽引與受拋運動會先取消，避免多個移動系統互相搶控制。
6. 預設 **64 塊**不規則、平頂、凸形碰撞的墨汁地塊上升。大落腳點會依擊飛速度與原有重力計算落地位置，而不是完全隨機放置。其餘平台有最小間距，相鄰高度採漸變，避免獨立亂數產生無法跳上的垂直牆。
7. 老者跟隨自己的平台升起，使用原有飛墨投射物持續攻擊。玩家落地後仍使用原有跑、跳、翻滾、技能系統，沒有整段鎖住操作。
8. 墨海內低處不是安全地板：落入會受傷；預設再被震回最近平台，可關閉救援。
9. 持續時間結束，法像、墨海、平台淡出，雙方回到起招位置，靶場進入原有的「等 ↑／↓」待命。切招、停測、停用、死亡／卸載則立即停流程與清理，不等待淡出。

## 參數群

| 群組 | 可調內容 |
|---|---|
| `Roar Seconds` | 長嘯時間；會至少等待法像顯現時間結束 |
| `Pillars` | 原包 Prefab、大小、透明度、每組數量、XYZ 生成範圍、穿插時間差、單次持續時間、每組頻率、隨機／規則分布、跟隨位置、高度、最小／最大距離 |
| `Avatar` | 完整模型世界高度（預設100）、偏移、老者後方距離、高度、透明度、顯現／消散秒數、流動強度、面部清晰度、跟隨朝向、材質 |
| `Sweep` | 角度、基礎半徑、每掃秒數、兩掃間隔、兩次傷害、各自擊退開關、擊退速度、離地接觸高度、尾流寬度、毛筆長度倍率、尾流大小／透明度／壽命 |
| `Slam` | 插地秒數、傷害、傷害半徑、插地前方距離、玩家擊飛高度、最低上升速度、水平推力 |
| `Sea` | 半徑、擴散秒數、空戰持續時間、淡出秒數、透明度、濃度、落海傷害／間隔、救援開關、材質 |
| `Platforms` | 數量、直徑範圍、高度範圍、生成半徑、上升速度、最小邊緣間距、隨機水平旋轉、預留落腳點大小、材質 |
| 六組 `…Shake` | 長嘯、顯現、第一掃、第二掃、插地、上升的各別強度／持續時間／頻率 |
| `Air Combat Projectiles` 等 | 空戰攻擊開關、間隔、傷害、投射物速度、平台與特效的亂數種子 |

`Sweep` 的有效筆尖／判定半徑 = `Radius × Brush Length Multiplier`；改長筆也會同步改判定，不留下傷害與畫面脫節。平台預設高度 7–16m、直徑 7–10m，第一落腳點直徑14m。若把範圍縮小到放不下指定數量，Console 會明確警告實際數量，不能同時要求極小場地、大平台、過大間距和128塊。

## 新增／修改的實作

- 新增 `CalligraphyAvatarSettings.cs`：集中設定與第9招的薄整合層。
- 新增 `CalligraphyInkAvatarUltimate.cs`：階段流程、法像視覺複製、原包粒子編排、扇形判定、插地、墨海、64平台、預測落腳點、空戰與唯一清理入口。
- 新增 `CalligraphyInkPlatform.cs`：可站立的運動平台，只在上升時帶動站在上方的膠囊，不取得玩家輸入控制權。
- 新增 `CalligraphyUltimateCameraShake.cs`：渲染前附加、渲染後還原的震動，不覆寫既有鏡頭角度、距離或 Cinemachine 設定。
- 新增 `CalligraphyAvatar.shader`、`CalligraphyInkDomain.shader` 與3個 `M_InkAvatar…` 材質。
- 修改 `CalligraphyBrushPresentation.cs`：長嘯、兩方向橫掃、撐地姿勢，以及僅法像使用的筆尖目標／雙手握筆約束。原本各招的動作仍保留。
- 修改 `CalligraphyBossCombat.cs`、專用 Inspector：加入第10招與取消入口。
- `CalligraphyPlayerHitGate.cs` 加上可選的回饋特效父物件、普通硬直開關；本招的橫掃使用自己的短暫擊退而不取得動作鎖，切招時可立即清除。`CalligraphyInkProjectile.cs` 加上可選的清理擁有者。舊呼叫保持原行為，新技能的命中特效與投射物尾跡則不會逃出擁有者而殘留。
- `CalligraphyPackageEffect.cs` 加上可選的 `KeepAliveFor`，讓火柱的自訂持續時間不被原包預設尾長截斷；只調整本招生成的粒子實例，原包 Prefab 不變。
- 共用 Battle Prefab 與靶場場景只加入新招參數與參考，沒有重跑舊場景建置器或覆寫現有移動／普通攻擊系統。

## 驗證

可重用檢查碼位於 `Docs/Validation/CalligraphyAvatarChecks.cs`，透過 `unity command run_script` 執行（先進 Play Mode；`AssetAudit` 可在 Edit Mode 執行）。測試會暫時縮短演出時間，不改存檔數值；測完停止 Play Mode 會還原鏡頭與測試參數。

- `AssetAudit`：Prefab 材質／原包參考、100m、64平台、60° 預設、兩個著色器錯誤檢查。
- `Flow`：原五官衣袍貼圖保留、法像沒有碰撞器、實際筆尖右→左／左→右、兩次命中、插地位置、64個不重疊凸碰撞、強制擊飛、成功落在約11.1m高的平台、空戰無輸入鎖、墨海傷害／救援，以及切招後平台／法像／震動清零與原座標恢復。
- `Cancellation`：長嘯、第一掃、插地、空戰、淡出期間切招；自然結束待命；重播；停用老者時清理。
- `DefaultPlayback`：正式存檔預設值的長嘯／原包火柱、64平台、擊飛、插地後持續撐地及雙手握筆檢查，並輸出總覽與臉部近照。
- `InterruptDuringHit`：橫掃擊中後立即切招，確認不殘留操作鎖、硬直或本招拋物線。
- `Presentation`：暫停在空戰並輸出總覽圖供檢查；結束後應停止 Play Mode，不要儲存測試鏡頭。
- 圖片：`Logs/Avatar-roar.png`、`Avatar-sweep-right-left.png`、`Avatar-sweep-left-right.png`、`Avatar-ink-sea-launch.png`、`Avatar-landed-on-platform.png`、`Avatar-overview.png`。

本次在 Unity 6000.6.3f1 / Windows Editor 的實際結果：

| 檢查 | 結果 |
|---|---|
| AssetAudit | PASS |
| Flow（包含筆尖方向／接地位置） | PASS，兩掃各命中一次，64平台，落地高度約11.10m |
| Cancellation | PASS，五個中斷階段、自然待命、重播、停用清理 |
| InterruptDuringHit | PASS，命中後立即切招沒有操作鎖、硬直或拋物線殘留 |
| DefaultPlayback | PASS，實際存檔預設值、持續火柱、100m法像、雙手握筆、巨筆保持插地 |
| 舊 DefenceAndSlamResidue | PASS，翻滾無敵保留、直擊不被範圍重複命中、舊墨漬不造成傷害 |

已實際查看 Game View 的長嘯、兩掃、擊飛、落平台、全場總覽與臉部近照。測試期間的短時間參數及測試鏡頭未存回場景。

最後一次編譯完成、靶場存檔乾淨、Console 當前錯誤為0。仍有11個既有警告，來源是 Viking／船隻與舊改鍵程式的過時 API 或多餘 `new` 關鍵字；本次沒有擴大修改這些系統。

## 明確限制

- 動作是程序化骨骼姿勢、雙手約束與原模型的組合，沒有新製作專門的 FBX／AnimationClip，也沒有新增長嘯配音。
- 墨汁是透明度、紋理細節、噪聲流動、暈染邊緣與粒子合成，不是昂貴的真實三維流體模擬。
- 平台可行走／跳躍，保留平頂；不做任意傾斜，避免玩家無法站穩。玩家初次擊飛沿原控制器的拋物線，到平台落地後恢復自由操作；沒有擅自重寫隊友的空中操控。
- 極端參數（例如極高水平推力、非常慢的平台上升速度、非常短海面持續時間）仍需自行搭配。預設參數與驗收參數有實際測試。
- 本次只修改專案；未 commit／push，也沒有更新先前 Windows／Ubuntu 的試玩壓縮包。
