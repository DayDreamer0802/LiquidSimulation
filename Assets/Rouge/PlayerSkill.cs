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

public enum SkillExecutionType{
    Instant,
    Sustained,
    Passive
}

public enum SkillKnockbackCenter
{
    SkillPosition,
    CasterPosition
}

[Serializable]
public struct ResolvedSkillHitEffectConfig
{
    public SkillHitEffectTag Tags;
    public SkillKnockbackCenter KnockbackCenter;
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

[Serializable]
public abstract class LevelScaledSkillConfig
{
    public int MaxLevel = 60;

    public float GetValue(float3 value, int level)
    {
        return GetSkillValue(value, level, MaxLevel);
    }

    public int GetIntValue(float3 value, int level)
    {
        return Mathf.FloorToInt(GetValue(value, level));
    }

    public float GetBaseValue(float3 value)
    {
        return value.x;
    }

    public int GetBaseIntValue(float3 value)
    {
        return Mathf.FloorToInt(value.x);
    }

    public static float GetSkillValue(float3 value, int level, int maxLevel)
    {
        int lerpType = Mathf.FloorToInt(value.z);
        if (lerpType <= 0 || maxLevel <= 0)
        {
            return value.x;
        }

        float t = math.saturate(level / (float)maxLevel);
        switch (lerpType)
        {
            case 1:
                return math.lerp(value.x, value.y, t);
            case 2:
                return math.lerp(value.x, value.y, 1f - math.pow(1f - t, 2f));
            case 3:
                return math.lerp(value.x, value.y, t * t);
            case 4:
                return math.lerp(value.x, value.y, t * t * (3f - 2f * t));
            case 5:
                return math.lerp(value.x, value.y, math.sin(t * math.PI * 0.5f));
            case 6:
                return math.lerp(value.x, value.y, 1f - math.cos(t * math.PI * 0.5f));
            default:
                return value.x;
        }
    }
}

[Serializable]
public static class PlayerSkillScaling
{
    public static float3 Constant(float value)
    {
        return new float3(value, value, 0f);
    }
}

[Serializable]
public struct SkillHitEffectConfig
{
    public SkillHitEffectTag Tags;
    public SkillKnockbackCenter KnockbackCenter;
    public float3 KnockbackForce;
    public float3 LaunchHeight;
    public float3 LaunchLandingRadius;
    public float3 PoisonSpreadRadius;
    public float3 SlowPercent;
    public float3 SlowDuration;
    public float3 CurseExplosionDamage;
    public float3 CurseExplosionRadius;
    public float3 BurnDamage;
    public float3 BurnDuration;

    public ResolvedSkillHitEffectConfig Resolve(int level, int maxLevel)
    {
        return new ResolvedSkillHitEffectConfig
        {
            Tags = Tags,
            KnockbackCenter = KnockbackCenter,
            KnockbackForce = LevelScaledSkillConfig.GetSkillValue(KnockbackForce, level, maxLevel),
            LaunchHeight = LevelScaledSkillConfig.GetSkillValue(LaunchHeight, level, maxLevel),
            LaunchLandingRadius = LevelScaledSkillConfig.GetSkillValue(LaunchLandingRadius, level, maxLevel),
            PoisonSpreadRadius = LevelScaledSkillConfig.GetSkillValue(PoisonSpreadRadius, level, maxLevel),
            SlowPercent = LevelScaledSkillConfig.GetSkillValue(SlowPercent, level, maxLevel),
            SlowDuration = LevelScaledSkillConfig.GetSkillValue(SlowDuration, level, maxLevel),
            CurseExplosionDamage = LevelScaledSkillConfig.GetSkillValue(CurseExplosionDamage, level, maxLevel),
            CurseExplosionRadius = LevelScaledSkillConfig.GetSkillValue(CurseExplosionRadius, level, maxLevel),
            BurnDamage = LevelScaledSkillConfig.GetSkillValue(BurnDamage, level, maxLevel),
            BurnDuration = LevelScaledSkillConfig.GetSkillValue(BurnDuration, level, maxLevel)
        };
    }
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
    public bool Enabled = true;
    public bool IsPassive;
    public KeyCode ActivationKey = KeyCode.None;
    public SkillExecutionType ExecutionType = SkillExecutionType.Instant;
    public int SustainPriority;

