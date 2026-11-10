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

### 日式村莊（`Scenes/JapaneseVillage.unity`，還沒加進關卡流程）

選單 **GeminiGrowUp > 建立日式村莊場景** 會產生整個場景（`Scripts/Editor/JapaneseVillage*.cs`），重新執行會覆蓋場景和產生出來的資產，所以大改配置請改 Builder。

- **素材**：建築、石燈籠、石階、竹子來自 `ThirdParty/URP 3D Sample Garden`（Unity 官方範本 `com.unity.template.urp-sample` 的 Garden 場景，只複製用得到的檔案）；松、楓、櫻、鳥居、岩石、地面貼圖來自 `Assets/Waldemarst`（Asset Store 的 Japanese Garden Pack，和隊友的道場場景共用，它的樹是舊管線材質，Builder 會另外做 URP 材質）。漁船、曬網架、暖簾、茶屋長椅和大部分村民這兩包沒有，仍是程式產生（部分村民改用 Tripo 模型，見下面「村民」）。
- **房屋**：`JapaneseVillageHouses` 用 Garden 模組組成 Prefab（`Prefabs/JapaneseVillage/Houses`），改 `BuildHouseRows` 裡的款式清單就能換寬度和門窗配置。村長家在三排房屋東側盡頭，直接用道場場景的主殿 `PF_MainDojo_Complete`（縮小 85%，可以走進去），外觀和最終決戰的道場一致；宅院東邊的後院有一道大門，就是通往道場的傳送門。
- **樹**：Terrain Tree（`Prefabs/JapaneseVillage/Trees`），可以用 Terrain 筆刷再刷。
- **村民**：都是 `MerchantNPC`（`sellsGoods` 關掉就是一般對話）；會走路的另外掛 `Scripts/Level/VillagerWalker.cs`，路線在 `PlaceWalkers` 裡設定。所有村民（含師父、僧侶）都是原本的 1.2 倍大（Builder 的 `NpcScale`）。村莊裡的人物頭上不顯示任何文字或指引標記。
  - 6 位成年男性村民（賣魚的阿伯、巡邏的村丁、收網的漁夫、宅院的家僕、年輕漁夫、砍竹人）用 Tripo 生成、已綁人形骨架的模型 `Models/JapaneseVillage/Villager/Villager_Peasant.fbx`（約 1.1 萬面），在 `WalkerAt` / `NpcAt` 傳 `villager: "Grey"`（或 `Indigo`、`Brown`、`Olive`）就會換成這個模型；僧侶和村長也是 Tripo 模型（僧侶見下面「祠堂的試煉」）；其他人（婦人、小孩、老奶奶、師父、行商、老漁夫）仍是程式產生。
  - 村長是另一個 Tripo 模型 `Models/JapaneseVillage/Elder/Elder.fbx`（Mixamo 骨架，貼圖用 `python Art/JapaneseVillage/make_monk_textures.py <basecolor> <normal> <metallic> <roughness> Elder` 產生），站在村長家主殿裡。台詞在 `ShrineTrial.ElderLines`：第一次對話講村子的往事（海龍、玄清道人、百足巨蟲、黑衣人奪走遺體）和玩家在村裡能做的事（草藥換藥水、上山進習武場、僧侶、後院的老門），之後依進度提示下一步，並可以選擇再聽一次往事。
  - 茶屋老闆娘、女鐵匠、村長沒跟玩家說話時有自己的日常（`Scripts/Level/NpcRoutine.cs`，路線在 Builder 的 `AddDailyRoutines`）：老闆娘招呼客人 → 擦長椅 → 坐在長椅上 → 進茶屋一陣子；女鐵匠在門口 → 打鐵 → 進鋪子顧爐火；村長看院子 → 走進主殿 → 到大櫻花旁的長椅坐。玩家走近或對話時會停下來（坐著的站起來）看著玩家。坐、打鐵等動作是程式擺骨頭。村長比其他村民再大 1.2 倍。
  - 老漁夫（`Models/JapaneseVillage/Fisherman`）坐在棧橋末端釣魚，玩家走近也不會站起來；釣竿是 `HeldProp`，跟著右手但不是他的子物件（半身像才不會被拉遠）。砍竹人用 `Models/JapaneseVillage/Woodcutter`。
  - 行商用 `Models/JapaneseVillage/Merchant`。僧侶跟玩家說話時會搔頭、歪頭、腳尖點地，一臉不耐煩（`ImpatientTalker`）。
  - 守祠的師父（`Models/JapaneseVillage/Master`）、村裡的小孩（小女孩，`Models/JapaneseVillage/ChildGirl`）也換成 Tripo 模型。小女孩會跑來跑去、蹲下來摸地上的石頭、原地蹦跳（`NpcRoutine` 的 Crouch / Hop，跑步用 `standing run forward`）。
  - 村民之間的互動（`Scripts/Level/VillageLife.cs`，Builder 的 `BuildVillageLife`）：每 7～14 秒安排一件事——兩個走動的村民約在中間碰面聊天（一個比手畫腳、一個點頭，輪流，`TalkGestures`），或一個村民離開路線去拜訪（海邊看人補網、行商的攤子、砍竹人、鐵匠鋪看兵器），結束後走回原本的路線。路上被房子或圍牆擋住就不安排。
  - 還沒有 Tripo 模型、仍是程式產生的人物：跑腿的男孩、散步的老奶奶。
  - 女村民（挑水的婦人、賣花的姑娘、茶屋老闆娘）用 Tripo 模型 `Models/JapaneseVillage/VillagerWoman/VillagerWoman.fbx`（`WomanLook`）。
  - 動畫：`Animations/JapaneseVillage/AC_Villager.controller`，Speed 0 = `unarmed idle 01`（Lite Longbow Pack）、1 = `Walk_N`（StarterAssets）。`VillagerWalker.animationWalkSpeed`（1.72 m/s）是 Walk_N 在這個模型上原本的前進速度，用來調播放速度，腳步才不會滑。
  - 衣服顏色：`Art/JapaneseVillage/make_villager_textures.py` 從灰色原圖只改衣服的顏色（皮膚、頭髮不動）。換新的 Tripo 模型時：FBX 覆蓋 `Villager_Peasant.fbx`，再執行 `python make_villager_textures.py <BaseColor.png> <Normal_Bake.png>`。
