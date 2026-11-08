# 主道場與決戰庭院：靜態建築第一版

2026-11-08。已使用使用者提供、清理並完成貼圖的 Tripo 屋頂，建立實際 Unity 建築與獨立預覽場景。

## 直接查看

**玩家更新：** 儲存場景現已加入共用 `PlayerRig.prefab`，出生在庭院 `(0,1.06,65)`，面向主殿。按 Play、點 Game，即可用 WASD／滑鼠／Shift／Space／V 操作現有角色與第三人稱相機。預覽飛行 Camera 預設停用，避免兩套相機及輸入同時生效。共用 Prefab 與遊戲控制腳本未修改，只保存場景內的位置、引用與技能不自動播放設定。

下列自由飛行操作為原模式；要使用時，停止 Play、停用場景 PlayerRig，啟用 `PreviewLightingAndCameras/Main Camera`。本機模型與 Unity 匯出包仍為先前建築版本，未重新匯出玩家依賴。

- 場景：`Assets/_Project/Scenes/DojoArena_ArchitecturePreview.unity`。
- 單一主屋：`Assets/_Project/Prefabs/Environment/FinalDojoArena/PF_MainDojo_Complete.prefab`。
- 主屋＋左右附室、可放在任意位置的組合：`Assets/_Project/Prefabs/Environment/FinalDojoArena/PF_DojoBuildings_Complete.prefab`。組合原點在主屋地面中心。
- 整座庭院：`Assets/_Project/Prefabs/Environment/FinalDojoArena/PF_FinalDojoArena_Architecture.prefab`。

Unity 開啟場景後按 Play，再點一下 Game 畫面，即可自由參觀。`PreviewLightingAndCameras/Main Camera` 掛有專用 `DojoArchitectureTour`，預設從主入口開始；這是無碰撞的參觀飛行相機，可穿牆查看內部，沒有改寫遊戲玩家或 Cinemachine 相機。

| 操作 | 功能 |
| --- | --- |
| 按住滑鼠右鍵拖曳 | 轉動視角；放開右鍵或按 Esc 釋放游標 |
| W/A/S/D | 前後左右移動 |
| Q / E | 下降 / 上升 |
| Shift / Ctrl | 加速 / 慢速精調 |
| 滑鼠滾輪 | 調整飛行速度（1～40m/s） |
| F1 / F2 / F3 / F4 | 總覽 / 入口 / 大廳 / 茶室 |
| H | 隱藏或恢復屋頂與天花板；離開參觀元件時恢復原始顯示狀態 |
| Tab | 顯示或隱藏中文操作提示 |

不按 Play 時，也可在 Scene 視窗選擇 `FinalDojoArena/Architecture/MainDojo` 並按 F 聚焦，按住右鍵＋WASD 使用 Unity 自己的場景檢視控制。

每棟的 `RemovableRoofs` 與 `RemovableCeiling` 可分別隱藏，以查看室內。主殿包含中央大廳、後室、左側武器室、右側茶室、左右後側收納室；另有兩座外側附館。家具為基礎靜態陳設，主入口及內部門洞保持開放，門扇固定停在開啟位置，沒有互動開門腳本。

## 已建立的幾何與配置

- 主屋：40m 寬屋頂、34×20m 室內外殼、38×26m 抬高木地板／前廊。地板高 1.2m，主階梯 6 階、每階 0.2m。
- 原屋頂約長寬 1:0.703，因此 40m 屋頂深約 28.12m；未把屋頂拉扁來硬套早期 40×18m 草案。
- 主屋中央入口淨寬約 8m，後室門洞約 6m，側室與附館通道約 3m。
- 兩側附館中心 X=±27m，屋頂寬 18m，含茶室／武器展示與前門階梯。
- 場地地面 98×122m；主屋中心 Z=88m。圍牆位於 X=±48m、Z=5/115m。
- 中央戰鬥區 X=±26m、Z=19～71m，保持 52×52m 平坦淨空；景物集中在外圈。
- 東西側有頂廊道、北端連接平台與階梯、入口門樓、鳥居、石燈籠、基礎松樹造景、庭石。
- 地板、牆、柱、門片、階梯、天花板有靜態碰撞；屋頂有共用 MeshCollider。
- 原屋頂 4K 底色貼圖保留。屋面使用雙面 URP Lit 材質；室內另做木天花板，不以瓦片背面充當室內裝修。

這些尺寸是本次模型組裝後的實際值，更新了早期尚未統一的建築／附室占地草案。原概念圖保留作美術方向參考。

## 交接檔

`output/dojo-building/`：