    public SkillPresentationConfig()
    {
    }

    public SkillPresentationConfig(string displayName, string triggerLabel, bool isPassive, KeyCode activationKey = KeyCode.None)
    {
        DisplayName = displayName;
        TriggerLabel = triggerLabel;
        Enabled = true;
        IsPassive = isPassive;
        ActivationKey = activationKey;
        ExecutionType = isPassive ? SkillExecutionType.Passive : SkillExecutionType.Instant;
        SustainPriority = 0;
    }

    public SkillExecutionType GetExecutionType()
    {
        return IsPassive ? SkillExecutionType.Passive : ExecutionType;
    }

    public void NormalizeExecutionType()
    {
        if (IsPassive && ExecutionType != SkillExecutionType.Passive)
        {
            ExecutionType = SkillExecutionType.Passive;
        }

        IsPassive = ExecutionType == SkillExecutionType.Passive;
        if (ExecutionType != SkillExecutionType.Sustained)
        {
            SustainPriority = 0;
        }
    }

    public PlayerSkillDefinition ToDefinition(PlayerSkillType type)
    {
        NormalizeExecutionType();
        SkillExecutionType executionType = GetExecutionType();
        string displayName = string.IsNullOrWhiteSpace(DisplayName) ? type.ToString() : DisplayName;
        string triggerLabel = string.IsNullOrWhiteSpace(TriggerLabel)
            ? (ActivationKey == KeyCode.None ? (executionType == SkillExecutionType.Passive ? "PASSIVE" : "UNBOUND") : ActivationKey.ToString().ToUpperInvariant())
            : TriggerLabel;
        return new PlayerSkillDefinition(type, displayName, triggerLabel, executionType == SkillExecutionType.Passive);
    }
}

[Serializable]
public class AutoShootSkillConfig : LevelScaledSkillConfig
{
    public SkillPresentationConfig Presentation = new SkillPresentationConfig("Auto Shoot", "AUTO SHOOT", true);
    public SkillHitEffectConfig Effects;
    public float3 MaxBullets = new float3(128f, 128f, 0f);
    public float3 FireInterval = new float3(0.06f, 0.06f, 0f);
    public float3 BulletSpeed = new float3(42f, 42f, 0f);
    public float3 BulletRadius = new float3(0.2f, 0.2f, 0f);
    public float3 BulletDamage = new float3(14f, 14f, 0f);
    public float3 BulletLifetime = new float3(1.5f, 1.5f, 0f);
    public float3 BulletsPerShot = new float3(1f, 1f, 0f);
    public float3 SpreadAngle = new float3(4f, 4f, 0f);

