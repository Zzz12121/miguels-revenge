# 遊墨行者第四輪修正交付

日期：2026-10-24。驗證環境：Unity 6000.6.3f1 / Windows Editor / DX11。

## 同日後續調整：前搖中段墨光與墨浪左轉

- 墨光特效從揮筆前搖的一半開始播放（預設第 0.9 秒），第二筆 X 形揮動不被指向姿勢中斷。特效自身的蓄能／光束播放仍保留，傷害仍依實際光束網格而非蓄能粒子判斷。
- 躍筆重砸 Frontal、連環墨浪 Pulses 都在上一版方向上再向左轉 90°；不改重砸跳躍目的地。新增 `Wave Settings → Yaw Offset`，預設 `-90`。傷害沿旋轉後實際粒子網格，擊退也同步轉向。
- 調整路徑：同一 Boss Prefab / Calligraphy Boss Combat → `6 躍筆重砸／Frontal → Leap Slam → Wave → Yaw Offset`；或 `7 連環墨浪／Frontal Pulses → Frontal Pulses → Yaw Offset`。墨光前搖總長仍在 `5 交叉運筆／墨光 → Ink Beam → Windup Seconds`。
- `MidWindupAndLeftWaves` 實測：特效起播約 0.903 秒（目標 0.9），兩種波相對舊朝向都左轉 90°，後續玩家移動不改向，Game View 已檢視。前輪下方「蓄力後命中」測試的時間門檻已隨前移調整；蓄能階段不會因這次提早播放而直接造成傷害。
- 編譯通過。Game View 截圖：本機 `Logs/FourthSkills-mid-windup-beam.png`、`left-wave-6.png`、`left-wave-7.png`（後兩張同樣帶 `FourthSkills-` 前綴）。

## 需求完成情況

| 需求 | 結果 | 實際修改與驗證 |
|---|---|---|
| 1 Rune 銜接與緩速 | 已完成 | `CalligraphySkillSequence.cs`、`CalligraphyPackageEffect.cs` 改按原資源符印結束時間接續 Fall，移除 Fall 前置聚氣延遲。撞擊才扣血；殘留原資源墨紋提供 0.6 倍、2.5 秒緩速。`ThirdPersonController.cs` 僅新增可移除速度租約，不改基礎跑速。 |
| 2 躍筆重砸 | 已完成 | `CalligraphyBossSkills.cs`、`CalligraphyBrushPresentation.cs`、`CalligraphyBossCombat.cs` 鎖定起手方向，筆尖實際貼地後沿鎖定水平前向播放 Frontal。直接命中改擊飛，波及同樣有擊飛，不落地後回頭追玩家。 |
| 3 一般投射物 | 已完成 | `CalligraphyInkProjectile.cs`、`CalligraphyBossSettings.cs`、Boss Prefab：追蹤比例 0.9、強度 0.9、轉向上限 65°/秒；以累積路程切換末段固定方向，仍瞄準腹部。 |
| 4 畫中世界 | 已完成；美術仍受原模型限制 | `CalligraphyPaintingWorld.cs`、`CalligraphyFluid.shader`：不再擴大邊界，60 條墨流重新分散，竹、松、墨斑、竹筏移向中遠景，新增 9 組內部水紋／蘆葦／石景與少量白鷺。眼睛直接在原頭部表面描繪眼皮、瞳孔，不放浮空眼球。 |
| 5 墨龍爆炸 | 已完成 | `CalligraphySkillSequence.cs`、`CalligraphyPackageEffect.cs`、`CalligraphyGroundImpact.cs`：原 Big Explotion 撞地就爆發，尾聲同步 Buff up / Flash AoE。碎裂保留並縮小、稍延後；不恢復舊獨立太極。已實測完整落龍及特效自然清理。 |
| 6 交叉運筆墨光 | 已完成 | `CalligraphyBossSkills.cs`、`CalligraphySkillSequence.cs`：平地鎖定施放點高度，獨立於腹部投射物瞄準。依實際 Beam 粒子網格長度判定，最大射程同時限制視覺與傷害。 |

共用參數在 `CalligraphyBossSettings.cs`，分類面板在 `Scripts/Editor/CalligraphyBossCombatEditor.cs`，永久預設儲存在 `Prefabs/Boss/PF_CalligraphyElder_Battle.prefab`；以上路徑均相對 `Assets/_Project/`。這輪不另造替代特效、不新增套件或測試 Scene。前輪尚未提交的技能模組、玩家動畫與畫中隔離必要整合一併交付，詳細前輪結果見 `CALLIGRAPHY_THIRD_SKILL_REPORT.md`。

