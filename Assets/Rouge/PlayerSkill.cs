using System;
using Unity.Mathematics;
using UnityEngine;

public enum PlayerSkillType
{
    AutoShoot,
    LeapSmash,
    LightPillarStrike,
    BombThrow,
    LaserBeam,
    MeleeSlash,
    Shockwave,
    MeteorRain,
    IceZone,
    PoisonBottle,
    Dash,
    OrbitBall
}

[Flags]
public enum PlayerSkillTag
{
    None = 0,
    Movement = 1 << 0
}

[Flags]
public enum SkillHitEffectTag
{
    None = 0,
    Knockback = 1 << 0,
    Launch = 1 << 1,
    Poison = 1 << 2,
    Slow = 1 << 3,
    Curse = 1 << 4,
    Burn = 1 << 5
}

[Serializable]
public struct SkillHitEffectConfig
{
    public SkillHitEffectTag Tags;
    public float KnockbackForce;
    public float LaunchHeight;
    public float LaunchLandingRadius;
    public float PoisonSpreadRadius;
    public float SlowPercent;
    public float SlowDuration;
    public float CurseExplosionDamage;
    public float CurseExplosionRadius;
    public float BurnDamage;
    public float BurnDuration;
}

public readonly struct PlayerSkillDefinition
{
    public PlayerSkillDefinition(PlayerSkillType type, string displayName, string triggerLabel, bool isPassive = false)
    {
        Type = type;
        DisplayName = displayName;
        TriggerLabel = triggerLabel;
        IsPassive = isPassive;
    }

    public PlayerSkillType Type { get; }
    public string DisplayName { get; }
    public string TriggerLabel { get; }
    public bool IsPassive { get; }
}

public readonly struct PlayerSkillProgressBinding
{
    public PlayerSkillProgressBinding(PlayerSkillType type, int progressionIndex, string shortLabel)
    {
        Type = type;
        ProgressionIndex = progressionIndex;
        ShortLabel = shortLabel;
    }

    public PlayerSkillType Type { get; }
    public int ProgressionIndex { get; }
    public string ShortLabel { get; }
}

public readonly struct SkillUpdateContext
{
    public SkillUpdateContext(
        float deltaTime,
        float2 playerPosition,
        float2 aimDirection,
        bool hasMouseGroundPoint,
        Vector3 mouseGroundPoint,
        float renderHeight,
        float arenaHalfExtent)
    {
        DeltaTime = deltaTime;
        PlayerPosition = playerPosition;
        AimDirection = aimDirection;
        HasMouseGroundPoint = hasMouseGroundPoint;
        MouseGroundPoint = mouseGroundPoint;
        RenderHeight = renderHeight;
        ArenaHalfExtent = arenaHalfExtent;
    }

    public float DeltaTime { get; }
    public float2 PlayerPosition { get; }
    public float2 AimDirection { get; }
    public bool HasMouseGroundPoint { get; }
    public Vector3 MouseGroundPoint { get; }
    public float RenderHeight { get; }
    public float ArenaHalfExtent { get; }
}

[Serializable]
public class SkillPresentationConfig
{
    public string DisplayName = "Skill";
    public string TriggerLabel = "UNBOUND";
    public bool IsPassive;
    public KeyCode ActivationKey = KeyCode.None;

    public SkillPresentationConfig()
    {
    }

    public SkillPresentationConfig(string displayName, string triggerLabel, bool isPassive, KeyCode activationKey = KeyCode.None)
    {
        DisplayName = displayName;
        TriggerLabel = triggerLabel;
        IsPassive = isPassive;
        ActivationKey = activationKey;
    }

    public PlayerSkillDefinition ToDefinition(PlayerSkillType type)
    {
        string displayName = string.IsNullOrWhiteSpace(DisplayName) ? type.ToString() : DisplayName;
        string triggerLabel = string.IsNullOrWhiteSpace(TriggerLabel)
            ? (ActivationKey == KeyCode.None ? (IsPassive ? "PASSIVE" : "UNBOUND") : ActivationKey.ToString().ToUpperInvariant())
            : TriggerLabel;
        return new PlayerSkillDefinition(type, displayName, triggerLabel, IsPassive);
    }
}