- **鐵匠鋪**（中間那排房子、主街西邊第一間，Builder 的 `BuildSmithy`）：開著大門看得到裡面的爐子，門口有鐵砧和三座武器架。門口的女鐵匠（Tripo 模型 `Models/JapaneseVillage/Blacksmith`，台詞在 `Scripts/Level/BlacksmithShop.cs`）第一次對話說明各種武器，之後每次都可以選要帶哪一組：
  - 大刀（原本的光劍）：判定距離比原本多約 0.5 m（前方 1.6 m、半徑 1.6 m），可以舉盾。
  - 弓與小刀：和維京村莊海戰同一套弓箭操作（`PlayerCombat.bowMode`：右鍵瞄準、左鍵射箭），箭 20 支、每支 14 傷害；射完自動換成小刀（判定最遠約 2 m、出手快 40%、每下 70% 傷害）。小刀有自己的三段招式：直刺 → 反手橫斬 → 反握下刺（`PlayerLoadout.KnifeMoves`，程式擺姿勢，蓋在原本揮刀的手臂動作上）。按住右鍵瞄準時，右手先從右後腰的箭筒抽一支箭搭上弦（約 0.5 秒），接著開始蓄力：畫面中央一個大圓往內收、一個小圓固定（半徑 70 / 12 像素，1080p），大圓收到跟小圓重合時（約 1.1 秒）放箭最準、威力最大（傷害 1.6 倍、不偏），太早或太晚都會變差；沒瞄準直接按左鍵射會亂飄（±9°）、傷害 0.6 倍。射出去的箭是 `Resources/Weapons/JV_Arrow`（竹箭桿、鋼箭頭、紅白箭羽），箭筒是 `JV_Quiver`。畫面右下角用弓的圖示 + 20 支箭的小圖表示剩下幾支，射完變成小刀的圖示（圖示在 `Resources/UI`，`python Art/JapaneseVillage/make_ui_icons.py` 產生）。回鐵匠鋪再選一次就補滿。弓的模型是 Tripo 的 `Models/JapaneseVillage/Bow`（換掉 Erika 的弓，掛在同一個位置）。
  - 長槍：戳擊（出招時右手往前伸、槍頭朝前），判定 1.2 ~ 3.6 m、左右較窄。
  - 換武器的程式在 `Scripts/Player/PlayerLoadout.cs`（自動掛在每個場景的玩家身上，只調 PlayerCombat 現有的欄位；選的武器和剩下的箭跨場景保留，但不寫進存檔）。小刀、長槍、弓的 Prefab 由 Builder 產生在 `Resources/Weapons`。武器架只擺玩家沒帶的武器（`WeaponRackDisplay`）；走近武器架按 E 也能直接換，架子上方會浮出一張說明卡（`WeaponRackSlot`）。
  - 打擊回饋（`PlayerLoadout`）：打中東西時鏡頭輕微震動、頓幀約 0.05 秒，有手把的話手把也震一下（`PlayerCombat.OnDealtHit`）。
