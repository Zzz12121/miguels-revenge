# 墨中故城：美術包對照與驗收紀錄

> 本文保留前版歷史紀錄。V3 已取代人口數、布局與探索限定規則，請以 [V3 交付紀錄](INKBOUND_TOWN_V3.md) 及 [逐圖對照](INKBOUND_TOWN_V3_REFERENCE_REVIEW.md) 為準。V3 使用專用重建選單，不再沿用本頁舊版局部更新流程。

最新人氣與無字招牌更新：全城共 80 人（78 名日常居民與 2 名船夫），新增逛攤顧客、聊天群組、親子、茶客、橋上行人及廣場散步居民。所有場景招牌文字已清空，生成器不再生成字牌文字。居民仍忽視玩家、沒有阻擋碰撞。局部更新選單為「GeminiGrowUp → 河鎮 → 更新居民並移除招牌文字」；重複執行會替換專用新增人群群組，不會持續堆出重複居民。最新必要確認在 `output/inkbound-town/crowd-update/change-summary.json`；舊段落中的 38 名居民為前版紀錄。

本輪依 `C:/NTHU/Introduction_to_Game_Programming/Inkbound_Town_Art_Pack/README.md`、`CODEX_PROMPT.md` 實作。已完整閱讀兩份 Markdown，實際檢視其全部 30 張本地引用圖片，包括原始 Unity 圖、目標概念、16 張細節、9 張示意及 3 張不作驗收依據的附錄人物圖。影像清單與雜湊在 `output/inkbound-town/art-pack-review/reference-inventory.json`。

使用者先前指定「先做無戰鬥場景」與「只做必要測試，主要測試由使用者執行」。美術包同時提出完整戰鬥流程，因此已提出本輪範圍釐清；環境與居民互動先持續完成。下表逐項保留驗收狀態，未經實際確認的項目不勾選為通過。

## 實作

- Unity 6000.6.3f1、URP 17.6、Linear 色彩空間；沿用現有管線與玩家，不新增套件。
- 曲線管狀樹幹、根部、大小分枝及下垂細枝；彎曲披針葉分成深、淺兩組。樹皮 UV 沿曲線行進，植被具有緩慢風動。
- 主屋採專案既有 Soswaewon `SM_JWD_Roof01a.fbx` 實體瓦頂，兩個坡面組成立體屋頂，使用河鎮專用墨色材質；小棚及牌坊為有厚度的凹曲面，帶轉折簷角、圓屋脊及少量瓦口；房屋加入低棚、側屋、木廊、招幌、無光燈籠與商品。碰撞仍由簡化實體負責。
- 河岸使用錯縫、缺角與分層石塊，保留平滑碰撞代理。地面材質使用抖動石塊分區與斷續接縫。橋拱補厚度、石圈和橋墩。
- 材質分別處理白牆留白、樹皮縱筆、瓦頂積墨、石面擦染、布紋、山體皴紋及遠近細節淡化；未使用黑白 LUT 或全畫面等粗描邊。
- 四層有縱深的山體網格，具有非等距峰形、坡面起伏與淡墨山腰；紙色天空只有微弱紙纖維，沒有使用概念圖作背景。
- 河面保留紙白，以破碎、低對比倒影、岸邊淡墨、橫向筆觸及兩艘船的尾流交代水體。反射仍為單張 512×512、每秒最多 8 次。
- 增加河岸蔬果攤、茶桌交談與釣魚小組；籃筐、布棚、燈籠、垂繩和釣竿提供生活用途。保留獨立居民動畫與日常路線。
- 散墨以穩定動畫網格取樣、局部膠囊／掃掠來源、速度傳遞、彎曲回流、濃芯／濕層／淡暈及分岔細絲組成。主要墨粒回到當前動畫位置。是受控即時近似，沒有宣稱完整三維流體。
- `InkboundDisturbanceSource` 支援身體膠囊、筆尖掃掠、球形來源；一名居民保留最多四個局部來源，同時最多五名居民使用細緻效果。戰鬥來源可透過此轉接元件接入，元件本身不發出傷害。

## README 10.2 逐項對照

「待玩測」表示程式／資產已實作，依使用者要求保留實際操作驗收；不等於測試通過。

