# Miguel's Revenge

Unity 6000.6.3f1（URP）第三人稱動作遊戲。

## 遊戲流程

`MainMenu`（主選單）→ 按「開始遊戲」→ 三個關卡 → 通關畫面 / Game Over 畫面

- **MainMenu**（`Scripts/Core/MainMenu.cs`、`Scenes/MainMenu.unity`，Build Settings 第一個）：
  標題、用 GUI Text 說明規則和操作，「開始遊戲」「結束遊戲」按鈕（uGUI Button，畫面由程式產生）。
- **生命**：玩家有 3 條命（`GameManager.maxLives`），倒下扣一條命並在檢查點復活，用完 → **Game Over**
  （重新挑戰本關 / 從第一關重新開始 / 回主選單）。
- **通關**：打倒第三關 Boss → 通關畫面（通關時間、剩餘生命；重新開始 / 回主選單）。
- **暫停**：遊戲中按 `Esc` / 手把 Start（繼續 / 存檔 / 讀取存檔 / 設定 / 重新開始本關 / 回主選單）。漁村這條主線（`GameManager.storyLevels`：漁村 `JapaneseVillage` ↔ 道場 `DojoArena_ArchitecturePreview`）也一樣可以暫停、存讀檔、進設定（漁村風格：暫停選單是一幅掛軸，跟對話框同一套牛皮紙、深紅褐色裱布、上下木軸，墨色的「暫停」大字旁邊蓋一個朱印、下面寫在漁村還是道場，按鈕是紙上的淡墨框、選到或滑鼠移上去變朱紅；設定畫面整個寫在一大幅掛軸上，淺色的字都換成墨色，`UIKit.Washi`）。「從漁村重新開始」不管在漁村還是道場都回到漁村，帶著最近一次進漁村時的狀態（`GameManager.RestartStory`）。換場景時帶著玩家的狀態（血量、背包、錢、魚、造型），進場景時自動存檔，主選單的「繼續遊戲」也讀得到；但不算進「下一關」的順序，沒有關卡標題和轉場動畫。
- **用手把選選項**（`MerchantNPC`，村民對話的選項和所有對話框商店）：十字鍵 ↑ ↓ 或左搖桿上下移動游標（亮起來的那一格，推著不放會連續移動），A 選，B / Y 離開或選最後一項（`PadInput.NavigatePressed`）；鍵盤照樣按數字鍵。
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
- **玩家動作音效**（`Scripts/Audio/PlayerSfx.cs`，`PlayerCombat` 開場自動掛上，只加聲音不改動作）：起跳、二段跳有風聲，在空中待超過 0.3 秒落地有「咚」一聲（掉得越快越重），揮刀墊一層風切聲，打中敵人墊一層悶響（重擊換更重的）。音效在 `Resources/Audio/SFX/Gen/`，用 `Art/Audio/synth_sfx.py` 合成的原創音效（鳥叫、喝酒的聲音也在這裡）。
- **鳥叫**（`Scripts/Audio/BirdAmbience.cs`）：庭園裡每隔幾秒在樹梢高度隨機叫一聲（麻雀、顫音、口哨、日本樹鶯、遠處的斑鳩），有時另一隻會回應；玩家離太遠就不叫。日式村莊放在村長家庭園（Builder 的 `GardenBirds`）；道場不用改場景，開場時找到 `CourtyardAndGarden` / `LivingGarden` 就自動在它的範圍放一個。
- **道場老師父喝酒**：開場坐著喝酒時，酒壺舉到嘴邊會咕嚕咕嚕吞兩三口，放下時「哈——」一口氣（`DojoOpeningCutscene.DrinkSound`）。
- **英文配音**（`Scripts/Audio/DialogueVoice.cs`）：村民對話（`MerchantNPC`）和劇情對話（`StoryDialogue`，例如道場開場）字幕維持中文，每一句播英文語音。語音檔在 `Resources/Audio/Voice/`，對照表 `voice_index.txt`（中文台詞 → 語音檔 → 英文）；用 Windows 內建的英文語音 Zira 念，男聲、老人、小孩靠調音高區分。對照表裡沒有的台詞就不出聲：改了台詞要在 `Art/Audio/voice_lines.tsv` 加上英文，再照 `Art/Audio/make_voice.py` 開頭寫的三個步驟重新產生（數字會自動對上，例如「還差 3 個」和「還差 2 個」共用一段）。
- **玩家說話**：`MerchantNPC` 的台詞開頭寫 `@` 就是玩家（米格爾）說的，名字和左邊的半身像會換成玩家；選了選項之後，玩家會先把選的那句話說出來，再接 NPC 的回答。
- **攻擊特效**（`Scripts/VFX/CombatVFX.cs`）：骷髏揮劍淡綠色火花、聖騎士橘紅色火花、
  時鐘敵人落地的金色震波；第二關追擊敵艦上的弓箭手（Mixamo Erika Archer，`BoatArcherVisual.cs`）拉弓放箭，箭有白色尾跡、射出時有白色氣流、射中時有木屑火花。
  選單 **GeminiGrowUp > 安裝敵艦弓箭手** 建立 `Resources/Prefabs/BoatArcher.prefab`。
- **海浪與船**（`Scripts/Level/SeaWaves.cs`、`BoatNavigation.cs`）：`SeaWaves` 用和水面著色器同一套 Gerstner 公式算出海面高度，
  玩家的船和敵艦都在船頭 / 船尾 / 兩舷取海面高度，跟著浪平滑起伏、傾斜（和著色器一樣，淺水區的浪比較小；船身往下吃水 0.2 m，船和水面之間看不到縫，`VikingBoat.waveDraft`）；兩邊也都不能開上陸地（敵艦會左右找方向繞開）。
  浪的大小在 `Boat Attack Water System/Prefabs/WaterSurfaceData.asset` 的 Basic Wave Settings（目前振幅 0.8、波長 12）。
- **氧氣條**（`Scripts/Player/PlayerOxygen.cs`）：只在第二關維京村莊啟用。頭沉到海平面（y = 1.3）以下就會消耗氧氣，
  氧氣條只在水中時顯示在血條下方；氧氣用完每秒扣 10 血（溺水），浮出水面會回滿。
