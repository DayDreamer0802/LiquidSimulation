using System.Text;
using Unity.Mathematics;
using UnityEngine;

public partial class RougeGameManager
{
    private void UpdateHud()
    {
        if (_uiText == null)
        {
            return;
        }

        int mm = Mathf.FloorToInt(_survivalTime / 60f);
        int ss = Mathf.FloorToInt(_survivalTime % 60f);
        var sb = new StringBuilder(1024);
        sb.AppendLine($"FPS: {Mathf.RoundToInt(_fps)}  |  SURVIVAL: {mm:D2}:{ss:D2}");
        sb.AppendLine($"LEVEL: {currentLevel} | KILLS: {totalKills}");
        sb.AppendLine($"ACTIVE ENEMIES: {_currentMaxEnemies} / {enemyCount}");
        sb.AppendLine($"PLAYER HP: {Mathf.RoundToInt(playerHealth)} / {playerMaxHealth}");
        sb.AppendLine();

        AppendSkillProgressHud(sb);
        sb.AppendLine();
        AppendSkillHud(sb);
        _uiText.text = sb.ToString();
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
            string disabledSuffix = IsSkillEnabled(binding.Type) ? string.Empty : " [OFF]";
            sb.AppendLine($"{binding.ShortLabel}: {definition.DisplayName} Lv{_skillLevels[progressionIndex]} ({_skillTotalKills[progressionIndex]}){disabledSuffix}");
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
                    return "AIRBORNE";
                }

                break;
            case PlayerSkillType.LightPillarStrike:
                if (_pillarStrikesDone < _pillarStrikesTotal)
                {
                    return $"CASTING {_pillarStrikesTotal - _pillarStrikesDone}X";
                }

                break;
            case PlayerSkillType.BombThrow:
                if (HasActiveBomb())
                {
                    return "DEPLOYED";
                }

                break;
            case PlayerSkillType.LaserBeam:
                if (_laserTimer > 0f)
                {
                    return $"CHANNEL {Mathf.Max(0f, _laserTimer):F1}s";
                }

                break;
            case PlayerSkillType.MeleeSlash:
                if (_meleeTimer > 0f || _meleeFinisherSlamTimer > 0f || _spikeStartupTimer > 0f || _spikeTimer > 0f)
                {
                    return "ATTACKING";
                }

                break;
            case PlayerSkillType.Shockwave:
                if (_shockwaveState == 1)
                {
                    return "ASCENDING";
                }

                if (_shockwaveState == 2)
                {
                    return "SLAMMING";
                }

                break;
            case PlayerSkillType.MeteorRain:
                if (_meteorTimer > 0f)
                {
                    return $"RAINING {Mathf.Max(0f, _meteorTimer):F1}s";
                }

                break;
            case PlayerSkillType.IceZone:
                if (_iceZoneTimer > 0f)
                {
                    return $"ACTIVE {Mathf.Max(0f, _iceZoneTimer):F1}s";
                }

                break;
            case PlayerSkillType.PoisonBottle:
                if (HasActivePoisonState())
                {
                    return "ACTIVE";
                }

                break;
            case PlayerSkillType.Dash:
                if (_dashSpinTimer > 0f)
                {
                    return $"SPINNING {Mathf.Max(0f, _dashSpinTimer):F1}s";
                }

                break;
        }

        return cooldown <= 0.05f ? "READY" : $"CD: {cooldown:F1}s";
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
            default:
                return 0f;
        }
    }
}