[Serializable]
public class AutoShootSkillConfig
{
    public SkillPresentationConfig Presentation = new SkillPresentationConfig("Auto Shoot", "AUTO SHOOT", true);
    public SkillHitEffectConfig Effects;
    public int MaxBullets = 128;
    public float FireInterval = 0.06f;
    public float BulletSpeed = 42f;
    public float BulletRadius = 0.2f;
    public float BulletDamage = 14f;
    public float BulletLifetime = 1.5f;
    public int BulletsPerShot = 1;
    public float SpreadAngle = 4f;

    public PlayerSkillDefinition ToDefinition()
    {
        return Presentation.ToDefinition(PlayerSkillType.AutoShoot);
    }
}

[Serializable]
public class LeapSmashSkillConfig
{
    public SkillPresentationConfig Presentation = new SkillPresentationConfig("Leap Smash", "SPACE", false, KeyCode.Space);
    public SkillHitEffectConfig Effects;
    public float Cooldown = 8f;
    public float AirTime = 0.5f;
    public float MaxDistance = 20f;
    public float ArcHeight = 8f;
    public float LandingRadius = 18f;
    public float LandingDamage = 1500f;
    public float LandingPullForce = -300f;
    public float LandingVerticalForce = 60f;
    public float LandingInvincibility = 0.5f;

    public PlayerSkillDefinition ToDefinition()
    {
        return Presentation.ToDefinition(PlayerSkillType.LeapSmash);
    }
}

[Serializable]
public class LightPillarSkillConfig
{
    public SkillPresentationConfig Presentation = new SkillPresentationConfig("Light Pillar Strike", "Q", false, KeyCode.Q);
    public SkillHitEffectConfig Effects;
    public float Cooldown = 10f;
    public int BaseStrikeCount = 4;
    public int BonusStrikeLevelStep = 5;
    public float StartDistance = 6f;
    public float DistanceStep = 14f;
    public float BaseRadius = 10f;
    public float RadiusPerLevel = 0.1f;
    public float MaxRadius = 25f;
    public float BaseDamage = 400f;
    public float DamagePerLevel = 40f;
    public float PullForce = -120f;
    public float VerticalForce = 70f;
    public float StrikeInterval = 0.15f;
    public float VisualDuration = 0.5f;
    public float RingDuration = 0.4f;

    public PlayerSkillDefinition ToDefinition()
    {
        return Presentation.ToDefinition(PlayerSkillType.LightPillarStrike);
    }
}

[Serializable]
public class BombThrowSkillConfig
{
    public SkillPresentationConfig Presentation = new SkillPresentationConfig("Bomb Throw", "E", false, KeyCode.E);
    public SkillHitEffectConfig Effects;
    public float Cooldown = 3f;
    public float SpawnHeight = 2f;
    public float MaxThrowDistance = 30f;
    public float FlightTime = 0.8f;
    public float BaseRadius = 15f;
    public float FragmentRadius = 10f;
    public float MinRadius = 4f;
    public float RadiusLossPerBounce = 2.5f;
    public float BaseDamage = 350f;
    public float DamageFalloff = 0.65f;
    public float MinDamage = 60f;
    public float PullForce = -150f;
    public float VerticalForce = 50f;
    public int MaxBounceCount = 4;
    public float StopHorizontalVelocitySq = 4f;
    public float BounceHorizontalRetention = 0.75f;
    public float BounceVerticalRetention = 0.85f;
    public float BounceUpVelocity = 12f;
    public float BounceUpVelocityLossPerBounce = 2f;
    public int FragmentUnlockLevel = 5;
    public int FragmentBaseCount = 2;
    public int FragmentCountLevelStep = 10;
    public int MaxFragmentCount = 10;
    public float FragmentHorizontalSpeed = 12f;
    public float FragmentVerticalSpeed = 14f;
    public float RingDuration = 0.4f;

    public PlayerSkillDefinition ToDefinition()
    {
        return Presentation.ToDefinition(PlayerSkillType.BombThrow);
    }
}

