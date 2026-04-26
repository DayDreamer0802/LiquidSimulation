using System.Text;
using Unity.Mathematics;
using UnityEngine;

public partial class RougeGameManager
{
    private const float HudRefreshInterval = 0.1f;

    private readonly StringBuilder _hudBuilder = new StringBuilder(1024);
    private float _hudRefreshTimer;
    private string _lastHudText = string.Empty;

    private void ResetHudRefreshState()
    {
        _hudRefreshTimer = 0f;
        _lastHudText = string.Empty;
    }

    private void UpdateHudIfNeeded()
    {
        if (_uiText == null)
        {
            return;
        }

        _hudRefreshTimer -= Time.unscaledDeltaTime;
        if (_hudRefreshTimer > 0f)
        {
            return;
        }

        _hudRefreshTimer = HudRefreshInterval;

        int mm = Mathf.FloorToInt(_survivalTime / 60f);
        int ss = Mathf.FloorToInt(_survivalTime % 60f);
        _hudBuilder.Clear();
        _hudBuilder.AppendLine($"FPS: {Mathf.RoundToInt(_fps)}  |  SURVIVAL: {mm:D2}:{ss:D2}");
        _hudBuilder.AppendLine($"LEVEL: {currentLevel} | KILLS: {totalKills}");
        _hudBuilder.AppendLine($"ACTIVE ENEMIES: {_currentMaxEnemies} / {enemyCount}");
        _hudBuilder.AppendLine($"PLAYER HP: {Mathf.RoundToInt(playerHealth)} / {playerMaxHealth}");
        _hudBuilder.AppendLine($"VIEW ZOOM: {cameraZoomMultiplier:F2}x");
        _hudBuilder.AppendLine();

        AppendSkillProgressHud(_hudBuilder);
        _hudBuilder.AppendLine();
        AppendSkillHud(_hudBuilder);

        string hudText = _hudBuilder.ToString();
        if (hudText == _lastHudText)
        {
            return;
        }

        _lastHudText = hudText;
        _uiText.text = hudText;
    }

    private void AppendSkillProgressHud(StringBuilder sb)
    {
        sb.AppendLine("SKILL PROGRESS:");
        EnsureSkillConfigInitialized();

        for (int i = 0; i < PlayerSkillCatalog.ProgressionBindings.Length; i++)
        {
            PlayerSkillProgressBinding binding = PlayerSkillCatalog.ProgressionBindings[i];
            int progressionIndex = binding.ProgressionIndex;
            PlayerSkillDefinition definition = skillConfig.GetDefinition(binding.Type);
            string displayName = string.IsNullOrWhiteSpace(binding.DisplayNameOverride) ? definition.DisplayName : binding.DisplayNameOverride;
            string disabledSuffix = IsSkillEnabled(binding.Type) ? string.Empty : " [OFF]";
            string lockSuffix = IsSkillProgressionLocked(progressionIndex) ? " [NO-UP]" : string.Empty;
            sb.AppendLine($"{binding.ShortLabel}: {displayName} Lv{_skillLevels[progressionIndex]} ({GetSkillProgressSummary(progressionIndex)}){lockSuffix}{disabledSuffix}");
        }
    }

    private void AppendSkillHud(StringBuilder sb)
    {
        sb.AppendLine("SKILLS:");

        EnsureSkillConfigInitialized();

        foreach (PlayerSkillType skillType in PlayerSkillCatalog.DisplayOrder)
        {
            PlayerSkillDefinition skill = skillConfig.GetDefinition(skillType);
            string triggerLabel = GetSkillTriggerLabel(skillType, skill);
            if (!IsSkillEnabled(skillType))
            {
                sb.AppendLine($"{triggerLabel}: {skill.DisplayName} (Disabled)");
                continue;
            }

            if (skill.Type == PlayerSkillType.OrbitBall)
            {
                int numOrbBalls = math.max(0, skillConfig.OrbitBall.GetIntValue(skillConfig.OrbitBall.MaxBalls, _skillLevels[4]));
                int maxOrbBalls = math.max(0, skillConfig.OrbitBall.GetIntValue(skillConfig.OrbitBall.MaxBalls, skillConfig.OrbitBall.MaxLevel));
                sb.AppendLine($"{triggerLabel}: {skill.DisplayName} x{numOrbBalls}/{maxOrbBalls} (Passive)");
                continue;
            }

            if (skill.IsPassive)
            {
                sb.AppendLine($"{triggerLabel}: {skill.DisplayName} (Passive)");
                continue;
            }

            string status = GetSkillStatusText(skill.Type);
            sb.AppendLine($"{triggerLabel}: {skill.DisplayName} ({status})");
        }
    }

