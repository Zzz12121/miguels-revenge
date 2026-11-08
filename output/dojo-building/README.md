# 道場建築與決戰庭院

已建立實際 Unity 場景：`Assets/_Project/Scenes/DojoArena_ArchitecturePreview.unity`。

- `DojoBuildings_Complete.glb`：主屋＋左右附館，內嵌貼圖，可用於模型檢視或後續加工。
- `MainDojo_Complete.glb`：單獨中央主屋。
- `FinalDojoArena.glb`：包含側廊、庭院、門樓、鳥居及圍牆的整院模型。
- `FinalDojoArena_Architecture.unitypackage`：提供 Unity 原生場景、Prefab、Mesh、材質、貼圖與靜態碰撞；接收專案需有 URP。未打包或安裝任何套件程式碼。
- 編號 01～05 PNG：實際 Unity 相機渲染，包含外觀、全院、室內、移除屋頂後的配置與庭院平視。

這是完整靜態建築第一版。室內有實際地板、牆面、門洞、木天花板與基本家具。門扇固定開啟，植物與家具為基礎模型；玩家、Boss 和戰鬥流程尚未整合到這個獨立預覽場景。

開啟場景按 Play，點 Game 後按住右鍵轉視角、WASD 移動、Q/E 升降、Shift 加速、Ctrl 慢速。F2 回入口、F3 進大廳、F1 看總覽、F4 去茶室；H 切換屋頂剖視，Esc 放開滑鼠，Tab 切換中文提示。`06-free-tour-controls.png` 是參觀模式實際 Game 畫面。

GLB 和 Unity 資源包是可重建的本機交付檔，Git 保存 Unity 原生資產、程式、文件與預覽圖，避免重複保存大型二進位匯出。

詳細配置、來源與使用方式：`Docs/ConceptArt/FinalDojoArena/BUILDING_IMPLEMENTATION.md`。
檢查記錄：`architecture-checks.json`（786 項）、`tour-checks.json`（11 項 Play 模式輸入）、`glb-validation.json`、`package-validation.json`。
