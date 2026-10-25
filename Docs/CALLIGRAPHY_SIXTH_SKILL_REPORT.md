# 第六次技能修正與後續操作調整

墨海的最新方向已改成「平墨面，墨絲向空氣滲出、分岔後淡去」，取代下文較早的黏稠捲浪。最新參數與驗證見 `Docs/CALLIGRAPHY_INK_DIFFUSION_UPDATE.md`。

## 2026-10-25 法像臉部後續修正

依使用者後續要求，移除材質額外繪製的眼眶、眼白與瞳孔，保留模型原本的眼睛。原老者頭髮與頭部共用蒙皮，沒有獨立髮骨，因此採用使用者同意的固定方案：移除髮鬚／頭部的額外頂點飄動，只保留胸部以下衣袍的變形。慣性位移也經過同一胸部邊界，避免再次拉扯頭型；找不到胸部骨骼就關閉額外變形。原有抬頭／攻擊姿勢仍正常播放。

下方較早版本的髮鬚、點睛與凝視設定已停用，Inspector 只保留有效的衣袍控制。舊欄位隱藏以保留序列化資料。使用 `Docs/Validation/CalligraphyAvatarFacePreview.cs` 在實際法像上檢查正面、側面、背面，衣袍飄動開關比較的頭部近照只有 1／1,000,000 像素差異，材質無額外眼睛屬性、Shader 無編譯錯誤。截圖位於本機 `Logs/Avatar-face-fixed-*.png`。

## 本次結果與界線

沿用技能索引 9「墨相鎮海・浮墨天階」，沒有再建立另一套大招。保留長嘯、原版白焰柱、兩次 60° 橫掃、插筆、升石、空中戰鬥和消散流程。這次沒有安裝套件、修改正常移動系統或重新產出 Windows／Ubuntu 執行檔。

依最後一則使用者訊息，攀爬不再自動觸發，且本次允許 commit／push；這取代附件較早的「不要 Push」。

## 可直接操作的設定位置

1. 開 `Assets/_Project/Scenes/VFX_VoidRend_Bocchi.unity`。
2. Hierarchy 選 `CalligraphyElder`，Inspector 找 `Calligraphy Boss Combat`。
3. 展開 `9 墨相鎮海／浮墨天階（全部參數）` → `Ink Avatar Ultimate`。
4. 要調畫中墨龍，展開 `3 畫中世界`，最上方就是 **墨龍數量（平均環繞 360°）**。
5. 可共用的預設在 `Assets/_Project/Prefabs/Boss/PF_CalligraphyElder_Battle.prefab` 的同名元件。場景有自己的覆寫，調 Prefab 後請留意場景 Overrides；Play Mode 改值不會永久儲存。

| 分組 | 主要欄位及預設 |
|---|---|
| Avatar | Size 100、Opacity .78、Face Clarity .86；Hair Amplitude 2.2、Hair Speed .85、Beard Amplitude 1.3、Robe Amplitude 2.8、Robe Speed .65、Motion Inertia .8；Eye Visibility .9、Eye Contrast 1、Gaze Speed .7、Gaze Offset |
| Sweep | Angle 60、Seconds、Interval、Radius、兩次傷害與擊退設定 |
| Slam | Forward Distance、Vertical Ready .38、Grip Offset、擊飛高度／速度／方向、傷害 |
| Sea | Radius 75、Mean Height .15、Wave Amplitude 2.4、Churn Speed .85、Vortex Density 1、Vortex Strength 1.3、Ink Filaments 20、Edge Strength .8；Damage 8、Damage Interval 1.5、Slow Multiplier .45 |
| Platforms | Count 64、Height Range (3,85)、Height Weights；Small Size (3.5,6)、Medium Size (8,15)、Large Size (19,29)、Size Weights (.42,.4,.18)；Thickness Range (4,22)、Irregularity .65、Sharpness .85、Clustering .4、Spawn Radius 68、Minimum Gap .7、Spawn Stagger .035、Rise Speed 18、Rise Speed Variation .4、Landing Platform Size 14 |
| Climb | Enabled、Reach 1.35、Seconds 2.1、Facing Angle 55°、Waist Clearance .08、Ledge Height (.5,2.5) |
| Pillars / Shake | 保留原資源、柱數／大小／透明度／交錯時間，以及各階段鏡頭震動 |

