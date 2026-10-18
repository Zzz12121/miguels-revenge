# Arcane Cat Warrior — Tripo 生成指南

這份資料包是為 Tripo Image-to-3D、後續 Humanoid Auto Rig，以及 Unity Humanoid 匯入所準備。

## 檔案用途

- `01_Primary_SingleView.png`
  - 免費方案或單張 Image-to-3D 的首選。
  - 微三分之四正面角度，兼顧臉、身體正面與側向厚度。
- `02_Front.png`
  - 完全正面視圖。
  - 用於 Multi-View 的 Front 欄位，也可作為造型核對基準。
- `03_RightSide.png`
  - 完全右側視圖，人物面向畫面右方。
  - 用於 Multi-View 的 Side 欄位。
- `04_Back.png`
  - 完全背面視圖。
  - 用於 Multi-View 的 Back 欄位，明確描述背帶、衣襬與尾巴根部。

不要把四張圖合併成一張再使用單圖生成；Tripo 可能將它們理解成四個角色。

## 角色規格

- 成年女性魔法戰士，動漫風格，運動型身材。
- 暖棕膚色、琥珀色眼睛、及肩銀白髮。
- 兩隻白色貓耳、僅一條白色尾巴，尾端為炭灰色。
- 象牙白不對稱包覆式上衣，完整不透明。
- 炭黑短褲、護腿、黃銅護甲與戰鬥靴。
- 胸口、腰帶與雙護腕有虹彩魔晶。
- 上衣、衣襬與護腕有低亮度魔法符文。
- 尾巴只有一組編繩、金屬環與單一流蘇。
- 角色本體不包含武器；武器與刀鞘在 Unity 中作為獨立 Prefab。

## 如果 Tripo 提供補充描述欄位

```text
Adult anime fantasy cat warrior and spellblade, warm brown skin, amber eyes,
shoulder-length silver-white layered hair, two white cat ears, exactly one white
cat tail with a charcoal tip, athletic mature proportions, fuller balanced bust,
secure opaque ivory asymmetric wrap top, charcoal shorts and leggings, ivory split
hip panels, antique brass armor and combat boots, opalescent arcane crystals on the
sternum, belt and both bracers, subtle iridescent rune embroidery, neutral A-pose,
open separated hands, full body, clean light-gray background, even studio lighting,
game-ready character, no weapon.
```

如果有 Negative Prompt，貼上：

```text
weapon, sword, sheath, dynamic pose, crossed arms, crossed legs, bent elbows,
bent knees, hidden hands, fused fingers, extra fingers, extra arms, extra legs,
extra ears, extra tail, multiple tails, long hair, floor-length skirt, cape,
oversized sleeves, excessive ornaments, many chains, floating magic, particles,
busy background, strong shadow, extreme perspective, cropped feet, text, watermark
```

## 免費／單圖方案：建議操作

1. 開啟 `3D Workspace`。
2. 選擇 `Smart Topology Mesh`，或首頁中央的 `Clean Topology Trial x2`。
3. 上傳 `01_Primary_SingleView.png`。
4. 確認預覽只有一名角色，而且頭、手、腳與尾巴都未裁切。
5. 不要開啟 `Generate in Parts`、8K Texture 或其他額外付費選項。
6. 按下最終 Generate 前，先閱讀按鈕顯示的實際點數；介面價格可能變動。
7. 第一次生成只驗證幾何，不要立刻做 Auto Rig。

目前畫面顯示 Multi-View 與 DCC Bridge 需要升級。免費測試先只上傳 `01_Primary_SingleView.png`。

## 如果帳號可使用 Multi-View

依欄位分別上傳：

- Front：`02_Front.png`
- Side：`03_RightSide.png`
- Back：`04_Back.png`
- Fourth / 3-Quarter：`01_Primary_SingleView.png`

若只能上傳三張，使用 Front、Side、Back。所有圖片都要保持獨立，不要做成拼貼圖。

## 第一次生成後的停止檢查點

先切換白色／黏土材質並旋轉 360 度，逐項確認：

- 臉部沒有凹洞，眼、鼻、嘴的位置正常。
- 恰好兩隻貓耳，沒有額外耳朵或耳朵黏到頭髮。
- 頭髮沒有黏到肩膀、背部或腋下。
- 左右手各五指，手指沒有融合成整塊。
- 腋下與手臂之間有明確空隙。
- 上衣表面完整，沒有穿孔或多餘胸部幾何。
- 兩片衣襬沒有黏到大腿，也沒有連成裙桶。
- 腿與靴子左右分離，腳底大致落在同一平面。
- 只有一條尾巴；尾巴根部接在骨盆後方，沒有接到腿或衣襬。
- 尾巴綁繩和流蘇沒有變成大量漂浮碎片。
- 胸口、腰帶與雙護腕晶體位置正常。

