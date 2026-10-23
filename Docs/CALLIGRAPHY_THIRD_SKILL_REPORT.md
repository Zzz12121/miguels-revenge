# 遊墨行者：第三次技能修改與操作手冊

日期：2026-10-24。依據「第三次技能.md」，修改既有 Boss，不另建戰鬥系統。
本輪沒有 commit / push、安裝或移除套件，也沒有覆寫原本已修改的 Piloto 材質。

## 逐項結果

| 需求 | 狀態 | 實作及驗證 |
|---|---|---|
| Frontal Pulses 邊長 ×4、朝玩家 | 已完成 | Scale `.45 → 1.8`，資源原生 +X 補償成水平攻擊方向；Play Mode 驗證縮放、朝向。傷害讀取實際波浪粒子網格，逐波判斷，不用放大的固定圓形傷害區。 |
| Frontal 朝落地瞬間玩家 | 已完成 | 筆尖地面撞擊點為起點，落地時計算水平目標方向，釋放後不再追蹤。移動玩家的測試通過。 |
| Smoke ×2、穩定跟隨 | 已完成 | `.35 → .7`。調查發現原資源葉片本身具有約 180 度/秒的旋轉；不是掛在手臂骨骼造成。位置跟隨與旋轉分離，原生旋轉降至 .25 倍。旋轉 Boss 85 度後 Smoke 仍維持固定地面方向。 |
| 移除舊太極，三段爆炸 | 已完成 | 刪掉 `CalligraphyGroundImpact` 的舊 `Yin yang impact` 生成，以及龍的舊獨立圖案；預警改為圓圈。實測只有新 Big Explotion，尾段同時出現 Buff Up / Flash AoE；保留裂地。新資源內必要的陰陽圖案未刪除。 |
| Stab 朝前、人物真正刺擊 | 已完成 | 原資源實際伸展軸是 +Y，已補償至水平前方；修改共用程序姿勢的蓄力、伸筆、身體傾斜與收筆。本體與分身共用。實測筆軸水平、VFX 軸向一致；不是另外烘焙 AnimationClip。 |
| 分身左右距離 ×2.5 | 已完成 | 本地左右 `2.3 → 5.75`，生成時查地面與空間，不安全則尋找同側替代位置或不生成。實測兩側約 ±5.67（生成後已有少量移動）；最多兩個、五次命中／十秒仍保留。極端地形沒有全面走測。 |
| 前 70% 輕追蹤／後 30% 鎖定 | 已完成 | 使用累積路徑長度，有限角速度、禁止掉頭；實測 20 Units 於 14 Units 鎖定，玩家變向後不重新追蹤，20 Units 結束。 |
| 遠程瞄準腹部 | 已完成 | CharacterController 中心往下 20% 半高，避免把根物件誤當腳底再加身高。實測目標低於膠囊中心。 |
| Rune → Fall 無縫與落地傷害 | 已完成 | 等實際有效粒子播完，同一幀建立 Fall；等原 Fall 的地面撞擊環真的生成才判傷害。預警不造成傷害。中央命中設定一秒暈眩。實測兩個時間戳完全相同，撞擊晚於 Fall 開始。 |
| Rune ×3、半徑20、分散 | 已完成 | 預設 `4 → 12`；半徑20、最小間距4、保留60度逃生扇區。起手快照中心，不跟著玩家一路搬移全部符文。實測數量、半徑及間距。過大的數量／間距組合會少生，避免硬塞重疊。 |
| 所有技能主要 Inspector 數值 | 已完成（主要參數） | 九招及死亡分組；傷害、命中間隔、控制、數量、範圍、冷卻、速度、縮放等均可調。實測改 Pulses 縮放、波數、間隔會影響下一次生成。Smoke 額外提供偏移、固定水平角、位置跟隨、世界空間設定。 |
| 每種 VFX 任意偏移／旋轉／空間模式 | 部分完成 | Smoke 已提供；其他攻擊保持經驗證的原生軸補償、筆尖／撞擊點與命中網格綁定，尚未為每種特效開放所有任意轉換欄位。資源內裝飾粒子的個別曲線仍由原 Prefab 控制。 |
| Scene Gizmos | 部分完成 | 已實作選取 Boss 時的紅色攻擊範圍、青色生成區、黃色投射距離、藍色世界盒；播放中的實際波浪包圍盒為橙色。已編譯，但本輪未以 Scene 視窗截圖完成視覺驗收。 |
| 畫中世界 ×1.7 | 已完成 | 舊實際水平192、高108，改為326.4、高183.6；半徑163.2。透明支撐面、邊界、背景與攝影機遠裁切同步，舊1.5只保留原本一次。Play Mode 查驗尺寸。 |
| 天空巨大老者 | 已完成 | 保留頭部各材質與眼睛，裁出頭部有效網格、修正透明排序、降低背景雲對比；大幅放大淡墨臉、緩慢俯視。實際 Game View 截圖已看見臉及眼睛，不是 UI 貼圖。 |
| 外部模型／血條／UI／傷害隔離 | 已完成（現有靶場） | 不只 Renderer：既有 OnGUI 血條與名稱、World Canvas、外部目標判定、雙向傷害、碰撞與鏡頭障礙均加世界邊界；玩家 HUD 保留。地形樹草也隱藏。未對專案所有正式關卡逐一測試。 |
| 正常結束／取消／死亡／重啟恢復 | 已完成 | 正常離開以持續更新的外界畫面交叉淡入，不在半途砍掉；實測正常結束、取消、直接死亡事件、重新開始靶場恢復。修掉取消後立刻重放造成「恢復失敗」的問題。 |
| 地面走動／被動位移 Pulse | 已完成 | 依真實水平位移與接地，不讀 WASD。位於 FeetY 接觸面，移除此效果的大塊煙霧，保留水波。原 Prefab 首波每秒兩粒、零數量 burst，原來太早停播導致未生成；現在明確發出第一圈。Game View 已確認可見。 |
| 靜止／空中不冒水波 | 已完成 | 水平最小速度與位移門檻排除微小修正；停止發射後1.5秒淡去。測試實際位置固定兩秒後為0個，空中被動移動為0個；地面推動有真實水波粒子。輸入放開但角色仍在被推動，仍會正常產生漣漪。 |
| 統一方向／位置／世界狀態 | 已完成 | 腹部、腳底、筆尖、水平目標、資源原生軸分開；世界分 Entering / Contained / Exiting / Finished，UI 與傷害共用隔離判斷。 |