選取老者並開啟 Scene Gizmos 可看海域半徑、平台生成範圍、最高／最低頂面、初始落腳點與插地垂直方向。

### 攀爬規則（最後追加要求）

- 必須按住 **W**（且沒有同時按 S）、面向岩壁 55° 以內、腰部低於頂面至少 .08 公尺、水平距離不超過 1.35，腳底到邊緣高度在 .5–2.5 內。
- 只適用這招生成的浮石；地面站立、翻滾、受控狀態、按鍵設定面板開啟時不開始攀爬。
- 下落時或滾輪拉向石塊時均能抓邊；拉繩仍需 W 和朝向／高度條件，不會自行攀爬。
- 開始後可以放開 W。順序為抓邊 → 左腳抬過邊緣 → 右腳 → 俯身撐上 → 站起；腳先抬到岩壁外側再越過頂緣。
- 起攀前檢查頂部膠囊空間是否被其他物體擋住。攀爬中以平台的局部邊緣座標跟隨升降；完成或中斷都解除本次動作鎖並恢復 Animator、控制器與碰撞。

### 墨龍數量

預設 5 隻；起始水平角度為 `i × 360 / count`，5 隻 72°、4 隻 90°、6 隻 60°。範圍 0–12，0 表示不產生龍。全部起點使用同一次玩家位置取樣；保留原本的高度錯落、延遲與追蹤方式。改值於下次施放生效，不在本輪中途補生或刪龍。

## 實作內容

### 法像、眼睛與垂直插筆

`CalligraphyAvatarLife` 將頭部、眼睛、手部位置與緩慢追視量送入材質。髮鬚／袍角用不同相位與速度做頂點飄動，加上頭部移動的延遲慣性；臉和手保持穩定，不用黑煙遮掩。眼睛在原面部表面加眼窩、眼瞼和瞳孔方向的對比，保留原模型貼圖與五官光影。

`CalligraphyBrushPresentation` 對法像專用的插地姿勢逐漸校正筆桿，移動上半身到可握持的位置，限制手臂長度，再解雙手姿勢和筆長。實際筆尖接地後才觸發撞擊。測得接觸時筆尖軸與世界向下方向點積為 **1**，筆尖與撞擊高度誤差小於 .15。

### 宿墨海

海面約 18,433 頂點，使用不同方向與相位的幾何位移形成波峰凹谷，另有 20 組動態回捲薄片形成翻轉的浪緣；不是單純 UV 平移。材質以流場扭曲、積墨濃淡、留白痕與幾何法線分辨浪面。岩石抬升時附少量向下回落的墨滴。這是可控的即時視覺近似，不是 CFD 流體模擬。

只有最初插筆衝擊會強制擊飛。之後掉回海域，在地面接觸高度持續每 1.5 秒受傷並降至 .45 倍移速，離海或技能清除時移除緩速；不再自動彈回平台。海浪為視覺表面，傷害判定使用穩定平均接觸面，不逐波做浮力碰撞。

### 浮石

使用不規則凸多邊形頂面和多層斷裂岩壁，混合尖垂山體、厚岩板、不對稱碎崖三類輪廓；下方收束成參差岩脊。每塊具有獨立凸 MeshCollider 和運動學剛體，頂面保持水平。

頂面高度分五個權重帶（3–9、9–25、25–45、45–65、65–85），帶內再隨機分布，並非階梯。大中小尺寸指生成外接直徑，實際可站頂面會因輪廓略小。低空岩石厚度會裁短，避免底部穿回原地面。XZ 採群聚混合隨機散布與間距拒絕；保留初次擊飛的安全落腳平台。如果密度過高，重試後以小石補位，因此尺寸比例是目標權重，不保證精確比例。

## 檔案範圍

