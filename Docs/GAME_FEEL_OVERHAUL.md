# 戰鬥與操作手感調整報告

本次已把輸入、移動、鏡頭、普通攻擊、翻滾、動畫與命中回饋接到一致的動作階段及時間規則。Unity 編譯通過，基本操作檢查 26 項與道場整合檢查 5 項全部通過。實作已落在專案內，真實滑鼠、手把與完整無保護 Boss 戰的舒適度和難度，仍需互動遊玩確認。

**主要問題與修正**

| 原有問題與原因 | 實作後行為 |
| --- | --- |
| 近戰預約使用長壽命旗標，過早輸入可能在很久之後才執行。不同動作缺少共用的合法銜接時機。 | 攻擊、防禦、翻滾及技能使用短期輸入緩衝。每種動作只保留一筆待執行命令，到期清除。受傷、死亡、切場景與控制權移交會清理輸入。 |
| 停步與反向共用加速度，撞牆時動畫仍參考希望移動的速度。 | 分開加速、減速及反向加速。動畫與腳步改用 CharacterController 的實際水平位移，降低撞牆原地跑與停步滑行。 |
| 鏡頭輸入在角色移動之後才更新，朝向又可能包含震動。自動回正會介入手動環繞。 | 先讀取鏡頭方向，再計算移動。角色使用沒有震動的水平朝向。手動環繞優先，跟隨與碰撞仍在 LateUpdate 完成。 |
| 攻擊、普通受擊與裝備系統會重複震動或改變全域時間。命中後還有全域加速補償。 | 命中回饋由確認傷害事件統一發出。移除裝備額外頓幀及 1.7 倍時間補償。空揮保留揮刀聲，沒有命中震動與頓幀。 |
| 普攻判定主要依賴角色前方球形範圍，畫面上刀刃與碰撞不完全一致。 | 使用可見刀刃根部到尖端的膠囊掃掠，補取前後幀路徑並去除同刀重複傷害。加入牆面遮擋檢查，保留原有最大水平攻擊距離。 |
| 翻滾位移、無敵和暫停使用不同時鐘，距離也受更新步長影響。 | 翻滾狀態與無敵使用遊戲時間，按累積曲線差計算位移。無敵只在實際動作區間內有效，消耗體力只在確認位移時扣一次。 |
| 動畫收尾和受擊動畫會把操作鎖得比必要時間長。 | 明確分成起手、有效揮擊、收招。保留出刀承諾，在合法收招時段允許翻滾取消，提早交還受擊後的移動控制。 |
| 成功格擋的 0.8 倍速可能被技能或選單保存成日後恢復值。技能以真實時間播放，暫停時也可能繼續推進。 | 保存時間前先終止暫時頓幀及格擋慢動作。虛空連斬使用扣除暫停時間的時鐘，暫停時停止演員、姿勢、鏡頭和主流程推進，恢復後繼續。 |
| 高頻命中反覆建立短命粒子，輸入查詢每次重新解析控制路徑。 | 依效果名稱回收粒子，每類保留最多 12 組。輸入控制元件快取會隨目前裝置變更失效。多段技能另有限頻回饋。 |

**操作與鏡頭設計**

普通攻擊開始時只允許有限角度的朝向修正，起手期間可繼續有限速追蹤，進入有效揮擊後固定方向。這保留選擇出刀方向的責任，也減少大角度瞬間轉身。攻擊傷害與原有速度倍率仍由既有系統決定。

新增 L 鎖定與 J 切換目標，並接進既有改鍵介面。GameAction 新值附加在列舉尾端，舊存檔的動作編號與既有鍵位繼續沿用。手把的新鎖定動作預設留空，可在設定內指派，避免占用已有技能鍵。

鎖定會檢查距離、死亡、停用和遮擋，以目標身體的穩定注視點追蹤。大型目標依身體尺寸調整距離與注視高度，短時間遮擋有寬限，目標失效就解除。手動轉鏡頭暫時優先，系統不會自行換到另一隻敵人。解除鎖定後平順回到原本跟隨距離。

普通命中的鏡頭反作用只疊加旋轉，方向來自實際接觸位置，並依距離減弱。它不改變移動方向、鏡頭碰撞位置或基準 yaw。Boss 原有震動事件接到相同的距離衰減入口，招式內容維持原設計。鏡頭撞牆立即內縮，離牆後平順拉回。