    public PlayerSkillDefinition ToDefinition()
    {
        return Presentation.ToDefinition(PlayerSkillType.AutoShoot);
    }
}

[Serializable]
public class PlayerContactSkillConfig : LevelScaledSkillConfig
{
    public SkillPresentationConfig Presentation = new SkillPresentationConfig("Player Contact", "PASSIVE", true);
    public SkillHitEffectConfig Effects;
    public float3 PlayerDamage = new float3(8f, 8f, 0f);
    public float3 InvincibilityDuration = new float3(0.33f, 0.33f, 0f);
    public float3 ContactPadding = new float3(0.22f, 0.22f, 0f);
    public float3 RepulseRadius = new float3(8f, 8f, 0f);
    public float3 RepulseForce = new float3(220f, 220f, 0f);
    public float3 RepulseLift = new float3(18f, 18f, 0f);
    public float3 RepulseDamage = new float3(0f, 0f, 0f);
    public float3 RingDuration = new float3(0.22f, 0.22f, 0f);
    public bool DefeatEnemyOnContact = true;
}

[Serializable]
public class LeapSmashSkillConfig : LevelScaledSkillConfig
{
    public SkillPresentationConfig Presentation = new SkillPresentationConfig("Leap Smash", "SPACE", false, KeyCode.Space);
    public SkillHitEffectConfig Effects;
    public float3 Cooldown = new float3(8f, 8f, 0f);
    public float3 AirTime = new float3(0.5f, 0.5f, 0f);
    public float3 MaxDistance = new float3(20f, 20f, 0f);
    public float3 ArcHeight = new float3(8f, 8f, 0f);
    public float3 LandingRadius = new float3(18f, 18f, 0f);
    public float3 LandingDamage = new float3(1500f, 1500f, 0f);    public float3 LandingInvincibility = new float3(0.5f, 0.5f, 0f);

    public PlayerSkillDefinition ToDefinition()
    {
        return Presentation.ToDefinition(PlayerSkillType.LeapSmash);
    }
}

[Serializable]
public class LightPillarSkillConfig : LevelScaledSkillConfig
{
    public SkillPresentationConfig Presentation = new SkillPresentationConfig("Light Pillar Strike", "Q", false, KeyCode.Q);
    public SkillHitEffectConfig Effects;
    public float3 Cooldown = new float3(10f, 10f, 0f);
    public float3 StrikeCount = new float3(4f, 16f, 1f);
    public float3 StartDistance = new float3(6f, 6f, 0f);
    public float3 DistanceStep = new float3(14f, 14f, 0f);
    public float3 Radius = new float3(10f, 25f, 1f);
    public float3 Damage = new float3(400f, 2800f, 2f);    public float3 StrikeInterval = new float3(0.15f, 0.15f, 0f);
    public float3 VisualDuration = new float3(0.5f, 0.5f, 0f);
    public float3 RingDuration = new float3(0.4f, 0.4f, 0f);

    public PlayerSkillDefinition ToDefinition()
    {
        return Presentation.ToDefinition(PlayerSkillType.LightPillarStrike);
    }
}

[Serializable]
public class BombThrowSkillConfig : LevelScaledSkillConfig
{
    public SkillPresentationConfig Presentation = new SkillPresentationConfig("Bomb Throw", "E", false, KeyCode.E);
    public SkillHitEffectConfig Effects;
    public float3 Cooldown = new float3(3f, 3f, 0f);
    public float3 SpawnHeight = new float3(2f, 2f, 0f);
    public float3 MaxThrowDistance = new float3(30f, 30f, 0f);
    public float3 FlightTime = new float3(0.8f, 0.8f, 0f);
    public float3 BaseRadius = new float3(15f, 22f, 1f);
    public float3 FragmentRadius = new float3(10f, 14f, 1f);
    public float3 MinRadius = new float3(4f, 4f, 0f);
    public float3 RadiusLossPerBounce = new float3(2.5f, 1.25f, 3f);
    public float3 BaseDamage = new float3(350f, 1800f, 2f);
    public float3 DamageFalloff = new float3(0.65f, 0.85f, 2f);
    public float3 MinDamage = new float3(60f, 240f, 2f);    public float3 MaxBounceCount = new float3(4f, 6f, 3f);
    public float3 StopHorizontalVelocitySq = new float3(4f, 4f, 0f);
    public float3 BounceHorizontalRetention = new float3(0.75f, 0.82f, 3f);
    public float3 BounceVerticalRetention = new float3(0.85f, 0.92f, 3f);
    public float3 BounceUpVelocity = new float3(12f, 15f, 2f);
    public float3 BounceUpVelocityLossPerBounce = new float3(2f, 1f, 3f);
    public float3 FragmentUnlockLevel = new float3(5f, 5f, 0f);
    public float3 FragmentCount = new float3(2f, 10f, 1f);
    public float3 FragmentHorizontalSpeed = new float3(12f, 12f, 0f);
    public float3 FragmentVerticalSpeed = new float3(14f, 14f, 0f);
    public float3 RingDuration = new float3(0.4f, 0.4f, 0f);

