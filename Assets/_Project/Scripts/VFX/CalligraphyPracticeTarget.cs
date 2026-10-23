using UnityEngine;

/// <summary>Range-only damage receiver. Deliberately has no enemy AI or outgoing attacks.</summary>
[DisallowMultipleComponent]
public sealed class CalligraphyPracticeTarget : MonoBehaviour, IDamageable
{
    public float DamageTaken { get; private set; }
    [Min(1)] public float maximumHealth = 5000f;
    [Min(.1f)] public float resetAfterSeconds = 5f;
    public float Health { get; private set; }
    float lastDamage = float.NegativeInfinity;
    float barHeight = 2.5f;
    void Awake()
    {
        Health = maximumHealth;
        foreach (var c in GetComponentsInChildren<Collider>())
            if (!c.isTrigger) barHeight = Mathf.Max(barHeight, c.bounds.max.y - transform.position.y + .3f);
    }
    void Update()
    {
        if (Time.time - lastDamage >= resetAfterSeconds) Health = maximumHealth;
    }
    void OnGUI()
    {
        if(!CalligraphyWorldIsolation.Allows(transform))return;
        var camera = Camera.main;
        if (!camera) return;
        Vector3 p = camera.WorldToScreenPoint(transform.position + Vector3.up * barHeight);
        if (p.z <= 0) return;
        Rect rect = new Rect(p.x - 65, Screen.height - p.y, 130, 12);
        Color old = GUI.color;
        GUI.color = new Color(.05f,.05f,.05f,.85f); GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = new Color(.9f,.15f,.12f); GUI.DrawTexture(new Rect(rect.x+1,rect.y+1,128*Mathf.Clamp01(Health/maximumHealth),10),Texture2D.whiteTexture);
        GUI.color = Color.white;
        GUI.Label(new Rect(rect.x,rect.y-21,160,22), $"{Mathf.CeilToInt(Health)} / {maximumHealth:0}");
        GUI.color = old;
    }
    public bool IsDead => false; // Reusable practice dummy: survives a full skill and repeated tests.
    public void TakeDamage(float amount, Vector3 hitPoint)
    {
        if(!CalligraphyWorldIsolation.Allows(transform))return;
        if (amount <= 0) return;
        DamageTaken += amount;
        Health = Mathf.Max(0, Health - amount);
        lastDamage = Time.time;
        var targetable = GetComponent<VoidRendTargetable>();
        if (targetable) targetable.PlayHitReaction();
    }
    [ContextMenu("Reset damage counter")]
    public void ResetDamage() { DamageTaken = 0; Health = maximumHealth; lastDamage = float.NegativeInfinity; }
}
