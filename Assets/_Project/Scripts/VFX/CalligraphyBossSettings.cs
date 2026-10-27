using System;
using UnityEngine;

public sealed partial class CalligraphyBossCombat
{
    [Serializable] public class ProjectileSettings
    {
        [Min(1)] public int singleCount=1,comboCount=4;
        [Min(.1f)] public float strokeSeconds=1.15f;
        [Min(1),Tooltip("累積飛行距離；不是直線離開 Boss 的距離。最後一段永久停止追蹤。")] public float maximumDistance=28;
        [Range(0,1)] public float homingEndPercentage=.9f,homingStrength=.9f;
        [Range(0,180),Tooltip("每秒最大轉向角度，還會乘上 Homing Strength。")] public float turnRate=65;
        [Min(.01f)] public float collisionRadius=.32f,vfxScale=.65f;
    }
    [Serializable] public class MeleeSettings
    {
        [Range(0,2)] public int cloneCount=2;
        [Min(1)] public int comboCount=5;
        [Min(.1f)] public float cloneSideDistance=5.75f,cloneClearance=.75f,strokeSeconds=1.2f;
        [Min(0)] public float damage=15;
        [Min(.05f)] public float hitRadius=.8f,vfxScale=1;
        [Min(0)] public float cloneDamage=8,cloneMoveSpeed=2.6f;
        [Min(.1f)] public float cloneStrokeSeconds=2.2f,cloneLifetime=10,cloneFadeSeconds=1.3f;
        [Min(1)] public int cloneHits=5;
    }
    [Serializable] public class WaveSettings
    {
        [Range(-180,180),Tooltip("相對原有特效朝向的水平偏移；-90 向左轉九十度，判定與擊退一起旋轉。")] public float yawOffset=-90;
        [Range(1,16),Tooltip("實際傷害波的數量；周圍裝飾粒子仍保留原資源構成。")] public int waveCount=1;
        [Min(.1f),Tooltip("相鄰衝擊波的生成間隔（秒）。")] public float waveInterval=1;
        [Min(.01f),Tooltip("Pulses 原本 .45，現在 1.8：每一空間尺寸都是原本四倍。判定讀取實際粒子網格。")] public float vfxScale=1.8f;
        [Min(0)] public float damage=10;
        [Min(.05f)] public float hitInterval=.8f,controlSeconds=.5f;
        [Min(0),Tooltip("水平擊退初速 / 垂直擊飛初速；高度同時受玩家重力影響。")] public float knockbackSpeed=3,launchSpeed=3;
        [Min(.05f),Tooltip("連環墨浪波前的離地判定高度（公尺）；腳底高於此值即可跳過該波。")] public float pulseContactHeight=.55f;
        [Range(.01f,1),Tooltip("重砸最外緣保留的擊退比例；從筆尖落點到特效邊緣線性減弱，不影響傷害值。")] public float edgeKnockbackMultiplier=.15f;
    }
    [Serializable] public class PulseSettings : WaveSettings
    {
        public PulseSettings(){waveCount=6;}
        [Min(0)] public float releaseSeconds=.8f;
        [Min(.1f)] public float castSeconds=4.5f;
    }
    [Serializable] public class SlamSettings
    {
        [Range(-45,45)] public float forwardYawOffset;
        [Min(.1f)] public float castSeconds=3.4f,jumpHeight=3.8f,jumpDistance=12,directHitRadius=.8f;
        public WaveSettings wave=new WaveSettings{vfxScale=.45f,damage=22,controlSeconds=1.15f,knockbackSpeed=9,launchSpeed=7};
    }
    [Serializable] public class RuneSettings
    {
        [Min(1),Tooltip("上一版每輪 4 個；本版預設 12。")] public int count=12;
        [Min(1)] public float spawnRadius=20;
        [Min(.1f)] public float minimumSpacing=4,strokeSeconds=.5f;
        [Min(.05f),Tooltip("越大預警越短；完成由實際粒子生命週期判斷，不額外等待。")] public float warningSpeed=4,fallSpeed=1,scale=.45f;
        [Min(.05f)] public float hitRadius=1.5f,centerRadius=.85f,stunSeconds=1;
        [Tooltip("負數自動讀取 Rune 最後一層符印的 delay + lifetime；秒數以原資源時間計。")] public float warningTimelineEnd=-1;
        [Min(0),Tooltip("略去 Fall 前置聚氣；0.75 對應原資源開始落筆的符印。")] public float fallTimelineStart=.75f;
        [Min(.1f)] public float residueRadius=2.2f,residueLifetime=6,slowSeconds=2.5f;
        [Range(.05f,1)] public float slowMultiplier=.6f;
    }
    [Serializable] public class DragonSettings
    {
        [Min(.1f)] public float spawnHeight=25,diveSpeed=26,impactRadius=7,releaseSeconds=3.25f,castSeconds=7;
        [Min(.01f)] public float smokeScale=.7f,explosionScale=1.1f,buffScale=.65f,flashScale=.8f;
        [Range(0,1)] public float smokeSpinMultiplier=.25f;
        public Vector3 smokeOffset=new Vector3(0,.04f,0);
        [Tooltip("僅跟隨腳下位置，不繼承骨骼旋轉。關閉則留在施法起點。")] public bool smokeFollowPosition=true;
        [Tooltip("開啟後已生成粒子留在世界座標；關閉則跟著穩定的地面 Anchor 位移。")] public bool smokeWorldSpace=true;
        [Range(-180,180),Tooltip("地面特效的固定水平旋轉，不允許翻成直立。")] public float smokeYawOffset;
        public Vector3 explosionOffset=new(0,.35f,0),buffOffset=new(0,.12f,0),flashOffset=new(0,.18f,0);
        public Vector3 explosionEuler,buffEuler,flashEuler=new(0,37,0);
        [Min(0),Tooltip("原資源 Burst 群在第 5 秒；重排 delay，不預先模擬掉爆炸。")] public float explosionTimelineStart=5;
        [Min(.1f)] public float tailStartSeconds=3.6f;
        [Range(0,30)] public int debrisCount=12;
        [Min(0)] public float debrisScale=.6f,debrisLift=3,debrisDelay=.15f;
    }
    [Serializable] public class PaintingSettings
    {
        [Min(.1f),Tooltip("相對上一版 192×108×192 包覆空間；預設 1.7，不再次套用之前的 1.5。")] public float sizeMultiplier=1.7f;
        [Min(.1f)] public float entrySeconds=4,containedSeconds=20,exitSeconds=2.5f;
        [Range(0,12),Tooltip("畫中世界墨龍總數。起點以 360°／數量平均環繞玩家；5 隻=72°、6 隻=60°、4 隻=90°。改值於下次施放生效。")]
        public int dragonCount=5;
        [Min(.05f)] public float pulseScale=.35f,pulseInterval=.3f,pulseFade=1.5f;
        [Min(1)] public float faceScale=125;
        [Range(0,1)] public float faceOpacity=.72f;
        [Range(.2f,.95f)] public float streamSpread=.85f,scenerySpread=.85f;
        [Range(3,18)] public int sceneryGroups=9;
        public Vector3 faceOffset;
        [Range(0,1)] public float eyeVisibility=1;
        public Vector2 eyeCenter=new(.035f,.07f);
    }
    [Serializable] public class BeamSettings
    {
        [Min(.05f)] public float windupSeconds=1.8f,hitInterval=.55f,hitRadius=.65f,vfxScale=.7f;
        [Min(0),Tooltip("蓄能固定在老者身上的高度；不使用揮動中的筆尖。")] public float emitterHeight=1.5f;
        public bool horizontalOnLevelGround=true;
        [Min(0)] public float heightTolerance=2;
        [Min(.1f)] public float maximumRange=35;
        public Vector3 emitterOffset;
        public Vector3 vfxEuler;
    }
    public ProjectileSettings inkProjectile=new();
    public MeleeSettings inkMelee=new();
    public PulseSettings frontalPulses=new();
    public SlamSettings leapSlam=new();
    public RuneSettings runeAttack=new();
    public DragonSettings inkDragon=new();
    public PaintingSettings paintingWorld=new();
    public BeamSettings inkBeam=new();
    [Serializable] public class DeathSettings
    {
        [Min(.01f)] public float vfxScale=.65f,playbackSpeed=1.5f,fadeSeconds=1.5f;
    }
    public DeathSettings deathPresentation=new();
    [Tooltip("各招收招後冷卻；0 使用共用 Recovery Seconds。順序與技能清單相同。")]
    public float[] skillRecovery=new float[10];