[Serializable]
public class LaserBeamSkillConfig
{
    public SkillPresentationConfig Presentation = new SkillPresentationConfig("Laser Beam", "R", false, KeyCode.R);
    public SkillHitEffectConfig Effects;
    public float Cooldown = 6f;
    public float BaseDuration = 0.5f;
    public float DurationPerThirtyLevels = 1.5f;
    public float MaxLength = 150f;
    public float MaxWidth = 14f;
    public float Damage = 400f;
    public float PullForce = 50f;
    public int ExtraBeamLevelStep = 10;
    public int ExtraBeamsPerStep = 2;
    public int MaxBeamCount = 7;
    public float ScatterAngle = 20f;
    public float SubBeamRadiusMultiplier = 0.5f;
    public float SubBeamDamageMultiplier = 0.5f;

    public PlayerSkillDefinition ToDefinition()
    {
        return Presentation.ToDefinition(PlayerSkillType.LaserBeam);
    }
}

[Serializable]
public class MeleeSlashSkillConfig
{
    public SkillPresentationConfig Presentation = new SkillPresentationConfig("Melee Slash", "MOUSE L-CLICK", false);
    public SkillHitEffectConfig Effects;
    public float SlashCooldown = 0.22f;
    public float FinisherCooldown = 1.5f;
    public float ComboWindow = 1.5f;
    public float SlashDuration = 0.22f;
    public float SlashRadius = 8f;
    public float SlashDepth = 3f;
    public float ThrustRadiusMin = 3f;
    public float ThrustRadiusMax = 10f;
    public float ThrustWidth = 3f;
    public float ThrustLength = 8f;
    public float DefaultLunge = 3f;
    public float ThrustLunge = 4f;
    public float SpinLunge = 1.5f;
    public float ThrustAdvanceSpeed = 20f;
    public float SlashDamage = 800f;
    public float SpinDamage = 1200f;
    public float PullForce = 120f;
    public float SlashVerticalForce = 80f;
    public float ThrustVerticalForce = 90f;
    public float FinisherSlamDuration = 0.12f;
    public float FinisherSlamWidth = 5f;
    public float FinisherSlamLength = 12f;
    public float FinisherSlamThickness = 0.8f;
    public float FinisherSlamForwardOffset = 7f;
    public float FinisherArcHeight = 13f;
    public float FinisherArcRadius = 10f;
    public float FinisherArcStartAngle = 120f;
    public float FinisherArcEndAngle = -12f;
    public float FinisherArcTilt = 0f;
    public float SpikeDuration = 0.28f;
    public float SpikeRiseRatio = 0.25f;
    public float CenterSpikeHeight = 14f;
    public float SideSpikeHeight = 10f;
    public float CenterSpikeRadius = 7f;
    public float SideSpikeRadius = 5f;
    public float CenterSpikeDistance = 14f;
    public float SideSpikeDistance = 12f;
    public float SideSpikeAngle = 28f;
    public float CenterSpikeVerticalForce = 90f;
    public float SideSpikeVerticalForce = 65f;
    public float SpikePullForce = 40f;

    public PlayerSkillDefinition ToDefinition()
    {
        return Presentation.ToDefinition(PlayerSkillType.MeleeSlash);
    }
}

[Serializable]
public class ShockwaveSkillConfig
{
    public SkillPresentationConfig Presentation = new SkillPresentationConfig("Shockwave", "V", false, KeyCode.V);
    public SkillHitEffectConfig Effects;
    public float Cooldown = 30f;
    public float Duration = 0.6f;
    public float LaunchDuration = 0.18f;
    public float SlamDuration = 0.12f;
    public float JumpHeight = 12f;
    public float RingLifetime = 0.7f;
    public float RingDelay = 0.06f;
    public float RingStartRadius = 8f;
    public float RingEndRadius = 48f;
    public float ImpactRadius = 38f;
    public int ImpactRingCount = 5;
    public float RingThickness = 7f;
    public float RingRadiusOffset = 0f;
    public float RingLiftHeight = 4f;
    public float RingLiftFalloff = 0.5f;
    public float ImpactDamage = 2400f;
    public float PullForce = -240f;
    public float VerticalForce = 125f;
    public float CameraLift = 1.35f;
    public float CameraFovKick = 8f;
    public float LandingShake = 0.26f;

