# 蜈蚣戰後陰陽符陣 → 河鎮

ForestIsland 的百足駭骨本體倒下後，`CentipedeCredits` 訂閱原有 `OnDefeated` 事件，改為建立 `ForestInkboundPortal`；分身不開傳送門。死亡演出後約 3.2 秒，在玩家附近約 6 公尺的平坦空地出現原始 **Ying Yang Vertical Rune** 與金色光柱／上升光點。位置會避開地形陡坡、岩石、樹與屍體碰撞，並確認玩家到符陣之間沒有遮擋。

等待期間只循環原始符陣前 3 秒。玩家仍可自由活動，走入半徑 1.8 公尺且腳接近地面時才開始傳送；沒有自動切關、沒有按鍵確認。

| 踏入後時間 | 演出 |
|---|---|
| 0 秒 | 暫停玩家輸入與技能，站姿開始緩慢沉降；鏡頭留在地面。 |
| 1 秒 | 符陣重新從第 0 秒播放一次。 |
| 約 3 秒 | 角色與武器完整沉入地面，符陣進入最後爆發段。 |
| 約 4 秒 | 尾段結束，透過既有 GameManager 淡出、保留血量／能力／背包並載入河鎮。 |
| 河鎮顯示後 | 玩家從河鎮原本起點上方 18 公尺落下，使用原控制器的重力、碰撞與落地動畫，落地後恢復操作。 |

原始符陣尾煙超過 4 秒，因此只在這個傳送門實例重排時間：重播後的蓄積段配合剩餘沉降時間，原始尾段壓縮到約 1 秒；不修改廠商原 Prefab、材質或老者技能的播放速度。控制／攝影／無敵皆由小型場景轉接器暫時取得並釋放，不重寫移動或 Boss AI。

**製作名單改到 InkboundTown_Exploration 的實際老者 `BossHealth.OnDied` 後觸發**，等待既有死亡演出約 6 秒，再播放原製作名單及原回漁村流程。蜈蚣與河鎮老者不再彈出會暫停世界的 Congratulations 畫面；其他 Boss 不受影響。

## 調整

主要 Prefab：`Assets/_Project/Resources/ScenePortals/PF_ForestInkboundPortal.prefab`。

- **After Defeat Delay**：死亡演出後等多久開門，預設 3.2。
- **Idle Source Seconds**：等待循環的原始片段長度，預設 3。
- **Sink Seconds / Restart After Entry / Final Tail Seconds**：預設 3／1／1；重播時間應小於沉降時間。
- **Nearby Distance / Trigger Radius / Arrival Height**：預設 6／1.8／18 公尺。
- **Rune Scale**：原始符陣的尺寸，預設 0.8。

金光 Prefab 與自有材質、網格、Shader 放在 `Assets/_Project/VFX/ForestIsland/Portal/`；金光是柔邊多層光柱、閃爍流光、金色星點與一盞無陰影暖光。重新產生可執行 `Docs/Validation/ForestPortalSetup.cs` 的 `Install`，不重建 ForestIsland 或河鎮。

`Docs/Validation/ForestPortalChecks.cs` 在原始 ForestIsland 做必要的跨場景確認：實際死亡事件、兩次三秒循環、W 走入、沉降時序、完全入地、空中到達、原起點落地、操作／血量保留，以及老者死亡後才進片尾。結果與原生 Unity 畫面在 `output/forest-town-portal/`；只確認片尾開始，不在檢查背景繼續跑回漁村，主要實玩仍交給使用者。

實測重播延遲約 1.001 秒、完全沉入約 3.001 秒、尾段約 1.004 秒；入地時實際骨架網格最高點低於地面約 0.35 公尺。`cleanup-checks.json` 另確認取消時控制、碰撞、模型、Animator、鏡頭均恢復，金色光點使用一致速度曲線模式；完成後 Console 無錯誤。
