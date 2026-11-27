using UnityEngine;

/// <summary>Authored permeable scenery; keeps its mesh batch separate from solid architecture.</summary>
[DisallowMultipleComponent]
public sealed class InkboundPassableGroup : MonoBehaviour
{
    [Tooltip("容器本身不消失；讓裡面的桌、椅、器皿各自散墨。")]
    public bool containerOnly;
}
