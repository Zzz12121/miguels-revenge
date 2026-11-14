# 後院溪流、密草與外圍地面

2026-11-09。場景：`Assets/_Project/Scenes/DojoArena_ArchitecturePreview.unity`。

- 後院深度先由 14m 擴為 21m，再擴 1.2 倍至 25.2m；後牆 Z126.2，側牆碰撞及外觀同步延伸。外圍可行走地面 380×360m。
- 前院保留櫻花；松、楓、竹在後院散植。56 叢低灌木、108 叢蕨類，前後院共 279,290 片風動草葉及連續綠底。
- 2026-11-09 草地／河岸修正：房屋周圍、側廊外、後院擴建區及竹林下方全部接草地，移除米白砂地、主屋後方碎石弧面與小碎石。保留前院 34 塊踏石及其原始位置／尺寸、木平台、建築台階與石橋。舊 GravelGround 及平地碰撞保持停用，避免遮住新河床。
- 曲折水道寬約 3.5–4.75m，水面 Y-0.22、中央河床 Y-1.05，緩坡可涉水。175 顆連續岸石與 8 顆大景石共用 `DojoRiverWater.Center/Width`，以縮放後的實際網格中心對齊水邊，石腳沉入水下。草皮以連續帶狀網格貼合水岸，移除方格切邊與裸露碎石空帶。
- KHS 原始資產在 `Assets/Soswaewon`，不是名為 KHS 的資料夾。使用 Structure/SM_Stonewall02a 做外牆石基，使用 KHS 石材製作的連續石板橋橫跨溪流；實例材質換為本專案 URP 材質，原 Bridge01b 為細框架，因此改為完整石板橋面並保留獨立連續碰撞。

## 水面與接水特效

SUIMONO 原始資產：`Assets/SUIMONO - WATER SYSTEM 2`。其舊版 Built-in 水面渲染器不能直接套在目前 URP。這次使用 SUIMONO 的雙層波紋法線、泡沫、水花／漣漪 prefab 與貼圖、水聲，搭配 `DojoGardenStream.shader` 與 `DojoWaterContact.shader` 的 URP 相容實作；沒有啟用完整原生 SuimonoModule。

`DojoRiverWater` 位於 `FinalDojoArena/Architecture/LivingGarden/RearLandscape`：

- 角色控制器、帶 Rigidbody 或 Animator 的碰撞體進水／移動會觸發水花、漣漪。
- 2026-11-14：玩家腳部浸入後院河水超過 8cm 時，行走／衝刺速度降為原本的 55%；可在 `DojoRiverWater.wadingSpeedMultiplier` 調整。上橋、離水、跳離水面、死亡或停用河流／角色時解除。沿用 `ThirdPersonController` 的來源獨立減速介面，不改寫移動控制器；保留其他技能效果。每幀於玩家移動前更新，傳送／重生清掉暫時倍率後，在水中仍會重新套用。
- 自動抽樣場景 ParticleSystem 接水，使用最多 12 組循環重用的水花、漣漪，附冷卻時間。
- 網格／Trail／Line 型攻擊，以及生命短於掃描間隔的特效，應在實際接水點呼叫 `water.EmitImpact(worldPosition, strength)`。位置必須位於河道水面附近；不會在乾地產生水花。此為未來 Boss 攻擊整合入口，不會改寫隊友傷害系統。
- 流速／波紋在 `Rear_StreamWater.mat`；水聲為有距離衰減的環境音。沒有加入游泳、浮力、流水推人或水下相機效果。

## 重建與驗證

Edit 模式執行 `unity command run_script --file Docs/Validation/DojoRearGardenBuild.cs --entry DojoRearGardenBuild.Build`。會重建 RearLandscape 並重新排列松楓竹。

只更新草地與岸石而保留目前樹木／灌木、河水及橋的位置，執行同檔案的 `DojoRearGardenBuild.ApplyGroundAndBanks`。重用 mesh 時會更新原生渲染緩衝並標記資產待儲存，避免 CPU 幾何已更新但畫面仍沿用舊地形。

`DojoGroundBankChecks.Run`（Edit 模式）驗證 72 點庭院／擴建區草皮覆蓋、279,290 片草葉不伸入河道、183 顆岸石貼水與 171 組相鄰岸石連續性，以及 34 塊前院踏石未改動。報告 `output/dojo-building/grass-bank-checks.json`；實際 Unity 截圖 25–29 顯示房屋四周、後院、河岸近景與前院石路。Play 模式 `DojoRiverChecks.Run` 再驗證涉水、水花及橋面支撐。

Play 模式執行 `DojoRearGardenChecks.Run` 與 `DojoRiverChecks.Run`（對應 Docs/Validation 同名 .cs）。驗證包含：49 點外圍碰撞、玩家 W 行走及後牆外支撐、固定鏡頭水面流動、玩家沉到河床、橋面支撐、角色及粒子接水、乾地拒絕水花。報告在 `output/dojo-building/*checks.json`，截圖由 `DojoGardenCapture.Run` 產生。

涉水減速驗證：Play 模式執行 `Docs/Validation/DojoWadingChecks.cs` 的 `DojoWadingChecks.Run`，使用虛擬鍵盤比較 W、Shift+W 在岸上／水中的速度及實際位移，另檢查跳出／落水、上橋、離水、重設移動、停用／重啟與其他減速共存。輸出 `wading-checks.json` 與 `30-player-wading.png`。

可重用 prefab：`PF_DojoRearLandscape`、`PF_DojoLivingGarden`、`PF_FinalDojoArena_Living`。材質／網格在 `_Project/Materials/FinalDojoArena/Garden`、`_Project/Models/Environment/FinalDojoArena/Garden`。

草地已合併網格並保留綠底；尚無指定硬體的效能基準。舊 GLB／unitypackage 未重新匯出，不代表此版後院。