- **祠堂的試煉**（`Scripts/Level/ShrineTrial.cs`，流程和台詞都在這裡）：
  1. 走進山上的祠堂，跟「守祠的師父」對話，選擇進入習武場，後門就會打開（`SlidingDoor`）。
  2. 習武場在祠堂後面（144 × 36 m），圍牆內不會被打倒。
     - 東半邊是一道 14 m 深的深溝（Terrain 挖洞 + 石牆），橫跨整個院子；只能用拉索抓著 6 根懸空木條（`TetherTrainingBeam`）一路盪到對岸。掉進溝裡會回到起點，還沒全部碰過的話燈籠全熄、從頭來（`TetherCourse`）。木條的「◆」標記要進入習武場才顯示。第 2～5 根木條下面各吊一塊木板（`SwayPlank`）可以站上去喘口氣，但站上去會下沉、搖晃、嘎吱作響，約 1.6 秒就翻過去，3 秒後翻回來。
     - 西半邊 3 根木樁人偶（`TrainingDummy`）：打一下就變成約 3 m 高的木頭人（Banana Man 模型換成木頭材質、背上綁一根木樁、四肢外側各綁兩段短木樁，動畫用 `Animations/JapaneseVillage/AC_WoodenFighter`），會走過來用雙手推人（雙手往後收就是要推了）；共打中 6 下過關，過關後鞠躬、頭綁紅布。大小改 Builder 的 `FighterHeight`（木樁、碰撞範圍、推擊距離一起跟著縮放）。
  3. 習武場完成後，僧侶從祠堂後門走進習武場（`Scripts/Level/MonkSparring.cs`），跟他對話選「請大師指教」開始過招：看他抬手就舉盾或翻滾，出完招之後的空檔打中他一下算一回合；沒有空檔時打他會被擋下來。來回 5 回合結束，僧侶鞠躬。
     - 輕功：被打中第 1、3 下之後，他會一躍跳上北邊的圍牆頂，站一下再從牆上飛撲到玩家面前，落地直接點穴（`wallPerches`、`leapSpeed`、`diveSpeed`）。
     - 點穴：快速的戳擊（Attack1）真的打中的話玩家暈眩 1.6 秒（`ThirdPersonController.Stun`，頭上繞著星星 `DizzyStars`）；舉盾擋下或翻滾躲開就沒事。另一招推掌（Attack2）只會把人推開。
    僧侶是 Tripo 模型 `Models/JapaneseVillage/Monk/Monk.fbx`（Mixamo 骨架，貼圖用 `Art/JapaneseVillage/make_monk_textures.py` 轉成 2048 並合成金屬度貼圖），Animator `AC_Monk`：平常空手站 / 走路，過招時用劍盾包的架勢、揮擊（空手就是掌擊）和格擋動畫。
  4. 祠堂地上散著 4 幅古畫和 1 首詩（古畫的 `SteleInscription` 開了 `pictureOnly`：只秀畫本身、不包在卷軸裡，標題和說明寫在下面；詩仍是卷軸），要全部看過；混在裡面的 6 本村民的書可以翻開，但不算進度。畫和詩的文字在 Builder 的 `Paintings`、`PoemText`、`VillagerBooks`。
  5. 全部完成後跟習武場裡的僧侶對話，選擇開門，村長家後院大門的鎖就解開了（`Scripts/Level/DojoGate.cs`）。走到門前按 E 或直接往門走過去，兩扇門往外推開、門洞亮起墨色漩渦；再按 E 進入道場（`DojoArena_ArchitecturePreview`），換場景時黑底輪播四幅古畫（`PaintingSlideshow`，每張 1.8 秒，新場景載好、四張都播過才淡出；這段時間遊戲先暫停）。

  四幅畫用 `Art/JapaneseVillage/Paintings_Source.png`（2 × 2 拼成一張，左上起：海上鬥龍、與民同醉、百足現世、黑衣奪骸），執行 `python Art/JapaneseVillage/generate_paintings.py` 會切成四張、做舊（撕破的透明邊、缺角、蟲洞、水漬、摺痕、泛黃，`wear()`）後放到 `Materials/JapaneseVillage/Paintings`；地上的畫用 Alpha Clipping，破掉的地方看得到底下的榻榻米；詩和後院大門的墨色漩渦由同一支程式畫。要換畫：換掉 Paintings_Source.png 再執行一次，或直接覆蓋同名 PNG。
  傳送門目的地是 Builder 的 `FinalBattleScene`；重建場景時會自動把它加進 Build Settings。注意選單 **設定關卡流程** 會重寫 Build Settings（只留主選單 + 三關），執行過之後要重建日式村莊或手動把道場場景加回去。進度只存在場景裡，重新載入會從頭開始。

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
