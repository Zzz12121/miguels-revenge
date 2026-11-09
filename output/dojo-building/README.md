# 道場建築與決戰庭院

已建立實際 Unity 場景：`Assets/_Project/Scenes/DojoArena_ArchitecturePreview.unity`。

**最新後院更新：** 後院深度由 14m 擴為 21m（1.5 倍），增加灰碎石、密草、56 叢低灌木、108 叢蕨類、蜿蜒溪流與岸石；前後院草區均有綠底及密草，院外新增 380×360m 支撐地面。圖片17～21與 `rear-landscape-build.json`／`rear-landscape-checks.json` 記錄實景及驗證，說明見 `Docs/ConceptArt/FinalDojoArena/REAR_LANDSCAPE.md`。本次尚未 push。

**大門／配置修正：** 門柱與兩側牆面已連接，靠近入口按 E 可從內外開關雙扇門；玩家站在門扇掃過範圍時會阻止關門。前院櫻花增為 18 棵，其餘松、竹、楓移到後院，松樹根部下移貼地。`14-connected-entrance.png`、`15-rear-garden.png`、`16-pine-root-grounding.png` 為實景；`gate-and-layout-checks.json` 記錄 E、實際 W 通行與碰撞測試。

**最新庭院更新：** 已換上套件鳥居與石燈籠，加入櫻花、松、竹、楓、灌木、蕨類、草地及 34 塊不規則踏石。Play 中可見草木風動與持續交錯的花瓣／竹葉飄落。圖片 08～13 為更新後實景；`garden-checks.json`、`garden-motion-checks.json` 與更新的 `player-checks.json` 記錄通路、風動與角色測試。詳細交接見 `Docs/ConceptArt/FinalDojoArena/LIVING_GARDEN.md`。以下 GLB／unitypackage 仍是較早的建築快照，未包含新庭院或玩家。

- `DojoBuildings_Complete.glb`：主屋＋左右附館，內嵌貼圖，可用於模型檢視或後續加工。
- `MainDojo_Complete.glb`：單獨中央主屋。
- `FinalDojoArena.glb`：包含側廊、庭院、門樓、鳥居及圍牆的整院模型。
- `FinalDojoArena_Architecture.unitypackage`：提供 Unity 原生場景、Prefab、Mesh、材質、貼圖與靜態碰撞；接收專案需有 URP。未打包或安裝任何套件程式碼。
- 編號 01～05 PNG：實際 Unity 相機渲染，包含外觀、全院、室內、移除屋頂後的配置與庭院平視。

這是完整靜態建築第一版。室內有實際地板、牆面、門洞、木天花板與基本家具。門扇固定開啟，植物與家具為基礎模型。儲存的 Unity 場景已加入現有貓耳玩家 PlayerRig；Boss 與戰鬥流程尚未接入。

現在開啟場景按 Play，點 Game 後可操作玩家：WASD 移動、滑鼠轉視角、Shift 衝刺、Space 跳躍、V 翻滾；Esc 釋放滑鼠，點畫面重新鎖定。玩家從庭院 X0/Z65 面向主殿開始，可以走上階梯進屋。`07-player-in-courtyard.png` 是實測畫面，`player-checks.json` 記錄 7 項角色與相機檢查。

原自由飛行相機保留但預設停用。需要恢復飛行參觀時，先停止 Play，停用場景的 PlayerRig，再啟用 PreviewLightingAndCameras/Main Camera。原右鍵轉向、WASD 飛行、Q/E 升降、F1～F4 與 H 剖視控制只適用於該模式，`06-free-tour-controls.png` 是先前飛行模式的截圖。本機 GLB／unitypackage 仍是先前靜態建築快照，未重新打包玩家依賴。

GLB 和 Unity 資源包是可重建的本機交付檔，Git 保存 Unity 原生資產、程式、文件與預覽圖，避免重複保存大型二進位匯出。

詳細配置、來源與使用方式：`Docs/ConceptArt/FinalDojoArena/BUILDING_IMPLEMENTATION.md`。
檢查記錄：`architecture-checks.json`（786 項）、`tour-checks.json`（11 項 Play 模式輸入）、`glb-validation.json`、`package-validation.json`。

2026-11-09 後院更新：後牆 Z126.2、局部圓弧碎石、下凹曲折河床、KHS 石橋與石牆基座、SUIMONO 貼圖／粒子／音效的 URP 接水效果。詳見 REAR_LANDSCAPE.md。22、23 圖為溪流與碎石配置；river-contact-checks.json 為實際玩家河床／橋面及水花測試。舊封裝未更新。