**可調參數與取捨**

以下是新增欄位預設值或程式行為。場景和 prefab 已序列化的既有數值仍有優先權，調整時應看執行中的 Inspector。

| 元件與欄位 | 數值 | 調整目的 |
| --- | --- | --- |
| ThirdPersonController.acceleration | 40，沿用 | 保持既有啟動速度。 |
| deceleration / reversalAcceleration | 55 / 1.65 | 停步與快速反向更明確。 |
| PlayerCombat.attackBufferSeconds | 0.16 秒 | 接近可執行時按下的普通攻擊可以接上，過早輸入會失效。 |
| blockBufferSeconds / dodgeBufferSeconds | 0.12 / 0.15 秒 | 短期容錯，避免長時間預約防禦或閃避。 |
| MeleeCombat.dodgeCancelRecovery | 收招 55% | 起手及揮擊維持承諾，收招後半可閃避。 |
| 普攻朝向修正 | 起始最多 35 度，起手追蹤 540 度每秒 | 降低瞬間背身吸附目標。 |
| PlayerCombat.bladeRadius | 0.18 公尺 | 容許模型和骨架小幅誤差，仍依可見刀路判定。 |
| rollInvulnerabilityStart / End | 0.025 / 0.42 秒，結尾受實際翻滾長度限制 | 開始及後段恢復仍可受傷，取消動作就立即結束無敵。 |
| PlayerVisualAnimator.hitControlReturnCap | 0.32 秒 | 提早恢復移動，同時保留受擊姿勢。可與既有 hitMoveLockMax 及 Fraction 一起調整。 |
| locomotion 動畫參考速度 | 走路 4.5、跑步 8 公尺每秒 | 依實際移動速度縮放步頻，只作用於 Walk/Run。 |
| Animator 移動轉場 | 進入 0.08、停止 0.06 秒 | 取代較長的移動混合。Land 保留短暫落地姿勢及 exit time。 |
| comboGraceTime | 0.45 → 0.16 秒 | 減少連段結束後懸著等待的感覺。收尾視覺混合 0.12 → 0.06 秒。 |
| PlayerHitFeedback 頓幀 | 輕擊 0.045、重擊 0.075、擊破額外 0.025 秒 | 依重量區分，單次絕對上限 0.12 秒。 |
| multiHitstop / multiHitInterval | 0.018 / 0.10 秒 | 多段技能可感到接觸，又不讓每段傷害連續凍結全場。 |
| effectInterval | 0.055 秒 | 限制密集命中的粒子與聲音堆疊。 |
| ThirdPersonCamera.cameraShakeIntensity | 0.7，0 可關閉 | 同時控制普通反作用及接入此入口的技能震動。 |
| lockAcquireRange / RetainRange | 22 / 28 公尺 | 避免目標剛離開初始範圍就突然解除。 |
| lockOcclusionGrace / TurnSpeed / MaxPitch | 0.9 秒 / 240 度每秒 / 35 度 | 平衡遮擋容錯、追蹤速度與垂直舒適度。 |

頓幀採用一個受限的全域時間管理元件，因為現有攻擊、動畫、物理與移動多數依賴遊戲時間。局部凍結全部參與者需要更大幅度的 AI 與技能改寫，本次以單一管理、短上限及控制權交接維持一致性。多段技能和電影慢動作期間，頓幀不會搶走原本的時間控制。

**修改範圍**

既有 prefab 與場景沒有為這次實作另存變更。測試地板和生成的玩家實例只存在未保存的測試場景。唯一修改的動畫資產為 [AC_PlayerCatWarrior.controller](C:/NTHU/Introduction_to_Game_Programming/final/Assets/_Project/Animations/Player/AC_PlayerCatWarrior.controller)，新增 LocomotionRate 與移動轉場設定。動畫片段、角色模型、材質和技能特效素材均沿用。

下列元件可從同名腳本定位，完整檔案清單另記於 [change-scope.json](C:/NTHU/Introduction_to_Game_Programming/final/output/game-feel/change-scope.json)。