- **拉索（繩子 + 鉤爪）**（`Scripts/Player/EnergyTetherSkill.cs` 的 `look = Rope`，外觀在 `Scripts/Player/TetherRopeVisual.cs`）：操作照舊（牽引鍵瞄準、左鍵鎖定、滾輪↑ 拉自己 / ↓ 拉目標、按住左鍵甩出去），外觀從紅藍能量光束改成一條麻繩：先從右手丟出三爪的鐵鉤爪（飛的時候繩子鬆鬆的甩動，勾住時「喀」一聲、繩子拉直），勾住了才開始拉，右手臂伸向鉤爪。每拉一次照距離扣精力：貼著目標 25%、最遠射程（90 m）75%，中間照比例（`minStaminaCost` / `maxStaminaCost`）；精力不夠就拉不動（精力條閃紅，連 25% 都不夠就不進入瞄準）。拉的時候武器立刻收起來（`PlayerLoadout.Holster(owner, instant: true)`，不做收刀手勢），拉完再拔出來。鉤爪、繩子的紋路都是程式做的；`look = Energy` 可以換回原本的光束。鉤索的造型（繩子、鉤爪的顏色）可以在漁村的行商那裡買（`TetherRopeVisual.SetSkin`）。
- **劇情動作前先收武器**（`PlayerLoadout.Holster` / `Unholster`）：喝藥、在暫居地睡覺之前，右手往左腰（刀）或右肩後面（長槍）一收、武器消失，拿弓的左手把弓背到左肩後面；做完再從那裡拔出來（原本拿在手上的才拿，大刀照原本的方式聚合出來）。
- **射出去的箭**：直接射到地上、牆上的照飛來的方向插著；射中敵人（或插得太高）的箭不再平躺，而是照飛來的方向、再往下彎一點插進後面 / 腳邊的地上（`ArrowPickup.SpawnFallen`）。
- **對話框**（`GameUI.ScrollPanel`，劇情對話 `StoryDialogue` 和村民對話 `MerchantNPC` 共用）：一幅卷軸的上半截，下半截在畫面外（紙一直延伸到畫面最下面）：最上面一根木軸、兩端圓的軸頭，下面一條素面深紅褐色的裱布夾兩道金線，紙的左右也是窄窄的裱布；紙是素面的舊牛皮紙，像古畫的底（黃褐色、深淺不太均勻、細細的纖維，沒有任何圖案，`GameUI.PaintKraft`），邊緣微微發暗。紙是淺色的，上面的字改用深色：內文墨色（`GameUI.Ink`），名字用 `GameUI.OnPaper` 把原本的名字顏色壓深（沒指定顏色的是朱紅 `GameUI.Cinnabar`），商人的選項卡片、按鍵框也有紙上用的版本（`GameUI.Option` / `GameUI.Key` 的 `onPaper`）。其他選單還是原本的面板。
- **打擊手感**（`PlayerLoadout.OnDealtHit`）：刀、長槍砍中敵人時，整個畫面停在砍中的那一下 0.09 ~ 0.14 秒（傷害越高停越久，原本 0.04 ~ 0.07 秒），接著 0.07 秒走得比平常快 1.7 倍，像把刀從敵人身上快速抽出來；射箭打中照原本的短頓幀。
- **翻滾**：精力從 20% 降到 12%（`PlayerStamina.rollCost`，空中側閃也一樣）；沒按方向鍵時往鏡頭看的方向翻（`ThirdPersonController.rollTowardCameraWhenIdle`，關掉 = 原本往角色面向翻）；攻擊到一半隨時可以翻，翻的那一下先把攻擊整個取消（攻擊幀歸零、下一刀從第一段開始），翻滾的速度不會被攻擊時壓低的移動速度拖慢。
- **戰鬥音樂、心跳聲**（`Scripts/Audio/CombatMusic.cs`，自動執行，每一關都有）：跟敵人打起來（`CombatLog.InFight`：打中敵人或被敵人打到，練習的木人、跟僧侶過招不算）就換成緊湊的戰鬥曲（`Music/bgm_battle_clock`），對手都倒下或 7 秒沒再交手，過 1.5 秒換回這一關的背景音樂；已經在播 Boss 曲 / 其他戰鬥曲時不搶。血量低於 20% 時聽得到心跳（`LowHealthHeartbeat`，程式合成的兩下低沉悶響），血越少跳得越快、越大聲，補血到 20% 以上或倒下就停。
- **血量 / 精力條**（`Scripts/Player/VitalsHud.cs`，遊戲開始自動建立、換場景不會消失）：日式的樣子，頭尾斜切的平行四邊形、深色底、咖啡色外框，不顯示文字和數字；血條的紅色壓暗、對比度低（不搶畫面）；血條右邊一排小小的、往右斜的暗紅血滴是剩下的命（取代原本的「生命 × 3」，少一條命那顆血滴破掉、濺出小血點，變成咖啡色的空殼）。血量像液體：數值用彈簧追（晃一下才停），液面邊緣是波浪，扣血時波浪變大、濺出幾滴血，失去的那段先留暗紅色再慢慢流掉，補血時液面發亮、冒小氣泡，血少時一明一暗。精力像氣體：半透明的霧、濃淡隨時間飄移、尾端散開成煙，用掉的那段化成一團團煙往上飄，平常也偶爾冒出細煙；精力不夠時外框閃紅，精力低時偏橘。用 GL 在 OnGUI 畫，原本 `PlayerHealth` / `PlayerStamina` 的長方形條在它畫的時候不畫（`PlayerHealth.SimpleBarHidden`）。茶屋甜點的護盾是血條上一層淡金色（長度 = 護盾 / 最大生命，擋下傷害時閃一下），額外精力是精力條右邊多出來的一截金色的霧。

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

