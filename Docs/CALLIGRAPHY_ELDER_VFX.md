# 書法老者／巨型毛筆展示

## 怎麼看

- 開啟 `Assets/_Project/Scenes/VFX_VoidRend_Bocchi.unity`，Play。
- 老者在靶場左側，物件名稱 `CalligraphyElder_Showcase`，位置約 (-7, 0, 2.5)。
- 場景實例每 7 秒展示一次雙手揮筆；周圍墨流持續暈開與消散，約每 3.2 秒產生一個水墨殘影。
- 選取老者，Inspector 的 `Calligraphy Brush Presentation` 下方有 41 個原始動畫的選單，以及「播放選取動畫」、「雙手揮筆」、「恢復自動展示」、「水墨分身」按鈕。按鈕在 Play 時可用。

## 可重用內容

- `Assets/_Project/Prefabs/Boss/PF_CalligraphyElder_Presentation.prefab`：模型、毛筆、雙手約束、墨流與分身。不含 Boss AI、血量、傷害、攻擊判定。靶場的場景實例另外掛受擊與牽引轉接元件。
- `Assets/_Project/Animations/CalligraphyElder/AC_CalligraphyElder.controller`：41 個具名狀態，每個原始動畫各有一份獨立 `.anim`，沒有更動原始 FBX。
- Prefab 的 `showcase` 預設關閉；只有靶場實例開啟。正式 Boss 可呼叫 `PlayAnimation(index)`、`Sweep()`、`EmitDouble()`。
- 原始動作仍是輸入模型的 Generic 骨架；雙手揮筆是後置的手臂／手指姿勢修正，不是重新綁定整套角色。
- 所有動畫播放期間墨流元件都持續運作；需要檢查原始動作時，把 `Grip Weight` 設為 0。

## 可調整

- `Grip Spacing`：兩手距離。`Right/Left Palm Euler`：手掌方向微調。
- `Cycle Seconds`：展示揮筆間隔。
- `Ambient Rate`、`Stroke Rate`：身邊和揮筆的墨量。
- `Ink Size`、`Ink Lifetime`：暈染尺寸與消散時間。
- `Echo Interval`：殘影間隔，0 關閉自動殘影。`Max Echoes` 與 `Max Blooms` 控制池容量，請在 Play 前修改。
- `M_InkDiffusion`：濃淡、內部流動速度。`M_InkDouble`：水墨分身顏色。

Play 時改參數不會自動永久保存。先記錄數值，停止 Play 後修改 Prefab 或場景實例。

## 實作與限制

- 墨流是非加色透明暈染、內部多層噪聲流動及筆尖墨痕，不是完整流體模擬。
- 分身是包含毛筆的姿勢快照，短暫留在空間並溶解；不是有 AI 的獨立敵人。
- 墨片池預設上限 80，分身上限 3；不持續建立無限 GameObject。腳本停用時會清理自己建立的網格和特效物件。
- 長袍下襬誤綁到手臂／手指的權重已另存修正版網格，套用於共用 Prefab；重建展示也保留此修正。原始 FBX 不變。這是蒙皮修正，不是布料碰撞模擬，極端姿勢仍可能穿插。
- 編輯器選單 `VFX > Calligraphy > Rebuild Elder Brush Showcase` 可重建展示。會更新專用 Prefab 並重設靶場同名展示物件的調整，不會重建其他靶場內容。

## 衣服權重修正

- 工具：`Assets/_Project/Scripts/Editor/GarmentSkinningRepair.cs`，選單 `VFX > Characters > Repair Garment Skin Weights`（停止 Play 後使用）。
- 老者：`CalligraphyElder_GarmentFixed.asset`，修正 985 個頂點；袖子與手掌附近保留原本連動，下襬權重轉回身體／腿部。
- 玩家：`PlayerCatWarrior_TailRigged_GarmentFixed.asset`，修正 294 個頂點；471 個含尾骨權重的頂點完全不動，既有尾骨、綁定矩陣與幾何也不變。
- 修正存於兩個共用角色 Prefab，玩家一般 PlayerRig 與 Resources PlayerRig 均繼承修正版。工具每次從原始網格重算，不累積修改。
- 針對性檢查：老者雙手持筆姿勢與玩家 slash 姿勢前後對照、權重正規化、幾何／綁定矩陣與尾巴權重保持不變。沒有全動畫回歸測試。

## 靶場與碼頭的差異

- 靶場 `CalligraphyElder_Showcase`：Enemy 標籤、`CalligraphyPracticeTarget`（IDamageable、累計傷害、不死亡）、`VoidRendTargetable` 與 `TetherInteractable/Pullable`。頭、胸、雙手、小腿有附著在骨頭上的瞄準區；不依毛筆 Bounds 產生人體區域。可普攻、技能鎖定、範圍挑飛及滾輪拉動，沒有對玩家造成傷害的 AI。
- 維京關 `CalligraphyElder_DockDisplay`：木棧橋約 (-15.8, 3.19, 48.4)，名字「沒人知道是誰的老者」。只揮筆與生成墨汁分身；無 IDamageable／敵人標記，碰撞器關閉，不加入戰鬥或波次計數。
- `VFX > Calligraphy > Install Practice Target and Dock Elder` 可重新安裝兩個場景的配置；會儲存這兩個場景，使用前先處理未儲存修改。

## 1 技能範圍行為

- 跑到目標前，短暫收刀蓄力 0.16 秒；Launch 開始同幀挑飛，0.35 秒升至原有高度。範圍預設 4.5m，起挑傷害 100。
- 保留被挑飛者各自的水平位置，鎖定 Animator、移動／攻擊，並中止已啟動的攻擊協程；落地才恢復。Boss/Clock 透過小型中斷／恢復介面續接既有 AI，不重置整場戰鬥。
- 每段衝刺對空中鎖住的目標及軌跡／地面投影範圍造成一次傷害；同一單位多個 Collider 不重複計算。範圍半徑 1.6m。
- 最終爆炸為 12m 球形判定，包括地面敵人。傷害沿用 `voidRendDashDamage`／`voidRendDamage`。
- 確認施放後的接近、上挑、轉刀前搖受技能來源管理的無敵保護；進入連續衝刺解除。取消／停用也解除，不影響翻滾無敵。
- 轉刀時右臂維持頭頂姿勢，副手保持胸前，只有武器軸心旋轉。
- 已跑一次完整靶場流程，檢查立即群體挑飛、地面光束傷害去重、地面爆炸、落地解鎖、前搖無敵及取消清理；未做全關卡回歸。