    public static Vector3 Abdomen(PlayerHealth target)
    {
        var cc=target.GetComponent<CharacterController>();
        return cc?cc.bounds.center-Vector3.up*(cc.bounds.extents.y*.2f):target.transform.position+Vector3.up*.8f;
    }
    Vector3 HorizontalAim(Vector3 from)
    {
        Vector3 d=Vector3.ProjectOnPlane(player.transform.position-from,Vector3.up);
        return d.sqrMagnitude>.001f?d.normalized:transform.forward;
    }
    bool ClonePosition(int side,out Vector3 position)
    {
        // Search only the requested local side. Never fall back to stacking doubles on the boss.
        for(int attempt=0;attempt<12;attempt++)
        {
            Vector3 wanted=transform.position+transform.right*side*(inkMelee.cloneSideDistance+(attempt/3)*.6f)+transform.forward*((attempt%3-1)*1.5f);
            position=CalligraphyGeometry.Ground(wanted,transform);
            if(!Physics.Raycast(position+Vector3.up*.3f,Vector3.down,.6f,~0,QueryTriggerInteraction.Ignore))continue;
            bool blocked=false;
            foreach(var c in Physics.OverlapCapsule(position+Vector3.up*(inkMelee.cloneClearance+.1f),position+Vector3.up*1.8f,inkMelee.cloneClearance,~0,QueryTriggerInteraction.Ignore))
                if(!c.isTrigger){blocked=true;break;}
            if(!blocked)return true;
        }
        position=default;return false;
    }
    void OnDrawGizmosSelected()
    {
        if(inkAvatarUltimate!=null)
        {
            var a=inkAvatarUltimate;Vector3 impact=transform.position+transform.forward*a.slam.forwardDistance;
            if(avatarUltimate && avatarUltimate.GroundIsHazardous)impact=avatarUltimate.ImpactPoint;
            void Ring(Vector3 center,float radius)
            {Vector3 prev=center+Vector3.right*radius;for(int i=1;i<=64;i++){float angle=i*Mathf.PI*2/64;Vector3 next=center+new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle))*radius;Gizmos.DrawLine(prev,next);prev=next;}}
            Gizmos.color=new Color(.15f,.75f,.8f,.8f);Ring(impact,a.sea.radius);
            Gizmos.color=new Color(.8f,.65f,.2f,.8f);Ring(impact+Vector3.up*a.platforms.heightRange.x,a.platforms.spawnRadius);Ring(impact+Vector3.up*a.platforms.heightRange.y,a.platforms.spawnRadius);
            Gizmos.color=Color.white;Gizmos.DrawLine(impact,impact+Vector3.up*25);Gizmos.DrawWireSphere(impact,1);
            if(avatarUltimate && avatarUltimate.GroundIsHazardous){Gizmos.color=Color.green;Ring(avatarUltimate.LandingPoint,a.platforms.landingPlatformSize*.5f);}
        }
        Gizmos.color=Color.red;Gizmos.DrawWireSphere(transform.position,meleeDistance);
        Gizmos.DrawWireSphere(transform.position+transform.forward*7,inkDragon.impactRadius);
        Gizmos.color=Color.cyan;Gizmos.DrawWireSphere(player?player.transform.position:transform.position,runeAttack.spawnRadius);
        Gizmos.DrawWireSphere(transform.position-transform.right*inkMelee.cloneSideDistance,inkMelee.cloneClearance);
        Gizmos.DrawWireSphere(transform.position+transform.right*inkMelee.cloneSideDistance,inkMelee.cloneClearance);
        Gizmos.color=Color.yellow;Gizmos.DrawLine(transform.position+Vector3.up,transform.position+Vector3.up+transform.forward*inkProjectile.maximumDistance);
        // Actual live wave bounds, not a guessed oversized cone. Collision further tests mesh triangles.
        if(effectsRoot)
        {
            Gizmos.color=new Color(1,.45f,.1f);
            foreach(var ps in effectsRoot.GetComponentsInChildren<ParticleSystem>())
                if(ps.name.StartsWith("yy_Shockwave_1") && ps.particleCount>0)
                {var r=ps.GetComponent<ParticleSystemRenderer>();if(r && r.enabled)Gizmos.DrawWireCube(r.bounds.center,r.bounds.size);}
        }
        Gizmos.color=new Color(.4f,.5f,1,.5f);Gizmos.DrawWireCube(transform.position+Vector3.up*39*paintingWorld.sizeMultiplier,new Vector3(paintingRadius*2,108,paintingRadius*2)*paintingWorld.sizeMultiplier);
    }
}
