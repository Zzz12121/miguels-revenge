# 重砸與連環墨浪判定（2026-10-24）

## 行為

- 躍筆重砸：中央墨圈、地面墨跡與前方主要衝擊面全部納入，不再只算中央小圈。按筆尖落點到玩家的水平距離，線性降低水平／垂直擊退初速；特效外緣保留 15%。傷害值不衰減。直接被筆尖命中的玩家不會再被範圍重複命中；主衝擊結束後的殘墨不持續造成擊退。
- 連環墨浪：套件的主波是在靜態表面上移動材質墨紋，不能把整片模型當作有效判定。現在以原粒子的 `Custom1.w`、`_MainTex` tiling/offset 和實際烘焙網格 UV 裁出移動波帶，並涵蓋上一幀到這一幀的掃過區域，避免低幀率漏判。
- 每個粒子波各自記錄接觸。跳過、閃避或擋掉後，該波不會在玩家落地或無敵結束時補打；波與波之間可以安全移動。擊退仍沿實際可見波的前進方向。
- `WaveSettings.pulseContactHeight` 預設 0.55 公尺，腳底高於波面加此高度即可跳過。`edgeKnockbackMultiplier` 控制重砸最外緣擊退比例。兩者可在現有 Boss Inspector 的技能設定調整。
- 沒有改動玩家移動／跳躍程式、原套件材質或 Prefab，也未納入工作區原有的 Boss Prefab、ProjectSettings、第三方材質變更。

## Play Mode 驗證

場景：`Assets/_Project/Scenes/VFX_VoidRend_Bocchi.unity`。`unity command run_script --file Docs/Validation/CalligraphyWaveChecks.cs --entry CalligraphyWaveChecks.<方法> --format json`。

- `SlamArea [0..3]`：近處水平初速約 8.38、前方 8m 約 3.99、側前方約 3.45，均正常擊退且只命中一次；特效外側不命中。
- `PulseArrival [0/90]`：12m 處在約 0.20–0.22 秒波到達時命中，之前安全；兩個朝向的速度分別為 +Z / +X，與波同向。
- `JumpRepeatedPulses`：使用控制器原有輸入掛勾走正常跳躍／重力流程，不使用 Launch 或傳送假裝跳躍。連跳避開第一、第二波，落地不補打；第三波站在地上，約 2.72 秒正常命中。最初也通過 Space 輸入測試；背景 Editor 不穩定接收虛擬鍵盤，因此重複測試改用既有輸入掛勾。
- `WaveGap`：0.6 秒走進已經通過的舊模型不會被打，約 1.47 秒下一波到達才命中。
- `DefenceAndSlamResidue`：閃避無敵有效、不對直接命中的人補打範圍傷害、殘墨無持續擊退。
- `CalligraphyPlaybackChecks.ManualSkill [6/7]`：兩招正常播完待命，可重播，不會卡住或自動切下一招。
- 檢視 Game View 截圖確認原特效與跳躍呈現；編譯完成、Console 當前沒有錯誤。

波帶的 texture-V 0.4–0.85 是依目前 `Tx_Line_08_Up` 可見主體校準的範圍，不是逐像素雜訊碰撞。若將來更換主波貼圖或 shader 的 UV 定義，需重新校準，不能沿用固定模型整面碰撞。