只要主要輪廓、臉、手、腿或尾巴錯誤，就先停止。不要把點數花在 Texture、Segmentation、Retopology 或 Auto Rig。先記錄錯誤，再用第二次 Clean Topology Trial 修正輸入。

## 網格合格後的處理順序

```text
生成模型
→ 幾何檢查
→ 必要的分割／補面
→ 必要的 Retopology
→ UV／貼圖
→ Auto Rig
→ 動畫測試
→ FBX 匯出
```

所有會改變網格的操作都必須放在 Auto Rig 之前；重新拓撲、分割或補面可能移除骨架與蒙皮權重。

### 首輪建議略過

- Segmentation：角色本體已設計為單一角色，首輪不需要，而且成本高。
- Generate in Parts：可能造成接縫與材質不一致。
- 8K Texture：目前 Unity 專案不需要，會增加成本與記憶體。
- 第二次 Retopology：Smart Topology 結果若已乾淨就不要重做。
- 額外 PBR：先確認免費／試用生成的貼圖品質，再決定是否需要。

若需要手動面數目標，可先以約 10,000 quads（約 20,000 triangles）作為可動畫測試角色；英雄近景仍可在確認效能後使用更高面數。

## Auto Rig 設定

網格與貼圖都確定後再做：

1. 進入 `Rig`／`Animate`。
2. Rigging Type 選 `Humanoid`。
3. Skeleton Preset 選 `Mixamo`。
4. 執行 Rig Check（若介面提供且免費）。
5. 再執行 Auto Rig。

至少測試下列動作：

- Idle：檢查肩膀、胸口與呼吸變形。
- Walk／Run：檢查骨盆、膝蓋、衣襬與靴子。
- Punch：檢查肩膀、腋下和護腕。
- Sword Slash／Attack：檢查上半身旋轉與雙手。
- Crouch：檢查短褲、髖部與大腿。

尾巴不一定會自動取得可用骨骼。首輪只要尾巴能隨骨盆移動即可；尾巴擺動之後可在 Blender 或 Unity 追加 Secondary Motion。

## 匯出設定

- Format：`FBX`
- Rig preset／target：可選時使用 `Mixamo`
- Export Skeleton：`ON`
- Bottom-Center Pivot：`ON`
- Texture Resolution：先使用 Current／4K，不需要 8K
- Animations：正式角色可先不嵌入；測試時只附一個 Idle 或 Walk 即可
- File name：`ArcaneCatWarrior_Rigged`

下載後確認同一資料夾內包含 FBX 與所有貼圖。若 `Export Skeleton` 仍為灰色，先確認目前選取的是 Auto Rig 完成後的新輸出，而不是原始靜態模型；若仍不可用，可能是目前方案或模型版本的匯出限制。

## Unity 匯入檢查

1. 選取 FBX，開啟 `Rig` 分頁。
2. Animation Type 選 `Humanoid`。
3. Avatar Definition 選 `Create From This Model`。
4. Apply 後按 `Configure`，確認骨盆、脊椎、頭、手臂與腿均為綠色。
5. Materials 分頁抽取或重新指定 Base Color、Normal、Metallic／Roughness 貼圖。
6. 用 Idle、Run、Punch 和 Sword Slash 各測一次。
7. 不要把移動、相機或戰鬥邏輯寫進角色模型；角色只提供 Animator、骨架與掛點。

## 點數節省原則

- 帳號目前有 Clean Topology Trial 時，先用 Trial，不要同時開啟額外功能。
- Auto Rig 在先前介面顯示約 20 點；只對合格模型使用。
- 先前匯出視窗顯示約 5 點；以實際按鈕為準。
- Segmentation 先前顯示 40 點，首輪略過。
- Texture／PBR／Retopology 是否收費，以按鈕當下顯示為準。
- 每次按下黃色按鈕前先截圖點數與設定，避免不知道點數花在哪一步。

## 判定成功的最低標準

- Unity 能將 FBX 設成 Humanoid 並完成 Avatar mapping。
- Idle、Run、Punch、Slash 不會讓肩膀、胸口、手肘、髖部與膝蓋嚴重塌陷。
- 頭髮、衣襬和尾巴沒有成片黏住主要肢體。
- 模型面數與材質數量適合即時運行。
- 武器可以透過右手 Socket 與腰部／背部 Sheath Socket 分開掛載。

