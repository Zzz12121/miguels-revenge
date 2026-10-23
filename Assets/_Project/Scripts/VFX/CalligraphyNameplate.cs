using UnityEngine;

/// <summary>Presentation-only screen-facing name. Does not make the dock elder targetable.</summary>
public sealed class CalligraphyNameplate : MonoBehaviour
{
    public string displayName = "沒人知道是誰的老者";
    public float height = 2.65f;
    public float visibleDistance = 35f;
    GUIStyle style;
    void OnGUI()
    {
        if(!CalligraphyWorldIsolation.Allows(transform))return;
        var camera = Camera.main;
        if (!camera || Vector3.Distance(camera.transform.position, transform.position) > visibleDistance) return;
        Vector3 p = camera.WorldToScreenPoint(transform.position + Vector3.up * height);
        if (p.z <= 0) return;
        if (style == null) style = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
        style.fontSize = Mathf.Clamp(Mathf.RoundToInt(Screen.height / 48f), 14, 24);
        var rect = new Rect(p.x - 180, Screen.height - p.y - 20, 360, 40);
        style.normal.textColor = Color.black;
        GUI.Label(new Rect(rect.x + 1, rect.y + 2, rect.width, rect.height), displayName, style);
        style.normal.textColor = new Color(.95f, .91f, .78f);
        GUI.Label(rect, displayName, style);
    }
}