    private string GetSkillStatusText(PlayerSkillType type)
    {
        float cooldown = Mathf.Max(0f, GetSkillCooldown(type));
        switch (type)
        {
            case PlayerSkillType.LeapSmash:
                if (_jumpState == 1)
                {
                    return FormatActiveSkillStatus("AIRBORNE", cooldown);
                }

                break;
            case PlayerSkillType.LightPillarStrike:
                if (_pillarStrikesDone < _pillarStrikesTotal)
                {
                    return FormatActiveSkillStatus($"CASTING {_pillarStrikesTotal - _pillarStrikesDone}X", cooldown);
                }

                break;
            case PlayerSkillType.BombThrow:
                if (HasActiveBomb())
                {
                    return FormatActiveSkillStatus("DEPLOYED", cooldown);
                }

                break;
            case PlayerSkillType.LaserBeam:
                if (_laserTimer > 0f)
                {
                    return FormatActiveSkillStatus($"CHANNEL {Mathf.Max(0f, _laserTimer):F1}s", cooldown);
                }

                break;
            case PlayerSkillType.MeleeSlash:
                if (_meleeTimer > 0f || _meleeFinisherSlamTimer > 0f || _spikeStartupTimer > 0f || _spikeTimer > 0f)
                {
                    return FormatActiveSkillStatus("ATTACKING", cooldown);
                }

                break;
            case PlayerSkillType.Shockwave:
                if (_shockwaveState == 1)
                {
                    return FormatActiveSkillStatus("ASCENDING", cooldown);
                }

                if (_shockwaveState == 2)
                {
                    return FormatActiveSkillStatus("SLAMMING", cooldown);
                }

                break;
            case PlayerSkillType.MeteorRain:
                if (_meteorTimer > 0f)
                {
                    return FormatActiveSkillStatus($"RAINING {Mathf.Max(0f, _meteorTimer):F1}s", cooldown);
                }

                break;
            case PlayerSkillType.IceZone:
                if (_iceZoneTimer > 0f)
                {
                    return FormatActiveSkillStatus($"ACTIVE {Mathf.Max(0f, _iceZoneTimer):F1}s", cooldown);
                }

                break;
            case PlayerSkillType.PoisonBottle:
                if (HasActivePoisonState())
                {
                    return FormatActiveSkillStatus("ACTIVE", cooldown);
                }

                break;
            case PlayerSkillType.Dash:
                if (_dashSpinTimer > 0f)
                {
                    return FormatActiveSkillStatus($"SPINNING {Mathf.Max(0f, _dashSpinTimer):F1}s", cooldown);
                }

                break;
            case PlayerSkillType.Skateboard:
                switch (_skatePhase)
                {
                    case 1: return FormatActiveSkillStatus("JUMP", cooldown);
                    case 2: return FormatActiveSkillStatus("LANDING", cooldown);
                    case 3: return FormatActiveSkillStatus($"RIDING {Mathf.Max(0f, _skateRideTimer):F1}s", cooldown);
                    case 4: return FormatActiveSkillStatus("TRICK", cooldown);
                    case 5: return FormatActiveSkillStatus("SLAM", cooldown);
                }

                break;
        }

        return cooldown <= 0.05f ? "READY" : $"CD: {cooldown:F1}s";
    }

    private static string FormatActiveSkillStatus(string activeStatus, float cooldown)
    {
        return cooldown <= 0.05f ? activeStatus : $"{activeStatus} | CD {cooldown:F1}s";
    }

