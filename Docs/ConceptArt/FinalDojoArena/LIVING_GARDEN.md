# 道場庭院：植物、石路與風動

2026-11-08。此更新已存入 `Assets/_Project/Scenes/DojoArena_ArchitecturePreview.unity`。

## 查看與交接

開啟場景、按 Play，再點 Game 畫面。現有玩家從庭院開始，沿石路可走上主殿階梯並進入室內；沿用原本角色操作。草木擺動與花葉飄落在 Play 中持續播放。

**大門與樹木修正：** 靠近門口約 4m，畫面顯示提示後按 **E** 可開／關雙扇大門，內外兩側皆可操作。門預設關閉，向玩家的另一側打開；門移動時不接受重複操作，玩家在門扇掃過範圍內時會阻止關閉，關閉途中有人進入則重新打開。牆面已補至兩側門柱。這個庭院互動沒有連接 Boss 戰啟動或封門流程。

- 庭院裝飾預製物：`Assets/_Project/Prefabs/Environment/FinalDojoArena/PF_DojoLivingGarden.prefab`。需要另有地面碰撞，不能獨立充當完整關卡。
- 完整靜態院落：同資料夾 `PF_FinalDojoArena_Living.prefab`。含原建築、地面與新庭院，不含獨立的 PlayerRig 或預覽相機。
- 獨立互動入口：同資料夾 `PF_DojoInteractiveEntrance.prefab`。含兩扇活動門、門板碰撞與補牆；搭配原有門樓及門柱使用，沒有重複包含屋頂／門柱。
- 場景階層：`FinalDojoArena/Architecture/LivingGarden`。
- 新材質：`Assets/_Project/Materials/FinalDojoArena/Garden/`。
- 新網格：`Assets/_Project/Models/Environment/FinalDojoArena/Garden/`。
- 風動 Shader：`Assets/_Project/Shaders/DojoGardenWind.shader`。

原本的建築預製物保留。先前匯出的 GLB、unitypackage 是歷史靜態建築版本，沒有此次庭院與玩家更新。

## 實際配置

| 項目 | 數量／處理 |
| --- | --- |
| 櫻花樹 | 18，全部位於前院 |
| 黑松 | 8 |
| 竹子 | 18 |
| 日本楓樹 | 6 |
| 黃楊、蕨類 | 各 20 |
| 套件石燈籠 | 10 |
| 朱紅稻荷鳥居 | 1 |
| 大型不規則踏石 | 34 |
| 庭石 | 14 |
| 合併草網格區塊 | 14 |
| 飄落花瓣／竹葉粒子系統 | 24 |

石路從門口延伸至主屋，踏石尺寸、旋轉與左右位置有變化；淺碰撞面可供現有角色通行。草分布於兩側與局部院內，主路保持通暢。植物與造景改變了原先完全空白的中央方形區域，不能再把舊版「52×52m 全區無障礙」檢查當作此版的保證；此次另檢查中央通路。

松、竹、楓已全部移到主屋後方、圍牆內的後院，松樹根頸下移並埋住低垂根尖；竹林單獨靠後牆排列，避免與松樹樹幹重疊。前院保留櫻花、低矮灌木、蕨類、草與石造景。

原始簡易松樹、庭石、石燈籠與鳥居停用。舊鳥居部分與圍牆合併，因此另外保存移除該鳥居三角面的圍牆副本，保留圍牆及門樓。建築簷下的紅色吊燈籠仍保留。

## 美術來源與相容處理

- Japanese Garden Pack：`Assets/Waldemarst/JapaneseGardenPackage/`。
- Japanese Garden 2 Free：`Assets/Waldemarst/FreeJapaneseGarden/`。
- Stone Lantern Pack：`Assets/JTS_StoneLanterns/`。
- Shrine Torii Gate Pack：`Assets/JTS_Shrine_Torii_Gates/`。

四包原始資源已匯入本機。Git 交接保存場景與重建器實際使用之來源預製物的完整依賴；未使用的套件展示場景與其他模型仍留在本機。大型來源貼圖／模型使用既有 Git LFS 工作流程，組員拉取時需有 Git LFS（必要時執行 `git lfs pull`）。`DojoGardenCommitAssets.cs` 可重新產生依賴清單，不會修改場景。

