# Piloto Realistic Ying Yang 相容性修復

2026-10-23，Unity 6000.6.3f1／URP 17.6／新版 Input System。

## Demo 操作

開啟 `Assets/Piloto Studio/Super Realistic FX Bundle 02/Realistic Ying Yang/Ying Yang Demo Scene.unity`，按 Play，再點 Game 視窗取得鍵盤焦點。

- W 或 D：下一個特效。
- Q：上一個特效。
- Space：清除殘留粒子後從頭重播。

## 修改內容

- `ParticleShowcase` 原本使用舊 `UnityEngine.Input.GetKeyDown`，在本專案會每幀拋出 InvalidOperationException。現在優先新版 Input System，保留條件式舊輸入支援；未更改專案全域輸入設定。
- 補回缺少的兩個螢幕平面輔助功能：`PilotoScreenPlane`、`PilotoCameraOffset`，保留原序列化欄位與遺失 GUID，使 Big Explotion、Pulses、Screen Space FX 的引用恢復。這是本地相容實作，不是原廠原始碼還原。
- `ParticleHandler` 移除不必要的 UnityEditor 依賴與編輯模式協程，重播先取消舊協程並清粒子；停放階段不再覆蓋不同粒子系統的發射率。
- `DistortionGrabber` 的條件編譯結尾移回類別內，避免定義 USING_URP／USING_HDRP 後括號失配。URP 下沿用套件 Shader 與既有 Opaque Texture，不安裝／移除套件。
- Boss 太極引用原始 `Ying Yang Rune.prefab`，保留套件粒子、材質與貼圖；未直接改造原始特效形狀。

## 驗證

- W／Q／D／Space 模擬按鍵通過，全部 42 個 Demo 項目輪播。
- 188 個材質未發現不支援 Shader 或 Shader 編譯錯誤。
- 三個原本缺腳本的 Prefab 已無 Missing Script。
- 已檢查 Demo 與 Boss 落地太極的 Game View 截圖。
- 玩家在落筆前橫移 7 公尺，墨龍鎖定新位置；落筆後再移動 4 公尺，落點保持不變。

最後調整 ParticleHandler 的停止發射方式後，重新編譯通過；但最後一輪背景按鍵重測時 Editor 在 500ms 內沒有推進任何遊戲影格，W 斷言未通過。上述 42 項與按鍵通過紀錄來自先前一輪；最終版仍需在前景 Game 視窗補驗按鍵與重播，不能把這次背景重測算作通過。測試用輸入及背景設定已恢復，Demo 留在停止播放狀態。

以上不是完整 standalone Build 或全部渲染平台保證。匯入套件仍為未追蹤資產；分享給隊友時須連同合法套件依賴處理，不可只提交 Boss Prefab 的引用。
