# 靶場自訂按鍵與畫中世界技能限制

## 使用方式

- 靶場按 K：顯示「衝刺／跳躍／翻滾」三格及目前按鍵。點格子，再按一次新按鍵立即套用。
- 衝突顯示「按鍵衝突，無法改變」，保留舊設定並繼續等待其他按鍵。檢查包含另兩個動作、WASD／方向鍵、技能 1–4、互動、藥水、地圖、F8、K、Esc 與確認鍵，也會讀取目前藥水／地圖設定。
- 等待改鍵時 Esc 取消該次選鍵；未等待時 K／Esc 或關閉按鈕離開。面板擁有滑鼠、暫停時間與動作鎖，關閉時只還原自己的狀態。
- 使用 PlayerPrefs `RangeControls.v1.Sprint/Jump/Roll` 保存，重新進入靶場仍保留。預設 Left Shift／Space／C；一般場景維持 Left Shift／Space／V。
- HUD 與跑跳動畫的按鍵回饋共用實際綁定。沒有變更翻滾位移、無敵或精力扣除邏輯。

## 畫中世界

`PlayerSkillAvailability.TryUse()` 是玩家技能共用入口：進入、停留、消散階段皆禁止四招數字鍵技能與中鍵牽引，顯示「畫中世界無法使用技能」。不會進入瞄準、改變時間或施放特效；離開後恢復。一般移動、跳躍、翻滾與普攻沒有因此封鎖。

進畫前取消已存在的瞄準／技能／牽引，再記錄回場座標，避免技能的舊位置還原與畫中世界返回互相干擾。未來新增玩家技能也應在入口呼叫此 gate。

## 驗證

在靶場 Play Mode 執行：

```powershell
unity command run_script --file Docs/Validation/RangeControlsAndPaintingChecks.cs --entry RangeControlsAndPaintingChecks.Rebinding --format json
unity command run_script --file Docs/Validation/RangeControlsAndPaintingChecks.cs --entry RangeControlsAndPaintingChecks.PaintingSkills --format json
unity command run_script --file Docs/Validation/RangeRollKeyChecks.cs --entry RangeRollKeyChecks.Run --format json
```

覆蓋 K 開啟、三格 UI、衝突拒絕、鍵盤擷取、保存／重載、取消與關閉、暫停時不誤動、改鍵後原鍵失效、其他場景隔離；四招技能／程式施放入口／牽引在畫中禁止，四個原始數字鍵皆能顯示提示，退出後瞄準恢復。

背景 Editor 測試以 Dynamic Input step 與暫時鍵盤狀態餵入事件，避開 Manual step／焦點的按鍵邊緣差異；測試結束恢復裝置、焦點政策與原本三筆 PlayerPrefs。已檢視 Game View 面板、衝突、改鍵結果和畫中禁止提示截圖。測試腳本在 Assets 外，不會進正式遊戲。