    public PlayerSkillDefinition ToDefinition()
    {
        return Presentation.ToDefinition(PlayerSkillType.Shockwave);
    }
}

[Serializable]
public class MeteorRainSkillConfig
{
    public SkillPresentationConfig Presentation = new SkillPresentationConfig("Meteor Rain", "T", false, KeyCode.T);
    public SkillHitEffectConfig Effects;
    public float Cooldown = 8f;
    public float Duration = 2f;
    public int WaveCount = 8;
    public float WaveInterval = 0.2f;
    public float ScatterRadius = 20f;
    public float VisualDuration = 0.5f;
    public float StartOffsetX = 10f;
    public float StartOffsetZ = 5f;
    public float FallHeight = 60f;
    public float StartScale = 6f;
    public float EndScale = 3f;
    public float ImpactRadius = 15f;
    public float ImpactDamage = 900f;
    public float PullForce = -220f;
    public float VerticalForce = 55f;
    public float RingDuration = 0.45f;

    public PlayerSkillDefinition ToDefinition()
    {
        return Presentation.ToDefinition(PlayerSkillType.MeteorRain);
    }
}

[Serializable]
public class IceZoneSkillConfig
{
    public SkillPresentationConfig Presentation = new SkillPresentationConfig("Ice Zone", "C", false, KeyCode.C);
    public SkillHitEffectConfig Effects;
    public float Cooldown = 10f;
    public float Duration = 4f;
    public float BaseRadius = 8f;
    public float MaxRadius = 25f;
    public float MaxRadiusLevel = 500f;
    public float TickDamage = 80f;
    public float TickPullForce = 30f;
    public float BurstRadiusBonus = 5f;
    public float BurstDamage = 800f;
    public float BurstPullForce = -250f;
    public float BurstVerticalForce = 40f;
    public float RingDuration = 0.6f;
    public float PulseBaseAlpha = 0.3f;
    public float PulseAmplitude = 0.1f;
    public float PulseSpeed = 6f;

    public PlayerSkillDefinition ToDefinition()
    {
        return Presentation.ToDefinition(PlayerSkillType.IceZone);
    }
}

[Serializable]
public class PoisonBottleSkillConfig
{
    public SkillPresentationConfig Presentation = new SkillPresentationConfig("Poison Bottle", "X", false, KeyCode.X);
    public SkillHitEffectConfig Effects = new SkillHitEffectConfig
    {
        Tags = SkillHitEffectTag.Poison,
        PoisonSpreadRadius = 4f
    };
    public float Cooldown = 5f;
    public float SpawnHeight = 2f;
    public float MaxThrowDistance = 28f;
    public float FlightTime = 0.65f;
    public float BottleVisualScale = 1.6f;
    public float ZoneDuration = 5f;
    public float ZoneRadius = 10f;
    public float ZoneCoreRatio = 0.65f;
    public float ZoneIrregularity = 0.18f;
    public float ZoneNoiseScale = 0.18f;
    public float PoisonDuration = 2f;
    public float StackDpsPerSecond = 26f;
    public float MaxPoisonDps = 120f;
    public int InitialSpreadCount = 2;
    public float SpreadRadius = 4f;
    public float SpreadStackDps = 36f;
    public float SpreadRingDuration = 0.3f;
    public float ZonePulseSpeed = 3.5f;
    public float ZonePulseAmplitude = 0.12f;

    public PlayerSkillDefinition ToDefinition()
    {
        return Presentation.ToDefinition(PlayerSkillType.PoisonBottle);
    }
}