    public PlayerSkillDefinition ToDefinition()
    {
        return Presentation.ToDefinition(PlayerSkillType.BombThrow);
    }
}

[Serializable]
public class LaserBeamSkillConfig : LevelScaledSkillConfig
{
    public SkillPresentationConfig Presentation = new SkillPresentationConfig("Laser Beam", "R", false, KeyCode.R);
    public SkillHitEffectConfig Effects;
    public float3 Cooldown = new float3(6f, 6f, 0f);
    public float3 Duration = new float3(0.5f, 3.5f, 2f);
    public float3 MaxLength = new float3(150f, 150f, 0f);
    public float3 MaxWidth = new float3(14f, 18f, 2f);
    public float3 Damage = new float3(400f, 2000f, 2f);    public float3 BeamCount = new float3(1f, 7f, 2f);
    public float3 ScatterAngle = new float3(20f, 26f, 2f);
    public float3 SubBeamRadiusMultiplier = new float3(0.5f, 0.7f, 3f);
    public float3 SubBeamDamageMultiplier = new float3(0.5f, 0.8f, 2f);

    public PlayerSkillDefinition ToDefinition()
    {
        return Presentation.ToDefinition(PlayerSkillType.LaserBeam);
    }
}

[Serializable]
public class MeleeSlashSkillConfig : LevelScaledSkillConfig
{
    public SkillPresentationConfig Presentation = new SkillPresentationConfig("Melee Slash", "MOUSE L-CLICK", false);
    public SkillHitEffectConfig Effects;
    public float3 SlashCooldown = new float3(0.22f, 0.22f, 0f);
    public float3 FinisherCooldown = new float3(1.5f, 1.5f, 0f);
    public float3 ComboWindow = new float3(1.5f, 1.5f, 0f);
    public float3 SlashDuration = new float3(0.22f, 0.22f, 0f);
    public float3 SlashRadius = new float3(8f, 12f, 2f);
    public float3 SlashDepth = new float3(3f, 4.5f, 2f);
    public float3 ThrustRadiusMin = new float3(3f, 4f, 2f);
    public float3 ThrustRadiusMax = new float3(10f, 14f, 2f);
    public float3 ThrustWidth = new float3(3f, 4f, 2f);
    public float3 ThrustLength = new float3(8f, 12f, 2f);
    public float3 DefaultLunge = new float3(3f, 3f, 0f);
    public float3 ThrustLunge = new float3(4f, 5f, 2f);
    public float3 SpinLunge = new float3(1.5f, 2.2f, 2f);
    public float3 ThrustAdvanceSpeed = new float3(20f, 24f, 2f);
    public float3 SlashDamage = new float3(800f, 2200f, 2f);
    public float3 SpinDamage = new float3(1200f, 3200f, 2f);
    public float3 PullForce = new float3(120f, 160f, 2f);
    public float3 SlashVerticalForce = new float3(80f, 110f, 2f);
    public float3 ThrustVerticalForce = new float3(90f, 125f, 2f);
    public float3 FinisherSlamDuration = new float3(0.12f, 0.12f, 0f);
    public float3 FinisherSlamWidth = new float3(5f, 6.5f, 2f);
    public float3 FinisherSlamLength = new float3(12f, 15f, 2f);
    public float3 FinisherSlamThickness = new float3(0.8f, 1.1f, 2f);
    public float3 FinisherSlamForwardOffset = new float3(7f, 8.5f, 2f);
    public float3 FinisherArcHeight = new float3(13f, 16f, 2f);
    public float3 FinisherArcRadius = new float3(10f, 12f, 2f);
    public float3 FinisherArcStartAngle = new float3(120f, 120f, 0f);
    public float3 FinisherArcEndAngle = new float3(-12f, -12f, 0f);
    public float3 FinisherArcTilt = new float3(0f, 0f, 0f);
    public float3 SpikeDuration = new float3(0.28f, 0.28f, 0f);
    public float3 SpikeRiseRatio = new float3(0.25f, 0.25f, 0f);
    public float3 CenterSpikeHeight = new float3(14f, 18f, 2f);
    public float3 SideSpikeHeight = new float3(10f, 14f, 2f);
    public float3 CenterSpikeRadius = new float3(7f, 9f, 2f);
    public float3 SideSpikeRadius = new float3(5f, 7f, 2f);
    public float3 CenterSpikeDistance = new float3(14f, 17f, 2f);
    public float3 SideSpikeDistance = new float3(12f, 15f, 2f);
    public float3 SideSpikeAngle = new float3(28f, 35f, 2f);
    public float3 CenterSpikeVerticalForce = new float3(90f, 120f, 2f);
    public float3 SideSpikeVerticalForce = new float3(65f, 95f, 2f);
    public float3 SpikePullForce = new float3(40f, 60f, 2f);