「已完成」以上表列出的實測範圍為準；不代表所有關卡、平台效能或所有參數組合均已驗證。

## Unity 裡如何調整

### 永久儲存設定（建議）

1. 停止 Play。
2. Project 選取 `Assets/_Project/Prefabs/Boss/PF_CalligraphyElder_Battle.prefab`。
3. Inspector 找 `Calligraphy Boss Combat`（C# 類別 `CalligraphyBossCombat`）。
4. 展開下表對應的中文摺疊群組，再展開內層 Settings。
5. 修改 Prefab 欄位；下次生成 Boss 就會套用。

### 靶場即時測試

1. 開啟 `Assets/_Project/Scenes/VFX_VoidRend_Bocchi.unity`，按 Play。
2. F8 打開測試選單，點「遊墨行者 Boss 測試」。
3. Hierarchy 搜尋 `PF_CalligraphyElder_Battle(Clone)`，選根物件，不是展示用老者或筆骨骼。
4. Inspector 找 `Calligraphy Boss Combat`，最下方可點指定技能按鈕；按鈕會中止當前招式。
5. 修改參數再重放技能。已生成的投射物／波浪通常使用生成當下的值，不會全部追溯重建。
6. **Play 裡的修改停止後會還原**。想保留請把確認好的值填回上述 Prefab。
7. Scene 視窗開 Gizmos，選取同一個 Boss；波浪橙框須在波真的生成時觀看。Game View 的 Gizmos 可關閉，正式遊戲不會包含這些線框。

### 各技能欄位對照