## 找到的時序根因

- Rune 含很長的空粒子系統生命期；用整組 IsAlive 判斷會等到看不見的部分也結束。現在依最後符印 `yy_2_symbol_alpha_soft` 的 delay + lifetime：原時間 2.9 秒 / Warning Speed 4 = 0.725 秒。
- Rune Fall 自己還含前置聚氣。現在把原時間 0.75 秒重新對齊播放起點，而不是先模擬並跳過可見畫面。原撞擊環 1.95 秒因此在開始 Fall 後約 1.2 秒才實際撞擊；預警沒有傷害。
- Big Explotion 的主要 Burst 原在第 5 秒；先模擬跳過 5 秒會錯過關鍵爆發。現在重新排各子粒子的 delay，讓主要爆發從撞地當刻開始，保留其後壽命。
- 清理時間也改依有 Renderer／Emission 的實際 burst 與 lifetime 計算，避免空系統的 duration 使尾聲或清理拖延。

## 靶場與 Inspector 具體操作

1. 開啟 `Assets/_Project/Scenes/VFX_VoidRend_Bocchi.unity`。
2. Play，按 F8，右上按「遊墨行者 Boss 測試」。
3. Hierarchy 選 `PF_CalligraphyElder_Battle(Clone)`，Inspector 找 **Calligraphy Boss Combat**。
4. 面板底部「測試指定技能（中止目前技能）」按 8 測 Rune、6 測重砸、0／1 測追蹤、3 測畫中世界、4 測天降墨龍、5 測墨光。
5. Play 中改值只供暫時比較。要永久保存：停止 Play，在 Project 選 `Assets/_Project/Prefabs/Boss/PF_CalligraphyElder_Battle.prefab` 根物件的同名 Component 修改。

| 分類 | 展開設定與重要欄位（英文名稱由 Unity 顯示為空格分詞） |
|---|---|
| 0–1 飛墨一筆／連筆四式 | `Projectile Speed`；`Ink Projectile` → `Homing Strength`、`Turn Rate`、`Homing End Percentage`（0.9）、`Maximum Distance` |
| 8 點墨落印／Rune | `Rune Attack` → `Warning Speed`、`Warning Timeline End`（-1 自動）、`Fall Timeline Start`（0.75）、`Fall Speed`、`Hit Radius`、`Residue Radius`（2.2）、`Residue Lifetime`（6）、`Slow Multiplier`（0.6）、`Slow Seconds`（2.5） |
| 6 躍筆重砸／Frontal | `Leap Slam` → `Cast Seconds`、`Jump Height`、`Jump Distance`、`Direct Hit Radius`、`Forward Yaw Offset`；`Wave` → `Vfx Scale`、`Control Seconds`、`Knockback Speed`、`Launch Speed` |
| 3 畫中世界 | `Painting World` → `Stream Spread`、`Scenery Spread`、`Scenery Groups`（竹叢組數）、`Face Scale`、`Face Opacity`、`Face Offset`、`Eye Visibility`、`Eye Center`。本輪保持邊界尺寸不變；Stream Spread 改位置而不是加黑線。 |
| 4 天降墨龍 | `Ink Dragon` → `Explosion Scale/Offset/Euler`、`Buff Scale/Offset/Euler`、`Flash Scale/Offset/Euler`、`Explosion Timeline Start`（5）、`Tail Start Seconds`（3.6）；`Debris Count/Scale/Lift/Delay` |
| 5 交叉運筆／墨光 | `Ink Beam` → `Horizontal On Level Ground`、`Height Tolerance`（2）、`Emitter Offset`、`Vfx Euler`、`Maximum Range`（35）、`Hit Radius`（0.65）、`Windup Seconds` |

Rune 撞擊隨真實撞擊粒子出現觸發；需要調整落下時間可改 Fall Speed，不另設與視覺脫節的硬編碼傷害時間。重砸起始方向由玩家當時水平位置決定，之後鎖定；Forward Yaw Offset 是資源朝向微調。

## 使用的既有資源

以下都位於 `Assets/Piloto Studio/Super Realistic FX Bundle 02/Realistic Ying Yang/`，由 `Assets/_Project/Resources/CalligraphyVfxLibrary.asset` 引用：

- `Ying Yang Rune.prefab`
- `Ying Yang Rune Fall.prefab`
- `Ying Yang Frontal.prefab`
- `Ying Yang Big Explotion.prefab`（資源原名就是 Explotion）
- `Ying Yang Buff up.prefab`
- `Ying Yang Flash AoE.prefab`
- `Ying Yang Beam.prefab`