| 系統 | 腳本 |
| --- | --- |
| 新增共用基礎 | CombatInputBuffer、CombatHitStop、CombatCameraImpulse、PlayerCombat.MeleeSweep、PlayerMeleeSweepDriver、ThirdPersonCamera.LockOn、ImpactParticlePool，及對應 meta。 |
| 玩家操作與狀態 | ThirdPersonController、PlayerCombat、PlayerHealth、PlayerLoadout、PlayerVisualAnimator。 |
| 鏡頭 | ThirdPersonCamera、ThirdPersonCamera.AutoBehind、ThirdPersonCamera.FullView。 |
| 攻擊與音效 | MeleeCombat、EnemyCombat、PlayerSfx。 |
| 命中與特效 | PlayerHitFeedback、ImpactFx、ShieldParryVFX、ParrySlowMotion、CalligraphyPlayerBladeTrail、CalligraphyUltimateCameraShake。 |
| 既有技能整合 | VoidRendSkillDemo、VoidRendFinisher、VoidRendTargetingSkills、VoidRendClipPoses、VoidRendDrawDash、EnergyTetherSkill、LeapCleaveStrike。 |
| UI 與時間交接 | InputBindings、PadInput、GameManager、StoryDialogue、RangeInputSettings、UpgradePlant、CombatHistoryBook。 |
| Boss 震動入口 | BossController、DojoMasterBoss。 |

新增的掃掠驅動器與 Cinemachine 反作用元件會自動掛載，命中時間管理與回饋元件會按需建立，現有場景沒有必須手動補接的參考。想永久調整自動建立元件的數值，可以將元件加到場景或玩家 prefab，再從 Inspector 設定。

玩家與 Boss 的血量、傷害公式、成長經濟、招式傷害上限、施放機制及劇情流程維持既有規則。普通刀的命中形狀及閃避時機改變，仍可能影響實際戰鬥難度。空中斬、突進斬與落地範圍攻擊保留各自原有範圍行為，沒有全部改成刀刃掃掠。

**編譯與執行結果**

Unity 6000.6.3f1 使用既有 unity CLI 與連線中的 Editor 進行編譯及 runtime eval 檢查。編譯狀態 completed、failed=false、errors=[]。最終基本操作及道場執行沒有新增 Console error。編譯紀錄仍包含既有 FindObjectsSortMode 等淘汰 API 警告，以及 Unity MCP 在重載時的 logger dispose 警告。

本次驗證程式位於現有 Docs/Validation，透過執行中的 Editor 執行。它們不是 NUnit 或 Unity Test Runner 測試組。測試用真實 PlayerRig prefab、CharacterController、碰撞和既有事件，部分動作透過虛擬鍵盤及反射觸發，並把結果逐項寫入 JSON，避免中途停止被誤認為通過。

基本操作 26 項全數通過，涵蓋輸入到期與覆蓋、加減速及反向、固定步長翻滾、閃避無敵與暫停、攻擊階段及承諾、接刀及閃避緩衝、空揮音效、刀刃接觸、鎖定與大體型垂直目標、重擊及牆面遮擋、多段回饋與粒子重用、受傷中斷、格擋、時間交接、震動關閉、選單輸入抑制、切場景、死亡與重生。詳見 [runtime-checks.json](C:/NTHU/Introduction_to_Game_Programming/final/output/game-feel/runtime-checks.json)。

道場整合 5 項全數通過，涵蓋正式變身入口、原有進場及一般招式、虛空連斬暫停與恢復、技能結束歸還操作與鏡頭、半血逃離及對話後控制交接。測試觀察到 CastHistory 為 6、2，最終為 Escaped／Waiting for rematch。測試期間玩家有保護，玩家技能期間也暫時保護 Boss，讓劇情測試能分段執行。詳見 [boss-checks.json](C:/NTHU/Introduction_to_Game_Programming/final/output/game-feel/boss-checks.json)。

| 量測 | 結果與適用範圍 |
| --- | --- |
| 普通攻擊動作開始到有效階段 | 約 0.16668 遊戲秒，測試玩家的現有攻速與動畫配置。起點為程式確認動作開始，沒有包含實體輸入或顯示器延遲。 |
| 翻滾距離 | 手動用 1/30、1/60、1/120 秒呼叫正式翻滾更新，各為 3.8 公尺。這驗證控制器固定步長的一致性，沒有驗證三種實際渲染幀率。 |
| 同配置普通刀與重擊傷害 | 120 與 180，沿用 1.5 倍重擊規則。同一揮擊只計一次傷害，牆後目標沒有受傷。 |
| 鏡頭牆面測試 | 受阻距離約 2.45974 公尺，障礙解除後第一筆恢復約 2.53548 公尺，確認內縮及逐步恢復。 |
| 粒子重用 | 最終暖機測試保留 13 個效果物件，多次呼叫後重用。實際數量取決於不同效果及同時存活粒子。 |
| 10,000 次暖機輸入查詢 | 原查詢路徑約 2.8881 ms，快取約 0.2007 ms。僅為同一 Editor 中的局部微型基準，不能推算整體 FPS。 |