| Inspector 群組 | 主要欄位與效果 |
|---|---|
| 共用／戰鬥與冷卻 | `Max Health` 生命；`Move Speed` 移速；`Melee Distance` 近戰接近距離；`Recovery Seconds` 共用收招空檔；`Skill Recovery` Element 0–8 對應下方九招，0代表沿用共用值，越大休息越久。`Cycle Demonstration` 開啟依序展示；關閉用現有距離／冷卻選招。 |
| 0–1 飛墨一筆／連筆四式 | `Projectile Damage / Speed` 傷害／速度；`Ink Projectile → Single Count / Combo Count` 每輪筆數；`Stroke Seconds` 每筆時間；`Maximum Distance` 累積路程；`Homing End Percentage` .7為70%；`Turn Rate × Homing Strength` 限制轉向；`Collision Radius` 命中半徑；`Vfx Scale` 外觀。 |
| 2 近戰／左右墨影 | `Ink Melee → Damage / Hit Radius / Combo Count / Stroke Seconds` 傷害、筆尖掃過半徑、連擊數、單筆時間；`Vfx Scale` 尾流縮放；`Clone Count` 0–2；`Clone Side Distance` 每側距離；`Clone Clearance` 生成所需空間；`Clone Damage / Move Speed / Stroke Seconds / Hits / Lifetime / Fade Seconds` 分身專用設定。 |
| 3 畫中世界 | `Painting Radius` 先前基準半徑96，不是放大後值；`Painting World → Size Multiplier` 新倍率1.7，勿再把基準手動乘一次；`Entry / Contained / Exit Seconds` 進入／停留／消散；`Dragon Count` 畫內龍數；`Pulse Scale / Interval / Fade` 漣漪大小／間隔／淡出；`Face Scale / Opacity` 天空臉尺寸／濃淡。`Painting Dragon Damage / Scale` 龍傷害／體型；`Painting Cooldown` 畫世界再次可用時間。 |
| 4 天降墨龍 | `Giant Dragon Scale / Damage` 龍體型／傷害；`Ink Dragon → Spawn Height / Dive Speed / Impact Radius` 高度／落速／傷害及碎裂半徑；`Release Seconds / Cast Seconds` 放龍時刻／收招時刻，Cast應大於Release加俯衝時間；`Smoke / Explosion / Buff / Flash Scale` 各段大小；`Smoke Offset / Yaw Offset` 地面偏移／固定水平角；`Smoke Follow Position` 位置跟隨；`Smoke World Space` 已生成粒子是否留在世界；`Smoke Spin Multiplier` 原生旋轉倍率。 |
| 5 交叉運筆／墨光 | `Beam Damage` 每次傷害；`Ink Beam → Windup Seconds` 運筆時間；`Hit Interval` 重複命中間隔；`Hit Radius` 光束判定厚度；`Vfx Scale` 光束視覺尺寸。光束有效長度由原資源實際網格取得。 |
| 6 躍筆重砸／Frontal | `Slam Damage / Stun Seconds` 筆尖直接命中傷害／暈眩；`Leap Slam → Cast Seconds / Jump Height / Jump Distance / Direct Hit Radius` 動作時長／跳高／位移上限／筆尖判定；`Wave` 子群組控制落地波的傷害、縮放、波數／間隔、擊退與擊飛。 |
| 7 連環墨浪／Frontal Pulses | `Frontal Pulses → Vfx Scale` 預設1.8，縮放連動實際命中網格；`Wave Count / Wave Interval` 預設6波／1秒；`Damage / Hit Interval` 傷害／同目標命中間隔；`Knockback Speed / Launch Speed` 水平／垂直初速（不是最終高度）；`Control Seconds` 控制時間；`Release Seconds / Cast Seconds` 發射／收招時間。 |
| 8 點墨落印／Rune | `Rune Damage` 每次落印傷害；`Rune Attack → Count / Spawn Radius / Minimum Spacing` 數量／半徑／最小間距；`Stroke Seconds` 每次虛點時間；`Warning Speed / Fall Speed` 播放倍率，越大越快，兩階段仍用生命週期銜接；`Scale` 視覺與判定同步倍率；`Hit Radius / Center Radius` 基準半徑；`Stun Seconds` 中央暈眩。 |
| 死亡／水墨消散 | `Death Presentation → Vfx Scale / Playback Speed / Fade Seconds` 死亡特效大小／播放倍率／角色淡出時間。 |

調大波數、Rune數、世界龍數及透明效果尺寸都會增加開銷；波數限制16，分身限制2，畫內龍限制12。波的裝飾墨滴仍沿用原資源生命週期，不會每改波數就倍增所有裝飾系統。

## 修改來源與主要檔案

全部以下路徑相對專案根目錄。