| 編號 | 驗收項目 | 狀態與證據 |
|---|---|---|
| A1 | 自然柳幹、柔枝、疏密葉簇 | 已以實際 Unity 渲染確認，畫面見 art-revision；美術精細度仍待使用者確認。 |
| A2 | 石岸／石板不規則、斷續筆縫及乾濕區分 | 已以實際 Unity 渲染確認，畫面見 art-revision；美術精細度仍待使用者確認。 |
| A3 | 建築屋脊、屋簷、格窗、店面與用途變化 | 已以實際 Unity 渲染確認，畫面見 art-revision；美術精細度仍待使用者確認。 |
| A4 | 明亮河水、破碎倒影、橫筆與船尾流 | 已以實際 Unity 渲染確認，畫面見 art-revision；美術精細度仍待使用者確認。 |
| A5 | 立體、有厚度且可通行的橋 | 模型／碰撞已實作；本輪通行操作待玩測。 |
| A6 | 多層有機遠山淡入紙白 | 已以實際 Unity 渲染確認，畫面見 art-revision；美術精細度仍待使用者確認。 |
| A7 | 各物件不同筆觸與墨階 | 已以實際 Unity 渲染確認，畫面見 art-revision；美術精細度仍待使用者確認。 |
| A8 | 關閉全局調色仍成立 | 本場景相機後處理關閉；依局部材質與幾何呈現。 |
| A9 | 至少五角度成立 | 已以實際 Unity 渲染確認，畫面見 art-revision；美術精細度仍待使用者確認。 |
| A10 | 市集生活群組、疏密、行人與船 | 已呈現河岸攤販、茶桌、釣魚、38 名日常居民與兩名船夫；密度與生活節奏待玩家確認。 |
| B1 | 完整居民自然動畫、忽視玩家 | 沿用獨立 NpcRoutine，ignorePlayer=true；實際操作待玩測。 |
| B2 | 肩部局部擾動、奔跑方向與強度 | 膠囊局部遮罩與速度權重已實作；肩部擦過與奔跑操作待玩測。 |
| B3 | 濃芯、濕團、細絲、淡暈、少量滴 | 已擷取單人接觸畫面：濃芯、濕層、淡暈與連續分岔帶狀網格；仍是即時近似，與真實水中墨汁攝影有差距。 |
| B4 | 可見回流至當前動畫位置 | 已擷取完整／接觸／回流／恢復四階段，同一人物路線繼續；最後 Disturbance=0，位移在設定上限內。 |
| B5 | 持續、反覆及雙來源接觸穩定 | 四來源保留與連續速度狀態已實作；完整操作留給玩家。 |
| B6 | 老者武器／技能實際掃掠擾動、無傷害 | 擾動轉接元件已提供；實際 Boss 技能接線待本輪範圍確認。 |
| B7 | 結構碰撞、生活物件穿透 | 原結構碰撞保留，新增居民／商品無阻擋碰撞；更新幾何後待玩家走查。 |
| B8 | 預警、鎖定、相機與輪廓可讀 | 近景低霧、散墨距離及數量限制已實作；正式戰鬥待範圍確認與玩測。 |
| C1 | 半血一次、主動進入、狀態傳遞 | 待本輪戰鬥整合範圍確認。 |
| C2 | 死亡、重試、重載無重複物件 | 本輪未改寫流程；新版場景重載操作待玩家驗收。 |
| C3 | 第一階段維持正常 | 第一階段來源及資產未因美術重製修改；本輪不重跑回歸測試。 |
| C4 | 無編譯／Missing Script／粉紅材質／重要例外／持續 GC 尖峰 | 必要編譯、有效網格、Shader 與 Missing Script 檢查通過；目前 Console 結果見 required-checks.json。長時 GC 未量測。 |
| C5 | 指定解析度／硬體 CPU、GPU、FPS 與數量 | 數量由實際場景盤點；未執行目標硬體效能基準，不宣稱 60 FPS。 |
| C6 | 檔案、路徑、參數、前後畫面與限制 | 本文件、探索說明及 output 畫面共同交付。 |

## 開啟與可調參數

