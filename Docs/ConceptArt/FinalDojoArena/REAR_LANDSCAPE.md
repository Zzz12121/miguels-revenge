# 後院溪流、密草與外圍地面

2026-11-09。場景：`Assets/_Project/Scenes/DojoArena_ArchitecturePreview.unity`。

- 後院深度先由 14m 擴為 21m，再擴 1.2 倍至 25.2m；後牆 Z126.2，側牆碰撞及外觀同步延伸。外圍可行走地面 380×360m。
- 前院保留櫻花；松、楓、竹在後院散植。56 叢低灌木、108 叢蕨類重新分布，前後院約 200,325 片風動草葉及連續綠底。
- 碎石改為主屋平台外局部圓弧、不規則邊緣，寬約 44m、最深約 5.7m。其他後院區域接草坪。停用舊整片 GravelGround 及平地碰撞，避免遮住新河床。
- 曲折水道寬約 3.5–4.75m，水面 Y-0.22、中央河床 Y-1.05，緩坡可涉水。100 顆岸石、8 顆大景石，另有庭石與石燈籠。
- KHS 原始資產在 `Assets/Soswaewon`，不是名為 KHS 的資料夾。使用 Structure/SM_Stonewall02a 做外牆石基，使用 KHS 石材製作的連續石板橋橫跨溪流；實例材質換為本專案 URP 材質，原 Bridge01b 為細框架，因此改為完整石板橋面並保留獨立連續碰撞。

## 水面與接水特效

SUIMONO 原始資產：`Assets/SUIMONO - WATER SYSTEM 2`。其舊版 Built-in 水面渲染器不能直接套在目前 URP。這次使用 SUIMONO 的雙層波紋法線、泡沫、水花／漣漪 prefab 與貼圖、水聲，搭配 `DojoGardenStream.shader` 與 `DojoWaterContact.shader` 的 URP 相容實作；沒有啟用完整原生 SuimonoModule。

`DojoRiverWater` 位於 `FinalDojoArena/Architecture/LivingGarden/RearLandscape`：

- 角色控制器、帶 Rigidbody 或 Animator 的碰撞體進水／移動會觸發水花、漣漪。
- 自動抽樣場景 ParticleSystem 接水，使用最多 12 組循環重用的水花、漣漪，附冷卻時間。
- 網格／Trail／Line 型攻擊，以及生命短於掃描間隔的特效，應在實際接水點呼叫 `water.EmitImpact(worldPosition, strength)`。位置必須位於河道水面附近；不會在乾地產生水花。此為未來 Boss 攻擊整合入口，不會改寫隊友傷害系統。
- 流速／波紋在 `Rear_StreamWater.mat`；水聲為有距離衰減的環境音。沒有加入游泳、浮力、流水推人或水下相機效果。

## 重建與驗證

Edit 模式執行 `unity command run_script --file Docs/Validation/DojoRearGardenBuild.cs --entry DojoRearGardenBuild.Build`。會重建 RearLandscape 並重新排列松楓竹。

Play 模式執行 `DojoRearGardenChecks.Run` 與 `DojoRiverChecks.Run`（對應 Docs/Validation 同名 .cs）。驗證包含：49 點外圍碰撞、玩家 W 行走及後牆外支撐、固定鏡頭水面流動、玩家沉到河床、橋面支撐、角色及粒子接水、乾地拒絕水花。報告在 `output/dojo-building/*checks.json`，截圖由 `DojoGardenCapture.Run` 產生。

可重用 prefab：`PF_DojoRearLandscape`、`PF_DojoLivingGarden`、`PF_FinalDojoArena_Living`。材質／網格在 `_Project/Materials/FinalDojoArena/Garden`、`_Project/Models/Environment/FinalDojoArena/Garden`。

草地已合併網格並保留綠底；尚無指定硬體的效能基準。舊 GLB／unitypackage 未重新匯出，不代表此版後院。