- 主要技能與參數：`Assets/_Project/Scripts/VFX/CalligraphyBossCombat.cs`、`CalligraphyBossSkills.cs`、`CalligraphyBossSettings.cs`。
- Inspector：`Assets/_Project/Scripts/Editor/CalligraphyBossCombatEditor.cs`。
- 序列、原資源播放、碰撞：`CalligraphySkillSequence.cs`、`CalligraphyPackageEffect.cs`、`CalligraphyPlayerHitGate.cs`、`CalligraphyPlayerControl.cs`。
- 刺擊、分身、投射物、龍：`CalligraphyBrushPresentation.cs`、`CalligraphyMeleeEffects.cs`、`CalligraphyClone.cs`、`CalligraphyInkProjectile.cs`、`CalligraphyInkDragon.cs`、`CalligraphyGroundImpact.cs`。
- 世界：`CalligraphyPaintingWorld.cs`、`CalligraphyWorldIsolation.cs`、`CalligraphyNameplate.cs`、`CalligraphyPracticeTarget.cs`、`VoidRendTargetingSkills.cs`。
- Shader：`Assets/_Project/VFX/Calligraphy/Shaders/CalligraphyFluid.shader`。依 URP HLSL 技能的透明渲染原則調整既有材質呈現、排序與淡墨對比，沒有新加一整套後製。
- Prefab：`Assets/_Project/Prefabs/Boss/PF_CalligraphyElder_Battle.prefab` 儲存新群組及預設值。
- 最小整合：玩家 `PlayerCombat / PlayerHealth / ThirdPersonCamera`；既有 `EnemyHealth / EnemyAI / BossHealth / BossController / AbyssMark / ClockEnemyHealth / ClockTrap / MerchantNPC / SpiderBurrow / SandWormArena / BoatCrewMember / EnemyBoat / HealthPickup / Knockable / VikingBoat` 的外界顯示或傷害入口。沒有接管這些系統的 AI、移動或戰鬥設計。
- 可重跑檢查：`Docs/Validation/CalligraphyThirdChecks.cs`，放在 Assets 外，不隨遊戲編譯。

本輪沒有建立或修改 AnimationClip、AnimatorController、Scene、ScriptableObject 資產；工作區若看到這些差異，是先前任務已存在的修改。這輪使用既有 VFX Library 和 Piloto Prefab，僅對執行期副本調參。

## 驗證紀錄及限制

Unity 6000.6.3f1、目前 Windows/DX11 靶場，透過 unity-pipeline 技能連接現有 Editor 執行。已通過：

最終編譯成功，Console 即時狀態為0錯誤、0警告；工具的歷史累計不是本輪即時錯誤數。`git diff --check` 無空白錯誤，只有既有 Shader 的換行格式提示。

- `Directions`：Pulses 4倍和水平方向、重砸瞬間目標與釋放鎖向。
- `Clones`：左右站位、數量、五擊／十秒設定。
- `Homing`：受限轉向、70%距離鎖向、射程結束、腹部目標。
- `Runes`：12點、20半徑、4間距、同幀接續、實際撞擊粒子。
- `Dragon`：Smoke縮放與固定朝向、舊圖案不存在、三段新效果。
- `Isolation`：世界支撐尺寸、UI規則、外界傷害被拒／畫內傷害可通過、外界Boss與木樁不被擊中、正常淡出和取消恢復。
- `InterruptedWorld`：直接觸發死亡事件，以及重新開始靶場；地形與樹草顯示狀態還原。
- `Ripples`：不使用玩家按鍵的接地推動能生成真實環粒子；固定位置2秒為0；離地移動為0。
- `InspectorAndStab`：調整Scale、Wave Count與Wave Interval，生成結果跟著改；筆軸水平和Stab原生+Y轉向。
- 既有 `DefenceAndWaves` 回歸：格擋不扣血／不觸發控制、空中避開地面波、Frontal強擊飛。
- Game View 實際截圖：`Logs/ThirdSkills-sky.png`、`Logs/ThirdSkills-pulse.png`。為確認臉與腳部，截圖測試暫時調整取景，結束Play後恢復；沒有保存攝影機／場景變更。

注意：

- 擊波判定是粒子網格表面與角色半徑，不是讀 GPU 每個像素的透明度；不會把多個波之間的空白連成整塊傷害區。
- 停止「按鍵」不等於停止「位移」；角色被碰撞修正或外力推動仍會產生水波。驗收的兩秒消失以實際位置停止計算。
- 分身複雜牆角／坡地、正式關卡全部 HUD、Scene Gizmos 視覺、其他平台與大量特效下效能，尚未全面驗證。
- 新增的外界 Screen Space UI 若不掛在傷害對象下，應在顯示入口使用 `CalligraphyWorldIsolation.Allows(ownerTransform)`，不能把全域UI自動認作玩家HUD。
- 原生發射軸固定補償是有意設計，避免使用者只轉外觀造成 Hitbox 不一致；尚未提供每個特效的所有任意旋轉欄位。

重跑例子（先在上述靶場 Play）：

```powershell
unity command run_script --file Docs/Validation/CalligraphyThirdChecks.cs --entry CalligraphyThirdChecks.Directions --timeout_ms 30000 --format json
```

改 `--entry` 最後的方法名可跑其他檢查。測試會生成測試Boss、暫時保護玩家及更改執行期數值，跑完請停止Play；不要把測試期間的場景／物件儲存成正式配置。