- Scene：`Assets/_Project/Scenes/InkboundTown_Exploration.unity`。Play 從城外進入；既有移動、跳躍、翻滾及鏡頭操作維持。
- 生成器：`Assets/_Project/Scripts/Editor/InkboundTownBuilder*.cs`，新增 `.Sculpt.cs` 與 `.Detail.cs`。選單「GeminiGrowUp → 墨中故城 → 建立探索場景」。重建覆寫本場景與生成資產，手工修改前請另存。
- 樹木／建築 Prefab：`Assets/_Project/Prefabs/Environment/InkboundTown/`。
- 居民 Prefab：`Assets/_Project/Prefabs/NPC/InkboundTown/`。
- 材質／Shader：`Assets/_Project/Materials/InkboundTown/`、`Assets/_Project/VFX/InkboundTown/Shaders/`。
- 筆墨材質：`_Grain`、`_Wash`、`_Outline`、`_StrokeScale`、`_DirectionalUV`、`_FadeNear`、`_FadeFar`。
- 居民 `InkboundEcho`：`touchRadius`、`returnSeconds`、`sampleCount`（176）、`flowStrength`、`maximumDisplacement`。
- 擾動 `InkboundDisturbanceSource`：`shape`、`radius`、`strength`、`localBottom`、`localTop`、`onlyWhileMoving`。
- 河流 `InkboundWaterReflection`：`resolution`、`updatesPerSecond`。渡船 `wakeIndex` 區分兩艘船尾流。
- 玩家 `InkboundGroundWash`：`contactOpacity`、`traceLifetime`，只提供柔和接地與短暫墨跡。

## 證據與限制

重製前可回退資產備份：`output/inkbound-town/before-art-pack.zip`；前版實際 Unity 渲染仍留在 `output/inkbound-town/`，本輪畫面獨立放在 `output/inkbound-town/art-revision/`。概念圖不作實際引擎成果。

本輪主要提升可重建的幾何剪影、筆墨層次、場景生活和互動。建築由既有瓦頂與本專案生成的牆、木構及道具組合，植被為曲線生成的美術資產，與概念圖的手繪精細度仍有差距。新增完整市井人聲、專屬人物服裝貼圖及最終戰演出不應因為已有環境而假稱完成。未購買素材，未安裝或移除套件，未提交或推送 Git。


## 本輪必要確認與畫面閱讀方式

- `art-revision/01-player-arrival.png`：正式玩家出生相機的實際渲染。
- `02-side-bank.png`、`03-river-bridge.png`、`04-market-street.png`、`05-old-tree-square.png`、`07-bridge-view.png`：河岸、河道、市街、廣場與橋上五個實際 Unity 相機角度。巡檢圖為環境觀察相機，沒有將概念圖貼到場景。
- `06-riverside-tea.png`：開放茶館近景。
- `07-echo-intact.png`、`08-echo-contact.png`、`09-echo-returning.png`、`10-echo-reformed.png`：同一居民的局部接觸與回流示意，實際引擎渲染。擾動採程式控制的玩家位置，並非宣稱已人工操作所有移動方式。
- `11-actual-game-view.png`：直接讀取原生 Game View RenderTexture，包含玩家介面；解析度記於檢查 JSON，並未假稱這就是 1080p build 效能測試。
- `required-checks.json`、`echo-visual-check.json`：本輪必要結果；前版 `runtime-checks.json` 不作本輪遊玩驗收。
- `changed-files.json`：相對本輪開始前資產備份的新增／修改清單。

正式遊玩建議由使用者依 README B2／B5／B7 先確認肩部擦碰、奔跑、持續停留、反覆穿過與橋／牆通行，再確認人群密度及鏡頭遮擋。Boss 轉場與決戰仍維持未接入狀態，不能將環境截圖當成戰鬥驗收通過。


最後保存狀態：Edit mode、`InkboundTown_Exploration` 為目前開啟場景、無未保存變更。必要檢查：有效網格、Shader 與 Missing Script 均為 0 個錯誤；單一玩家、38 名日常居民、2 名船夫、41 個可擾動實體（含商品）。本輪 Play Console 錯誤／警告均為 0。CPU／GPU frame time、FPS 與長時間 GC 未測。