    public PlayerSkillDefinition ToDefinition()
    {
        return Presentation.ToDefinition(PlayerSkillType.MeleeSlash);
    }
}

[Serializable]
public class ShockwaveSkillConfig : LevelScaledSkillConfig
{
    public SkillPresentationConfig Presentation = new SkillPresentationConfig("Shockwave", "V", false, KeyCode.V);
    public SkillHitEffectConfig Effects;
    public float3 Cooldown = new float3(30f, 30f, 0f);
    public float3 Duration = new float3(0.6f, 0.6f, 0f);
    public float3 LaunchDuration = new float3(0.18f, 0.18f, 0f);
    public float3 SlamDuration = new float3(0.12f, 0.12f, 0f);
    public float3 JumpHeight = new float3(12f, 16f, 2f);
    public float3 RingLifetime = new float3(0.7f, 0.9f, 2f);
    public float3 RingDelay = new float3(0.06f, 0.04f, 3f);
    public float3 RingStartRadius = new float3(8f, 10f, 2f);
    public float3 RingEndRadius = new float3(48f, 60f, 2f);
    public float3 ImpactRadius = new float3(38f, 48f, 2f);
    public float3 ImpactRingCount = new float3(5f, 8f, 2f);
    public float3 RingThickness = new float3(7f, 10f, 2f);
    public float3 RingRadiusOffset = new float3(0f, 0f, 0f);
    public float3 RingLiftHeight = new float3(4f, 6f, 2f);
    public float3 RingLiftFalloff = new float3(0.5f, 0.35f, 3f);
    public float3 ImpactDamage = new float3(2400f, 5200f, 2f);    public float3 CameraLift = new float3(1.35f, 1.35f, 0f);
    public float3 CameraFovKick = new float3(8f, 8f, 0f);
    public float3 LandingShake = new float3(0.26f, 0.26f, 0f);

    public PlayerSkillDefinition ToDefinition()
    {
        return Presentation.ToDefinition(PlayerSkillType.Shockwave);
    }
}

[Serializable]
public class MeteorRainSkillConfig : LevelScaledSkillConfig
{
    public SkillPresentationConfig Presentation = new SkillPresentationConfig("Meteor Rain", "T", false, KeyCode.T);
    public SkillHitEffectConfig Effects;
    public float3 Cooldown = new float3(8f, 8f, 0f);
    public float3 Duration = new float3(2f, 2.8f, 2f);
    public float3 WaveCount = new float3(8f, 14f, 2f);
    public float3 WaveInterval = new float3(0.2f, 0.14f, 3f);
    public float3 ScatterRadius = new float3(20f, 26f, 2f);
    public float3 VisualDuration = new float3(0.5f, 0.5f, 0f);
    public float3 StartOffsetX = new float3(10f, 10f, 0f);
    public float3 StartOffsetZ = new float3(5f, 5f, 0f);
    public float3 FallHeight = new float3(60f, 60f, 0f);
    public float3 StartScale = new float3(6f, 7f, 2f);
    public float3 EndScale = new float3(3f, 4f, 2f);
    public float3 ImpactRadius = new float3(15f, 22f, 2f);
    public float3 ImpactDamage = new float3(900f, 2600f, 2f);    public float3 RingDuration = new float3(0.45f, 0.45f, 0f);