- **素材**：建築、石燈籠、石階、竹子來自 `ThirdParty/URP 3D Sample Garden`（Unity 官方範本 `com.unity.template.urp-sample` 的 Garden 場景，只複製用得到的檔案）；松、楓、櫻、鳥居、岩石、地面貼圖來自 `Assets/Waldemarst`（Asset Store 的 Japanese Garden Pack，和隊友的道場場景共用，它的樹是舊管線材質，Builder 會另外做 URP 材質）。漁船、曬網架、暖簾和大部分村民這兩包沒有，仍是程式產生；長椅（茶屋兩張、村長家櫻花樹下、女書生）是 Tripo 模型 `Models/JapaneseVillage/Bench`（Builder 的 `PlaceBench`，坐面高 `BenchSeatHeight` = 0.48 m）；茶屋的大紅傘是 Tripo 和傘 `Models/JapaneseVillage/Parasol`（`PlaceParasol`：模型是斜拿著的，用傘面的法線轉直、放大到直徑 3 m，傘柄底下補一根竹竿）；日式道具組 `Models/JapaneseVillage/JpProps`（11 樣，`PlaceKitPiece` 拆開擺、`alignLong` 把斜放的轉正）分散在村裡：茶屋旁兩個酒樽和一塊立招牌、行商攤子旁的包鐵角木箱和米草包、砍竹人旁的一捆竹子和竹籬笆、鐵匠鋪門口的兵器架和刀架、女書生長椅旁的小石燈籠、藥師陶爐旁的陶甕和木板屏風（部分村民改用 Tripo 模型，見下面「村民」）。
- **房屋**：`JapaneseVillageHouses` 用 Garden 模組組成 Prefab（`Prefabs/JapaneseVillage/Houses`），改 `BuildHouseRows` 裡的款式清單就能換寬度和門窗配置。村長家在三排房屋東側盡頭，直接用道場場景的主殿 `PF_MainDojo_Complete`（縮小 85%，可以走進去），外觀和最終決戰的道場一致；宅院東邊的後院有一道大門，就是通往道場的傳送門。
- **樹**：Terrain Tree（`Prefabs/JapaneseVillage/Trees`），可以用 Terrain 筆刷再刷。
- **村民**：都是 `MerchantNPC`（`sellsGoods` 關掉就是一般對話）；會走路的另外掛 `Scripts/Level/VillagerWalker.cs`，路線在 `PlaceWalkers` 裡設定。所有村民（含師父、僧侶）都是原本的 1.2 倍大（Builder 的 `NpcScale`）。村莊裡的人物頭上不顯示任何文字或指引標記。
  - 6 位成年男性村民（賣魚的阿伯、巡邏的村丁、收網的漁夫、宅院的家僕、年輕漁夫、砍竹人）用 Tripo 生成、已綁人形骨架的模型 `Models/JapaneseVillage/Villager/Villager_Peasant.fbx`（約 1.1 萬面），在 `WalkerAt` / `NpcAt` 傳 `villager: "Grey"`（或 `Indigo`、`Brown`、`Olive`）就會換成這個模型；僧侶和村長也是 Tripo 模型（僧侶見下面「祠堂的試煉」）；其他人（婦人、小孩、老奶奶、師父、行商、老漁夫）仍是程式產生。
  - 村長是另一個 Tripo 模型 `Models/JapaneseVillage/Elder/Elder.fbx`（Mixamo 骨架，貼圖用 `python Art/JapaneseVillage/make_monk_textures.py <basecolor> <normal> <metallic> <roughness> Elder` 產生），站在村長家主殿裡。台詞在 `ShrineTrial.ElderLines`：第一次對話講村子的往事（海龍、玄清道人、百足巨蟲、黑衣人奪走遺體）和玩家在村裡能做的事（草藥換藥水、上山進習武場、僧侶、後院的老門），之後依進度提示下一步，並可以選擇再聽一次往事。
  - 茶屋老闆娘、女鐵匠、村長沒跟玩家說話時有自己的日常（`Scripts/Level/NpcRoutine.cs`，路線在 Builder 的 `AddDailyRoutines`）：老闆娘招呼客人 → 擦長椅 → 坐在長椅上 → 進茶屋一陣子；女鐵匠在門口 → 打鐵 → 進鋪子顧爐火；村長看院子 → 走進主殿 → 到大櫻花旁的長椅坐。玩家走近或對話時會停下來（坐著的站起來）看著玩家。坐、打鐵等動作是程式擺骨頭。村長比其他村民再大 1.2 倍。進屋（茶屋老闆娘、藥師）不是直接穿過去：走到門口停下來面向門，正門是兩片拉門（`Scripts/Level/HouseEntryDoor.cs`，Builder 的 `MakeEntryDoor` 把這兩棟房子的門板做成會開會關），先踏上台階站到門前，兩手伸到兩片門板的引手上（`HouseEntryDoor.Grip`，左邊那片用左手）把門往兩邊推開，跨過門檻後轉身、伸手把門拉上才進屋；出來時在門檻後面伸手拉開門、跨出來、轉身把門拉上，再走下台階（手臂用兩節骨頭的 IK，門板滑遠了手放下來；拿扇子的先把扇子收起來）。藥鋪只開一扇（`MakeEntryDoor` 的 singleLeaf）：藥師先橫著挪到引手前面，用右手拉開，從那一半的縫走進去。這兩間的門後面不是黑的：一般房子裡面是整塊黑色的實心內芯，`BuildEntryRoom` 在門後挖出一間約 5.2 × 3.6 m 的房間（木地板、腰板 + 白牆、柱子和長押、木天花板和樑、暖色燈），後牆的拉門開一道縫，後面還有一小段亮著行燈的走廊；茶屋是架高的榻榻米座敷（矮桌、茶具、坐墊、掛軸、插花、茶具架、客人坐的長凳），藥鋪是一整排百味箪笥、藥罐架、樑上倒吊的草藥和帳場矮桌。正面每 4 m 一根的柱子如果落在拉門中間（門從 2 m 的地方開始的房子）就不放（`BuildFacade`）。
  - 老漁夫（`Models/JapaneseVillage/Fisherman`）坐在棧橋末端釣魚，玩家走近也不會站起來；釣竿是 `HeldProp`，跟著右手但不是他的子物件（半身像才不會被拉遠）。砍竹人用 `Models/JapaneseVillage/Woodcutter`。
  - 行商用 `Models/JapaneseVillage/Merchant`。第一次對話會問玩家從哪裡來（海的那一邊 / 翻過北邊的山 / 不記得了，`NpcQuestion`），依回答回話，之後再來對話也記得玩家的回答；他不再收草藥，改成指路去找藥師。
  - 手和衣服黏在一起：Tripo 自動綁骨會把垂在大腿旁的手和褲子 / 衣襬綁在同一塊，手一抬就把衣服拉出去。Builder 的 `UnhookAllNpcArms` 對所有村民的兩隻手臂都檢查一次，只拆腰以下、明顯比較靠近腿 / 身體、綁骨本來就有一點跟著腿 / 骨盆、又不是整個綁在手臂上的頂點（整個綁在手臂上的是袖子，要跟著手抬起來）；一隻手臂超過 400 個就當成寬袖子 / 長袍不改，也不再挖掉三角形（會在衣服上留下洞、看起來衣服消失）。藥師兩隻手用比較寬的條件另外處理。
  - 藥師（`NPC_Apothecary`，`Models/JapaneseVillage/Apothecary`，Builder 的 `PlaceApothecary`）：藥鋪是第一排房子主街東側第一間，門口擺著 Tripo 的藥師道具組（`Models/JapaneseVillage/ApoTable`，一個模型裡有 12 樣東西，Builder 的 `PlaceKitPiece` 一樣一樣拆開擺：矮桌上放石臼、藥草、葫蘆藥瓶、一包藥，旁邊小板凳、兩盤草藥，另一邊陶爐上放陶壺、地上一塊布和攪藥棒；遊戲開始時 `StripMeshParts.keepIsland` 才只留下各自那一塊，所以編輯器裡看到的是整組疊在一起）。陶爐的炭火會發光，旁邊有封口的陶甕、擋風的木板屏風。藥師磨藥時右手握著石臼裡的杵畫圈（手上不另外拿杵）。日常：坐在桌前用藥杵磨藥（`NpcRoutine` 的 `Grind`，藥杵是 `NpcRoutine.tool`，只有磨藥時才拿出來）→ 進鋪子裡煎藥 → 出來在陶爐前顧火 → 站在門口看看。他的左手上山採藥時摔傷了，整隻手垂在身體旁邊不動（`NpcRoutine.injuredLeftArm`，最後才擺、蓋掉走路和做事的動作），磨藥、顧火都只用右手。模型的衣襬、褲子被 Tripo 綁到了手臂和指尖的骨頭，手一動就被勾起來：Builder 的 `UnhookFromArm` 找出離腿 / 身體比離手臂近、或綁在指尖卻離指尖很遠的頂點，遊戲開始時 `StripMeshParts` 把它們從手臂的骨頭上拿掉（改跟著腿和身體動）；腰以下手指 / 前臂和衣襬黏在一起的那一百多片三角形直接切掉（`StripMeshParts.removedTriangles`），手抬起來時衣服就不會被拉成一大片。用草藥換治癒藥水（4 株）、精力藥水（3 株）找他。僧侶跟玩家說話時會搔頭、歪頭、腳尖點地，一臉不耐煩（`ImpatientTalker`）。
  - 守祠的師父（`Models/JapaneseVillage/Master`，偶爾在祠堂裡走動、蹲下整理地上的書）、村裡的小孩（小女孩，`Models/JapaneseVillage/ChildGirl`）也換成 Tripo 模型。小女孩會跑來跑去、蹲下來摸地上的石頭、原地蹦跳（`NpcRoutine` 的 Crouch / Hop，跑步用 `standing run forward`）。
  - 村民之間的互動（`Scripts/Level/VillageLife.cs`，Builder 的 `BuildVillageLife`）：每 7～14 秒安排一件事——兩個走動的村民約在中間碰面聊天（一個比手畫腳、一個點頭，輪流，`TalkGestures`），或一個村民離開路線去拜訪（海邊看人補網、行商的攤子、砍竹人、鐵匠鋪看兵器），結束後走回原本的路線。路上被房子或圍牆擋住就不安排。
  - 跑腿的男孩（`Models/JapaneseVillage/ErrandBoy`，走得快所以播跑步動畫，`VillagerWalker.runFrom`；跑步用的是原地跑的副本 `Animations/JapaneseVillage/JV_Run_InPlace.anim`，原本的動畫把往前的位移烤在姿勢裡，會一直卡頓）、散步的老奶奶（`Models/JapaneseVillage/Granny`）、巡邏的村丁（`Models/JapaneseVillage/Guard`，武士打扮；模型右手本來拿著一根長棍，走路時會跑位，所以拿掉了：Builder 的 `WeaponMask` 用綁定姿勢找出長棍的頂點，遊戲開始時 `StripMeshParts` 把那些三角形刪掉；腰上的刀和衣服長在一起，留著當服裝）也是 Tripo 模型：村裡的人物現在全部都有模型了。
  - 女書生（`NPC_Scholar`，`Models/JapaneseVillage/Scholar`）：從京城來抄錄傳說的書生，坐在鳥居西南邊（北街和石階之間）的長椅上捧著書看，看一陣子會走到鳥居前站著翻書對照，再走回長椅（`NpcRoutine` 的 `Read`，`seatY` 大於 0 就坐著看；書是 `NpcRoutine.book`，用 Tripo 古書組 `Models/JapaneseVillage/Books` 裡攤開的那本，只有看書的時候才出現，大約每 7 秒翻一頁；組裡闔著的書和卷軸擺在長椅兩頭）。沒在看書的時候右手拿著摺扇（`Models/JapaneseVillage/Fan`，`NpcRoutine.fan`）在臉旁慢慢搧。跟她說話會聊玄清道人的傳說，提示祠堂裡的古畫和詩。跟她說到第三次話，她會偷偷告訴玩家已經把村長家後院的門打開了，要玩家保密；這次對話結束時門就解鎖並推開（`SecretGateOpener`），不用等祠堂的試煉做完（試煉做完、門已經開了的話就講別的話）。
  - 女村民（挑水的婦人、賣花的姑娘、茶屋老闆娘）用 Tripo 模型 `Models/JapaneseVillage/VillagerWoman/VillagerWoman.fbx`（`WomanLook`）。
  - 動畫：`Animations/JapaneseVillage/AC_Villager.controller`，Speed 0 = `unarmed idle 01`（Lite Longbow Pack）、1 = `Walk_N`（StarterAssets）。`VillagerWalker.animationWalkSpeed`（1.72 m/s）是 Walk_N 在這個模型上原本的前進速度，用來調播放速度，腳步才不會滑。
  - 衣服顏色：`Art/JapaneseVillage/make_villager_textures.py` 從灰色原圖只改衣服的顏色（皮膚、頭髮不動）。換新的 Tripo 模型時：FBX 覆蓋 `Villager_Peasant.fbx`，再執行 `python make_villager_textures.py <BaseColor.png> <Normal_Bake.png>`。
