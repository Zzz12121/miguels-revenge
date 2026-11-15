# 道場開場與遊墨行者戰鬥

2026-11-15。場景：`Assets/_Project/Scenes/DojoArena_ArchitecturePreview.unity`。

後續賞景／逃跑修訂見 `Docs/DOJO_SCENIC_REVISION.md`：玩家改為慢走大幅抬頭轉頭，茶室鏡頭外移到石頭路75%；老頭連續走出門洞、對話後展臂45°幻筆再重砸；半血沿可通行短路徑飛過河，過第一鳥居就回玩家；新增山外局部遮蔽霧幕，院內及原山道霧不變。以下部分數值屬於較早版本。

## 開場

大門由 E 推開。玩家先在前院石頭路上走 5 秒，頭與上胸左右張望約 ±38°，停在前院中間（Z 約 35.3）。再切到老師父喝酒、沿原佝僂動畫走出來，停在正門露臺 `TerraceStop`（0, 1.2, 76.6）。鏡頭先回玩家第三人稱視角，才開始對話；玩家不會再被移到露臺前。

右手從喝酒到行走／對話都拿著 `PF_DojoWineFlask`。對話結束後，凍結原動畫、以骨架修正挺直上身，酒壺沿拋物線甩出並收掉；墨水顯現毛筆和站直的戰鬥形態。這段是程式驅動的姿勢、道具與形態切換，沒有新匯入站起／甩壺動畫。完成後才解鎖玩家、顯示原 BossHealth 血條並開始戰鬥。

`PF_DojoCalligraphyForm` 取自儲存的 `VFX_VoidRend_Bocchi/CalligraphyElder`，帶入其模型、雙手持筆、十招設定與素材。靶場本身及原共用 Battle prefab 保持原樣。

## 招式安排

一般循環：飛墨一筆 → 連筆四式 → 近戰・墨影合擊 → 畫中世界 → 天降墨龍 → 交叉運筆・墨光 → 躍筆重砸 → 連環墨浪 → 點墨落印 → 回到飛墨一筆。

開戰先從露臺用「躍筆重砸」跳到玩家前方，再開始上述循環。每兩招之間穿插毛筆近戰；隔次使用墨水瞬移後接「近戰・墨影合擊」。老頭僅在水平距離 3.6m、垂直差 1.6m 內揮筆；距離不夠（包括出招前玩家跑遠），改接飛墨一筆／連筆四式。實際筆尖掃過玩家碰撞體才命中，沿用 `DojoMasterBoss.DeliverBrushHit`／`PlayerHealth.TakeDamage` 的傷害、擊退、擊飛、受擊與鏡頭震動。新增 Ying Yang Burst Shockwave，其原素材的可見錐形朝 root -Z，校正後沿出筆／擊退方向播放。

墨影合擊的兩個戰鬥分身分別追玩家朝向前方和後方 2.25m 的位置，速度為原 `cloneMoveSpeed` 的 2 倍（預設 2.6 → 5.2m/s）。追擊時檢查障礙，繞過玩家；進入範圍才開始揮筆，離開範圍就收招追擊。維持原分身命中與受擊特效。這是道場外部排程的 opt-in 行為，靶場的分身不改。

瞬移保留原本「出現在玩家背後，阻擋時改選側面」的方式；目的地會檢查場地半徑、地面與膠囊空間。離開位置留下無碰撞／無傷害的凍結墨分身，一秒內消散；真身在落點用 0.3 秒由墨水組回原模型，恢復碰撞後施放毛筆近戰。分身只烘焙角色與毛筆網格，不會複製 AI 或技能元件。

本場在 50% 血量立刻進入逃跑演出，因此不再走到原 25% 的「墨相鎮海・浮墨天階」。其他場景可關閉 `escapeAtHalfHealth` 沿用 25% 單次大招。死亡、停用及玩家死亡時會取消施法與臨時物件。

## 雙擊 P 與半血逃跑

玩家身上的 `DojoTestMode` 以 0.4 秒內連按兩次 P 開關測試模式。使用既有 practice protection 防止一般傷害把生命降到 1 以下，最後一次受擊 5 秒後補滿；不改共用 PlayerHealth。直接墜落死亡等 `Kill` 流程仍由原系統處理。

`DojoElderEscape` 在血量 ≤50% 時只觸發一次，停止招式、墨影和戰鬥音樂，鎖住玩家輸入。老頭扭轉、縮成水墨團並收掉毛筆；主要循環素材為 `ARPG_White__Casting_Hand_attach`、`ARPG_White__Embers`，每 0.45–0.85 秒隨機插入 `ARPG_White__Hit_Nova_Heavy` 或 `ARPG_White__Projectile_Muzzle_Flash`。每個效果有 50% 機率保留原白色，另 50% 透過獨立 alpha 混色材質變黑（不直接把加法材質染黑而消失）。單一粒子系統上限 160，短命特效與自行複製的材質一併清理。