時序修正套在執行時實例，不覆寫供應商的原 Prefab。

## Play Mode / Game View 實際檢查

可重跑程式在 `Docs/Validation/CalligraphyFourthChecks.cs`，由已連線 Unity Pipeline 執行，例如：

```powershell
unity command run_script --file Docs/Validation/CalligraphyFourthChecks.cs --entry CalligraphyFourthChecks.RuneAndExplosion --timeout_ms 30000 --format json
```

先開上述 Scene 並進 Play；各方法會啟動靶場，暫時移動玩家／鏡頭、隔離技能並修改部分測試參數。請在獨立測試時執行，結束停止 Play 還原；`World` 會把 Time.timeScale 設為 0 方便觀看。`SaveDefaults` 是編輯模式保存 Prefab 預設的方法，不是唯讀測試。

| 檢查 | 實測結果 |
|---|---|
| RuneAndExplosion | PASS：Fall 至撞擊約 1.203 秒；緩速 0.6、2.5 秒、重入與取消清理；主爆炸可見粒子 341，兩個尾聲皆有粒子。 |
| DamageTiming | PASS：Rune 預警不扣血，撞擊扣 18；殘留區不追加爆炸傷害。 |
| SlamBeamHoming | PASS：筆尖撞擊 Y 約 0.04；平地 Beam direction.y = 0、發射高度約 1.58；投射物半秒轉約 29°，18/20 路程後不再轉向。 |
| SlamDirect | PASS：直接命中扣 24 並 StrongLaunch，觀察擊飛高度約 0.96m。測試將玩家放到實際筆尖接觸點並按 LateUpdate 順序驗證。 |
| BeamDamage | PASS：蓄力後實際波束抵達才傷害，射程外玩家不受傷。 |
| RollSafety | PASS：啟動真實翻滾狀態，沒有傷害、擊飛或緩速租約。 |
| FullDragon | PASS：完整施法／落龍／撞擊；主爆炸可見粒子 356、碎裂保留、無舊獨立太極，雙尾聲出現並自然清理。 |
| World | Game View 檢查墨流與場景布局、天空臉、近景濕地；60 條墨流與 9 組濕地。 |
| 既有防禦與墨浪回歸 | PASS：盾擋沒有傷害／控制／命中特效；空中越過地面波，地面命中仍擊飛約 0.96m。 |
| ThirdChecks.Isolation | PASS：畫中空間尺寸、支撐、外界 UI／傷害隔離，正常退出與中止恢復。 |

測試過程曾修正兩個測試定位問題：直接重砸原本放在預測落點而非實際筆尖；遠距 Beam 原本把玩家放到靶場地板外，墜落傷害混入結果。調整測試條件後重跑通過，並非把錯誤測試當成功。

Game View 截圖儲存在本機 `Logs/FourthSkills-*.png`（rune-warning/fall/impact、explosion-main/tail、slam-impact、beam、dragon-impact/tail、sky、landscape、wetland），已實際目視，不僅檢查 Instantiate。Logs 不加入 Git。

最後停止 Play 後：編譯未失敗，Unity Console 當前 Errors = 0、Warnings = 0。Pipeline 歷史累計包含先前測試／Unity AI 訊息，不代表當前 Console。未進行獨立 Player Build 或行動裝置效能測試。

## 限制與交付範圍

- 天空老者仍使用原有低多邊形模型，臉部輪廓沒有重新雕刻；眼睛是附著在頭部表面的水墨描繪，是否符合理想神情仍可透過 Inspector 微調。
- 白鷺是少量靜態水墨剪影，不是有骨骼的飛鳥動畫；景物無碰撞、不互動、不產生敵人血條。
- 依 URP HLSL skill 保留常數緩衝結構與既有透明流程，沒有為眼睛加入額外全螢幕後處理；大量透明水墨仍需要目標平台測量 overdraw。
- 本機原先已有供應商材質／Project Settings 變更及 Recovery 備份，本次不提交也不還原。畫面驗證使用本機現有 URP 匯入狀態；未宣稱在全新乾淨 clone 上完成圖形驗證。
- 未改套件清單。隊友系統只保留技能整合所需的隔離入口及速度租約，不重寫 Boss AI、移動或關卡流程。
- 使用者在本次訊息明確要求完成後 push，故覆蓋附件中「未另要求不 push」的一般規則。