- **雙尾虎**（`Scripts/Level/TigerBeast.cs`，Builder 的 `BuildTiger`）：趴在石階西側竹林的空地（約 (-24, 84)），玩家走近就怒吼衝過來決鬥：遠一點伏低蓄力飛撲（翻滾的無敵時間可以躲開），近身揮爪，越近揮得越頻繁（左右爪交替連揮最多三下，破綻和冷卻也越短），出招後有破綻；在撲的距離內冷卻時沿著圓弧繞著玩家走（身體朝走的方向、頭轉過去盯著玩家），貼很近時橫著小步挪。步伐照實際的移動算：每條腿往那隻腳實際移動的方向擺（身體的速度加上轉身造成的速度，原地轉身時前後腳往相反方向踏），橫著走、轉身都是四隻腳一步步踏出去。玩家死了就在原地趴下，不追也不走開，玩家再走近才又起來。被打中 8 下就再吼一聲逃往竹林深處，原地留下三株草藥。身長約 3.6 m，腿比模型原本長 1.22 倍（`TigerBeast.legLength`：遊戲開始時把膝蓋、腳踝的骨頭往外移，身體跟著抬高、腳還是踩在地上），身體往左右加寬 1.2 倍（`bodyWidth`），四條腿往外張開 9 度（`legSplay`），關節照貓科用腳趾站的力學微彎（`legBend`：後腳大腿往前、小腿往後、蹠骨往前的 Z 字形，前腳手肘、手腕微彎，腳掌平貼地面，身體壓低彎掉的高度），腳踩地用兩節骨頭的 IK（`PlantFeet`：在地上時每隻腳都放到地面上（地形或石階、石頭，往下打射線）、腳掌方向不變，後腳一定踩著；飛撲、趴下、揮爪舉起來的那隻不管；模型放大得不均勻，量一次修一次最多三次；後腳伸直了還踩不到地就把身體壓低；模型原本右後腳比較短、膝蓋彎著，遊戲開始時 `EqualizeLegs` 單獨把它拉長），走路時往前跨的那半圈才收起關節把腳抬起來（前腳手腕往後折、後腳膝蓋和腳踝一起折，`swingFold`），踩在地上往後推時腿撐直、剛落地時膝蓋微微一沉，步伐節奏放慢、每一步跨大（`strideScale`），吼聲用 dragon_roar 降低音調，動作（蓄力 0.8 秒、飛撲高 2.4 m、出招後破綻 1.2 秒）都比較慢、幅度大（飛撲時前腳往前伸直、略低於水平）、姿勢之間會慢慢混合。決鬥時畫面上方有血條。模型是 Tripo 四足骨架（`Models/JapaneseVillage/Tiger`，用 Generic 匯入保留蒙皮），沒有動畫，所有動作都是程式擺骨頭。
- **鐵匠鋪**（中間那排房子、主街西邊第一間，Builder 的 `BuildSmithy`）：開著大門看得到裡面的爐子，門口有鐵砧和三座武器架。門口的女鐵匠（Tripo 模型 `Models/JapaneseVillage/Blacksmith`，台詞在 `Scripts/Level/BlacksmithShop.cs`）第一次對話說明各種武器，之後每次都可以選要帶哪一組：
  - 大刀（原本的光劍）：判定距離比原本多約 0.5 m（前方 1.6 m、半徑 1.6 m），可以舉盾。
  - 弓與小刀：和維京村莊海戰同一套弓箭操作（`PlayerCombat.bowMode`：右鍵瞄準、左鍵射箭），箭 20 支、每支 14 傷害（畫面下方不顯示船戰的射箭說明，`PlayerCombat.hideBowHint`）；射完自動換成小刀（判定最遠約 2 m、出手快 40%、每下 70% 傷害）。小刀有自己的三段招式：直刺 → 反手橫斬 → 反握下刺（`PlayerLoadout.KnifeMoves`，程式擺姿勢，蓋在原本揮刀的手臂動作上）。按住右鍵瞄準時，右手先從右腰旁邊（貼著腰間、開口在腰帶高度）的箭筒抽一支箭搭上弦（約 0.5 秒），接著開始蓄力：畫面中央一個大圓往內收、一個小圓固定（半徑 70 / 12 像素，1080p），大圓收到跟小圓重合時（約 1.1 秒）放箭最準、威力最大（傷害 1.6 倍、不偏），太早或太晚都會變差；沒瞄準直接按左鍵射會亂飄（±9°）、傷害 0.6 倍。射出去的箭和搭在弦上的箭是 `Resources/Weapons/JV_Arrow`（Tripo 的箭模型 `Models/JapaneseVillage/Arrow`，長 0.94 m；箭頭朝哪一端是看兩端的金屬度自動判斷的；沒有模型時退回程式做的竹箭），箭筒是 `JV_Quiver`。畫面右下角用弓的圖示 + 20 支箭的小圖表示剩下幾支，射完變成小刀的圖示（圖示在 `Resources/UI`，`python Art/JapaneseVillage/make_ui_icons.py` 產生）。回鐵匠鋪再選一次就補滿。弓的模型是 Tripo 的 `Models/JapaneseVillage/Bow`（換掉 Erika 的弓，掛在同一個位置）。
  - 長槍：雙手拿（右手在右腰稍後、左手在肚子前面握著槍桿，平常槍放低在腰間、槍頭朝前稍微往上；手掌中心對準槍桿），戳擊時雙手一起往前推、槍放平（`PlayerLoadout.TwoBoneIK` 擺手），判定 1.2 ~ 3.6 m、左右較窄；出手比大刀快 20%，每下 85% 傷害（`PlayerLoadout.SpearStats`）。
  - 小刀、長槍一直拿在手上，不播大刀那種聚合 / 散開的收刀特效（`PlayerLoadout.KeepWeaponInHand`）；大刀照原本的。
  - 拉弓：瞄準時左手（拿弓）往瞄準的方向伸直；抽好箭之後蓄力時，右手把弓弦從弓上沿著箭的方向直直往後拉到下巴前面（靠弦點，跟右肩一樣高），手肘往外屈、維持水平不垂下去（大約在兩個圓重合前拉滿）。箭的那條線從靠弦點對著準心打到的那一點（`PlayerLoadout.DrawLine` / `AimPoint`，跟 `PlayerCombat.ShootArrow` 一樣從鏡頭中央打線），握弓的左手放在這條線上；射出去的箭從弓上那支箭的位置、沿著同一條線飛向原本的目標（`AlignShotWithDraw`），拉弓的角度跟射出去的角度一樣。瞄準時上半身側過來（`TurnTorsoForAim`：兩肩連線比瞄準方向往右轉到約 32 度，動畫本來就側身的話少轉一點；頭轉回來看著準心），左手就在身體的左前方。射出去的箭用 `JV_Arrow` 模型，飛行時只拖一條很短、很淡、跟箭身同色的尾巴（`ArrowRecover`）。瞄準時全身的 BowDraw 動畫直接從「拉滿弓」那一格開始（`PlayerLoadout.SnapBowDrawPose`，不播 Mixamo 動畫前半段自己伸手拿箭的部分，手臂都由程式擺），每次開始拉弓不會先跑掉一下。瞄準時走路由 `Scripts/Player/BowAimFootwork.cs` 踏步（`PlayerCombat` 自動掛上）：一隻腳踩住地面不動、另一隻腳抬起來往移動的方向跨一步，落地後換另一隻（跨出去的前腳落得比站姿前面一點，落地後兩腳一起踩住一下，要等身體走過它、落到後面夠遠才換腳，前腳定得比較久）；站著轉身轉太多才挪腳；腿伸不到時骨盆稍微下沉；開始 / 停止瞄準時慢慢混進混出。弓的模型原本是斜放、弓弦朝外：Builder 用兩端弓梢的連線把弓轉直、弓弦轉向拉弓的人，並拿掉模型本身那條直的弓弦（一樣用 `StripMeshParts`，不另存網格），弓弦（`JV_Bow` 的 `StringTop` / `StringBottom` 兩個弓梢之間的線）跟著右手拉成 V 字；拉太久手會開始抖。
  - 換武器的程式在 `Scripts/Player/PlayerLoadout.cs`（自動掛在每個場景的玩家身上，只調 PlayerCombat 現有的欄位；選的武器和剩下的箭跨場景保留，但不寫進存檔）。小刀、長槍、弓的 Prefab 由 Builder 產生在 `Resources/Weapons`。武器架只擺玩家沒帶的武器（`WeaponRackDisplay`）；走近武器架按 E 也能直接換，架子上方會浮出一張說明卡（`WeaponRackSlot`）。
  - 打擊回饋（`PlayerLoadout`）：打中東西時鏡頭輕微震動、頓幀約 0.05 秒，有手把的話手把也震一下（`PlayerCombat.OnDealtHit`）。
  - 撿回射出去的箭（`Scripts/Player/ArrowRecovery.cs`）：插在地上 / 牆上 / 樹上的箭留在原地（插得太高搆不到的掉到正下方），射中敵人的箭掉在敵人腳邊（20% 會斷掉）；拿著弓、箭沒滿的時候走過去就自動撿起來（右下角跳出「撿回一支箭」），箭射完換成小刀時撿到會換回弓。掉進海裡、飛太遠沒打到東西的箭就沒了；地上的箭 150 秒後消失，最多同時留 40 支。沒有水的關卡（道場、孤島）箭不會因為飛得比 1.3 m 低就被當成掉進水裡。
