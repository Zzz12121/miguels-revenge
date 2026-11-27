# 河鎮的不規則環山

`InkboundTown_Exploration.unity` 現在以三層立體山脈包住整座河鎮。近山、中山、遠山採不同的峰位、山脊厚度及濃淡；環線接近圓形／橢圓形，但半徑、山峰、山腳都有凹凸轉折。移除原有零散的 `V3 brush ridge` 背景組，另加弧形谷地接住舊地面的外緣。河道兩端延伸進較低的山谷。

這是外圍構圖更新；原有街道、房屋、居民與城內碰撞／導航仍沿用現有資料。山景與谷地延伸屬裝飾地形，沒有新增碰撞或擴張可行走範圍。居民多點散墨及使用者的速度 `0.7` 保留。

來源是 `Assets/_Project/Scripts/Editor/InkboundTownBuilder.Basin.cs`。完整生成流程的 `BuildV3Landscape()` 也會建立同一組山景。要只更新環山，先儲存並退出 Play，再執行 **GeminiGrowUp → Inkbound Town V3 → Update surrounding mountain basin**。

山形網格使用穩定路徑 `Models/Environment/InkboundTown/IT_Basin_*.asset`，材料為 `Materials/InkboundTown/M_IT_BasinRidge0..2.mat`。近山筆墨使用連續世界座標，避免閉合環線的 UV 接縫。

必要檢查與圖片在 `output/inkbound-town/mountain-basin/`：72 個五度方位都有山峰，網格接縫誤差為零、三層山景及網格均已存成資產，居民數量與散墨速度保持。`01`、`02` 是構圖檢查，拍攝期間暫停距離淡化並顯示近景 LOD，拍完立即復原；`03`–`06` 使用正常遊戲材質淡化，呈現從河鎮望向四周的效果。主要實玩交給使用者。