[Serializable]
public class DashSkillConfig
{
    public SkillPresentationConfig Presentation = new SkillPresentationConfig("Whirlwind", "L-SHIFT", false, KeyCode.LeftShift);
    public SkillHitEffectConfig Effects;
    public float Cooldown = 3f;
    public float Duration = 1.5f;
    public float Distance = 21f;
    public float InvincibilityDuration = 1.5f;
    public float SpinDamage = 9f;
    public float HitRadius = 8f;
    public float BladeWidth = 4f;
    public float BladeLength = 11f;
    public float BladeThickness = 0.75f;
    public float MaxSpinRate = 3000f;
    public float ImpactRadius = 10f;
    public float ImpactDamage = 260f;
    public float PullForce = 320f;
    public float VerticalForce = 90f;
    public float RingDuration = 0.5f;

    public PlayerSkillDefinition ToDefinition()
    {
        return Presentation.ToDefinition(PlayerSkillType.Dash);
    }
}

[Serializable]
public class OrbitBallSkillConfig
{
    public SkillPresentationConfig Presentation = new SkillPresentationConfig("Orbit Ball", "PASSIVE", true);
    public SkillHitEffectConfig Effects;
    public int MaxBalls = 8;
    public int LevelsPerBall = 4;
    public float OrbitSpeed = 2.5f;
    public float FirstRingRadius = 6f;
    public float SecondRingRadius = 10f;
    public float FirstRingSpeedMultiplier = 1f;
    public float SecondRingSpeedMultiplier = 0.75f;
    public float VisualScale = 3f;
    public float DamageRadius = 2f;
    public float BaseDamage = 45f;
    public float DamagePerLevel = 2f;
    public float PullForce = 15f;
    public float VerticalForce = 3f;

    public PlayerSkillDefinition ToDefinition()
    {
        return Presentation.ToDefinition(PlayerSkillType.OrbitBall);
    }
}

[Serializable]
public class PlayerSkillConfigSet
{
    public AutoShootSkillConfig AutoShoot = new AutoShootSkillConfig();
    public LeapSmashSkillConfig LeapSmash = new LeapSmashSkillConfig();
    public LightPillarSkillConfig LightPillar = new LightPillarSkillConfig();
    public BombThrowSkillConfig BombThrow = new BombThrowSkillConfig();
    public LaserBeamSkillConfig LaserBeam = new LaserBeamSkillConfig();
    public MeleeSlashSkillConfig MeleeSlash = new MeleeSlashSkillConfig();
    public ShockwaveSkillConfig Shockwave = new ShockwaveSkillConfig();
    public MeteorRainSkillConfig MeteorRain = new MeteorRainSkillConfig();
    public IceZoneSkillConfig IceZone = new IceZoneSkillConfig();
    public PoisonBottleSkillConfig PoisonBottle = new PoisonBottleSkillConfig();
    public DashSkillConfig Dash = new DashSkillConfig();
    public OrbitBallSkillConfig OrbitBall = new OrbitBallSkillConfig();

    public static PlayerSkillConfigSet CreateDefault()
    {
        return new PlayerSkillConfigSet();
    }

    public void EnsureInitialized()
    {
        if (AutoShoot == null) AutoShoot = new AutoShootSkillConfig();
        if (LeapSmash == null) LeapSmash = new LeapSmashSkillConfig();
        if (LightPillar == null) LightPillar = new LightPillarSkillConfig();
        if (BombThrow == null) BombThrow = new BombThrowSkillConfig();
        if (LaserBeam == null) LaserBeam = new LaserBeamSkillConfig();
        if (MeleeSlash == null) MeleeSlash = new MeleeSlashSkillConfig();
        if (Shockwave == null) Shockwave = new ShockwaveSkillConfig();
        if (MeteorRain == null) MeteorRain = new MeteorRainSkillConfig();
        if (IceZone == null) IceZone = new IceZoneSkillConfig();
        if (PoisonBottle == null) PoisonBottle = new PoisonBottleSkillConfig();
        if (Dash == null) Dash = new DashSkillConfig();
        if (OrbitBall == null) OrbitBall = new OrbitBallSkillConfig();
    }

