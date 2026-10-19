# Miguel's Revenge

Unity 6000.6.3f1（URP）第三人稱動作遊戲。

## 遊戲流程

`MainMenu`（主選單）→ 按「開始遊戲」→ 三個關卡 → 通關畫面 / Game Over 畫面

- **MainMenu**（`Scripts/Core/MainMenu.cs`、`Scenes/MainMenu.unity`，Build Settings 第一個）：
  標題、用 GUI Text 說明規則和操作，「開始遊戲」「結束遊戲」按鈕（uGUI Button，畫面由程式產生）。
- **生命**：玩家有 3 條命（`GameManager.maxLives`），倒下扣一條命並在檢查點復活，用完 → **Game Over**
  （重新挑戰本關 / 從第一關重新開始 / 回主選單）。
- **通關**：打倒第三關 Boss → 通關畫面（通關時間、剩餘生命；重新開始 / 回主選單）。
- **暫停**：遊戲中按 `Esc`（繼續 / 存檔 / 讀取存檔 / 重新開始本關 / 回主選單）。
- **血量延續**（`Scripts/Core/PlayerState.cs`）：換到下一關時血量、最大生命、能力升級（跑速 / 攻擊）、背包都會帶過去；
  「重新開始本關」和 Game Over 後「重新挑戰」會回到進入這一關時的狀態。
- **存檔**（`Scripts/Core/SaveSystem.cs`）：進入每一關自動存檔，暫停選單可以手動存檔。存的是關卡、剩餘生命、遊玩時間和玩家狀態，
  讀檔時從那一關的起點開始。檔案在 `Application.persistentDataPath/save.json`。主選單有存檔時會出現「繼續遊戲」。
- **小地圖**（`Scripts/Core/Minimap.cs`）：右上角圓形小地圖，跟著玩家和鏡頭方向轉動；紅點敵人、藍點傳送門、黃點檢查點。
  按 `M` 顯示 / 隱藏，進入關卡時自動建立。
- **玩家**（`Scripts/Player/PlayerVisual.cs`）：Mixamo 聖騎士模型，待機 / 走 / 跑 / 跳 / 空中 / 落地 / 三段連擊（左鍵）/ 舉盾衝撞（右鍵按一下）/ 舉盾格擋（右鍵按住：正面攻擊完全擋下，連續擋到第 3 下破防扣血）/ 受擊 / 死亡、在船上拉弓都有動畫。
  受傷後 0.1 秒無敵 + 0.1 秒僵直（不能移動、攻擊，揮到一半的劍會被打斷）。
  選單 **GeminiGrowUp > 安裝玩家（聖騎士 + Cinemachine）** 會裝到 `PlayerRig.prefab`。
- **鏡頭**（`Scripts/Player/ThirdPersonCamera.cs`）：Cinemachine 3（CinemachineBrain + CinemachineCamera 的 Third Person Follow，
  開啟 Avoid Obstacles 不穿牆）。滑鼠轉視角、滾輪縮放、畫面震動由 ThirdPersonCamera 控制注視點。
- **補血道具**（`Scripts/Level/HealthPickup.cs`、`Prefabs/HealthPickup.prefab`）：漂浮的紅十字，走過去自動回 30 血（血滿不會撿）。
  選單 **GeminiGrowUp > 放置補血道具** 會在沙漠、維京村莊各放 6 個；地下城由 DungeonGenerator 每間戰鬥房間放一個。
- **背景音樂**：主選單和每一關各一首（`Resources/Audio/Music/bgm_*.wav`，程式合成的原創音樂），Boss 戰換成 Boss 音樂。
  想換音樂：把檔案放進 `Resources/Audio/Music`，改 `GameManager.levels[].music` / `mainMenuMusic`。
- **受傷音效**：玩家、小怪、聖騎士、時鐘敵人、Boss 被打中都有各自的音效。
- **攻擊特效**（`Scripts/VFX/CombatVFX.cs`）：骷髏揮劍淡綠色火花、聖騎士橘紅色火花、
  時鐘敵人落地的金色震波；第二關追擊敵艦上的弓箭手（Mixamo Erika Archer，`BoatArcherVisual.cs`）拉弓放箭，箭有白色尾跡、射出時有白色氣流、射中時有木屑火花。
  選單 **GeminiGrowUp > 安裝敵艦弓箭手** 建立 `Resources/Prefabs/BoatArcher.prefab`。
