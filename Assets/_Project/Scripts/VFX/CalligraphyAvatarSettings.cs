using System;
using UnityEngine;

public sealed partial class CalligraphyBossCombat
{
    [Serializable] public class AvatarPillarSettings
    {
        public GameObject prefab;
        [Min(.1f)] public float scale=1.8f;
        [Range(0,1)] public float opacity=.6f;
        [Range(1,12)] public int count=4;
        public Vector3 spawnRange=new(7,.4f,7);
        [Min(.01f)] public float stagger=.18f,duration=1.6f,frequency=1.15f;
        public bool randomDistribution=true,followElder=true;
        public float height=.1f;
        [Min(0)] public float minimumDistance=1.8f,maximumDistance=7;
    }
    [Serializable] public class AvatarVisualSettings
    {
        [Min(10),Tooltip("法像完整模型的世界高度（公尺），不是 Transform 倍率。預設 100；下半身藏在地面下，只顯現上半身。")] public float size=100;
        public Vector3 positionOffset;
        [Min(0)] public float distanceBehind=24;
        public float height=0;
        [Range(0,1)] public float opacity=.78f;
        [Min(.1f)] public float appearSeconds=3.2f,dissolveSeconds=2.5f;
        [Range(0,2)] public float flowStrength=.65f;
        [Range(0,1)] public float faceClarity=.86f;
        [Header("髮鬚／衣袍生命感（世界公尺）")]
        [Min(0)] public float hairAmplitude=2.2f,beardAmplitude=1.3f,robeAmplitude=2.8f;
        [Min(.05f)] public float hairSpeed=.85f,robeSpeed=.65f,flowSpeed=1;
        [Range(0,2)] public float motionInertia=.8f;
        [Header("點睛／緩慢凝視")]
        [Range(0,1)] public float eyeVisibility=.9f;
        [Range(0,2)] public float eyeContrast=1;
        [Min(.05f)] public float gazeSpeed=.7f;
        public Vector2 gazeOffset;
        public bool followElderRotation=false;
        public Material material;
    }
    [Serializable] public class AvatarSweepSettings
    {
        [Range(10,170)] public float angle=60;
        [Min(2)] public float radius=58;
        [Min(.1f),Tooltip("每掃秒數；越小越快。")] public float seconds=1.55f;
        [Min(0)] public float interval=.65f,firstDamage=20,secondDamage=24;
        public bool firstKnockback=true,secondKnockback=true;
        [Min(0)] public float knockbackSpeed=10;
        [Min(.1f)] public float contactHeight=3.5f,trajectoryWidth=4.5f;
        [Min(.1f),Tooltip("乘在橫掃半徑上，筆尖與擊中判定一起改變；插地仍對準 Slam Forward Distance。")] public float brushLengthMultiplier=1.18f;
        [Min(.1f)] public float trailSize=1,trailLifetime=1.8f;
        [Range(0,1)] public float trailOpacity=.88f;
    }
    [Serializable] public class AvatarSlamSettings
    {
        [Min(.2f)] public float seconds=2.2f;
        [Min(0)] public float damage=32;
        [Min(1)] public float radius=75,forwardDistance=20;
        [Min(1)] public float launchHeight=25,minimumLaunchSpeed=28;
        [Min(0)] public float horizontalPush=5;
        [Range(.1f,.6f),Tooltip("前搖進度：此時完成直立握筆，之後沿垂直方向插下。")] public float verticalReady=.38f;
        public Vector3 gripOffset;
    }
    [Serializable] public class AvatarSeaSettings
    {
        [Min(5)] public float radius=75;
        [Min(.1f)] public float spreadSeconds=2.6f,duration=28,fadeSeconds=2;
        [Range(0,1)] public float opacity=.95f;
        [Range(.1f,2)] public float density=1;
        [Min(0)] public float damage=8;
        [Min(.2f)] public float damageInterval=1.5f;
        [Header("宿墨翻湧（視覺浪峰不施加彈跳）")]
        public float meanHeight=.15f;
        [Min(0)] public float waveAmplitude=2.4f;
        [Min(.05f)] public float churnSpeed=.85f;
        [Range(.1f,3)] public float vortexDensity=1,vortexStrength=1.3f;
        [Range(0,48)] public int inkFilaments=20;
        [Range(0,2)] public float edgeStrength=.8f;
        [Range(.1f,1),Tooltip("落海時移速倍率；離開墨海即解除。落海不再彈起。")] public float slowMultiplier=.45f;
        [HideInInspector] public bool rescueToPlatform=false;
        public Material material;
    }
    [Serializable] public class AvatarPlatformSettings
    {
        [Range(12,128)] public int count=64;
        [HideInInspector] public Vector2 sizeRange=new(7,10);
        public Vector2 heightRange=new(3,85);
        [Tooltip("3–9 / 10–25 / 25–45 / 45–65 / 65–85 高度帶權重；依 Height Range 等比例映射。")]
        public float[] heightWeights={.18f,.24f,.22f,.2f,.16f};
        [Header("破碎岩島尺寸（頂面最長直徑）")]
        public Vector2 smallSize=new(3.5f,6),mediumSize=new(8,15),largeSize=new(19,29);
        public Vector3 sizeWeights=new(.42f,.4f,.18f);
        public Vector2 thicknessRange=new(4,22);
        [Range(0,1)] public float irregularity=.65f,sharpness=.85f,clustering=.4f;
        [Min(0)] public float spawnStagger=.035f;
        [Range(0,1)] public float riseSpeedVariation=.4f;
        [Min(10)] public float spawnRadius=68;
        [Min(.1f)] public float riseSpeed=18;
        [Min(0)] public float minimumGap=.7f;
        public bool randomYaw=true;
        [Min(.1f)] public float landingPlatformSize=14;
        public Material material;
    }
    [Serializable] public class AvatarClimbSettings
    {
        public bool enabled=true;
        [Min(.1f)] public float reach=1.35f;
        [Min(.5f)] public float seconds=2.1f;
        [Range(10,80),Tooltip("面向岩壁的最大偏角。按住 W 且腰部低於邊緣才會抓邊，不會自動攀爬。")]
        public float facingAngle=55;
        [Min(.01f),Tooltip("腰部至少低於頂面多少公尺才可攀爬。")]
        public float waistClearance=.08f;
        [Tooltip("腳底到邊緣的可抓取高度；仍須腰部在邊緣下、面朝岩壁並按住 W。只限這招的浮石。")]
        public Vector2 ledgeHeight=new(.5f,2.5f);
    }
    [Serializable] public class AvatarShakeSettings
    {
        [Min(0)] public float strength=.15f;
        [Min(.05f)] public float duration=.6f;
        [Min(.1f)] public float frequency=12;
        public AvatarShakeSettings(){}
        public AvatarShakeSettings(float strength,float duration,float frequency)
        {this.strength=strength;this.duration=duration;this.frequency=frequency;}
    }
    [Serializable] public class InkAvatarUltimateSettings
    {
        [Min(.2f)] public float roarSeconds=4.3f;
        public AvatarPillarSettings pillars=new();
        public AvatarVisualSettings avatar=new();
        public AvatarSweepSettings sweep=new();
        public AvatarSlamSettings slam=new();
        public AvatarSeaSettings sea=new();
        public AvatarPlatformSettings platforms=new();
        public AvatarClimbSettings climb=new();
        public AvatarShakeSettings roarShake=new(.045f,3.5f,7),summonShake=new(.14f,1.1f,6),
            firstSweepShake=new(.23f,.6f,12),secondSweepShake=new(.28f,.7f,14),
            slamShake=new(.65f,1.1f,18),risingShake=new(.09f,2.6f,5);
        public bool airCombatProjectiles=true;
        [Min(.5f)] public float airAttackInterval=2.5f;
        [Min(0)] public float airAttackDamage=8;
        [Min(1)] public float airProjectileSpeed=18;
        public int randomSeed=521;
    }
    public InkAvatarUltimateSettings inkAvatarUltimate=new();
    public CalligraphyInkAvatarUltimate AvatarUltimate=>avatarUltimate;
    CalligraphyInkAvatarUltimate avatarUltimate;
    void InkAvatarUltimate()
    {
        if(!avatarUltimate)
            avatarUltimate=CalligraphyInkAvatarUltimate.Create(this,inkAvatarUltimate,effectsRoot);
        if(avatarUltimate.Completed)FinishCast();
    }
}
