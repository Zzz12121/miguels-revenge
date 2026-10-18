# 遊墨行者：Tripo 建模輸入包

## 建模拆分

- 角色本體：單獨生成，不含巨筆、墨影、飛散墨滴或場景。
- 巨筆：單獨生成，進 Unity 後再掛到手部骨骼。
- 墨影：不要交給 Tripo 建模；在 Unity 以透明材質、粒子、拖尾與投影 VFX 製作。

這樣可以避免巨筆黏到手臂／袖子，也不會讓墨影變成角色網格的一部分。

## 角色 Multi-View 圖片

依介面上標示的視角欄位分別上傳，不要靠檔名順序猜測：

- 正面：`youmo_front.png`
- 右側：`youmo_right.png`
- 背面：`youmo_back.png`
- 左側：`youmo_left.png`

四張圖已統一為淺灰背景、近似正交鏡頭、相同角色與相同比例。正面與背面負責提供手臂、服裝和身體寬度；側面主要提供身體厚度、駝背程度、髮束及袍子的前後層次。

## 建議生成設定

1. 有 Multi-View 時優先使用四視圖，而非把四張拼成一張。
2. 目標若要做骨架與動畫，優先選乾淨拓樸／可重拓樸的模式。
3. 第一次只生幾何，確認輪廓後再花點數生成 4K 貼圖、PBR 與骨架。
4. 不要在第一次生成時加入武器、特效或動作姿勢。
5. 成品要確認：手指數量、左右腳、袖子是否黏身體、鬍鬚是否黏胸口、長髮是否黏背部、袍子是否封住雙腿。

## 可貼入文字欄位的角色描述

```text
An elderly East Asian male ink-walker boss, 65-80 years old, lean and wiry, calm weathered face, long white-gray hair partly tied into a high knot, long narrow white beard, worn layered traditional Chinese robes in aged off-white and charcoal, restrained black ink stains, dark sash with a muted red accent, simple leather sandals. Otherworldly shanshui ink-painting aesthetic, readable broad cloth panels, animation-friendly silhouette, neutral A-pose, no weapon and no visual effects.
```

## 排除描述（若介面有 Negative Prompt）

```text
weapon, brush, staff, ink shadow, floating ink, magic aura, scenery, base, pedestal, action pose, crossed limbs, sleeves fused to torso, beard fused to chest, hair fused to back, closed robe between legs, extra fingers, extra limbs, asymmetrical body, thin loose ribbons, excessive ornaments
```

## 巨筆輸入

- 圖片：`youmo_brush.png`
- 巨筆結構接近軸對稱，先用單圖生成即可；若輪廓不穩，再補相反側視圖。
- 巨筆生成後在 Blender 設定握持點與模型原點，再進 Unity 掛到右手骨骼。

巨筆描述：

```text
A giant ancient Chinese calligraphy brush weapon taller than an adult man, long weathered black-brown lacquered wooden shaft, restrained dark bronze fittings, subtle red-brown cord wrapping, a huge dense white-to-black natural bristle head with a pointed ink-dark tip, practical construction, clean isolated game asset, no person, no magic and no scenery.
```

## 角色確認後的順序

1. 生成角色基礎幾何。
2. 旋轉檢查四面與關節區域；失敗就停在幾何階段重做。
3. Smart UV／UV。
4. 生成 4K Texture；再決定是否需要 PBR。
5. Humanoid Auto Rig；若目標是 Unity，優先使用 Unity Humanoid Compatible 預設。
6. 用走路、抬手與深蹲動作檢查權重。
7. 另生成並匯出巨筆，在 Blender 或 Unity 綁到手部掛點。
8. 墨影、墨滴與砸地浮石效果全部留在 Unity 製作。