    public PlayerSkillDefinition GetDefinition(PlayerSkillType type)
    {
        EnsureInitialized();

        switch (type)
        {
            case PlayerSkillType.AutoShoot:
                return AutoShoot.ToDefinition();
            case PlayerSkillType.LeapSmash:
                return LeapSmash.ToDefinition();
            case PlayerSkillType.LightPillarStrike:
                return LightPillar.ToDefinition();
            case PlayerSkillType.BombThrow:
                return BombThrow.ToDefinition();
            case PlayerSkillType.LaserBeam:
                return LaserBeam.ToDefinition();
            case PlayerSkillType.MeleeSlash:
                return MeleeSlash.ToDefinition();
            case PlayerSkillType.Shockwave:
                return Shockwave.ToDefinition();
            case PlayerSkillType.MeteorRain:
                return MeteorRain.ToDefinition();
            case PlayerSkillType.IceZone:
                return IceZone.ToDefinition();
            case PlayerSkillType.PoisonBottle:
                return PoisonBottle.ToDefinition();
            case PlayerSkillType.Dash:
                return Dash.ToDefinition();
            case PlayerSkillType.OrbitBall:
                return OrbitBall.ToDefinition();
            default:
                return new PlayerSkillDefinition(type, type.ToString(), "UNKNOWN");
        }
    }
}

public static class PlayerSkillCatalog
{
    public static readonly PlayerSkillType[] DisplayOrder = (PlayerSkillType[])Enum.GetValues(typeof(PlayerSkillType));

    public static readonly PlayerSkillProgressBinding[] ProgressionBindings =
    {
        new PlayerSkillProgressBinding(PlayerSkillType.LightPillarStrike, 0, "LPL"),
        new PlayerSkillProgressBinding(PlayerSkillType.BombThrow, 1, "BMB"),
        new PlayerSkillProgressBinding(PlayerSkillType.LaserBeam, 2, "LSR"),
        new PlayerSkillProgressBinding(PlayerSkillType.MeleeSlash, 3, "MLR"),
        new PlayerSkillProgressBinding(PlayerSkillType.OrbitBall, 4, "ORB"),
        new PlayerSkillProgressBinding(PlayerSkillType.AutoShoot, 5, "BLT")
    };

    public static PlayerSkillTag GetTags(PlayerSkillType type)
    {
        switch (type)
        {
            case PlayerSkillType.LeapSmash:
            case PlayerSkillType.Shockwave:
            case PlayerSkillType.Dash:
                return PlayerSkillTag.Movement;
            default:
                return PlayerSkillTag.None;
        }
    }

    public static bool HasTag(PlayerSkillType type, PlayerSkillTag tag)
    {
        return (GetTags(type) & tag) != 0;
    }
}

public static class PlayerSkillMath
{
    public static void StepCooldown(ref float timer, float deltaTime)
    {
        if (timer > 0f)
        {
            timer = math.max(0f, timer - deltaTime);
        }
    }

    public static bool TryGetMouseGroundPoint(Camera camera, Vector3 screenPosition, float groundY, out Vector3 hitPoint)
    {
        hitPoint = default;
        if (camera == null)
        {
            return false;
        }

        Ray ray = camera.ScreenPointToRay(screenPosition);
        float directionY = ray.direction.y;
        if (math.abs(directionY) < 0.0001f)
        {
            return false;
        }

        float distanceToGround = (groundY - ray.origin.y) / directionY;
        if (distanceToGround < 0f)
        {
            return false;
        }

        hitPoint = ray.origin + ray.direction * distanceToGround;
        return true;
    }

    public static Vector3 ClampPlanarDistance(Vector3 origin, Vector3 target, float maxDistance)
    {
        Vector3 offset = target - origin;
        offset.y = 0f;
        if (offset.sqrMagnitude <= maxDistance * maxDistance)
        {
            return target;
        }

        Vector3 clamped = origin + offset.normalized * maxDistance;
        clamped.y = target.y;
        return clamped;
    }

    public static float2 ToPlanar(Vector3 worldPosition)
    {
        return new float2(worldPosition.x, worldPosition.z);
    }

    public static Vector3 ToWorld(float2 planarPosition, float y)
    {
        return new Vector3(planarPosition.x, y, planarPosition.y);
    }
}