    public PlayerSkillDefinition ToDefinition()
    {
        return Presentation.ToDefinition(PlayerSkillType.MeteorRain);
    }
}

[Serializable]
public class IceZoneSkillConfig : LevelScaledSkillConfig
{
    public SkillPresentationConfig Presentation = new SkillPresentationConfig("Ice Zone", "C", false, KeyCode.C);
    public SkillHitEffectConfig Effects;
    public float3 Cooldown = new float3(10f, 10f, 0f);
    public float3 Duration = new float3(4f, 6f, 2f);
    public float3 Radius = new float3(8f, 25f, 2f);
    public float3 TickDamage = new float3(80f, 240f, 2f);    public float3 BurstRadiusBonus = new float3(5f, 8f, 2f);
    public float3 BurstDamage = new float3(800f, 2600f, 2f);    public float3 RingDuration = new float3(0.6f, 0.6f, 0f);
    public float3 PulseBaseAlpha = new float3(0.3f, 0.3f, 0f);
    public float3 PulseAmplitude = new float3(0.1f, 0.1f, 0f);
    public float3 PulseSpeed = new float3(6f, 6f, 0f);

    public PlayerSkillDefinition ToDefinition()
    {
        return Presentation.ToDefinition(PlayerSkillType.IceZone);
    }
}

[Serializable]
public class PoisonBottleSkillConfig : LevelScaledSkillConfig
{
    public SkillPresentationConfig Presentation = new SkillPresentationConfig("Poison Bottle", "X", false, KeyCode.X);
    public SkillHitEffectConfig Effects = new SkillHitEffectConfig
    {
        Tags = SkillHitEffectTag.Poison,
        PoisonSpreadRadius = new float3(4f, 6f, 2f)
    };
    public float3 Cooldown = new float3(5f, 5f, 0f);
    public float3 SpawnHeight = new float3(2f, 2f, 0f);
    public float3 MaxThrowDistance = new float3(28f, 28f, 0f);
    public float3 FlightTime = new float3(0.65f, 0.65f, 0f);
    public float3 BottleVisualScale = new float3(1.6f, 2f, 2f);
    public float3 ZoneDuration = new float3(5f, 7f, 2f);
    public float3 ZoneRadius = new float3(10f, 16f, 2f);
    public float3 ZoneCoreRatio = new float3(0.65f, 0.8f, 2f);
    public float3 ZoneIrregularity = new float3(0.18f, 0.18f, 0f);
    public float3 ZoneNoiseScale = new float3(0.18f, 0.18f, 0f);
    public float3 PoisonDuration = new float3(2f, 4f, 2f);
    public float3 StackDpsPerSecond = new float3(26f, 70f, 2f);
    public float3 MaxPoisonDps = new float3(120f, 320f, 2f);
    public float3 InitialSpreadCount = new float3(2f, 5f, 2f);
    public float3 SpreadRadius = new float3(4f, 8f, 2f);
    public float3 SpreadStackDps = new float3(36f, 90f, 2f);
    public float3 SpreadRingDuration = new float3(0.3f, 0.3f, 0f);
    public float3 ZonePulseSpeed = new float3(3.5f, 3.5f, 0f);
    public float3 ZonePulseAmplitude = new float3(0.12f, 0.12f, 0f);

