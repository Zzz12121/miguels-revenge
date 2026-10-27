# 遊墨行者：戰鬥與水墨靶場版本

## 啟動

1. 開啟 `Assets/_Project/Scenes/VFX_VoidRend_Bocchi.unity`，按 Play。
2. 按 **F8** 顯示游標，點右上角 **遊墨行者 Boss 測試**。按鈕可重置同一位 Boss，不會一直複製。
3. 方向鍵 **↓** 切下一招，長按不連跳。若玩家自己的技能仍握有目標，等該技能結束再切，避免刪除被控制的分身。
4. 測試順序：飛墨一筆 → 四段連筆 → 近戰與兩個墨影 → 畫中世界 → 天降墨龍。
5. F8 再開游標可按「停止測試」。玩家原本控制、血量 UI 和碼頭老者維持既有系統。

測試期間玩家保留傷害、盾擋、翻滾、僵直，生命最低 1；最後一次實際傷害後五秒回血。Boss 在靶場亦五秒無傷回滿。退出後移除不死保護。

## 組件與資產

| 功能 | 主要檔案（Assets/_Project 下） |
|---|---|
| Boss 狀態、移動、攻擊排程、被動閃避 | Scripts/VFX/CalligraphyBossCombat.cs |
| 技能施放與命中識別 | Scripts/VFX/CalligraphyAttackContext.cs |
| 雙手握筆、橫掃／上挑／下劈與重心 | Scripts/VFX/CalligraphyBrushPresentation.cs |
| 有上限的回捲墨絲／舊版墨霧池與三層筆觸尾流 | Scripts/VFX/CalligraphyInkField.cs、CalligraphyInkMist.cs、CalligraphyBrushWake.cs |
| 筆觸彈道與掃掠碰撞 | Scripts/VFX/CalligraphyInkProjectile.cs |
| 獨立受傷的閃避墨影／戰鬥墨影 | Scripts/VFX/CalligraphyClone.cs |
| 身體路徑延遲、頭角、鬚、下顎、四爪、逐節溶解 | Scripts/VFX/CalligraphyInkDragon.cs |
| 畫紙折疊、山水層、天空臉、懸空墨絲、腳步暈染 | Scripts/VFX/CalligraphyPaintingWorld.cs |
| 僅玩家入畫，外部物件隱藏及碰撞復原 | Scripts/VFX/CalligraphyWorldIsolation.cs |
| 放射裂紋與飛石 | Scripts/VFX/CalligraphyGroundImpact.cs |
| 沿用玩家防禦，限制一般僵直頻率 | Scripts/VFX/CalligraphyPlayerHitGate.cs |
| 靶場按鈕與保護 | Scripts/VFX/CalligraphyBossRangeTest.cs |
| 安裝／檢查工具 | Scripts/Editor/CalligraphyBossInstaller.cs、CalligraphyBossValidation.cs |
| 戰鬥／墨影 Prefab | Prefabs/Boss/PF_CalligraphyElder_Battle.prefab、PF_CalligraphyElder_InkClone.prefab |
| 墨絲、雕塑、紙張、太極、山體、懸浮墨流、天空臉材質 | VFX/Calligraphy/Materials/M_InkFluidRibbon.mat 等七個新增材質；另外重用舊版墨霧與墨影材質 |
| 流動侵蝕與表面水墨轉場 | VFX/Calligraphy/Shaders/CalligraphyFluid.shader、CalligraphyEnvironment.shader |

沒有安裝套件，使用 Unity 6 / URP 17.6、既有角色與動畫。碼頭展示 Prefab、原本衣服權重與毛筆模型未重建。

## 規則與整合