輸入查詢的 GC.GetAllocatedBytesForCurrentThread 在此 Editor 兩種路徑都回報 0，因此不據此聲稱減少多少 GC。非配置物理查詢、效果回收與關閉時不掃描遮擋物等改動，降低了可避免的重複工作，但尚無玩家版本的 GPU、overdraw、p95/p99 幀時間或整場 CPU Profiler 前後數據。詳見 [input-allocation-checks.json](C:/NTHU/Introduction_to_Game_Programming/final/output/game-feel/input-allocation-checks.json)。

**畫面與原要求情境的驗證界線**

已實際渲染並檢視道場戰鬥與虛空連斬畫面。戰鬥畫面可辨識前景玩家與 Boss，原有水墨效果與模型仍在。技能抬升階段會把焦點放到空中目標，部分玩家會離開下緣，這個既有電影構圖仍應在完整動畫播放時評估。靜態圖不能判斷連續鏡頭是否令人暈眩或攻擊節奏是否舒服。

| 原要求情境 | 本次覆蓋與仍需試玩的部分 |
| --- | --- |
| 1、2、6 行走、反向、牆邊 | 自動驗證加減速、反向、碰撞及牆邊鏡頭。繞競技場邊界連續閃避仍需互動試玩。 |
| 3、4、5 繞 Boss、站立及跑動轉鏡頭 | 驗證更新順序、基準方向與手動優先規則。實體滑鼠、手把同時移動與轉鏡頭未在 batch mode 測試。 |
| 7、8、9 鎖定、貼身、大型或垂直目標 | 驗證切換、失效、距離、垂直大型假目標及正式 Boss 畫面。快速繞背、貼牆與跳躍 Boss 的連續追蹤舒適度需實玩。 |
| 10 至 17 普通刀、重擊、連段、緩衝、承諾、空揮與命中 | 普通刀及重擊傷害、有效刀路、接刀一次、過早輸入到期、合法閃避和空揮回饋均有自動檢查。收尾視覺和完整三連段的主觀重量仍需試玩。 |
| 18、21 多段及既有技能 | 自動驗證回饋頻率和虛空連斬完整控制交接。其他特殊技能保留流程並整合時間快照及輸入緩衝，未逐招完成全套視覺回歸。 |
| 19、20 受傷、中斷、重複硬直 | 驗證受傷清除待執行命令與現有格擋。多敵包圍下的重複硬直壓力和公平性仍需實玩。 |
| 22、26 Boss 範圍招及階段 | 執行既有進場、一般招式、變身及半血逃離。沒有在無保護玩家下完成所有 Boss 招式輪替或整場難度評估。 |
| 23、24 暫停、死亡、重啟 | 自動檢查頓幀、格擋慢動作、技能慢動作暫停、死亡重生及切場景的狀態清理。 |
| 25 不同幀率 | 已驗證控制器固定步長翻滾距離。batch mode 不遵循一般遊戲幀率上限，實際 30／60／120 FPS 的幀時間與輸入體感待玩家版本量測。 |

**交接與後續調整**

測試結束已退出 Play mode，測試生成物沒有保存到正式場景。既有鍵位繼續使用，新增鎖定功能可以直接以 L／J 使用。需要手把鎖定時到改鍵設定指派即可，沒有套件安裝或必要的手動 Unity 配線。

建議下一次互動試玩先測停止、反向、同時跑動轉鏡頭，再測收招末段接刀與翻滾，最後測大型 Boss 貼牆和多段技能。優先調 deceleration、dodgeCancelRecovery、attackBufferSeconds、hitControlReturnCap 及 cameraShakeIntensity，每輪只改一組相關參數，保留傷害與 Boss 原有招式節奏作為比較基準。

本次開始前工作目錄已有大量改動。比對快照中的 3,647 個既有變更檔案，其中 2,519 個有 SHA-256、其餘依檔案大小檢查，沒有發現內容雜湊或大小改變。此次沒有 stage、commit 或 push。完整工作範圍見 change-scope.json。