    public PlayerSkillDefinition ToDefinition()
    {
        return Presentation.ToDefinition(PlayerSkillType.PoisonBottle);
    }
}

[Serializable]
public class DashSkillConfig : LevelScaledSkillConfig
{
    public SkillPresentationConfig Presentation = new SkillPresentationConfig("Whirlwind", "L-SHIFT", false, KeyCode.LeftShift);
    public SkillHitEffectConfig Effects;
    public float3 Cooldown = new float3(3f, 3f, 0f);
    public float3 Duration = new float3(1.5f, 1.8f, 2f);
    public float3 Distance = new float3(21f, 28f, 2f);
    public float3 InvincibilityDuration = new float3(1.5f, 1.8f, 2f);
    public float3 SpinDamage = new float3(9f, 22f, 2f);
    public float3 HitRadius = new float3(8f, 12f, 2f);
    public float3 BladeWidth = new float3(4f, 5f, 2f);
    public float3 BladeLength = new float3(11f, 14f, 2f);
    public float3 BladeThickness = new float3(0.75f, 1f, 2f);
    public float3 MaxSpinRate = new float3(3000f, 3600f, 2f);
    public float3 ImpactRadius = new float3(10f, 16f, 2f);
    public float3 ImpactDamage = new float3(260f, 900f, 2f);    public float3 RingDuration = new float3(0.5f, 0.5f, 0f);

    public PlayerSkillDefinition ToDefinition()
    {
        return Presentation.ToDefinition(PlayerSkillType.Dash);
    }
}

[Serializable]
public class OrbitBallSkillConfig : LevelScaledSkillConfig
{
    public SkillPresentationConfig Presentation = new SkillPresentationConfig("Orbit Ball", "PASSIVE", true);
    public SkillHitEffectConfig Effects;
    public float3 MaxBalls = new float3(8f, 12f, 2f);
    public float3 OrbitSpeed = new float3(2.5f, 3.2f, 2f);
    public float3 FirstRingRadius = new float3(6f, 7f, 2f);
    public float3 SecondRingRadius = new float3(10f, 12f, 2f);
    public float3 FirstRingSpeedMultiplier = new float3(1f, 1f, 0f);
    public float3 SecondRingSpeedMultiplier = new float3(0.75f, 0.9f, 2f);
    public float3 VisualScale = new float3(3f, 3.8f, 2f);
    public float3 DamageRadius = new float3(2f, 3f, 2f);
    public float3 Damage = new float3(45f, 180f, 2f);
    public PlayerSkillDefinition ToDefinition()
    {
        return Presentation.ToDefinition(PlayerSkillType.OrbitBall);
    }
}

[Serializable]
public class PlayerSkillConfigSet
{
    public AutoShootSkillConfig AutoShoot = new AutoShootSkillConfig();
    public PlayerContactSkillConfig PlayerContact = new PlayerContactSkillConfig();
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
        if (PlayerContact == null) PlayerContact = new PlayerContactSkillConfig();
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

        AutoShoot.Presentation.NormalizeExecutionType();
    PlayerContact.Presentation.NormalizeExecutionType();
        LeapSmash.Presentation.NormalizeExecutionType();
        LightPillar.Presentation.NormalizeExecutionType();
        BombThrow.Presentation.NormalizeExecutionType();
        LaserBeam.Presentation.NormalizeExecutionType();
        MeleeSlash.Presentation.NormalizeExecutionType();
        Shockwave.Presentation.NormalizeExecutionType();
        MeteorRain.Presentation.NormalizeExecutionType();
        IceZone.Presentation.NormalizeExecutionType();
        PoisonBottle.Presentation.NormalizeExecutionType();
        Dash.Presentation.NormalizeExecutionType();
        OrbitBall.Presentation.NormalizeExecutionType();
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