場景使用的植物實例解開連結，移除其舊風動控制元件，改用此專案的 URP 材質與統一風向。材質副本依來源 GUID 區分，避免多個同名 billboard 材質串錯貼圖。植物保留有效的 3D LOD，停用舊 billboard，避免遠處出現錯誤色塊。

Garden2 的 `Scripts/BroccoTreeController_FJG_1_10_3.cs` 使用了 Unity 6.6 已移除的 GetInstanceID。僅將這些識別值與字典鍵改為 EntityId/GetEntityId，讓套件正常編譯；未重寫套件功能。原始美術材質與貼圖沒有覆寫。

## 調整效果

- 選取新植物或草材質，調整 `Bend Distance`、`Leaf Flutter`、`Wind Speed`、`Gust Strength` 和 `Wind Direction XZ`。根部固定，越靠近枝葉／草尖擺幅越大。
- 草使用 UV1 高度遮罩；植物使用本地高度遮罩。風動同時作用於可見、深度和陰影通道。
- 花葉使用低面數雙面不透明網格，出生／消失時縮放，搭配旋轉、Noise、向下及橫向速度。持續低速生成加上交錯陣發，避免全部同時停止；每個系統最多 120 顆。
- 選取 LivingGarden 內粒子系統，可在 Emission 調密度，在 Main 調生命期／大小，在 Velocity over Lifetime 調飄落方向。場景 WindZone 是風向參考，自訂 Shader 與粒子的數值需各自調整。
- 配置、草密度與隨機種子集中在 `Docs/Validation/DojoGardenBuild.cs`。透過建置器重建會取代目前 LivingGarden 手動調整，先保存需要保留的版本。

植物材質採 alpha clipping、寫入深度與根部遮罩；草按區塊合併網格以減少物件數。此版已做功能與畫面驗證，尚未完成指定硬體的效能基準或最後美術調校。

## 重建與驗證

請在 Edit 模式、正確場景中執行，避免覆蓋未儲存的手動修改：

```powershell
unity command run_script --file Docs/Validation/DojoGardenBuild.cs --entry DojoGardenBuild.Build
unity command run_script --file Docs/Validation/DojoGardenBuild.cs --entry DojoGardenBuild.Finish
unity command run_script --file Docs/Validation/DojoGateBuild.cs --entry DojoGateBuild.Run
unity command run_script --file Docs/Validation/DojoGardenChecks.cs --entry DojoGardenChecks.Run
# 以下兩項需要 Play 模式：
unity command run_script --file Docs/Validation/DojoPlayerChecks.cs --entry DojoPlayerChecks.Run
unity command run_script --file Docs/Validation/DojoGateChecks.cs --entry DojoGateChecks.Run
unity command run_script --file Docs/Validation/DojoGardenMotionChecks.cs --entry DojoGardenMotionChecks.Run
unity command run_script --file Docs/Validation/DojoGardenCapture.cs --entry DojoGardenCapture.Run
```

若先重建整座原始建築，請先執行 `DojoPlayerSetup.Install` 恢復玩家，再建置庭院。此次沒有改寫共用移動、相機、技能或 Boss 邏輯。

驗證結果存於 `output/dojo-building/`：

- `garden-checks.json`：材質／網格引用、Shader、舊造景停用、130 個石路地面取樣、24 個循環粒子系統皆通過。
- `gate-and-layout-checks.json`：內外 E 開關、實際玩家 W 穿門、關門防夾、重複／遠處輸入不觸發、門板碰撞與 12 個牆門接縫取樣通過。測試程式明確啟用虛擬鍵盤，避免 Game View 失焦時測試裝置被禁用。
- `player-checks.json`：7 項 Play 檢查通過，以虛擬鍵盤操作實際角色跳躍、落地、從庭院沿階梯進入大廳。
- `garden-motion-checks.json`：固定鏡頭暫時隱藏花葉渲染後，草木風動仍改變 47,363 像素；另追蹤 441 顆既有花葉確認向下與橫向飄移。
- 圖片 `08`～`16`：實際 Unity 相機畫面，包含前院、全院、踏石草叢、固定鏡頭風動前後、補齊牆面的大門、後院與松樹貼地近景。`11`／`12` 為前一次配置的風動驗證截圖。

最終編譯與 Unity Console 檢查無錯誤、無警告。使用者隨後明確要求將本次庭院與大門修改提交並 push。