水墨團不使用地面移動器：沿可編輯的十二個 `HalfHealthEscapeRoute` 路點平滑飛行，中心約地面／階梯上方 1.25–1.3m，疊加上下浮動與左右迴旋。經前門、大廳、後院、東側月洞門，再沿 `DojoMountainPathBuilder.PathPts` 的山路高度飛入深山。鏡頭先拍往主屋飛行，入屋後切後院俯視，在 5 秒內由 32m 降到 8m，接著跟向側門和山路。最後飛回玩家視角、顯示「這老頭竟然逃跑！」，對話完隱藏血條並解鎖。這是劇情離場，不觸發死亡／勝利，也不會再自動開戰。

## 程式與可調設定

- `DojoOpeningCutscene`：原演出流程，新增 `terraceStop` 和 `wineFlaskPrefab`，對話後呼叫形態轉換。
- `DojoCalligraphyEncounter`：形態轉換、重砸登場、順序施法、距離檢查、近戰穿插與瞬移；`arrivalSeconds=.3`、`afterimageLifetime=1`、`betweenAttacks=.9`。
- `CalligraphyEncounterControl`：`CalligraphyBossCombat` 的 opt-in partial，僅生產戰鬥外部排程時啟用。手動靶場預覽仍由原邏輯管理。
- `DojoMasterBoss`：仍負責血量、音樂、死亡和重戰；偵測到新形態後委派攻擊排程，不同時跑舊掌擊／震地 AI。
- `DojoInkSilhouette`：瞬移殘影的網格與一秒壽命管理。

酒壺、材質、網格在原有 FinalDojoArena 資產資料夾，生產形態在 `_Project/Prefabs/Boss/PF_DojoCalligraphyForm.prefab`。新開場設定已接回原 Editor installer，重建開場時會沿用露臺與戰鬥配置。

## 重建與驗證

如要更新生產形態的技能參數：在已儲存的 `VFX_VoidRend_Bocchi` 場景，執行 `Docs/Validation/DojoCalligraphySetup.cs` 的 `SaveForm`；回道場場景執行同檔的 `Install`。不要在有未存修改時切換場景。

最新版本：Edit 模式執行 `DojoEscapeSetup.Install`；Play 模式執行 `DojoEscapeRevisionChecks.Run`。測試使用真實虛擬鍵盤的 P、E 與 Enter，確認最低 1 血、五秒回血、5 秒步行與左右張望、露臺停止、對話前鏡頭交接、重砸→飛墨開戰、近戰範圍、分身雙倍速度及前後定位、震波 -Z 校正、51% 不觸發／50% 觸發、十二路點逃跑、黑白效果、對話後血條與操作恢復。結果在 `output/dojo-building/dojo-escape-revision-checks.json`；實際渲染截圖 40–49。`DojoEncounterPresentationInstaller.Configure` 也接回原開場重建選單。

舊 `DojoCalligraphyChecks.Combat` 的 25% 預期屬於上一次版本，僅用於關閉半血逃跑的測試場；不適用新版道場流程。

最新完整實測通過：玩家步行約 5 秒、前進 21.24m；實際毛筆掃擊命中一次，造成 15 點傷害、向上初速 3m/s；十二個逃跑路點走完，返回玩家後收回血條與輸入鎖。編譯與執行無新增錯誤。手部 Casting 的原長時間單次爆發改成 0.7 秒循環、壽命至少 0.8 秒，確保水墨主體在偶發特效之間持續可見；只調整執行時實例，未改第三方素材。

結果：`output/dojo-building/dojo-opening-calligraphy-checks.json`、`dojo-calligraphy-combat-checks.json`；Unity 實際渲染截圖 34–39 包含酒壺、露臺、挺身、持筆形態、瞬移與法像。實測重組約 0.31 秒，殘影一秒內消失，普通招式順序為 0..8。

`DojoBrushContactChecks.Run` 另驗證實際筆尖碰到玩家一次、造成 15 點傷害並保留約 3m/s 擊飛初速，及 Boss 死亡取消排程；結果 `dojo-brush-contact-checks.json`。原 Battle prefab 的手動單招預覽也通過回歸（`dojo-range-regression.json`）。開場挺身／甩壺使用骨架和道具程式動畫，並非新增 FBX 動作；後續仍可替換專用動畫而保留目前流程。
