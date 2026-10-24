# 老者手動預覽與墨光／墨浪修正

日期：2026-10-24。Unity 6000.6.3f1，Windows Editor / DX11。

## 根因與結果

場景老者保存了診斷用 `moveSpeed=0`、`recoverySeconds=100`、`dodgeChance=0`，造成施放後看起來卡住。這次透過 Editor API 還原成 2.8、1.1、0.3333，並保留原有技能與特效尺寸設定。

靶場現在每次只播一個指定技能，完成後進入 `WaitingForPreview`，不自動切下一招或重播。`↓` 切換並播放下一招，`↑` 重播目前技能。Inspector 也有「重播目前技能（↑）」按鈕與待命提示。被其他技能中斷後同樣等待指令；若待命時觸發老者的閃避反應，閃避可以正常完成。一般非靶場 Boss 的自動選招流程保留。

墨光仍從前搖一半開始蓄能，生成點改為老者本體上方 1.5m，加上 `Ink Beam / Emitter Offset`。揮筆時生成點不跟筆尖移動。發射時鎖定位置與方向，玩家之後移動不再影響光柱，老者等待原特效自然結束才待命。

舊程式在傷害檢查中反覆乘上縮短比例；玩家躲開時，檢查頻率更高，可能讓光柱持續縮短。現在用未縮放的原始長度計算射程限制，每幀直接指定絕對比例，與玩家是否被命中分開；不會反覆累乘，橫向厚度不受影響。

Pulses 的前進方向原先判錯。實際 `yy_Shockwave_1_Distorted_short_alpha` 使用 `Cone_LongAOE`，子粒子物件旋轉 90°、粒子自身又有三軸旋轉，這個資源的可見錐形波並不沿根物件 +X。現在依實際命中波的烘焙網格中心相對生成點的方向決定擊飛，保留 VFX 的位置與旋轉。

先前的方向測試以根物件 +X 同時作為預期和實作，不能證明與可見波形一致；已更正舊測試與舊報告。新測試同時比較可見錐形波、擊飛速度及玩家實際位移，並換另一個朝向再驗證。

## 操作位置

開 `Assets/_Project/Scenes/VFX_VoidRend_Bocchi.unity`，Play → F8 開始老者測試。

- `↑`：重播目前技能。
- `↓`：選下一招並播放。
- 選 `CalligraphyElder / Calligraphy Boss Combat`，底部可重播或直接選任意一招。
- `5 交叉運筆／墨光 / Ink Beam / Emitter Height` 調整本體蓄能高度；`Emitter Offset` 調整位置，`Maximum Range` 控制射程。
- 施放完成後保持該招選擇與待命，方便走動觀察尚未消散的效果。

## 驗證

`Docs/Validation/CalligraphyPlaybackChecks.cs`：

- `ManualPlayback`：單發與連發完整施放後維持待命；等待超過正常收招仍不自動續播；重播不改技能，下一招會切換。
- `ManualSkill`：實際跑過技能 2、3、4、6、7、8 的正常結束與再次重播；畫中世界使用原本完整的進出時間，約 27 秒結束。加上單發／連發／墨光，九招均完成此流程。
- `BeamStability`：揮筆筆尖確實移動，蓄能點仍固定於本體；發射後玩家連續兩次換位置，光柱的位置、旋轉、縮放保持不變；自然播完後待命且可以重播。
- `ArrowKeys`：PASS，使用暫時的 Unity Input System Keyboard 發送上下鍵，實際確認 ↑ 重播及 ↓ 下一招；完成後移除測試鍵盤並恢復原裝置。
- `CalligraphyFourthChecks.BeamDamage`：PASS，蓄能階段不傷害、實際光柱抵達才命中，射程外不受傷。

`Docs/Validation/CalligraphyRangeChecks.cs`：

- `PulsesLaunch`：可見波方向 `(0,0,1)`、水平擊飛速度 `(0,0,6)`、玩家實際移動約 `(0,0,0.99)`。
- `PulsesLaunchRotated`：方向旋轉後，可見波方向 `(1,0,0)`、擊飛速度 `(6,0,0)`、玩家實際移動約 `(0.94,0,0)`。

本機 Game View 截圖在 `Logs/Playback-body-charge.png`、`Playback-beam-dodged.png`、`Playback-pulse-shape.png`、`RangeUpdate-pulse-launch.png`，已目視確認。未新增或升級套件，未改供應商原始特效資源。

最後編譯通過；停止 Play 後 Unity Console 當前錯誤 0、警告 0。未進行獨立 Player Build。

測試程式放在 Assets 外；`SaveRangeDefaults` 僅在停止 Play 時保存場景的三個錯誤診斷值。其他執行時檢查會暫時移動玩家／相機及隔離招式，停止 Play 還原。
