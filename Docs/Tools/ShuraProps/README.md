# 修羅場道具：Tripo 模型拆分 / 減面

Tripo 產生的道具合集是「一個網格、所有道具黏在一起、187 萬面」。這些腳本把它拆成 10 個道具、
各自減到約 7k–16k 面、轉正、縮放到真實尺寸（原點在底部中心），輸出成 Unity 可直接匯入的 OBJ。

只需要 Python 3 + numpy（matplotlib 只有預覽圖用到），不需要 Blender。

```bash
# 1. 拆分＋減面（最後一個數字是每個道具的目標面數）
python3 split_props.py <tripo.fbx> out 8000
# 2. 預覽（用顏色貼圖上色）
python3 render_props.py out <basecolor.jpg> preview.png
```

- 道具名稱、在原檔裡的位置、真實高度寫在 `split_props.py` 的 `PROPS` 表。重新生成模型後若排列不同，改這張表。
- 輸出的 `SM_Shura_*.obj` 放進 `Assets/_Project/Models/ShuraProps/`（記得拿掉 `mtllib` 那一行），
  貼圖改名 `T_ShuraProps_BaseColor.jpg` / `T_ShuraProps_Normal.png`。
- 匯入設定由 `Scripts/Editor/ShuraPropsImportSettings.cs` 自動套用；
  Prefab 與場景擺放由選單 `GeminiGrowUp > 建立修羅場 Boss 場景` 產生（`Prefabs/ShuraProps/`）。
