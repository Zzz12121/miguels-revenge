using UnityEngine;

/// <summary>
/// 掛在物件（或它的上層物件）上：第三人稱鏡頭的「擋住角色就暫時隱藏」不會隱藏它，改成把鏡頭拉近 / 抬高。
/// 用在名字裡沒有建築關鍵字、但藏掉會破圖的東西（例如拼起來的建築、大型擺設）。
/// 見 ThirdPersonCamera.FullView.cs。
/// </summary>
[DisallowMultipleComponent]
public sealed class CameraNeverHide : MonoBehaviour { }
