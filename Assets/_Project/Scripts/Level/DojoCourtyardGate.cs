using UnityEngine;

/// <summary>Standalone dojo entrance. Does not start or seal a boss encounter.</summary>
[DisallowMultipleComponent]
public sealed class DojoCourtyardGate : MonoBehaviour
{
    [Header("Door leaves (hinge origins)")]
    public Transform leftHinge;
    public Transform rightHinge;
    [Min(.2f)] public float moveSeconds = 1.4f;
    [Range(45,110)] public float openDegrees = 92f;
    [Min(1)] public float interactDistance = 4f;
    [Min(1)] public float halfOpening = 5.6f;
    [Min(1)] public float doorHeight = 5.3f;

    public bool IsOpen { get; private set; }
    public bool IsMoving { get; private set; }
    public bool CloseBlocked => IsOpen && PlayerInSweep();
    PlayerHealth player;
    CharacterController body;
    float progress;
    float fromAngle;
    float angle;
    float targetAngle;
    int openingDirection = 1;

    void Awake()
    {
        player = PlayerHealth.Find();
        if(player) body = player.GetComponent<CharacterController>();
        ApplyAngle(0);
    }

    public bool CanInteract()
    {
        if(!player || player.IsDead || Time.timeScale <= 0 || GameUI.OverlayOpen) return false;
        Vector3 p = transform.InverseTransformPoint(player.transform.position);
        return Mathf.Abs(p.x) < halfOpening + 1 && Mathf.Abs(p.z) <= interactDistance && p.y > -1 && p.y < doorHeight + 1;
    }

    // Conservative footprint of both complete door arcs, including the player's capsule radius.
    bool PlayerInSweep()
    {
        if(!body || !player) return false;
        Bounds b = body.bounds;
        if(b.min.y > transform.position.y + doorHeight || b.max.y < transform.position.y) return false;
        Vector3 p = transform.InverseTransformPoint(b.center);
        float radius = Mathf.Max(b.extents.x,b.extents.z) + .15f;
        if(Mathf.Abs(p.z) < radius && Mathf.Abs(p.x) < halfOpening + radius) return true;
        if(p.z * openingDirection < -radius) return false;
        float depth = p.z * openingDirection;
        foreach(int side in new[]{-1,1})
        {
            float inward = (p.x - side * halfOpening) * -side;
            if(inward < -radius || depth < -radius) continue;
            if(inward * inward + depth * depth < (halfOpening + radius) * (halfOpening + radius)) return true;
        }
        return false;
    }

    public bool TryToggle()
    {
        if(IsMoving || !CanInteract() || CloseBlocked) return false;
        if(!IsOpen)
        {
            // Swing away from the person opening it, from either side of the gate.
            openingDirection = transform.InverseTransformPoint(player.transform.position).z >= 0 ? -1 : 1;
        }
        IsOpen = !IsOpen;
        fromAngle = angle;
        targetAngle = IsOpen ? openDegrees * openingDirection : 0;
        progress = 0;
        IsMoving = true;
        return true;
    }

    void Update()
    {
        if(!player)
        {
            player = PlayerHealth.Find();
            if(player) body = player.GetComponent<CharacterController>();
        }
        if(IsMoving)
        {
            // If the player walks into a closing arc, reopen instead of trapping them.
            if(!IsOpen && PlayerInSweep())
            {
                IsOpen = true; fromAngle = angle;
                targetAngle = openDegrees * openingDirection; progress = 0;
            }
            progress = Mathf.Min(1, progress + Time.deltaTime / Mathf.Max(.2f,moveSeconds));
            ApplyAngle(Mathf.Lerp(fromAngle,targetAngle,Mathf.SmoothStep(0,1,progress)));
            if(progress >= 1) IsMoving = false;
        }
        if(!CanInteract()) return;
        bool blocked = CloseBlocked;
        InteractPrompt.Offer(this, Mathf.Abs(transform.InverseTransformPoint(player.transform.position).z),
            IsMoving ? "大門移動中" : blocked ? "請先離開門扇範圍再關門" : IsOpen ? "關閉大門" : "推開大門", !IsMoving && !blocked);
        if(!IsMoving && !blocked && InteractPrompt.Pressed(this)) TryToggle();
    }

    void ApplyAngle(float value)
    {
        angle = value;
        if(leftHinge) leftHinge.localRotation = Quaternion.Euler(0,-value,0);
        if(rightHinge) rightHinge.localRotation = Quaternion.Euler(0,value,0);
    }
}
