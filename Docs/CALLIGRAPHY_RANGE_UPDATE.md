# 靶場與畫中世界返回修正

2026-10-24，Unity 6000.6.3f1 / Windows Editor / DX11。

後續更正：本報告原先以根物件 +X 當作 Pulses 前進方向，對實際 `Cone_LongAOE` 粒子旋轉而言並不正確；原方向驗證因此無效。新修正直接從可見波形的烘焙網格判斷向外方向，並更新 `PulsesLaunch` 的預期。場景留下的移速 0／收招 100 秒測試值也已還原。最新播放操作為「↑ 重播、↓ 下一招，每招結束待命」，詳見 `CALLIGRAPHY_PLAYBACK_UPDATE.md`。

## 實際行為

- `Assets/_Project/Scenes/VFX_VoidRend_Bocchi.unity` 靶場地板從 80×80 改為 200×200，邊長 2.5 倍，隱形邊牆同步移到新邊緣。
- 中央起點：玩家 `(0, 1.05, -10)`、老者 `(0, 0, 8)`。膠囊／方塊靶位於左側 `VoidRend_Stage/Target practice zone`（中心 X=-65）；地形方塊在右側 `Terrain practice zone`（中心 X=65）。背景柱移到遠處 Z=85。
- 場景只保留 `CalligraphyElder`。Play 後按 F8 會讓這個老者開始攻擊並開啟測試游標；停止測試後仍是同一個老者。死亡後重置也沿用同一物件。
- 一般追蹤普攻施放期間，老者持續平滑面向玩家；重砸等固定方向技能保留原有鎖向規則。
- 畫中世界記錄進入時玩家座標。畫內移動照常；消散時，外界即時影像會從進入點對應的鏡頭位置繪製，玩家姿勢也同步對齊。外界影像完全顯示後才還原實際玩家座標與鏡頭，因此畫內走遠不會讓玩家退出到地板外。中止畫中世界也會恢復原座標。
- 隔離層在相機繪製前再次隱藏外界 Renderer；重新啟用、稍後生成或被其他程式重設顯示狀態的靶，仍會被隱藏。退出時恢復原本顯示狀態。
- 翻滾先確認條件與足夠精力，待 CharacterController 真正沿翻滾方向移動一小段才成立翻滾狀態、扣一次精力、發送事件及開始無敵。貼牆失敗、暫停、受控或動作鎖定不扣精力；ResetMotion 也會結束暫時翻滾。
- `Ying Yang Frontal Pulses` 的擊飛改依實際波形烘焙網格的向外方向；特效本身的位置和朝向保留。

## 修改檔案

場景及保留既有調整的 Boss Prefab，與以下 `Assets/_Project/Scripts/` 程式：

- `VFX/CalligraphyBossRangeTest.cs`：場景老者重用、F8 開戰。
- `VFX/CalligraphyBossCombat.cs`、`CalligraphyBossSkills.cs`：追蹤普攻面向、同一老者死亡重置。
- `VFX/CalligraphyPaintingWorld.cs`、`CalligraphyWorldIsolation.cs`：進入點記錄、外界畫面對齊、正常／取消返回、相機繪製前隔離。
- `VFX/CalligraphySkillSequence.cs`：依 Pulses 實際 +X 方向擊飛。
- `Player/ThirdPersonCamera.cs`：返回時同步平移鏡頭跟隨狀態。
- `Player/ThirdPersonController.cs`、`PlayerStamina.cs`：成功位移後才成立翻滾及扣精力。

保留本機原先調整過的 Pulses Scale=2.2、Slam Frontal Scale=1.1，同步用於場景中的老者。既有供應商材質、Project Settings、本機備份未納入此次提交。

## Play Mode / Game View 驗證

可重跑檢查在 `Docs/Validation/CalligraphyRangeChecks.cs`；需先開上述 Scene 並進入 Play Mode。

| 檢查方法 | 實測結果 |
|---|---|
| `LayoutAndFacing` | PASS：地板 200×200、側區靶位置、開始／停止仍為同一老者；追蹤普攻對移動後玩家面向誤差 0°。 |
| `RollPayment` | PASS：暫停／動作鎖／暈眩／貼牆不扣精力、不發成功翻滾事件；正常翻滾測試期間移動約 1.58m、只扣一次 20，重複輸入不重複扣。 |
| `PaintingReturn` | PASS：畫內移動 X=115m、Z=65m，正常退出及中止都回到進入點；新生與重新啟用的外界靶保持隱藏；退出後恢復顯示。 |
| `PulsesLaunch` | 原根物件 +X 預期已作廢；更新後以實際錐形波的網格中心向外方向比較，見後續播放修正報告。 |
| `LiveCameraAndReset` | PASS：正常 Cinemachine 跟隨鏡頭返回前後，相對玩家位置差約 0.000002m；老者死亡後重置仍沿用同一物件。 |

測試指令範例：

```powershell
unity command run_script --file Docs/Validation/CalligraphyRangeChecks.cs --entry CalligraphyRangeChecks.PaintingReturn --timeout_ms 30000 --format json
```

`Arrange` 為編輯模式場景整理方法，會透過 Unity Editor Undo 與 Scene API 修改並保存場景；`Audit` 是唯讀清單。執行時測試會暫時改動玩家、鏡頭及測試數值，停止 Play 即還原。

已目視檢查本機 `Logs/RangeUpdate-*.png` 的場地布局、畫內無外界靶、消散中段、退出、翻滾、墨浪命中與一般跟隨鏡頭返回。編譯通過；停止 Play 後當前 Unity Console 錯誤 0、警告 0。未做獨立 Player Build。

## 後續調整位置

- 場地與分區：上述 Scene 的 `VoidRend_Stage` 下。
- 這個靶場老者的數值：Scene 中 `CalligraphyElder → Calligraphy Boss Combat`；F8 控制器綁在 `CalligraphyBoss_TestControls → Existing Display`。
- `PF_CalligraphyElder_Battle.prefab` 保留作可重用 Boss 資源與備用生成來源；目前場景沿用原先已保存的場景老者，因此要調這個靶場應直接選取 Scene 老者。