- 玩家技能 2–4 和滾輪在實際施放／命中路徑提供 cast ID，Boss 每次施放最多抽一次 1/3 閃避。玩家技能 1、一般普攻不抽。已閃避的該次技能後續命中也不能扣真身血。
- Boss 被挑飛／滾輪接管時，停止自己的移動與出招；先取消畫中世界等附屬演出。落地後由原控制流程恢復。
- 戰鬥墨影五次有效命中或十秒開始溶解，溶解過程不再造成傷害；閃避墨影只需一擊，且不攻擊。技能傷害使用 cast + hit ID；原普攻沿用已命中集合。
- 玩家傷害仍經 `PlayerHealth.TakeDamage`；完全盾擋與翻滾不發出 `OnDamaged`，也不重設五秒回血。新增的 `applyHitStun` 預設 true，不改其他敵人的呼叫結果。
- 筆觸／天降墨龍單次結算；畫中追蹤龍可再次掠過攻擊，同一條龍兩次傷害至少間隔 2 秒。碰撞畫面與傷害結果分離，盾擋亦能看到墨汁散開。
- 畫中世界：4 秒進場、**20 秒完整狀態**、2.5 秒退場。六條龍持續追蹤玩家最新位置；命中後先掠過，再轉回。粗度參數 1.1 → 2.6（約 2.36 倍），身長獨立，不跟著拉成過長直管。
- 分身重用原展示版的 `CalligraphyEcho` 和 `CalligraphyInk` 材質、池化墨霧／消散殘影，仍保留戰鬥動作與五次命中規則。老者和分身各有三層筆尖尾流。
- 玩家留在原位置的碰撞地面上；原場景 Renderer 暫時不顯示。原支撐地面若是 MeshRenderer，複製僅渲染的網格到下方 9 公尺，畫紙底面位於下方 10 公尺；不移動原碰撞器。非網格地面以底部畫紙承接視覺，不複製 Terrain。
- 外面老者、靶子及新生成的 Renderer／世界空間血條隱藏；外部可拖動單位與玩家的碰撞暫時忽略。沒有刪除外部單位，也沒有關閉其整個 GameObject。既有分身暫停追擊，筆觸彈不穿入畫內；其他敵人自訂的無碰撞直接傷害系統仍需由隊友接入空間邊界規則。
- 畫中世界邊長由 64 → 128 公尺，高度由 36 → 72 公尺；底面仍在玩家下方 10 公尺，保留懸空的觀看感。20 組懸浮墨流（原 28 組的約 71%）和 24 個支撐面墨斑；靠近擾動、踩踏擴散與池化墨霧保持原有規則。
- 新增兩艘竹筏、三叢竹子，全部是下層景觀的渲染物件，沒有 Collider 或互動元件。天空巨臉採老者實際頭部、原膚色貼圖轉灰階水墨、42 倍縮放與緩慢透明度起伏；修正骨架擷取時重複縮放造成裁錯頭部的問題。
- 正常退場的 2.5 秒內，以同位置／角度的暫時相機即時繪製外界，再用不擋輸入的全螢幕畫面漸顯。HUD 保留在上層；確定外界完整顯示一幀後才清理畫中物件及恢復外部 Renderer。沒有傳送玩家或移動外界單位；暫時相機與 RenderTexture 只在退場時存在並於清理釋放。
- 天降龍截面與頭爪寬度再加粗兩倍，飛行路徑／長度不變：3.25 秒開始下降、約 4.21 秒撞地。太極圖延至最後一段揮筆結束（第 4 秒）才淡入，不延後龍的下降。
- 下筆發動下降時才鎖定玩家落點（`Dive()`），而非起手時；鎖定後不再追蹤，撞地事件與太極使用同一落點。`CalligraphyInkField.yinYangPrefab` 已接上 Piloto `Ying Yang Rune` 原始粒子 Prefab，第 4 秒播放；缺少引用時才使用舊程序太極備援。
- 畫中世界禁止開始滾輪選取，顯示「畫中之物，不可鎖定」；進場取消正在選取的技能。未關掉整個玩家控制器。
- 取消、切招、停測、Disable、換場時清理世界、龍、分身與攻擊；還原暫存的 Renderer 顯示、世界空間 Canvas、Terrain 與忽略碰撞狀態，不替換原環境材質。畫內技能選取不會鎖到外部隱藏靶子。
- `onDragonImpact(Vector3)` 是未來接浮空石塊的事件。現在裂紋／石塊是視覺幾何，沒有修改正式地形碰撞。

## Inspector 調整