    private bool HasActivePoisonState()
    {
        for (int i = 0; i < MaxPoisonBottles; i++)
        {
            if (_activePoisonBottles[i].Active)
            {
                return true;
            }
        }

        for (int i = 0; i < MaxPoisonZones; i++)
        {
            if (_activePoisonZones[i].Active)
            {
                return true;
            }
        }

        return false;
    }

    private string GetSkillTriggerLabel(PlayerSkillType type, PlayerSkillDefinition definition)
    {
        if (RougeInputManager.TryGetBinding(type, out RougeInputBinding binding))
        {
            return RougeInputManager.Instance.GetBindingDisplayString(binding);
        }

        if (type == PlayerSkillType.AutoShoot)
        {
            return definition.TriggerLabel;
        }

        SkillPresentationConfig presentation = GetPresentationConfig(type);
        if (presentation == null)
        {
            return definition.TriggerLabel;
        }

        if (presentation.ActivationKey == KeyCode.None)
        {
            return presentation.TriggerLabel;
        }

        return FormatKeyLabel(presentation.ActivationKey);
    }

    private SkillPresentationConfig GetPresentationConfig(PlayerSkillType type)
    {
        switch (type)
        {
            case PlayerSkillType.AutoShoot:
                return skillConfig.AutoShoot.Presentation;
            case PlayerSkillType.LeapSmash:
                return skillConfig.LeapSmash.Presentation;
            case PlayerSkillType.LightPillarStrike:
                return skillConfig.LightPillar.Presentation;
            case PlayerSkillType.BombThrow:
                return skillConfig.BombThrow.Presentation;
            case PlayerSkillType.LaserBeam:
                return skillConfig.LaserBeam.Presentation;
            case PlayerSkillType.MeleeSlash:
                return skillConfig.MeleeSlash.Presentation;
            case PlayerSkillType.Shockwave:
                return skillConfig.Shockwave.Presentation;
            case PlayerSkillType.MeteorRain:
                return skillConfig.MeteorRain.Presentation;
            case PlayerSkillType.IceZone:
                return skillConfig.IceZone.Presentation;
            case PlayerSkillType.PoisonBottle:
                return skillConfig.PoisonBottle.Presentation;
            case PlayerSkillType.Dash:
                return skillConfig.Dash.Presentation;
            case PlayerSkillType.Skateboard:
                return skillConfig.Skateboard.Presentation;
            case PlayerSkillType.OrbitBall:
                return skillConfig.OrbitBall.Presentation;
            default:
                return null;
        }
    }

    private static string FormatKeyLabel(KeyCode key)
    {
        switch (key)
        {
            case KeyCode.LeftShift:
                return "L-SHIFT";
            case KeyCode.RightShift:
                return "R-SHIFT";
            case KeyCode.Space:
                return "SPACE";
            case KeyCode.Mouse0:
                return "MOUSE L-CLICK";
            case KeyCode.Mouse1:
                return "MOUSE R-CLICK";
            default:
                return key.ToString().ToUpperInvariant();
        }
    }

    private float GetSkillCooldown(PlayerSkillType type)
    {
        switch (type)
        {
            case PlayerSkillType.LeapSmash:
                return _jumpCooldownTimer;
            case PlayerSkillType.LightPillarStrike:
                return _tornadoCooldownTimer;
            case PlayerSkillType.BombThrow:
                return _bombCooldownTimer;
            case PlayerSkillType.LaserBeam:
                return _laserCooldownTimer;
            case PlayerSkillType.MeleeSlash:
                return _meleeCooldownTimer;
            case PlayerSkillType.Shockwave:
                return _shockwaveCooldownTimer;
            case PlayerSkillType.MeteorRain:
                return _meteorCooldownTimer;
            case PlayerSkillType.IceZone:
                return _iceZoneCooldownTimer;
            case PlayerSkillType.PoisonBottle:
                return _poisonCooldownTimer;
            case PlayerSkillType.Dash:
                return _dashCooldownTimer;
            case PlayerSkillType.Skateboard:
                return _skateCooldownTimer;
            default:
                return 0f;
        }
    }
}