- **海浪與船**（`Scripts/Level/SeaWaves.cs`、`BoatNavigation.cs`）：`SeaWaves` 用和水面著色器同一套 Gerstner 公式算出海面高度，
  玩家的船和敵艦都在船頭 / 船尾 / 兩舷取海面高度，跟著浪平滑起伏、傾斜（和著色器一樣，淺水區的浪比較小；船身往下吃水 0.2 m，船和水面之間看不到縫，`VikingBoat.waveDraft`）；兩邊也都不能開上陸地（敵艦會左右找方向繞開）。
  浪的大小在 `Boat Attack Water System/Prefabs/WaterSurfaceData.asset` 的 Basic Wave Settings（目前振幅 0.8、波長 12）。
- **氧氣條**（`Scripts/Player/PlayerOxygen.cs`）：只在第二關維京村莊啟用。頭沉到海平面（y = 1.3）以下就會消耗氧氣，
  氧氣條只在水中時顯示在血條下方；氧氣用完每秒扣 10 血（溺水），浮出水面會回滿。

## 關卡流程

| # | 場景 | 過關方式 |
|---|------|----------|
| 1 | `Planet` | Boss 附近的火堆旁有一個傳送門 `Portal_Exit`，一直都在但鎖著，打倒時鐘敵人才能進入（Boss 戰前 15 m 有檢查點 `Checkpoint_Boss`，倒下從那裡復活） |
| 2 | `The_Viking_Village` | 擊敗村莊守衛 → 搭長船駛到終點（出航後 3 艘敵艦從後方追擊，離終點 50 m 時前方再出現 3 艘增援） |
| 3 | `Floor02` | 打倒 Boss（最後一關） |

- **GameManager**（`Scripts/Core/GameManager.cs`）：跨場景存在，按 Play 自動建立。管理關卡順序、淡出淡入、關卡標題。
  測試快捷鍵：`Shift + 1/2/3` 直接跳關（只在 Editor / Development Build）。
- **Portal**（`Scripts/Core/Portal.cs`、`Prefabs/Portal.prefab`）：走近按 `E` 前往下一關或指定場景。
- **SpawnOnDeath**（`Scripts/Level/SpawnOnDeath.cs`）：掛在敵人身上，死亡後讓物件出現或在原地生成 Prefab（目前沒有關卡使用；星球的傳送門由 `ClockEnemyHealth` 直接解鎖）。
- **UpgradePlant**（`Scripts/Planet/UpgradePlant.cs`）：蜘蛛附近的能力植物，按 `E` 撿起後三選一：跑速 / 攻擊力 / 最大生命。
- 選單 **GeminiGrowUp > 設定關卡流程（傳送門、能力植物）**：設定上面這些、在星球 Boss 戰前放檢查點，並更新 Build Settings（主選單 + 三關，可重複執行）。

改關卡順序：同時改 `GameManager.levels` 和 `PortalInstaller.LevelScenes`，再執行上面的選單。

## 資料夾結構

```
Assets/
├─ _Project/                 所有遊戲內容
│  ├─ Scenes/                關卡場景（+ 各自的光照資料資料夾）與測試場景
│  ├─ Scripts/
│  │  ├─ Core/               GameManager、Portal
│  │  ├─ Player/ Enemy/ Boss/ Combat/
│  │  ├─ Level/              關卡物件（門、檢查點、船、波次…）
│  │  ├─ Planet/             星球關卡專用（商人、馬、時鐘怪…）
│  │  ├─ Arena/ Dungeon/ Audio/ VFX/
│  │  └─ Editor/             選單工具（Installer / Builder）
│  ├─ Prefabs/（Planet/）    Materials/   Models/   Shaders/   VFX/   Terrain/
│  ├─ Resources/             執行時用 Resources.Load 讀的音效 / Prefab
│  └─ ThirdParty/            外部素材包（Viking Village、Mega Fantasy Props、POLY Medieval Camp、
│                            Spiders、Clock、SeedMesh、Kenney、SazenGames…）
├─ Settings/                 URP 設定
├─ Plugins/  TextMesh Pro/   套件（不要搬）
└─ InputSystem_Actions.inputactions
Art/                         原始美術檔（.blend、產生器腳本、預覽圖），不會被 Unity 匯入
```

規則：自己做的東西放 `_Project` 對應的類型資料夾；下載的素材包整包放 `_Project/ThirdParty`。
在 Unity 裡搬檔案（或連同 `.meta` 一起搬），引用才不會斷。