- `DojoBuildings_Complete.glb`：整棟主屋與兩座附館，99,320 三角面、41 網格、11 材質，貼圖內嵌。此檔不包含 Unity 碰撞及燈光。
- `MainDojo_Complete.glb`：單獨中央主屋，44,960 三角面。
- `FinalDojoArena.glb`：完整院落靜態模型，520,096 三角面、73 網格、14 材質。
- `FinalDojoArena_Architecture.unitypackage`：Unity 場景、原生網格、材質、Prefab 與必要貼圖；只匯出 Assets 下的依賴，不捆綁或安裝 URP 套件。接收專案需有 URP；本次使用 Unity 6000.6.3f1／URP 17.6。
- `01-main-building-exterior.png`、`02-complete-courtyard.png`、`03-main-hall-interior.png`、`04-building-cutaway.png`、`05-courtyard-eye-level.png`：實際 Unity 相機渲染。
- `06-free-tour-controls.png`：Play 模式 Game 視窗，包含中文操作提示與室內實景。

預覽截圖臨時延長陰影距離以拍攝整個院落，結束後恢復原本 PC_RPAsset；專案 QualitySettings 沒有保留修改。

## 來源與實作

- 屋頂：使用者的 Tripo 模型與貼圖，已移除兩片多餘平面；原 GLB、原始幾何轉接資料和 JPEG 存在 `Assets/_Project/Models/Environment/FinalDojoArena/`。
- 建築結構、障子框格、天花板、階梯、門樓、鳥居、家具與基礎造景：本次以 Unity 幾何組裝。
- 木板與石材底色：沿用專案已存在的 Mega Fantasy Props Pack 的 `wooden-boards-texture_d.png`、`stone_wall_2_d.png`。沒有重新產生或改寫第三方貼圖。
- 網格依群組／材質合併，以穩定名稱保存原生 Mesh；屋頂共用同一個 Mesh 與材質。完成後整院 73 個 MeshRenderer、264 個靜態 Collider。
- 屋頂以外的材質主要使用底色、純色與標量粗糙度；家具、植物及庭院地表目前是基礎美術版本，尚未做最終細節、LOD 或實機效能調校。

重建／檢查工具位於 `Docs/Validation/`，在 Assets 以外，不會把建模工具編入遊戲：

```powershell
unity command run_script --file Docs/Validation/DojoArenaBuild.cs --entry DojoArenaBuild.Build
unity command run_script --file Docs/Validation/DojoArenaChecks.cs --entry DojoArenaChecks.Run
unity command run_script --file Docs/Validation/DojoArenaBuild.cs --entry DojoArenaBuild.Capture
unity command run_script --file Docs/Validation/DojoArenaBuild.cs --entry DojoArenaBuild.Package
unity command run_script --file Docs/Validation/DojoGlbExport.cs --entry DojoGlbExport.Run
unity command run_script --file Docs/Validation/DojoTourSetup.cs --entry DojoTourSetup.Install
# 進入 Play 模式後驗證參觀輸入：
unity command run_script --file Docs/Validation/DojoTourChecks.cs --entry DojoTourChecks.Run
```

Build 會重建這個獨立場景；如果有開啟的未儲存場景，會停止而不覆蓋。檢查／匯出前請確認開啟的是建築預覽場景。

## 驗證範圍

786 項幾何／資產斷言通過，包括角色尺寸膠囊沿入口、內部門洞、附室連橋的淨空、地板與階梯高度、中央整個 52×52m 戰鬥體積無碰撞障礙，以及材質／原生網格引用。玩家更新另以虛擬鍵盤通過 7 項 Play 模式檢查：共用角色跳躍、回到地面、W 從庭院經階梯正門走到大廳、木地板支撐，以及唯一啟用相機／收音與正確跟隨目標。結果 `player-checks.json`、實景 `07-player-in-courtyard.png`。Boss 與完整戰鬥流程尚未整合。

幾何 Build 會重建場景並移除額外的玩家實例；需要重建時，之後在 Edit 模式執行 `DojoPlayerSetup.Install`，再進入 Play 執行 `DojoPlayerChecks.Run`。兩者都透過 `unity command run_script --file Docs/Validation/<檔名>.cs --entry <類別>.<方法>` 執行。玩家更新完成後，使用者已明確要求 push。

GLB 已重新解析檢查標頭、索引範圍、有限數值、材質／貼圖引用、內嵌圖片可解碼。報告在 `architecture-checks.json`、`glb-validation.json`。Unity Console 無錯誤；啟動時有既有 MCP 連線警告。

2026-11-08 參觀更新：已先從 6e9f93e pull 到 af01482，再實作預覽相機。Play 模式以臨時虛擬 Input System 裝置驅動實際 Update，11 項檢查通過，覆蓋 F2/F3、W、Shift、E、H、右鍵拖曳、Esc 及元件停用恢復。測試後移除虛擬裝置，恢復實體裝置及原本 Input System 編輯器設定。結果在 `tour-checks.json`。

本次未改寫隊友的移動、Boss AI、門流程或動態場地，未變更套件。參觀更新的後續使用者指示已授權 pull／push，道場建築與參觀功能會一起交接。