- Runtime：`CalligraphyAvatarSettings`、`CalligraphyInkAvatarUltimate`、`CalligraphyInkPlatform`、`CalligraphyAvatarLife`、`CalligraphyInkSeaMotion`、`CalligraphyRockGeometry`、`CalligraphyPlatformClimb`、`CalligraphyUltimateCameraShake`。
- 整合：`CalligraphyBossCombat`、`CalligraphyBossSettings`、`CalligraphyBrushPresentation`、`CalligraphyPaintingWorld`、`CalligraphyBossCombatEditor`、`CalligraphyInkProjectile`、`CalligraphyPackageEffect`、`CalligraphyPlayerHitGate`。`CalligraphyInkField` 補上腳本重載後重新綁定既有墨線物件池，避免非序列化快取遺失造成逐幀 NullReferenceException。玩家拉繩只新增唯讀 `IsPullingPlayer`，沒有重寫其移動。
- Shaders：`Assets/_Project/VFX/Calligraphy/Shaders/CalligraphyAvatar.shader`、`CalligraphyInkDomain.shader`。
- Materials：同資料夾群內 `M_InkAvatarDetail`、`M_InkAvatarSea`、`M_InkAvatarPlatform`。
- Prefab／Scene：Battle Prefab 和上述靶場 Scene 的老者設定。
- 攀爬和額外法像動態是程序化骨架／頂點動畫；沒有新增或改寫 `.anim`、Animator Controller、FBX。浮石與海面 Mesh 是執行時建立。

## 驗證與限制

已使用 Unity 6000.6.3f1 Play Mode：

- `CalligraphySixthChecks.Flow`：兩次橫掃、筆桿垂直、64 個凸碰撞岩島、高低與尺寸跨度、初次安全落地、回捲網格持續更新且無非法頂點、落海傷害與緩速但不彈起、W 攀爬到頂、切換技能恢復起點／操作。
- `IntentAndDragons`：不按 W 不爬、背對岩壁不爬、腰高於邊緣不爬、條件正確才爬；實際生成 5／4／6／0 隻並驗證 72°／90°／60° 起點間隔。
- `TetherAndInterrupt`：實際執行拉繩 PullPlayer 流程並持 W，接上攀爬，完成恢復輸入；再次抓邊時切換技能也恢復控制器與骨架。
- `CalligraphyAvatarChecks.Cancellation`：在長嘯、第一掃、插地、空中、消散各階段中止；自然結束待命、重播與停用老者均清理正常。
- `CalligraphyAvatarChecks.DefaultPlayback`：使用已儲存 Prefab 的預設設定，白焰柱正常播放、筆尖保持接地、雙手到筆軸距離小於 .8。
- `PoolReload`：模擬墨線非序列化快取遺失，重建後恢復發射且沒有重複建立物件池。最後清除舊錯誤並重新跑 Flow 與 IntentAndDragons，Unity Console **0 errors / 0 warnings**，腳本編譯通過。
- 實際遊戲相機截圖檢視海浪立體輪廓、岩島斷面、法像與攀爬分段。測試截圖留在本機 `Logs/Sixth-*.png`，不加入遊戲資源。
- 驗證腳本在 `Docs/Validation/CalligraphySixthChecks.cs`；測試用虛擬鍵盤只存在於該檢查作用域，結束或拋錯會還原原鍵盤，不會修改玩家綁鍵。

仍有限制：

- 原老者模型是低面數單一蒙皮，沒有獨立髮鬚骨架或表情 BlendShape；近距離仍可見稜角，不能等同參考圖的精細臉部雕刻、髮束和手指接觸。
- 攀爬是針對目前 Humanoid 模型的程序姿勢，不是手工逐幀製作的動畫；極端角落、其他模型比例和更複雜遮擋仍需實機手感調整。
- 岩島已有厚度與形狀差異，但保留水平凸頂面以穩定落腳，尚非參考圖的逐塊手工雕刻破碎地形。
- 墨海具立體浪與回捲，但不是粒子流體互相碰撞／融合模擬；微觀墨絲和手繪渲染仍與參考圖有差距。
- 未重新測試 Windows／Ubuntu 打包版本，也未在朋友的機器做效能測試；不承諾指定硬體 FPS。高低平台間需配合現有拉繩，不代表只靠普通跳躍能到全部平台。
