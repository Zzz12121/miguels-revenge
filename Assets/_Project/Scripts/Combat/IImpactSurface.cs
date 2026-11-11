/// <summary>被打中時的材質，決定打擊火花、碎屑和音效（PlayerHitFeedback 使用）。</summary>
public enum ImpactSurface
{
    Flesh,  // 敵人、Boss（預設）
    Rock,
    Wood,
}

/// <summary>可以被打的物件如果不是血肉（石頭、樹），實作這個介面告訴打擊回饋要用哪一種效果。</summary>
public interface IImpactSurface
{
    ImpactSurface Surface { get; }
}