- Battle Prefab 的 `CalligraphyBossCombat`：傷害、移速、近戰距離、射速、收招間隔、閃避距離／機率、畫中世界半徑、擾動範圍與強度、巨龍大小與傷害、`paintingDragonScale` 畫中龍粗度。
- `cycleDemonstration=true`：靶場依序輪替。false：一般距離選招（近距離筆戰，遠距離單筆／連筆），大招依 `paintingCooldown`／`dragonCooldown` 冷卻使用；也可由隊友的 Boss 排程呼叫 `SelectSkill(3/4)`。
- `CalligraphyInkField`：容量預設 96，上限 160；回捲強度、擴散、墨絲壽命、白墨比例。數量滿時回收最舊墨絲，不無限生成。
- `CalligraphyBrushPresentation`：握距、掌向、握持權重。程序化動作疊在既有動畫上；沒有輸出新的動作捕捉 Clip。
- `CalligraphyPlayerHitGate.minimumStunInterval`：Boss 普通受擊僵直最短間隔 0.35 秒。已受控制時不疊普通僵直。
- 正式使用可在 Battle Prefab 上呼叫 `StartBattle(player)`，HP 歸零停止戰鬥；靶場呼叫 `StartBattle(player, true)` 啟用 Boss 不死。正式關卡的門、獎勵、任務與存檔不在此元件內。

## 驗證與目前品質界線

已在 Unity Play Mode 驗證：技能 1 排除、可閃避技能與同 cast 不重抽、五次獨立命中、玩家致命傷最低 1 HP、翻滾／盾擋不扣血、一般命中僵直、五秒回血、六龍生成、完整 20 秒畫中狀態、自然退出解除限制、手動切招清理、巨龍撞擊事件只呼叫一次。另檢查過靶場、畫中世界與巨龍近景截圖。

Editor 選單 `VFX > Calligraphy > Validate Battle Assets` 可檢查引用與 Shader 編譯；Play Mode 的 `Validate Battle Rules` 可重跑機制檢查（會取消當下 Boss 招式）。

2026-10-23 視覺修訂回歸：六條龍粗度 2.6（原 1.1）、玩家橫移 5 公尺後每條龍追蹤點更新、玩家保持可見／老者與新生測試物件隱藏、切招還原顯示與滾輪、自然完整退場、天降龍單次撞擊、原分身墨影材質、原五次命中規則均通過。Unity 即時 Console 最後檢查為 0 error／0 warning（歷史緩衝中仍保留早先已修的錯誤，未清除歷史記錄）。

**這版是可操作、可逐招測試的程序化版本，不等同附圖的最終美術品質。**

本次擴景回歸：128 公尺平面邊長、20 組墨流、2 竹筏／3 竹叢無碰撞、巨臉網格與材質、完整 20 秒停留後交叉漸顯外界、最後還原 Renderer 與滾輪限制均通過。Game View 退場中段與末段已截圖檢查；巨龍於 3.25 秒開始下降，4 秒前太極不可見、4 秒後可見，實測撞地約 4.20 秒。資產／Shader 檢查與原分身五次命中規則回歸通過。

- 墨汁是回捲曲線＋侵蝕 Shader 的流體近似，不是體積流體／CFD。極近距離仍看得到帶狀幾何。
- 山水為分層網格與筆觸樹，河面是墨線／反射意象，尚沒有真正水面平面反射，也不是完整手工製作的山水場景。
- 畫中世界採獨立視覺隔離，不是另一個網路／物理 Scene；原物件仍存在外面，沒有對隊友的所有敵人 AI 進行全面暫停或重寫。鏡頭與角色座標未傳送。
- 中國龍有可動部件與連續身體，造型仍是程序化低面數版本，沒有精雕鱗片；天空臉取實際老者頭部網格作半透明投影，不是影片等級的體積人臉。
- Boss 地面移動做碰撞／落腳檢查，沒有重烘 NavMesh 或複雜繞障路徑。巨龍碎地為視覺效果，未實際破壞地板。
- 尚未做完整 Build、長時間效能壓測或每一種技能／中斷組合的窮舉測試。正常退場額外繪製一次外界，僅持續 2.5 秒；高解析度／低階硬體仍需進一步效能量測。