- **揮刀時上半身擺動更明顯**（`Scripts/Player/SwingBodyAccent.cs`，`PlayerCombat` 自動掛上）：每一刀抬手時把腰往起刀的那一側扭（蓄力），砍下去時甩到另一側、身體往前壓、往砍的方向側彎，收招時回正；大刀三段各自不同（最後一刀的劈砍前壓最多），長槍是往前戳，小刀幅度小一點。只轉脊椎下段、疊在原本的動作上（腳、刀的判定都不動），`amount` 可以調整幅度（0 = 關掉）。大刀第三刀（劈）兩隻手臂伸直：雙手握刀從頭頂稍微往後，沿著身體正前方的大圓弧劈到前下方，刀身和手臂成一直線、左手握在右手下面（`StraightChop`，蓋過原本動畫裡彎著手、刀和手對不上的姿勢）。
- **演出、動畫進行中不能互動**（`InteractPrompt.Blocked`，`Scripts/Core/GameUI.cs`）：過場演出（`InteractPrompt.Block / Unblock`）、劇情對話、玩家被鎖住不能操作的時候，「與誰對話」之類的互動提示都不出現、按 E 也沒反應；村民開門、關門、進出屋子的動畫進行中也不能對話（`NpcRoutine`）。
- **暫居地**（最北那排房子、主街西邊第一間，Builder 的 `BuildResidence`，互動在 `Scripts/Level/Residence.cs`）：米格爾暫住的老房子，正門是開著的門洞、門前三階木階梯，可以走進去、走出來（進門時畫面上方顯示「暫居地」）。屋裡有被褥、行燈、矮桌、坐墊、刀架、木箱、陶甕：
  - 被褥按 E 休息：畫面暗一下，體力、精力補滿；在村子裡倒下都會在被褥旁邊醒來。
  - 矮桌上的**戰鬥紀錄簿**（`Scripts/Level/CombatHistoryBook.cs`）按 E 翻開：最上面是總計（實戰幾場、勝敗、打倒幾名敵人、倒下幾次），下面一場一行（最新的在上面）——結果（勝利 / 敗北 / 脫離 / 練習）、對手、時間、地點、花了多久、命中幾下、造成 / 受到多少傷害。滾輪或 ↑↓ 捲動，E / 空白鍵 / 右鍵闔上，翻開時遊戲暫停。
  - 紀錄由 `Scripts/Core/CombatLog.cs` 自動記（不用在場景裡放東西，所有場景都會記）：打中敵人或被打到就開始一場，7 秒沒再交手就結束（對手全倒 = 勝利，不然 = 脫離），玩家倒下 = 敗北（記下原因）。對手的名字優先用物件上的 `bossName` / `enemyName`（道場老師父、時間吞噬者…），木樁人偶、僧侶過招算「練習」，火把、石頭、樹這些不算。存在 `Application.persistentDataPath/combat_history.json`（最多 200 場，關掉遊戲再開還在；batchmode 不寫檔）。
