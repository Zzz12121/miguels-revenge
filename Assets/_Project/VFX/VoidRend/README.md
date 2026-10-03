# Void Rend 終結技測試演出

開啟 `Assets/_Project/Scenes/VFX_VoidRend_Bocchi.unity`，Play 自動播放；空白鍵在待機時重播。

## 演出

- 上挑目標到 4.5 公尺高，角色移到正下方。
- 3.5 秒鏡頭由後側繞到正面，抬手甩刀並冒出淡藍煙霧與光點。
- 出拳擴散衝擊波，瞬間曝光、色散與畫面扭曲。
- 22 次衝刺，刀光由實際角色位移路徑逐步延伸，留下錯位、斷裂刀痕。
- 鏡頭隨攻擊旋轉，最後繞回正面，雙手配合刀鞘收刀，裂痕持續折射。
- 連斬結束後鏡頭拉遠至 11.5 公尺、60 度視角，保留角色與持續刀網的全景構圖。
- 最終破裂、目標落在角色後方 3.2 公尺並向側面錯開 1.3 公尺，解除鎖定後保留落點。下一次測試重播才重置靶子位置。整段約 13 秒。

`effectRangeMultiplier = 4` 控制攻擊路徑半徑、主要裂痕長度與衝擊波最終半徑；角色尺寸與貼身煙霧不跟著放大。

## 整合

控制器為 `VoidRendSkillDemo`，演出實作分在 `VoidRendFinisher.cs`。設定 caster、target、skillCamera，再呼叫 PlaySkill。CancelSkill 支援中斷與還原。

targetControllers 必須明確指定正式敵人的移動／AI元件；測試控制器只暫停這些元件、目標 Animator 和 Rigidbody，演出結束時還原原始狀態。可用 onTargetLocked、onTargetReleased、onFinisherImpact 事件接入既有控制鎖與傷害系統。本場景不含正式傷害、死亡或 boss 邏輯。

測試場景播放期間使用獨立的 VoidRend_Pipeline / VoidRend_Renderer。全畫面折射在透明物件繪製後、後處理前取得畫面，所以刀光與背景都會折射。退出播放／停用控制器會恢復先前品質設定的管線。正式整合應將此效果接入遊戲的攝影機控制權與渲染流程。

角色姿態使用 Bocchi 的 Bip001 骨架與双節骨骼求解；更換骨架命名不同的模型時，需要重新對應骨骼。武器、刀鞘為簡化測試幾何，收刀以沿鞘口移動並裁短外露刀身表示。沒有原片動畫資料，這是依觀察製作的近似演出，並非逐幀相同的動作還原。

## 驗證

Editor SessionState 的 `VoidRend.Capture` 設為 true 後進入 Play，可自動輸出六個階段的 Game View 截圖到 Temp/VoidRendReview，並檢查演出完成後的目標位置、解鎖及正面鏡頭。

參考影片：https://www.youtube.com/watch?v=d-ggzGbsEWE ，重點觀察 0:27–0:35 的甩刀、蓄力、折射刀網與近景收招。透過播放器逐格前進，於關鍵連續幀及段落取樣檢視；並未取得原片 3D 相機或骨骼資料。