- **倒下後被抬回村子**（`Scripts/Level/VillageReturn.cs`，自動執行）：在道場（`DojoArena_ArchitecturePreview`）、霧海孤島（`ForestIsland`）被打倒時，畫面暗下來顯示「你倒下了……村民把你抬回了村子」，載入漁村，在暫居地的被褥旁邊醒來；在村子裡倒下也在暫居地醒來。這條主線不會 Game Over，村長家後院的大門解鎖過的話回來還是開的（`VillageProgress`），不用重新試煉。醒來時米格爾自言自語一句（不再在村長、僧侶頭上放驚嘆號）；每次倒下照常扣一條命（血條旁的血滴少一顆），最後一條命用完時村民還是把你救回來、命補滿，不會 Game Over（`GameManager.SetLives`）；倒下後去找藥師說話，說完藥師會叫住玩家送一瓶壯力藥（`Scripts/Level/ApothecaryPotionGift.cs`，一次倒下送一瓶）。整段是過場演出（跟道場開場演出同一種拍法：上下黑邊、固定鏡頭依劇情切換、每個鏡頭慢慢推近並跟著人轉、血條藏起來，對話以外按 Space 跳過，最後鏡頭飛回玩家背後交還操作），鏡頭依序是越過玩家肩膀拍藥師叫住玩家、桌子對面的遠景、桌邊低角度的倒藥特寫、側面拍藥師走過來、越過肩膀拍遞藥、喝藥特寫、全身冒火：先把玩家的武器收起來，藥師走回磨藥的板凳坐下（桌子很矮，站著搆不到）、右手拿起藥缽（縮小到單手拿得起來）把暗紅的藥汁倒進桌上的藥瓶（Builder 做的 `Apothecary_PotionBottle`：陶瓶、軟木塞、腰上一圈繩子，高約 0.35 m；左手受傷，全部用右手），放下藥缽、拿起藥瓶站起來走到玩家面前遞過去；玩家接過來、瓶口湊到嘴邊仰頭喝三口（`Scripts/Player/PotionDrink.cs`），喝完再把武器拿出來，喝完身上冒出淡淡的紅黑火焰（`Scripts/Player/AttackPotionBuff.cs`：攻擊力變成 1.3 倍（`PlayerCombat.damageMultiplier` × 1.3）、持續 120 秒，火苗是往上竄的半透明火舌，剛喝時火比較旺、最後 10 秒慢慢變淡；這個加成不會寫進存檔、換關卡後在新的玩家身上重新加上，`PlayerState.TemporaryDamageBonus`）；之後跟村長、僧侶、守祠的師父、女鐵匠、藥師、行商、小孩、茶屋老闆娘、女書生說話時，他們會先安慰、鼓勵他繼續努力（每人每次倒下講一次，第一次倒下和之後講的不一樣，村長會提到上次輸給誰），講完恢復平常的對話。台詞在 `VillageReturn.Comfort`，用 `MerchantNPC.LinesOverride` 插在平常的對話前面。Builder 會把漁村場景加進 Build Settings（道場要載入它）。
- **釣魚、賺錢、買東西**（`Scripts/Level/VillageEconomy.cs`、`Scripts/Level/FishingSpot.cs`，自動執行，不用重建場景）：
  - 釣魚：跟老漁夫說話，最後選「一起釣魚」（對話框關掉後玩家直接站到他左手邊的位子上），或走到那個位子（棧橋末端、旁邊擺著一個魚簍）按 `E`（站在位子上時「釣魚」的提示優先，不會被「與老漁夫對話」搶走）下竿：武器收起來，右手拿出竹釣竿甩竿：把竿子舉到右肩後（浮標吊在竿尖晃）、往前一甩放線，紅白浮標劃一道弧線落到海上；之後每一竿都重新甩一次（魚跑掉的話先把浮標收回竿尖）。釣魚時兩手握竿（`FishingSpot.PoseHands`，雙骨 IK）：右手在肚子前面再往前一點握竿尾（竿尾只露出手後面 10 cm，不會插進肚子），竿子握在手指裡：中心在手腕往中指根部 75%、掌心外面 3.5 cm（不是插在掌心上），左手往前約五分之一隻手臂扶著竿子；手掌順著前臂、食指那一側朝竿尖，竿子斜斜地橫過掌心（像握掃把柄），IK 解兩次（第二次照掌心離手腕的偏移修正），掌心才會剛好在竿子上，手指往掌心握起來；甩竿時右手舉到右肩旁再往前甩，左手跟著鬆開一些。老漁夫的材質（`M_JV_Fisherman`）改成兩面都畫（他的 Tripo 模型斗笠、衣服下襬有單面 / 反面的面，從上面看會破洞），Builder 的 `TripoMaterial` 也一樣設定。等 2.5 ~ 6.5 秒魚上鉤（之前浮標偶爾輕輕抖一下），浮標沉下去、畫面中間出現一個木頭轉軸（捲線的線軸，兩邊擋板、右邊一支搖柄），1 ~ 9 的數字沿著轉軸排，現在要按的那個在正面的金色牌子上，按對了轉軸就轉一格（線跟著捲、搖柄跟著轉，像在收線），下一個從下面轉上來；下面一排小方格是進度。用手把時（魚上鉤那一刻最近用的是手把）轉軸上寫的是 A / B / X / Y（綠、紅、藍、黃的圓鈕），要順時針轉左搖桿把轉軸轉過來（轉半圈 = 一格，有喀喀的棘輪聲），轉到正面的框裡（框會亮）時按對應的鈕；轉軸不會轉過還沒按的那一格，還沒轉到就按不算（框抖一下），按錯魚就跑了，時間是鍵盤的 1.6 倍，收線時只有鍵盤的互動鍵能放棄，在時間內全部按完（鍵盤上排或數字鍵盤）就釣上來，按錯或時間到魚就跑了；釣上來的魚從水裡甩到手邊、放進魚簍（每一種的顏色、花紋不一樣：竹莢魚青灰背銀肚、一條黃色側線，鯖魚藍綠背上深色波紋，鱸魚灰黑背銀肚，真鯛粉紅帶藍點，金目鯛鮮紅、一雙大金眼），接著自動再甩一竿，按 `E` 收竿。第一次跟老漁夫說話他會教怎麼釣。
  - 魚（`FishCatalog`）：竹莢魚 5 碼 / 3.2 秒 / 6 文、鯖魚 6 碼 / 3.6 秒 / 10 文、鱸魚 7 碼 / 4 秒 / 16 文、真鯛 8 碼 / 4.4 秒 / 28 文、金目鯛 10 碼 / 5 秒 / 60 文（越貴越少上鉤）；數字出現後有 0.4 秒緩衝才開始計時。
  - 換錢：釣到的魚拿去岸邊補網的年輕漁夫（`NPC_NetMender`，他四周多擺了五座魚乾架：`VillageEconomy.AddFishRacks` 用程式做，跟原本的魚乾架同一種樣子、掛上下兩排魚乾；原本的是靜態合併的網格不能複製），講完話打開「賣魚」，可以一種一種賣或全部賣掉（`FishBuyerShop`）。錢的單位是「文」。錢只在店裡顯示：對話框商店打開時，畫面右上角一枚日本古代的小判金幣（稍微直立的橢圓、暗一點的黃金色，外圈一道邊、橫的槌紋、上下各一個刻印，往右斜放 18 度，`CoinHud`），旁邊是錢的數字（小一點、暗金色，不寫單位）；魚簍的數量只在釣魚時顯示在右上角。測試用：第一次進漁村時給 100 文（`VillageEconomy.StarterCoins`，一個存檔只給一次，改成 0 就不給）。
  - 行商（`NPC_Merchant`）賣造型（`CosmeticShop`，先選帽子 / 圍巾 / 鉤索造型再選商品，買過的隨時可以換回來）：帽子 斗笠 60（跟老漁夫一樣的草編斗笠：比較尖、稻草往外放射、幾圈編痕、帽沿參差不齊）、竹笠 90（跟巡邏的村丁一樣：灰黃的竹篾往外放射、中間綁兩圈、帽沿捲一圈粗邊）、烏帽子 120；圍巾（素面、沒有花紋）朱紅圍巾 50、藍染圍巾 70、雪白長圍巾 100；鉤索 朱繩金鉤 80、墨繩銀鉤 100、青藤竹鉤 130。造型由 `Scripts/Player/PlayerCosmetics.cs` 用程式做網格；大小照玩家模型量：換裝時把目前姿勢的網格烘出來（`BakeMesh`），取權重在頭上的頂點，帽子的半徑用頭（含頭髮、貓耳朵）最寬的地方再大 15%，帽子內側離頭 1 cm，斗笠、陣笠整頂往上挪到內側碰不到任何一點，烏帽子的帽口壓到額頭高度；讀不到網格時照貓戰士的比例估（頭骨離地高度的 14% 是耳朵尖、8.75% 是頭最寬）。圍巾在脖子上繞三圈（下面那圈大一點垂在衣領上、上面那圈貼著脖子，每圈高低起伏不同），水平地圍（脖子骨往上 1 cm，前面低 1 cm 不碰到下巴；脖子骨本身往前傾約 24 度，照它的方向圍會前低後高、掉到肩膀上），半徑照貓戰士量的脖子（前 4.8、兩側 5、後 5.5 cm，其他模型照身高比例），繞一圈扁扁的布，掛在胸口的骨頭上（轉頭時不跟著轉）；左後方打一個結、垂下兩條巾尾（有厚度的扁布條，不是一片；寬 9.5 cm、厚 4 cm，長 48 cm，雪白長圍巾 90 cm），每幀往下垂、往後往外飄，走路跑步時往反方向飄、擺動（雪白長圍巾的巾尾比較長）。鉤索換 `TetherRopeVisual.Skins` 的繩子顏色和鉤爪金屬。
  - 茶屋門口的兩張長椅可以坐（`Scripts/Level/BenchSeat.cs`，自動掛在 `TeaBench_1`、`TeaBench_2` 上）：走近按 `E`「坐下」，武器收起來，人走到椅子前面、背對椅子坐下（整個骨架往下往後移到椅面上，雙腳用 IK 踩在椅子前面的地上、膝蓋往前彎，腳掌平踩，雙手放在大腿上），坐在離玩家最近的位子，老闆娘坐著的那一邊就換另一邊；按 `E`、空白鍵或方向鍵起身。坐著的時候，掛在身上的東西（收在背上的刀、箭筒）先藏起來，不會插進椅子，起身再拿出來。
  - 茶屋老闆娘（`NPC_TeaHost`）賣甜點和藥草（`TeaHouseShop`），買了當場吃：三色糰子 30 文 = 護盾，先擋下最大生命 20% 的傷害（`Scripts/Player/SweetShield.cs`，`PlayerHealth.TakeDamage` 先扣它）；豆大福 20 文 = 額外精力 30%，精力條後面多一截金色的、花精力時先扣它、不會回復（`PlayerStamina.AddBonus`）；藥草 8 文一株（拿去藥鋪換藥水）。護盾和額外精力沒有時間限制，用完或倒下就沒了，換關卡還在（不寫進存檔），還有的時候不能再買。
  - 錢、魚、買過的造型、穿哪一件都存在 `PlayerInventory`（`Coin`、`Fish_*`、`Own_*`、`Wear_*`），跟草藥一樣會跟著換關卡和存檔。三家店都用 `MerchantNPC.customShop`（`IDialogueShop`：對話框裡的商店，數字鍵選、最後一項離開 / 返回）。
- **祠堂的試煉**（`Scripts/Level/ShrineTrial.cs`，流程和台詞都在這裡）：
  1. 走進山上的祠堂，跟「守祠的師父」對話，選擇進入習武場，後門就會打開（`SlidingDoor`）。
  2. 習武場在祠堂後面（144 × 36 m），圍牆內不會被打倒。
     - 東半邊是一道 14 m 深的深溝（Terrain 挖洞 + 石牆），橫跨整個院子；只能用拉索抓著 6 根懸空木條（`TetherTrainingBeam`）一路盪到對岸。掉進溝裡會回到起點，還沒全部碰過的話燈籠全熄、從頭來（`TetherCourse`）。木條的「◆」標記要進入習武場才顯示。第 2～5 根木條下面各吊一塊木板（`SwayPlank`）可以站上去喘口氣，但站上去會下沉、搖晃、嘎吱作響，約 1.6 秒就翻過去，3 秒後翻回來。
     - 西半邊 3 根木樁人偶（`TrainingDummy`）：打一下就變成約 3 m 高的木頭人（Banana Man 模型換成木頭材質、背上綁一根木樁、四肢外側各綁兩段短木樁，動畫用 `Animations/JapaneseVillage/AC_WoodenFighter`），會走過來用雙手推人（雙手往後收就是要推了）；共打中 6 下過關，過關後鞠躬、頭綁紅布。大小改 Builder 的 `FighterHeight`（木樁、碰撞範圍、推擊距離一起跟著縮放）。
  3. 習武場完成後，僧侶從祠堂後門走進習武場（`Scripts/Level/MonkSparring.cs`），跟他對話選「請大師指教」開始過招（他一出現、還在走進來的路上就能選，第一次對話就開始；選了之後他走到空地才出手）：看他抬手就舉盾或翻滾，出完招之後的空檔打中他一下算一回合；沒有空檔時打他會被擋下來。來回 5 回合結束，僧侶鞠躬。
     - 輕功：被打中第 1、3 下之後，他會一躍跳上北邊的圍牆頂，站一下再從牆上飛撲到玩家面前，落地直接點穴（`wallPerches`、`leapSpeed`、`diveSpeed`）。
     - 點穴：快速的戳擊（Attack1）真的打中的話玩家暈眩 1.6 秒（`ThirdPersonController.Stun`，頭上繞著星星 `DizzyStars`）；舉盾擋下或翻滾躲開就沒事。另一招推掌（Attack2）只會把人推開。
    僧侶是 Tripo 模型 `Models/JapaneseVillage/Monk/Monk.fbx`（Mixamo 骨架，貼圖用 `Art/JapaneseVillage/make_monk_textures.py` 轉成 2048 並合成金屬度貼圖），Animator `AC_Monk`：平常空手站 / 走路，過招時用劍盾包的架勢、揮擊（空手就是掌擊）和格擋動畫。
  4. 祠堂地上散著 4 幅古畫和 1 首詩（古畫的 `SteleInscription` 開了 `pictureOnly`：只秀畫本身、不包在卷軸裡，標題和說明寫在下面；詩仍是卷軸），要全部看過；混在裡面的 6 本村民的書可以翻開，但不算進度。畫和詩的文字在 Builder 的 `Paintings`、`PoemText`、`VillagerBooks`。
  5. 全部完成後跟習武場裡的僧侶對話，選擇開門，村長家後院大門的鎖就解開了（`Scripts/Level/DojoGate.cs`）。走到門前按 E 或直接往門走過去，兩扇門往外推開（門板對齊圍牆的牆面，兩側到圍牆盡頭的縫用固定的木板牆補起來）；門洞沒有擋板了，直接穿過門洞走出去（門外 1.2 m）就轉移到道場（`DojoArena_ArchitecturePreview`），換場景時黑底輪播四幅古畫（`PaintingSlideshow`，每張 1.8 秒，新場景載好、四張都播過才淡出；這段時間遊戲先暫停）。

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
