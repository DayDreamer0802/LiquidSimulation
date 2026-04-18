using Unity.Mathematics;
using UnityEngine;

public partial class RougeGameManager
{
    private void UpdateSkills(float dt)
    {
        EnsureSkillConfigInitialized();
        TickSkillCooldowns(dt);
        RefreshActiveSustainedSkill();

        SkillUpdateContext context = CreateSkillContext(dt);
        _skillAreaCount = 0;

        UpdateLeapSmashSkill(context);
        UpdateLightPillarSkill(context);
        UpdateBombSkill(context);
        UpdateLaserSkill(context);
        UpdateMeleeSkill(context);
        UpdateOrbitSkill(context);
        UpdateShockwaveSkill(context);
        UpdateMeteorSkill(context);
        UpdateIceZoneSkill(context);
        UpdatePoisonBottleSkill(context);
        UpdateDashSkill(context);
        RefreshActiveSustainedSkill();
    }

    private void TickSkillCooldowns(float dt)
    {
        PlayerSkillMath.StepCooldown(ref _jumpCooldownTimer, dt);
        PlayerSkillMath.StepCooldown(ref _tornadoCooldownTimer, dt);
        PlayerSkillMath.StepCooldown(ref _bombCooldownTimer, dt);
        PlayerSkillMath.StepCooldown(ref _laserCooldownTimer, dt);
        PlayerSkillMath.StepCooldown(ref _meleeCooldownTimer, dt);
        PlayerSkillMath.StepCooldown(ref _shockwaveCooldownTimer, dt);
        PlayerSkillMath.StepCooldown(ref _meteorCooldownTimer, dt);
        PlayerSkillMath.StepCooldown(ref _iceZoneCooldownTimer, dt);
        PlayerSkillMath.StepCooldown(ref _poisonCooldownTimer, dt);
        PlayerSkillMath.StepCooldown(ref _dashCooldownTimer, dt);
    }

    private SkillUpdateContext CreateSkillContext(float dt)
    {
        float2 playerPos = player != null ? player.PlanarPosition : float2.zero;
        Vector3 aim = player != null ? player.AimDirection : Vector3.forward;
        float2 aimDir = math.normalizesafe(new float2(aim.x, aim.z), new float2(0f, 1f));
        Vector2 pointerPosition = RougeInputManager.Instance.ReadPointerPosition();
        bool hasMouseGroundPoint = PlayerSkillMath.TryGetMouseGroundPoint(Camera.main, new Vector3(pointerPosition.x, pointerPosition.y, 0f), renderHeight, out Vector3 mouseGroundPoint);

        return new SkillUpdateContext(dt, playerPos, aimDir, hasMouseGroundPoint, mouseGroundPoint, renderHeight, arenaHalfExtent);
    }

    private bool TryAddSkillArea(RougeSkillArea area)
    {
        if (_skillAreaCount >= _skillAreasDb.Length)
        {
            return false;
        }

        _skillAreasDb[_skillAreaCount++] = area;
        return true;
    }

    private bool TryAddSkillArea(RougeSkillArea area, ResolvedSkillHitEffectConfig effects)
    {
        ApplySkillEffects(ref area, effects);
        return TryAddSkillArea(area);
    }

    private bool TryAddCircularSkillArea(float2 position, float radius, float damage, float pullForce, float verticalForce, ResolvedSkillHitEffectConfig effects, int type = 2)
    {
        RougeSkillArea area = new RougeSkillArea
        {
            Type = type,
            Position = position,
            Radius = radius,
            Damage = damage,
            PullForce = pullForce,
            VerticalForce = verticalForce
        };

        ApplySkillEffects(ref area, effects);
        return TryAddSkillArea(area);
    }

    private void ApplySkillEffects(ref RougeSkillArea area, ResolvedSkillHitEffectConfig effects)
    {
        area.EffectFlags = (int)effects.Tags;
        area.EffectKnockbackForce = effects.KnockbackForce;
        area.EffectLaunchHeight = effects.LaunchHeight;
        area.EffectLaunchLandingRadius = effects.LaunchLandingRadius;
        area.EffectPoisonSpreadRadius = effects.PoisonSpreadRadius;
        area.EffectSlowPercent = effects.SlowPercent;
        area.EffectSlowDuration = effects.SlowDuration;
        area.EffectCurseExplosionDamage = effects.CurseExplosionDamage;
        area.EffectCurseExplosionRadius = effects.CurseExplosionRadius;
        area.EffectBurnDamage = effects.BurnDamage;
        area.EffectBurnDuration = effects.BurnDuration;
    }

    private void RefreshActiveSustainedSkill()
    {
        if (!_hasActiveSustainedSkill)
        {
            return;
        }

        if (GetSkillExecutionType(_activeSustainedSkillType) != SkillExecutionType.Sustained || !IsSkillCurrentlyActive(_activeSustainedSkillType))
        {
            _hasActiveSustainedSkill = false;
            _activeSustainedSkillType = default;
            _activeSustainedSkillPriority = 0;
        }
    }

    private SkillExecutionType GetSkillExecutionType(PlayerSkillType type)
    {
        SkillPresentationConfig presentation = GetPresentationConfig(type);
        return presentation != null ? presentation.GetExecutionType() : SkillExecutionType.Instant;
    }

    private bool TryStartSkillActivation(PlayerSkillType type)
    {
        SkillPresentationConfig presentation = GetPresentationConfig(type);
        if (presentation == null)
        {
            return true;
        }

        SkillExecutionType executionType = presentation.GetExecutionType();
        if (executionType != SkillExecutionType.Sustained)
        {
            return true;
        }

        RefreshActiveSustainedSkill();
        int sustainPriority = Mathf.Max(0, presentation.SustainPriority);
        if (!_hasActiveSustainedSkill)
        {
            SetActiveSustainedSkill(type, sustainPriority);
            return true;
        }

        if (_activeSustainedSkillType == type)
        {
            return false;
        }

        if (sustainPriority > _activeSustainedSkillPriority)
        {
            InterruptSkill(_activeSustainedSkillType);
            SetActiveSustainedSkill(type, sustainPriority);
            return true;
        }

        return false;
    }

    private void SetActiveSustainedSkill(PlayerSkillType type, int sustainPriority)
    {
        _hasActiveSustainedSkill = true;
        _activeSustainedSkillType = type;
        _activeSustainedSkillPriority = sustainPriority;
    }

    private bool IsSkillCurrentlyActive(PlayerSkillType type)
    {
        switch (type)
        {
            case PlayerSkillType.LeapSmash:
                return _jumpState != 0;
            case PlayerSkillType.LightPillarStrike:
                return _pillarStrikesDone < _pillarStrikesTotal;
            case PlayerSkillType.BombThrow:
                return HasActiveBomb();
            case PlayerSkillType.LaserBeam:
                return _laserTimer > 0f;
            case PlayerSkillType.MeleeSlash:
                return _meleeTimer > 0f || _meleeFinisherSlamTimer > 0f || _spikeStartupTimer > 0f || _spikeTimer > 0f;
            case PlayerSkillType.Shockwave:
                return _shockwaveState != 0;
            case PlayerSkillType.MeteorRain:
                return _meteorTimer > 0f;
            case PlayerSkillType.IceZone:
                return _iceZoneTimer > 0f;
            case PlayerSkillType.PoisonBottle:
                return HasActivePoisonState();
            case PlayerSkillType.Dash:
                return _dashSpinTimer > 0f;
            default:
                return false;
        }
    }

    private void InterruptSkill(PlayerSkillType type)
    {
        switch (type)
        {
            case PlayerSkillType.LeapSmash:
                _jumpState = 0;
                _jumpTimer = 0f;
                if (player != null)
                {
                    Vector3 position = player.transform.position;
                    position.y = renderHeight;
                    player.transform.position = position;
                }
                break;
            case PlayerSkillType.LightPillarStrike:
                _pillarStrikesDone = _pillarStrikesTotal;
                _pillarNextStrikeTimer = 0f;
                if (_tornadoVisual != null)
                {
                    _tornadoVisual.SetActive(false);
                }
                break;
            case PlayerSkillType.BombThrow:
                for (int i = 0; i < MaxBombs; i++)
                {
                    _activeBombs[i].Active = false;
                    if (_bombVisuals[i] != null)
                    {
                        _bombVisuals[i].SetActive(false);
                    }
                }
                break;
            case PlayerSkillType.LaserBeam:
                _laserTimer = 0f;
                if (_laserVisual != null)
                {
                    _laserVisual.SetActive(false);
                }
                for (int i = 0; i < MaxLaserSubBeams; i++)
                {
                    if (_laserExtraVisuals[i] != null)
                    {
                        _laserExtraVisuals[i].SetActive(false);
                    }
                }
                break;
            case PlayerSkillType.MeleeSlash:
                _meleeTimer = 0f;
                _meleeFinisherSlamTimer = 0f;
                _spikeStartupTimer = 0f;
                _spikeTimer = 0f;
                if (_meleeVisual != null)
                {
                    _meleeVisual.SetActive(false);
                }
                if (_meleeFinisherVisual != null)
                {
                    _meleeFinisherVisual.SetActive(false);
                }
                for (int i = 0; i < _spikeVisuals.Length; i++)
                {
                    if (_spikeVisuals[i] != null)
                    {
                        _spikeVisuals[i].SetActive(false);
                    }
                }
                break;
            case PlayerSkillType.Shockwave:
                _shockwaveState = 0;
                _shockwaveTimer = 0f;
                _cameraLiftOffset = 0f;
                _cameraFovOffset = 0f;
                if (_shockwaveVisual != null)
                {
                    _shockwaveVisual.SetActive(false);
                }
                if (player != null)
                {
                    Vector3 position = player.transform.position;
                    position.y = renderHeight;
                    player.transform.position = position;
                }
                break;
            case PlayerSkillType.MeteorRain:
                _meteorTimer = 0f;
                _meteorWaveTimer = 0f;
                _meteorWaveIndex = 0;
                for (int i = 0; i < MeteorVisualMax; i++)
                {
                    _meteorVisualTimers[i] = 0f;
                    if (_meteorVisuals[i] != null)
                    {
                        _meteorVisuals[i].SetActive(false);
                    }
                }
                break;
            case PlayerSkillType.IceZone:
                _iceZoneTimer = 0f;
                if (_iceZoneVisual != null)
                {
                    _iceZoneVisual.SetActive(false);
                }
                break;
            case PlayerSkillType.PoisonBottle:
                for (int i = 0; i < MaxPoisonBottles; i++)
                {
                    _activePoisonBottles[i].Active = false;
                    if (_poisonBottleVisuals[i] != null)
                    {
                        _poisonBottleVisuals[i].SetActive(false);
                    }
                }
                for (int i = 0; i < MaxPoisonZones; i++)
                {
                    _activePoisonZones[i].Active = false;
                    if (_poisonZoneVisuals[i] != null)
                    {
                        _poisonZoneVisuals[i].SetActive(false);
                    }
                }
                break;
            case PlayerSkillType.Dash:
                _dashSpinTimer = 0f;
                if (_dashVisual != null)
                {
                    _dashVisual.SetActive(false);
                }
                break;
        }
    }

    private bool IsMovementSkillLocked(PlayerSkillType skillType)
    {
        if (!PlayerSkillCatalog.HasTag(skillType, PlayerSkillTag.Movement))
        {
            return false;
        }

        return _dashSpinTimer > 0f || _jumpState == 1 || _shockwaveState != 0;
    }

    private bool CanStartMovementSkill(PlayerSkillType skillType)
    {
        if (!PlayerSkillCatalog.HasTag(skillType, PlayerSkillTag.Movement))
        {
            return true;
        }

        if (!IsMovementSkillLocked(skillType))
        {
            return true;
        }

        RefreshActiveSustainedSkill();
        if (!_hasActiveSustainedSkill || _activeSustainedSkillType == skillType)
        {
            return false;
        }

        if (GetSkillExecutionType(skillType) != SkillExecutionType.Sustained || GetSkillExecutionType(_activeSustainedSkillType) != SkillExecutionType.Sustained)
        {
            return false;
        }

        SkillPresentationConfig presentation = GetPresentationConfig(skillType);
        if (presentation == null)
        {
            return false;
        }

        return Mathf.Max(0, presentation.SustainPriority) > _activeSustainedSkillPriority;
    }

    private void SpawnImpact(float2 position, float explosionRadius, float ringRadius, float ringDuration, Color ringColor, float heightOffset = 1f)
    {
        Vector3 ringCenter = PlayerSkillMath.ToWorld(position, renderHeight);
        SpawnExplosionVFX(ringCenter + Vector3.up * heightOffset, explosionRadius);
        SpawnAOERing(ringCenter, ringRadius, ringDuration, ringColor);
    }

    private void UpdateLeapSmashSkill(SkillUpdateContext context)
    {
        LeapSmashSkillConfig leap = skillConfig.LeapSmash;
        int leapLevel = currentLevel;
        ResolvedSkillHitEffectConfig leapEffects = leap.Effects.Resolve(leapLevel, leap.MaxLevel);
        float leapMaxDistance = leap.GetValue(leap.MaxDistance, leapLevel);
        float leapAirTime = leap.GetValue(leap.AirTime, leapLevel);
        float leapCooldown = leap.GetValue(leap.Cooldown, leapLevel);
        float leapInvincibility = leap.GetValue(leap.LandingInvincibility, leapLevel);
        float leapLandingRadius = leap.GetValue(leap.LandingRadius, leapLevel);
        float leapLandingDamage = leap.GetValue(leap.LandingDamage, leapLevel);
        float leapLandingPullForce = leap.GetValue(leap.LandingPullForce, leapLevel);
        float leapLandingVerticalForce = leap.GetValue(leap.LandingVerticalForce, leapLevel);
        float leapArcHeight = leap.GetValue(leap.ArcHeight, leapLevel);

        if (_jumpState == 0 && CanStartMovementSkill(PlayerSkillType.LeapSmash) && RougeInputManager.Instance.WasPressedThisFrame(RougeInputBinding.LeapSmash) && _jumpCooldownTimer <= 0f && player != null && TryStartSkillActivation(PlayerSkillType.LeapSmash))
        {
            Vector3 startPos = player.transform.position;
            Vector3 targetPos = context.HasMouseGroundPoint
                ? context.MouseGroundPoint
                : PlayerSkillMath.ToWorld(context.PlayerPosition + context.AimDirection * leapMaxDistance, renderHeight);
            targetPos = PlayerSkillMath.ClampPlanarDistance(startPos, targetPos, leapMaxDistance);
            targetPos.y = renderHeight;

            _jumpStart = startPos;
            _jumpTarget = targetPos;
            _jumpTimer = leapAirTime;
            _jumpState = 1;
            _jumpArcPos = startPos;
            _jumpCooldownTimer = leapCooldown;
            _invincibilityTimer = leapAirTime + leapInvincibility;
        }

        if (_jumpState == 1)
        {
            _jumpTimer -= context.DeltaTime;
            if (_jumpTimer <= 0f)
            {
                _jumpState = 0;
                _jumpArcPos = _jumpTarget;
                float2 landPos = PlayerSkillMath.ToPlanar(_jumpTarget);
                if (TryAddCircularSkillArea(landPos, leapLandingRadius, leapLandingDamage, leapLandingPullForce, leapLandingVerticalForce, leapEffects))
                {
                    SpawnImpact(landPos, leapLandingRadius * 0.5f, leapLandingRadius, 0.5f, new Color(1f, 0.85f, 0.1f, 1f));
                    _meleeHitShake = 0.25f;
                }
            }
            else
            {
                float t = 1f - (_jumpTimer / math.max(0.01f, leapAirTime));
                Vector3 flatPos = Vector3.Lerp(_jumpStart, _jumpTarget, t);
                float arcY = renderHeight + leapArcHeight * Mathf.Sin(t * Mathf.PI);
                _jumpArcPos = new Vector3(flatPos.x, arcY, flatPos.z);
            }
        }
    }

    private void UpdateLightPillarSkill(SkillUpdateContext context)
    {
        LightPillarSkillConfig lightPillar = skillConfig.LightPillar;
        int lightPillarLevel = currentLevel;
        ResolvedSkillHitEffectConfig lightPillarEffects = lightPillar.Effects.Resolve(lightPillarLevel, lightPillar.MaxLevel);
        float lightPillarCooldown = lightPillar.GetValue(lightPillar.Cooldown, lightPillarLevel);
        int lightPillarStrikeCount = math.max(1, lightPillar.GetIntValue(lightPillar.StrikeCount, lightPillarLevel));
        float lightPillarStartDistance = lightPillar.GetValue(lightPillar.StartDistance, lightPillarLevel);
        float lightPillarDistanceStep = lightPillar.GetValue(lightPillar.DistanceStep, lightPillarLevel);
        float lightPillarRadius = lightPillar.GetValue(lightPillar.Radius, lightPillarLevel);
        float lightPillarDamage = lightPillar.GetValue(lightPillar.Damage, lightPillarLevel);
        float lightPillarPullForce = lightPillar.GetValue(lightPillar.PullForce, lightPillarLevel);
        float lightPillarVerticalForce = lightPillar.GetValue(lightPillar.VerticalForce, lightPillarLevel);
        float lightPillarVisualDuration = lightPillar.GetValue(lightPillar.VisualDuration, lightPillarLevel);
        float lightPillarStrikeInterval = lightPillar.GetValue(lightPillar.StrikeInterval, lightPillarLevel);
        float lightPillarRingDuration = lightPillar.GetValue(lightPillar.RingDuration, lightPillarLevel);

        if (RougeInputManager.Instance.WasPressedThisFrame(RougeInputBinding.LightPillarStrike) && _tornadoCooldownTimer <= 0f && TryStartSkillActivation(PlayerSkillType.LightPillarStrike))
        {
            _tornadoCooldownTimer = lightPillarCooldown;
            _pillarStrikesTotal = lightPillarStrikeCount;
            _pillarStrikesDone = 0;
            _pillarNextStrikeTimer = 0f;
            _pillarBasePos = context.PlayerPosition;
            _pillarDirection = context.AimDirection;
            if (_tornadoVisual) _tornadoVisual.SetActive(false);
        }

        if (_pillarStrikesDone >= _pillarStrikesTotal)
        {
            return;
        }

        _pillarNextStrikeTimer -= context.DeltaTime;
        if (_pillarNextStrikeTimer > 0f)
        {
            return;
        }

        float dist = lightPillarStartDistance + _pillarStrikesDone * lightPillarDistanceStep;
        float2 strikePos = _pillarBasePos + _pillarDirection * dist;
        float strikeRadius = lightPillarRadius;

        for (int i = 0; i < MaxTornados; i++)
        {
            if (_tornadoLifeTimers[i] > 0f)
            {
                continue;
            }

            _tornadoPosData[i] = new float4(strikePos.x, renderHeight + 30f, strikePos.y, strikeRadius);
            _tornadoStateData[i] = new float4(strikeRadius, 60f, strikeRadius, 1f);
            _tornadoLifeTimers[i] = lightPillarVisualDuration;
            _tornadoMaxTimes[i] = lightPillarVisualDuration;
            break;
        }

        if (TryAddCircularSkillArea(strikePos, strikeRadius, lightPillarDamage, lightPillarPullForce, lightPillarVerticalForce, lightPillarEffects))
        {
            SpawnImpact(strikePos, strikeRadius, strikeRadius, lightPillarRingDuration, new Color(1f, 0.9f, 0.2f, 1f));
        }

        _pillarStrikesDone++;
        _pillarNextStrikeTimer = lightPillarStrikeInterval;
    }

    private void UpdateBombSkill(SkillUpdateContext context)
    {
        BombThrowSkillConfig bomb = skillConfig.BombThrow;
        int bombLevel = currentLevel;
        ResolvedSkillHitEffectConfig bombEffects = bomb.Effects.Resolve(bombLevel, bomb.MaxLevel);
        float bombCooldown = bomb.GetValue(bomb.Cooldown, bombLevel);
        float bombSpawnHeight = bomb.GetValue(bomb.SpawnHeight, bombLevel);
        float bombMaxThrowDistance = bomb.GetValue(bomb.MaxThrowDistance, bombLevel);
        float bombFlightTime = bomb.GetValue(bomb.FlightTime, bombLevel);
        float bombBaseRadius = bomb.GetValue(bomb.BaseRadius, bombLevel);
        float bombFragmentRadius = bomb.GetValue(bomb.FragmentRadius, bombLevel);
        float bombMinRadius = bomb.GetValue(bomb.MinRadius, bombLevel);
        float bombRadiusLossPerBounce = bomb.GetValue(bomb.RadiusLossPerBounce, bombLevel);
        float bombBaseDamage = bomb.GetValue(bomb.BaseDamage, bombLevel);
        float bombDamageFalloff = bomb.GetValue(bomb.DamageFalloff, bombLevel);
        float bombMinDamage = bomb.GetValue(bomb.MinDamage, bombLevel);
        float bombPullForce = bomb.GetValue(bomb.PullForce, bombLevel);
        float bombVerticalForce = bomb.GetValue(bomb.VerticalForce, bombLevel);
        int bombMaxBounceCount = math.max(1, bomb.GetIntValue(bomb.MaxBounceCount, bombLevel));
        float bombStopHorizontalVelocitySq = bomb.GetValue(bomb.StopHorizontalVelocitySq, bombLevel);
        float bombBounceHorizontalRetention = bomb.GetValue(bomb.BounceHorizontalRetention, bombLevel);
        float bombBounceVerticalRetention = bomb.GetValue(bomb.BounceVerticalRetention, bombLevel);
        float bombBounceUpVelocity = bomb.GetValue(bomb.BounceUpVelocity, bombLevel);
        float bombBounceUpVelocityLossPerBounce = bomb.GetValue(bomb.BounceUpVelocityLossPerBounce, bombLevel);
        int bombFragmentUnlockLevel = math.max(0, bomb.GetIntValue(bomb.FragmentUnlockLevel, bombLevel));
        int bombFragmentCount = math.max(0, bomb.GetIntValue(bomb.FragmentCount, bombLevel));
        float bombFragmentHorizontalSpeed = bomb.GetValue(bomb.FragmentHorizontalSpeed, bombLevel);
        float bombFragmentVerticalSpeed = bomb.GetValue(bomb.FragmentVerticalSpeed, bombLevel);
        float bombRingDuration = bomb.GetValue(bomb.RingDuration, bombLevel);

        bool hasActiveBomb = HasActiveBomb();
        if (RougeInputManager.Instance.WasPressedThisFrame(RougeInputBinding.BombThrow) && _bombCooldownTimer <= 0f && !hasActiveBomb && TryStartSkillActivation(PlayerSkillType.BombThrow))
        {
            _bombCooldownTimer = bombCooldown;

            Vector3 startPos = player != null
                ? player.transform.position + Vector3.up * bombSpawnHeight
                : new Vector3(context.PlayerPosition.x, renderHeight + bombSpawnHeight, context.PlayerPosition.y);
            Vector3 targetPos = context.HasMouseGroundPoint
                ? context.MouseGroundPoint
                : PlayerSkillMath.ToWorld(context.PlayerPosition + context.AimDirection * bombMaxThrowDistance, renderHeight);

            targetPos = PlayerSkillMath.ClampPlanarDistance(startPos, targetPos, bombMaxThrowDistance);

            float flightTime = bombFlightTime;
            Vector3 velocity = (targetPos - startPos) / flightTime;
            velocity.y = (targetPos.y - startPos.y - 0.5f * Physics.gravity.y * flightTime * flightTime) / flightTime;

            _activeBombs[0] = new RougeBomb
            {
                Active = true,
                Position = startPos,
                Velocity = velocity,
                BounceCount = 0,
                BaseRadius = bombBaseRadius
            };
            if (_bombVisuals[0]) _bombVisuals[0].SetActive(true);
        }

        for (int i = 0; i < MaxBombs; i++)
        {
            if (!_activeBombs[i].Active)
            {
                continue;
            }

            _activeBombs[i].Velocity += Physics.gravity * context.DeltaTime;
            _activeBombs[i].Position += _activeBombs[i].Velocity * context.DeltaTime;

            if (_bombVisuals[i])
            {
                _bombVisuals[i].transform.position = _activeBombs[i].Position;
                _bombVisuals[i].transform.localScale = Vector3.one * math.max(_activeBombs[i].BaseRadius * 0.5f - _activeBombs[i].BounceCount * 1.5f, 3f);
            }

            if (_activeBombs[i].Position.y > renderHeight)
            {
                continue;
            }

            float bounceRadius = math.max(_activeBombs[i].BaseRadius - _activeBombs[i].BounceCount * bombRadiusLossPerBounce, bombMinRadius);
            float bounceDamage = math.max(bombBaseDamage * (float)math.pow(bombDamageFalloff, _activeBombs[i].BounceCount), bombMinDamage);
            float2 bombPos = PlayerSkillMath.ToPlanar(_activeBombs[i].Position);

            if (TryAddCircularSkillArea(bombPos, bounceRadius, bounceDamage, bombPullForce, bombVerticalForce, bombEffects))
            {
                SpawnImpact(bombPos, bounceRadius * 0.5f, bounceRadius, bombRingDuration, new Color(1f, 0.5f, 0f, 1f));
            }

            if (i == 0 && _activeBombs[i].BounceCount == 0 && currentLevel >= bombFragmentUnlockLevel)
            {
                TrySpawnBombFragments(i, bombFragmentCount, bombFragmentRadius, bombFragmentHorizontalSpeed, bombFragmentVerticalSpeed);
            }

            _activeBombs[i].BounceCount++;
            float2 horizontalVelocity = new float2(_activeBombs[i].Velocity.x, _activeBombs[i].Velocity.z);

            if (_activeBombs[i].BounceCount >= bombMaxBounceCount || math.lengthsq(horizontalVelocity) < bombStopHorizontalVelocitySq)
            {
                _activeBombs[i].Active = false;
                if (_bombVisuals[i]) _bombVisuals[i].SetActive(false);
                continue;
            }

            _activeBombs[i].Position = new Vector3(_activeBombs[i].Position.x, renderHeight + 0.1f, _activeBombs[i].Position.z);
            float bounceUpVelocity = math.max(-_activeBombs[i].Velocity.y * bombBounceVerticalRetention, bombBounceUpVelocity - _activeBombs[i].BounceCount * bombBounceUpVelocityLossPerBounce);
            _activeBombs[i].Velocity = new Vector3(
                _activeBombs[i].Velocity.x * bombBounceHorizontalRetention,
                bounceUpVelocity,
                _activeBombs[i].Velocity.z * bombBounceHorizontalRetention);
        }
    }

    private bool HasActiveBomb()
    {
        for (int i = 0; i < MaxBombs; i++)
        {
            if (_activeBombs[i].Active)
            {
                return true;
            }
        }

        return false;
    }

    private void TrySpawnBombFragments(int rootBombIndex, int splitCount, float fragmentRadius, float fragmentHorizontalSpeed, float fragmentVerticalSpeed)
    {
        float angleBase = math.atan2(_activeBombs[rootBombIndex].Velocity.z, _activeBombs[rootBombIndex].Velocity.x);
        splitCount = math.max(0, splitCount);
        if (splitCount <= 0)
        {
            return;
        }

        int spawned = 0;

        for (int slot = 1; slot < MaxBombs && spawned < splitCount; slot++)
        {
            if (_activeBombs[slot].Active)
            {
                continue;
            }

            float spreadOffset = (spawned * (360f / splitCount)) * math.PI / 180f;
            Vector3 scatterVelocity = new Vector3(
                math.cos(angleBase + spreadOffset) * fragmentHorizontalSpeed,
                fragmentVerticalSpeed,
                math.sin(angleBase + spreadOffset) * fragmentHorizontalSpeed);

            _activeBombs[slot] = new RougeBomb
            {
                Active = true,
                Position = _activeBombs[rootBombIndex].Position,
                Velocity = scatterVelocity,
                BounceCount = 1,
                BaseRadius = fragmentRadius
            };

            if (_bombVisuals[slot]) _bombVisuals[slot].SetActive(true);
            spawned++;
        }
    }

    private void UpdateLaserSkill(SkillUpdateContext context)
    {
        LaserBeamSkillConfig laser = skillConfig.LaserBeam;
        int laserDurationLevel = _skillLevels[2];
        int laserBeamLevel = currentLevel;
        ResolvedSkillHitEffectConfig laserEffects = laser.Effects.Resolve(laserBeamLevel, laser.MaxLevel);
        float laserCooldown = laser.GetValue(laser.Cooldown, laserBeamLevel);
        float laserDuration = laser.GetValue(laser.Duration, laserDurationLevel);
        float laserLengthMax = laser.GetValue(laser.MaxLength, laserBeamLevel);
        float laserWidthMax = laser.GetValue(laser.MaxWidth, laserBeamLevel);
        float laserDamage = laser.GetValue(laser.Damage, laserBeamLevel);
        float laserPullForce = laser.GetValue(laser.PullForce, laserBeamLevel);
        int beamCount = math.max(1, laser.GetIntValue(laser.BeamCount, laserBeamLevel));
        float laserScatterAngle = laser.GetValue(laser.ScatterAngle, laserBeamLevel);
        float laserSubBeamRadiusMultiplier = laser.GetValue(laser.SubBeamRadiusMultiplier, laserBeamLevel);
        float laserSubBeamDamageMultiplier = laser.GetValue(laser.SubBeamDamageMultiplier, laserBeamLevel);

        if (RougeInputManager.Instance.WasPressedThisFrame(RougeInputBinding.LaserBeam) && _laserCooldownTimer <= 0f && TryStartSkillActivation(PlayerSkillType.LaserBeam))
        {
            _laserCooldownTimer = laserCooldown;
            _laserTimer = laserDuration;
            _laserPos = context.PlayerPosition;
            _laserDir = context.AimDirection;
        }

        if (_laserTimer <= 0f)
        {
            if (_laserVisual) _laserVisual.SetActive(false);
            for (int i = 0; i < MaxLaserSubBeams; i++)
            {
                if (_laserExtraVisuals[i] != null)
                {
                    _laserExtraVisuals[i].SetActive(false);
                }
            }
            return;
        }

        _laserTimer -= context.DeltaTime;
        float progress = 1f - math.max(0f, _laserTimer / math.max(0.01f, laserDuration));
        float sweepProgress = math.saturate(progress * 3f);
        float sweepEased = 1f - (1f - sweepProgress) * (1f - sweepProgress);
        float length = laserLengthMax * sweepEased;
        float width = laserWidthMax * math.min(1f, 2f * progress) * (1f - progress);

        if (_laserVisual)
        {
            _laserVisual.SetActive(true);
            _laserVisual.transform.position = new Vector3(_laserPos.x + _laserDir.x * (length * 0.5f), renderHeight + 1f, _laserPos.y + _laserDir.y * (length * 0.5f));
            _laserVisual.transform.rotation = Quaternion.LookRotation(new Vector3(_laserDir.x, 0f, _laserDir.y)) * Quaternion.Euler(90f, 0f, 0f);
            _laserVisual.transform.localScale = new Vector3(width, length * 0.5f, width * 0.4f);
        }

        TryAddSkillArea(new RougeSkillArea
        {
            Type = 3,
            Position = _laserPos,
            Direction = _laserDir,
            Length = length,
            Radius = width,
            Damage = laserDamage,
            PullForce = laserPullForce,
            VerticalForce = 0f
        }, laserEffects);

        int extraBeamCount = beamCount - 1;
        for (int i = 0; i < MaxLaserSubBeams; i++)
        {
            if (i >= extraBeamCount)
            {
                if (_laserExtraVisuals[i] != null)
                {
                    _laserExtraVisuals[i].SetActive(false);
                }

                continue;
            }

            int sideIndex = i / 2 + 1;
            float sign = i % 2 == 0 ? -1f : 1f;
            float normalizedOffset = sideIndex / math.max(1f, extraBeamCount * 0.5f);
            float angle = sign * laserScatterAngle * normalizedOffset * math.PI / 180f;
            float2 beamDir = Rotate(_laserDir, angle);
            float subWidth = width * laserSubBeamRadiusMultiplier;
            float subDamage = laserDamage * laserSubBeamDamageMultiplier;
            Vector3 beamPosition = new Vector3(_laserPos.x + beamDir.x * (length * 0.5f), renderHeight + 0.9f, _laserPos.y + beamDir.y * (length * 0.5f));

            if (_laserExtraVisuals[i] != null)
            {
                _laserExtraVisuals[i].SetActive(true);
                _laserExtraVisuals[i].transform.position = beamPosition;
                _laserExtraVisuals[i].transform.rotation = Quaternion.LookRotation(new Vector3(beamDir.x, 0f, beamDir.y)) * Quaternion.Euler(90f, 0f, 0f);
                _laserExtraVisuals[i].transform.localScale = new Vector3(subWidth, length * 0.5f, subWidth * 0.4f);
            }

            TryAddSkillArea(new RougeSkillArea
            {
                Type = 3,
                Position = _laserPos,
                Direction = beamDir,
                Length = length,
                Radius = subWidth,
                Damage = subDamage,
                PullForce = laserPullForce,
                VerticalForce = 0f
            }, laserEffects);
        }
    }

    private void UpdateMeleeSkill(SkillUpdateContext context)
    {
        MeleeSlashSkillConfig melee = skillConfig.MeleeSlash;
        int meleeLevel = currentLevel;
        ResolvedSkillHitEffectConfig meleeEffects = melee.Effects.Resolve(meleeLevel, melee.MaxLevel);
        float meleeSlashCooldown = melee.GetValue(melee.SlashCooldown, meleeLevel);
        float meleeFinisherCooldown = melee.GetValue(melee.FinisherCooldown, meleeLevel);
        float meleeComboWindow = melee.GetValue(melee.ComboWindow, meleeLevel);
        float meleeSlashDuration = melee.GetValue(melee.SlashDuration, meleeLevel);
        float meleeSlashRadius = melee.GetValue(melee.SlashRadius, meleeLevel);
        float meleeSlashDepth = melee.GetValue(melee.SlashDepth, meleeLevel);
        float meleeThrustRadiusMin = melee.GetValue(melee.ThrustRadiusMin, meleeLevel);
        float meleeThrustRadiusMax = melee.GetValue(melee.ThrustRadiusMax, meleeLevel);
        float meleeThrustWidth = melee.GetValue(melee.ThrustWidth, meleeLevel);
        float meleeThrustLength = melee.GetValue(melee.ThrustLength, meleeLevel);
        float meleeDefaultLunge = melee.GetValue(melee.DefaultLunge, meleeLevel);
        float meleeThrustLunge = melee.GetValue(melee.ThrustLunge, meleeLevel);
        float meleeSpinLunge = melee.GetValue(melee.SpinLunge, meleeLevel);
        float meleeThrustAdvanceSpeed = melee.GetValue(melee.ThrustAdvanceSpeed, meleeLevel);
        float meleeSlashDamage = melee.GetValue(melee.SlashDamage, meleeLevel);
        float meleeSpinDamage = melee.GetValue(melee.SpinDamage, meleeLevel);
        float meleePullForce = melee.GetValue(melee.PullForce, meleeLevel);
        float meleeSlashVerticalForce = melee.GetValue(melee.SlashVerticalForce, meleeLevel);
        float meleeThrustVerticalForce = melee.GetValue(melee.ThrustVerticalForce, meleeLevel);

        if (_meleeComboWindow > 0f)
        {
            _meleeComboWindow -= context.DeltaTime;
        }
        else
        {
            _meleeComboStep = 0;
        }

        if (RougeInputManager.Instance.WasPressedThisFrame(RougeInputBinding.PrimaryAttack) && _meleeCooldownTimer <= 0f && TryStartSkillActivation(PlayerSkillType.MeleeSlash))
        {
            _meleePos = context.PlayerPosition;
            _meleeDir = context.AimDirection;

            _meleeComboStep++;
            if (_meleeComboStep > 5)
            {
                _meleeComboStep = 1;
            }

            if (_meleeComboStep == 5)
            {
                _spikePos = context.PlayerPosition;
                _spikeDir = context.AimDirection;
                _meleeFinisherPos = context.PlayerPosition;
                _meleeFinisherDir = context.AimDirection;
                _meleeFinisherSlamTimer = melee.GetValue(melee.FinisherSlamDuration, meleeLevel);
                _spikeStartupTimer = _meleeFinisherSlamTimer;
                _spikeTimer = 0f;
                _meleeComboStep = 0;
                _meleeComboWindow = 0f;
                _meleeCooldownTimer = meleeFinisherCooldown;
                _meleeTimer = 0f;
                _meleeHitShake = 0.15f;
            }
            else
            {
                _meleeCooldownTimer = meleeSlashCooldown;
                _meleeComboWindow = meleeComboWindow;
                _meleeTimer = meleeSlashDuration;
                float lungeAmount = _meleeComboStep == 3 ? meleeThrustLunge : meleeDefaultLunge;
                if (_meleeComboStep == 4)
                {
                    lungeAmount = meleeSpinLunge;
                }

                if (player != null)
                {
                    player.transform.position += new Vector3(context.AimDirection.x, 0f, context.AimDirection.y) * lungeAmount;
                }

                _meleeHitShake = 0.08f;
            }
        }

        if (_meleeTimer > 0f)
        {
            _meleeTimer -= context.DeltaTime;
            float progress = 1f - math.max(0f, _meleeTimer / math.max(0.01f, meleeSlashDuration));
            float easedProgress = 1f - (1f - progress) * (1f - progress);

            float angle = 0f;
            float radius = meleeSlashRadius;
            Vector3 scale = new Vector3(radius * 2f, 0.5f, meleeSlashDepth);

            if (_meleeComboStep == 1)
            {
                angle = math.lerp(-70f, 70f, easedProgress) * math.PI / 180f;
            }
            else if (_meleeComboStep == 2)
            {
                angle = math.lerp(70f, -70f, easedProgress) * math.PI / 180f;
            }
            else if (_meleeComboStep == 3)
            {
                angle = 0f;
                radius = math.lerp(meleeThrustRadiusMin, meleeThrustRadiusMax, math.sin(easedProgress * math.PI));
                scale = new Vector3(meleeThrustWidth, 0.5f, meleeThrustLength);
                if (player != null)
                {
                    player.transform.position += new Vector3(_meleeDir.x, 0f, _meleeDir.y) * (meleeThrustAdvanceSpeed * context.DeltaTime);
                }
            }
            else if (_meleeComboStep == 4)
            {
                angle = math.lerp(0f, 360f, easedProgress) * math.PI / 180f;
                scale = new Vector3(radius * 2f, 0.5f, radius * 2f);
            }

            float2 swingDirection = Rotate(_meleeDir, angle);
            Vector3 center = new Vector3(context.PlayerPosition.x, renderHeight + 1f, context.PlayerPosition.y) + new Vector3(swingDirection.x, 0f, swingDirection.y) * (radius * 0.4f);

            if (_meleeVisual)
            {
                _meleeVisual.SetActive(true);
                _meleeVisual.transform.position = center;
                _meleeVisual.transform.rotation = Quaternion.LookRotation(new Vector3(swingDirection.x, 0f, swingDirection.y));
                _meleeVisual.transform.localScale = scale;
            }

            TryAddSkillArea(new RougeSkillArea
            {
                Type = 4,
                Position = new float2(center.x, center.z),
                Direction = swingDirection,
                Radius = radius,
                Damage = _meleeComboStep == 4 ? meleeSpinDamage : meleeSlashDamage,
                PullForce = meleePullForce,
                VerticalForce = _meleeComboStep == 3 ? meleeThrustVerticalForce : meleeSlashVerticalForce
            }, meleeEffects);
        }
        else if (_meleeVisual)
        {
            _meleeVisual.SetActive(false);
        }

        UpdateMeleeFinisherSlam(context);
        UpdateSpikeFinisher(context);
    }

    private void UpdateMeleeFinisherSlam(SkillUpdateContext context)
    {
        MeleeSlashSkillConfig melee = skillConfig.MeleeSlash;
        int meleeLevel = currentLevel;
        float meleeSpikeDuration = melee.GetValue(melee.SpikeDuration, meleeLevel);
        float meleeFinisherSlamDuration = melee.GetValue(melee.FinisherSlamDuration, meleeLevel);
        float meleeFinisherArcStartAngle = melee.GetValue(melee.FinisherArcStartAngle, meleeLevel);
        float meleeFinisherArcEndAngle = melee.GetValue(melee.FinisherArcEndAngle, meleeLevel);
        float meleeFinisherSlamLength = melee.GetValue(melee.FinisherSlamLength, meleeLevel);
        float meleeFinisherSlamWidth = melee.GetValue(melee.FinisherSlamWidth, meleeLevel);
        float meleeFinisherSlamThickness = melee.GetValue(melee.FinisherSlamThickness, meleeLevel);

        if (_spikeStartupTimer > 0f)
        {
            float previousStartupTimer = _spikeStartupTimer;
            _spikeStartupTimer -= context.DeltaTime;
            if (_spikeStartupTimer <= 0f && previousStartupTimer > 0f)
            {
                _spikeTimer = meleeSpikeDuration;
                _meleeHitShake = 0.12f;
            }
        }

        if (_meleeFinisherSlamTimer <= 0f)
        {
            if (_meleeFinisherVisual != null)
            {
                _meleeFinisherVisual.SetActive(false);
            }

            return;
        }

        _meleeFinisherSlamTimer -= context.DeltaTime;
        float progress = 1f - math.max(0f, _meleeFinisherSlamTimer / math.max(0.01f, meleeFinisherSlamDuration));
        float eased = 1f - math.pow(1f - progress, 3f);
        float angleDegrees = math.lerp(meleeFinisherArcStartAngle, meleeFinisherArcEndAngle, eased);
        float angleRadians = angleDegrees * math.PI / 180f;
        Vector3 forward = new Vector3(_meleeFinisherDir.x, 0f, _meleeFinisherDir.y);
        Vector3 pivot = new Vector3(_meleeFinisherPos.x, renderHeight, _meleeFinisherPos.y);
        Vector3 bladeDirection = (forward * math.cos(angleRadians) + Vector3.up * math.sin(angleRadians)).normalized;
        Vector3 swingNormal = Vector3.Cross(forward, Vector3.up).normalized;
        if (swingNormal.sqrMagnitude < 0.001f)
        {
            swingNormal = Vector3.forward;
        }

        Vector3 bladeUp = Vector3.Cross(swingNormal, bladeDirection).normalized;
        Vector3 slamPosition = pivot + bladeDirection * (meleeFinisherSlamLength * 0.5f);
        Quaternion slamRotation = Quaternion.LookRotation(bladeDirection, bladeUp);
        float arcScaleBoost = 1f + math.sin(eased * math.PI) * 0.1f;

        if (_meleeFinisherVisual != null)
        {
            _meleeFinisherVisual.SetActive(true);
            _meleeFinisherVisual.transform.position = slamPosition;
            _meleeFinisherVisual.transform.rotation = slamRotation;
            _meleeFinisherVisual.transform.localScale = new Vector3(meleeFinisherSlamWidth * arcScaleBoost, meleeFinisherSlamThickness, meleeFinisherSlamLength * arcScaleBoost);
        }

        if (_meleeFinisherSlamTimer > 0f)
        {
            return;
        }

        if (_meleeFinisherVisual != null)
        {
            _meleeFinisherVisual.SetActive(false);
        }
    }

    private void UpdateSpikeFinisher(SkillUpdateContext context)
    {
        MeleeSlashSkillConfig melee = skillConfig.MeleeSlash;
        int meleeLevel = currentLevel;
        ResolvedSkillHitEffectConfig meleeEffects = melee.Effects.Resolve(meleeLevel, melee.MaxLevel);
        float meleeSpikeDuration = melee.GetValue(melee.SpikeDuration, meleeLevel);
        float meleeSpikeRiseRatio = melee.GetValue(melee.SpikeRiseRatio, meleeLevel);
        float centerSpikeHeight = melee.GetValue(melee.CenterSpikeHeight, meleeLevel);
        float sideSpikeHeight = melee.GetValue(melee.SideSpikeHeight, meleeLevel);
        float centerSpikeRadius = melee.GetValue(melee.CenterSpikeRadius, meleeLevel);
        float sideSpikeRadius = melee.GetValue(melee.SideSpikeRadius, meleeLevel);
        float centerSpikeDistance = melee.GetValue(melee.CenterSpikeDistance, meleeLevel);
        float sideSpikeDistance = melee.GetValue(melee.SideSpikeDistance, meleeLevel);
        float sideSpikeAngle = melee.GetValue(melee.SideSpikeAngle, meleeLevel) * math.PI / 180f;
        float centerSpikeVerticalForce = melee.GetValue(melee.CenterSpikeVerticalForce, meleeLevel);
        float sideSpikeVerticalForce = melee.GetValue(melee.SideSpikeVerticalForce, meleeLevel);
        float spikePullForce = melee.GetValue(melee.SpikePullForce, meleeLevel);

        if (_spikeStartupTimer > 0f || _spikeTimer <= 0f)
        {
            return;
        }

        _spikeTimer -= context.DeltaTime;
        float normalizedTime = 1f - _spikeTimer / math.max(0.01f, meleeSpikeDuration);
        float heightFactor = math.saturate(math.sin(normalizedTime * math.PI));
        float[] spikeMaxHeights = { centerSpikeHeight, sideSpikeHeight, sideSpikeHeight };
        float[] spikeRadii = { centerSpikeRadius, sideSpikeRadius, sideSpikeRadius };
        float[] spikeDistances = { centerSpikeDistance, sideSpikeDistance, sideSpikeDistance };
        float[] spikeAngles = { 0f, -sideSpikeAngle, sideSpikeAngle };
        bool risingPhase = normalizedTime < meleeSpikeRiseRatio + 0.12f;

        for (int i = 0; i < 3; i++)
        {
            float2 spikeBase = _spikePos + Rotate(_spikeDir, spikeAngles[i]) * spikeDistances[i];
            float height = spikeMaxHeights[i] * heightFactor;
            float radius = spikeRadii[i];

            if (_spikeVisuals[i] != null)
            {
                bool visible = height > 0.05f;
                _spikeVisuals[i].SetActive(visible);
                if (visible)
                {
                    _spikeVisuals[i].transform.position = new Vector3(spikeBase.x, renderHeight + height * 0.5f, spikeBase.y);
                    _spikeVisuals[i].transform.localScale = new Vector3(radius * 2f, height * 0.5f, radius * 2f);
                }
            }

            if (risingPhase && height > 0.3f)
            {
                TryAddSkillArea(new RougeSkillArea
                {
                    Type = 6,
                    Position = spikeBase,
                    Radius = radius + 3f,
                    VerticalForce = i == 0 ? centerSpikeVerticalForce : sideSpikeVerticalForce,
                    PullForce = spikePullForce,
                    Damage = 0f
                }, meleeEffects);
            }
        }

        if (_spikeTimer > 0f)
        {
            return;
        }

        for (int i = 0; i < 3; i++)
        {
            if (_spikeVisuals[i] != null)
            {
                _spikeVisuals[i].SetActive(false);
            }
        }
    }

    private void UpdateOrbitSkill(SkillUpdateContext context)
    {
        OrbitBallSkillConfig orbit = skillConfig.OrbitBall;
        int orbitCountLevel = _skillLevels[4];
        int orbitDamageLevel = currentLevel;
        ResolvedSkillHitEffectConfig orbitEffects = orbit.Effects.Resolve(orbitDamageLevel, orbit.MaxLevel);
        int numBalls = math.max(0, orbit.GetIntValue(orbit.MaxBalls, orbitCountLevel));
        if (numBalls <= 0)
        {
            for (int i = _orbitVisuals.Count - 1; i >= 0; i--)
            {
                Destroy(_orbitVisuals[i]);
                _orbitVisuals.RemoveAt(i);
            }

            return;
        }

        _orbitTimer += context.DeltaTime * orbit.GetValue(orbit.OrbitSpeed, orbitDamageLevel);
        float[] ringRadii = { orbit.GetValue(orbit.FirstRingRadius, orbitDamageLevel), orbit.GetValue(orbit.SecondRingRadius, orbitDamageLevel) };
        float[] ringSpeeds = { orbit.GetValue(orbit.FirstRingSpeedMultiplier, orbitDamageLevel), orbit.GetValue(orbit.SecondRingSpeedMultiplier, orbitDamageLevel) };
        float orbitVisualScale = orbit.GetValue(orbit.VisualScale, orbitDamageLevel);
        float orbitDamageRadius = orbit.GetValue(orbit.DamageRadius, orbitDamageLevel);
        float orbitDamage = orbit.GetValue(orbit.Damage, orbitDamageLevel);
        float orbitPullForce = orbit.GetValue(orbit.PullForce, orbitDamageLevel);
        float orbitVerticalForce = orbit.GetValue(orbit.VerticalForce, orbitDamageLevel);

        while (_orbitVisuals.Count < numBalls)
        {
            GameObject ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(ball.GetComponent<Collider>());
            ball.name = "Orbit Ball " + _orbitVisuals.Count;
            ball.GetComponent<MeshRenderer>().material = _orbitMat;
            _orbitVisuals.Add(ball);
        }

        for (int i = _orbitVisuals.Count - 1; i >= numBalls; i--)
        {
            Destroy(_orbitVisuals[i]);
            _orbitVisuals.RemoveAt(i);
        }

        for (int i = 0; i < numBalls; i++)
        {
            int ring = i / 4;
            int ballInRing = i % 4;
            float orbitRadius = ringRadii[math.min(ring, ringRadii.Length - 1)];
            float speedMultiplier = ringSpeeds[math.min(ring, ringSpeeds.Length - 1)];
            float offset = (float)ballInRing / 4f * (math.PI * 2f);
            float orbitTime = _orbitTimer * speedMultiplier + offset;
            float2 orbitPos = context.PlayerPosition + new float2(math.cos(orbitTime), math.sin(orbitTime)) * orbitRadius;

            if (_orbitVisuals[i] != null)
            {
                _orbitVisuals[i].transform.position = new Vector3(orbitPos.x, renderHeight + 1.5f, orbitPos.y);
                _orbitVisuals[i].transform.localScale = Vector3.one * orbitVisualScale;
            }

            TryAddSkillArea(new RougeSkillArea
            {
                Type = 5,
                Position = orbitPos,
                Radius = orbitDamageRadius,
                Damage = orbitDamage,
                PullForce = orbitPullForce,
                VerticalForce = orbitVerticalForce
            }, orbitEffects);
        }
    }

    private void UpdateShockwaveSkill(SkillUpdateContext context)
    {
        ShockwaveSkillConfig shockwave = skillConfig.Shockwave;
        int shockwaveLevel = currentLevel;
        ResolvedSkillHitEffectConfig shockwaveEffects = shockwave.Effects.Resolve(shockwaveLevel, shockwave.MaxLevel);
        float shockwaveCooldown = shockwave.GetValue(shockwave.Cooldown, shockwaveLevel);
        float shockwaveLaunchDuration = shockwave.GetValue(shockwave.LaunchDuration, shockwaveLevel);
        float shockwaveSlamDuration = shockwave.GetValue(shockwave.SlamDuration, shockwaveLevel);
        float shockwaveJumpHeight = shockwave.GetValue(shockwave.JumpHeight, shockwaveLevel);
        float shockwaveRingStartRadius = shockwave.GetValue(shockwave.RingStartRadius, shockwaveLevel);
        float shockwaveRingEndRadius = shockwave.GetValue(shockwave.RingEndRadius, shockwaveLevel);
        float shockwaveImpactRadius = shockwave.GetValue(shockwave.ImpactRadius, shockwaveLevel);
        int shockwaveImpactRingCount = math.max(1, shockwave.GetIntValue(shockwave.ImpactRingCount, shockwaveLevel));
        float shockwaveRingThickness = shockwave.GetValue(shockwave.RingThickness, shockwaveLevel);
        float shockwaveImpactDamage = shockwave.GetValue(shockwave.ImpactDamage, shockwaveLevel);
        float shockwavePullForce = shockwave.GetValue(shockwave.PullForce, shockwaveLevel);
        float shockwaveVerticalForce = shockwave.GetValue(shockwave.VerticalForce, shockwaveLevel);
        float shockwaveCameraLift = shockwave.GetValue(shockwave.CameraLift, shockwaveLevel);
        float shockwaveCameraFovKick = shockwave.GetValue(shockwave.CameraFovKick, shockwaveLevel);
        float shockwaveLandingShake = shockwave.GetValue(shockwave.LandingShake, shockwaveLevel);
        float shockwaveRingLifetime = shockwave.GetValue(shockwave.RingLifetime, shockwaveLevel);
        float shockwaveRingDelay = shockwave.GetValue(shockwave.RingDelay, shockwaveLevel);

        if (_shockwaveState == 0 && CanStartMovementSkill(PlayerSkillType.Shockwave) && RougeInputManager.Instance.WasPressedThisFrame(RougeInputBinding.Shockwave) && _shockwaveCooldownTimer <= 0f && player != null && _jumpState == 0 && TryStartSkillActivation(PlayerSkillType.Shockwave))
        {
            _shockwaveCooldownTimer = shockwaveCooldown;
            _shockwavePos = context.PlayerPosition;
            _shockwaveJumpStart = player.transform.position;
            _shockwaveTimer = shockwaveLaunchDuration;
            _shockwaveState = 1;
            _invincibilityTimer = math.max(_invincibilityTimer, shockwaveLaunchDuration + shockwaveSlamDuration + 0.2f);
            SpawnAOERing(PlayerSkillMath.ToWorld(_shockwavePos, renderHeight + 0.04f), shockwaveRingStartRadius * 0.75f, 0.22f, new Color(1f, 0.86f, 0.2f, 1f));
        }

        if (_shockwaveState == 0)
        {
            if (_shockwaveVisual) _shockwaveVisual.SetActive(false);
            return;
        }

        Vector3 landingPosition = PlayerSkillMath.ToWorld(_shockwavePos, renderHeight);
        if (_shockwaveState == 1)
        {
            _shockwaveTimer = math.max(0f, _shockwaveTimer - context.DeltaTime);
            float riseProgress = 1f - math.max(0f, _shockwaveTimer / math.max(0.01f, shockwaveLaunchDuration));
            float riseEase = math.sin(riseProgress * math.PI * 0.5f);
            player.transform.position = new Vector3(landingPosition.x, renderHeight + shockwaveJumpHeight * riseEase, landingPosition.z);
            _cameraLiftOffset = math.max(_cameraLiftOffset, shockwaveCameraLift * riseEase);
            _cameraFovOffset = math.min(_cameraFovOffset, -shockwaveCameraFovKick * riseEase);

            if (_shockwaveTimer <= 0f)
            {
                _shockwaveState = 2;
                _shockwaveTimer = shockwaveSlamDuration;
            }

            return;
        }

        _shockwaveTimer = math.max(0f, _shockwaveTimer - context.DeltaTime);
        float slamProgress = 1f - math.max(0f, _shockwaveTimer / math.max(0.01f, shockwaveSlamDuration));
        float remainingHeight = shockwaveJumpHeight * math.pow(1f - slamProgress, 1.4f);
        player.transform.position = new Vector3(landingPosition.x, renderHeight + remainingHeight, landingPosition.z);
        _cameraLiftOffset = math.max(_cameraLiftOffset, shockwaveCameraLift * (1f - slamProgress));
        _cameraFovOffset = math.lerp(-shockwaveCameraFovKick * 0.55f, 0f, slamProgress);

        if (_shockwaveTimer > 0f)
        {
            return;
        }

        player.transform.position = landingPosition;
        _shockwaveState = 0;
        _cameraLiftOffset = 0f;
        _cameraFovOffset = 0f;
        _meleeHitShake = math.max(_meleeHitShake, shockwaveLandingShake);

        TryAddCircularSkillArea(_shockwavePos, shockwaveImpactRadius, shockwaveImpactDamage, shockwavePullForce, shockwaveVerticalForce, shockwaveEffects);

        for (int i = 0; i < shockwaveImpactRingCount; i++)
        {
            float t = shockwaveImpactRingCount == 1 ? 1f : i / (float)(shockwaveImpactRingCount - 1);
            float radius = math.lerp(shockwaveRingStartRadius, shockwaveRingEndRadius, t);
            float ringDuration = shockwaveRingLifetime + i * shockwaveRingDelay;
            SpawnAOERing(new Vector3(_shockwavePos.x, renderHeight + 0.04f, _shockwavePos.y), radius, ringDuration, new Color(1f, 0.82f, 0.16f, 1f));
            TryAddSkillArea(new RougeSkillArea
            {
                Type = 7,
                Position = _shockwavePos,
                Radius = radius,
                Length = shockwaveRingThickness,
                Damage = 0f,
                PullForce = shockwavePullForce,
                VerticalForce = shockwaveVerticalForce
            }, shockwaveEffects);
        }
    }

    private void UpdateMeteorSkill(SkillUpdateContext context)
    {
        MeteorRainSkillConfig meteor = skillConfig.MeteorRain;
        int meteorLevel = currentLevel;
        ResolvedSkillHitEffectConfig meteorEffects = meteor.Effects.Resolve(meteorLevel, meteor.MaxLevel);
        float meteorCooldown = meteor.GetValue(meteor.Cooldown, meteorLevel);
        float meteorDuration = meteor.GetValue(meteor.Duration, meteorLevel);
        int meteorWaveCount = math.max(1, meteor.GetIntValue(meteor.WaveCount, meteorLevel));
        float meteorWaveInterval = meteor.GetValue(meteor.WaveInterval, meteorLevel);
        float meteorScatterRadius = meteor.GetValue(meteor.ScatterRadius, meteorLevel);
        float meteorVisualDuration = meteor.GetValue(meteor.VisualDuration, meteorLevel);
        float meteorStartOffsetX = meteor.GetValue(meteor.StartOffsetX, meteorLevel);
        float meteorStartOffsetZ = meteor.GetValue(meteor.StartOffsetZ, meteorLevel);
        float meteorFallHeight = meteor.GetValue(meteor.FallHeight, meteorLevel);
        float meteorStartScale = meteor.GetValue(meteor.StartScale, meteorLevel);
        float meteorEndScale = meteor.GetValue(meteor.EndScale, meteorLevel);
        float meteorImpactRadius = meteor.GetValue(meteor.ImpactRadius, meteorLevel);
        float meteorImpactDamage = meteor.GetValue(meteor.ImpactDamage, meteorLevel);
        float meteorPullForce = meteor.GetValue(meteor.PullForce, meteorLevel);
        float meteorVerticalForce = meteor.GetValue(meteor.VerticalForce, meteorLevel);
        float meteorRingDuration = meteor.GetValue(meteor.RingDuration, meteorLevel);

        if (RougeInputManager.Instance.WasPressedThisFrame(RougeInputBinding.MeteorRain) && _meteorCooldownTimer <= 0f && TryStartSkillActivation(PlayerSkillType.MeteorRain))
        {
            _meteorCooldownTimer = meteorCooldown;
            _meteorTimer = meteorDuration;
            _meteorWaveIndex = 0;
            _meteorWaveTimer = 0f;
            float2 fallbackTarget = context.PlayerPosition + context.AimDirection * meteorScatterRadius;
            _meteorTargetPos = context.HasMouseGroundPoint ? PlayerSkillMath.ToPlanar(context.MouseGroundPoint) : fallbackTarget;
        }

        if (_meteorTimer > 0f)
        {
            _meteorTimer -= context.DeltaTime;
            _meteorWaveTimer -= context.DeltaTime;
            if (_meteorWaveTimer <= 0f && _meteorWaveIndex < meteorWaveCount)
            {
                _meteorWaveTimer = meteorWaveInterval;
                uint hash = math.hash(new uint2((uint)_meteorWaveIndex + 1u, (uint)Time.frameCount));
                float angle = ((hash & 0xFFFFu) / 65535f) * math.PI * 2f;
                float distance = ((hash >> 16) & 0xFFFFu) / 65535f * meteorScatterRadius;
                float2 impactPos = _meteorTargetPos + new float2(math.cos(angle), math.sin(angle)) * distance;

                if (_meteorWaveIndex < MeteorVisualMax)
                {
                    _meteorVisualTimers[_meteorWaveIndex] = meteorVisualDuration;
                    _meteorVisualTargets[_meteorWaveIndex] = new Vector3(impactPos.x, renderHeight, impactPos.y);
                    if (_meteorVisuals[_meteorWaveIndex] != null)
                    {
                        _meteorVisuals[_meteorWaveIndex].SetActive(true);
                    }
                }

                _meteorWaveIndex++;
            }
        }

        for (int i = 0; i < MeteorVisualMax; i++)
        {
            if (_meteorVisualTimers[i] <= 0f)
            {
                continue;
            }

            float previousTimer = _meteorVisualTimers[i];
            _meteorVisualTimers[i] -= context.DeltaTime;
            float progress = 1f - math.max(0f, _meteorVisualTimers[i] / math.max(0.01f, meteorVisualDuration));
            Vector3 target = _meteorVisualTargets[i];
            Vector3 meteorPosition = target + new Vector3(meteorStartOffsetX - progress * meteorStartOffsetX, meteorFallHeight * (1f - progress), meteorStartOffsetZ - progress * meteorStartOffsetZ);
            float scale = math.lerp(meteorStartScale, meteorEndScale, progress);

            if (_meteorVisuals[i] != null)
            {
                _meteorVisuals[i].transform.position = meteorPosition;
                _meteorVisuals[i].transform.localScale = new Vector3(scale, scale * 2f, scale);
                Vector3 fallDirection = target - meteorPosition;
                if (fallDirection.sqrMagnitude > 0.0001f)
                {
                    _meteorVisuals[i].transform.rotation = Quaternion.LookRotation(fallDirection.normalized);
                }
                if (progress >= 1f)
                {
                    _meteorVisuals[i].SetActive(false);
                }
            }

            if (_meteorVisualTimers[i] <= 0f && previousTimer > 0f)
            {
                float2 impactPos = new float2(target.x, target.z);
                if (TryAddCircularSkillArea(impactPos, meteorImpactRadius, meteorImpactDamage, meteorPullForce, meteorVerticalForce, meteorEffects))
                {
                    SpawnImpact(impactPos, meteorImpactRadius, meteorImpactRadius, meteorRingDuration, new Color(1f, 0.2f, 0f, 1f));
                }
            }
        }
    }

    private void UpdateIceZoneSkill(SkillUpdateContext context)
    {
        IceZoneSkillConfig iceZone = skillConfig.IceZone;
        int iceZoneLevel = currentLevel;
        ResolvedSkillHitEffectConfig iceZoneEffects = iceZone.Effects.Resolve(iceZoneLevel, iceZone.MaxLevel);
        float iceZoneCooldown = iceZone.GetValue(iceZone.Cooldown, iceZoneLevel);
        float iceZoneDuration = iceZone.GetValue(iceZone.Duration, iceZoneLevel);
        float iceRadius = iceZone.GetValue(iceZone.Radius, iceZoneLevel);
        float iceTickDamage = iceZone.GetValue(iceZone.TickDamage, iceZoneLevel);
        float iceTickPullForce = iceZone.GetValue(iceZone.TickPullForce, iceZoneLevel);
        float iceBurstRadiusBonus = iceZone.GetValue(iceZone.BurstRadiusBonus, iceZoneLevel);
        float iceBurstDamage = iceZone.GetValue(iceZone.BurstDamage, iceZoneLevel);
        float iceBurstPullForce = iceZone.GetValue(iceZone.BurstPullForce, iceZoneLevel);
        float iceBurstVerticalForce = iceZone.GetValue(iceZone.BurstVerticalForce, iceZoneLevel);
        float iceRingDuration = iceZone.GetValue(iceZone.RingDuration, iceZoneLevel);
        float icePulseBaseAlpha = iceZone.GetValue(iceZone.PulseBaseAlpha, iceZoneLevel);
        float icePulseAmplitude = iceZone.GetValue(iceZone.PulseAmplitude, iceZoneLevel);
        float icePulseSpeed = iceZone.GetValue(iceZone.PulseSpeed, iceZoneLevel);

        if (RougeInputManager.Instance.WasPressedThisFrame(RougeInputBinding.IceZone) && _iceZoneCooldownTimer <= 0f && TryStartSkillActivation(PlayerSkillType.IceZone))
        {
            _iceZoneCooldownTimer = iceZoneCooldown;
            _iceZoneTimer = iceZoneDuration;
            float2 fallbackTarget = context.PlayerPosition + context.AimDirection * iceRadius;
            _iceZonePos = context.HasMouseGroundPoint ? PlayerSkillMath.ToPlanar(context.MouseGroundPoint) : fallbackTarget;
        }

        if (_iceZoneTimer <= 0f)
        {
            if (_iceZoneVisual) _iceZoneVisual.SetActive(false);
            return;
        }

        float previousTimer = _iceZoneTimer;
        _iceZoneTimer -= context.DeltaTime;

        if (_iceZoneVisual)
        {
            _iceZoneVisual.SetActive(true);
            _iceZoneVisual.transform.position = new Vector3(_iceZonePos.x, renderHeight + 0.03f, _iceZonePos.y);
            _iceZoneVisual.transform.localScale = new Vector3(iceRadius * 2f, 0.06f, iceRadius * 2f);
            float pulse = icePulseBaseAlpha + icePulseAmplitude * math.sin(_survivalTime * icePulseSpeed);
            if (_iceZoneMat != null)
            {
                _iceZoneMat.color = new Color(0.3f, 0.7f, 1f, pulse);
            }
        }

        TryAddSkillArea(new RougeSkillArea
        {
            Type = 8,
            Position = _iceZonePos,
            Radius = iceRadius,
            Damage = iceTickDamage,
            PullForce = iceTickPullForce,
            VerticalForce = 0f
        }, iceZoneEffects);

        if (previousTimer > 0f && _iceZoneTimer <= 0f)
        {
            if (TryAddCircularSkillArea(_iceZonePos, iceRadius + iceBurstRadiusBonus, iceBurstDamage, iceBurstPullForce, iceBurstVerticalForce, iceZoneEffects))
            {
                SpawnImpact(_iceZonePos, iceRadius, iceRadius + iceBurstRadiusBonus, iceRingDuration, new Color(0.3f, 0.7f, 1f, 1f), 2f);
            }
        }
    }

    private void UpdatePoisonBottleSkill(SkillUpdateContext context)
    {
        PoisonBottleSkillConfig poison = skillConfig.PoisonBottle;
        int poisonLevel = currentLevel;
        ResolvedSkillHitEffectConfig poisonEffects = poison.Effects.Resolve(poisonLevel, poison.MaxLevel);
        float poisonCooldown = poison.GetValue(poison.Cooldown, poisonLevel);
        float poisonSpawnHeight = poison.GetValue(poison.SpawnHeight, poisonLevel);
        float poisonMaxThrowDistance = poison.GetValue(poison.MaxThrowDistance, poisonLevel);
        float poisonFlightTime = poison.GetValue(poison.FlightTime, poisonLevel);
        float poisonBottleVisualScale = poison.GetValue(poison.BottleVisualScale, poisonLevel);
        float poisonZoneDuration = poison.GetValue(poison.ZoneDuration, poisonLevel);
        float poisonZoneRadius = poison.GetValue(poison.ZoneRadius, poisonLevel);
        float poisonZoneCoreRatio = poison.GetValue(poison.ZoneCoreRatio, poisonLevel);
        float poisonZoneIrregularity = poison.GetValue(poison.ZoneIrregularity, poisonLevel);
        float poisonZoneNoiseScale = poison.GetValue(poison.ZoneNoiseScale, poisonLevel);
        float poisonZonePulseSpeed = poison.GetValue(poison.ZonePulseSpeed, poisonLevel);
        float poisonZonePulseAmplitude = poison.GetValue(poison.ZonePulseAmplitude, poisonLevel);

        if (RougeInputManager.Instance.WasPressedThisFrame(RougeInputBinding.PoisonBottle) && _poisonCooldownTimer <= 0f)
        {
            for (int i = 0; i < MaxPoisonBottles; i++)
            {
                if (_activePoisonBottles[i].Active)
                {
                    continue;
                }

                if (!TryStartSkillActivation(PlayerSkillType.PoisonBottle))
                {
                    break;
                }

                _poisonCooldownTimer = poisonCooldown;
                Vector3 startPos = player != null
                    ? player.transform.position + Vector3.up * poisonSpawnHeight
                    : new Vector3(context.PlayerPosition.x, renderHeight + poisonSpawnHeight, context.PlayerPosition.y);
                Vector3 targetPos = context.HasMouseGroundPoint
                    ? context.MouseGroundPoint
                    : PlayerSkillMath.ToWorld(context.PlayerPosition + context.AimDirection * poisonMaxThrowDistance, renderHeight);

                targetPos = PlayerSkillMath.ClampPlanarDistance(startPos, targetPos, poisonMaxThrowDistance);
                float flightTime = math.max(0.1f, poisonFlightTime);
                Vector3 velocity = (targetPos - startPos) / flightTime;
                velocity.y = (targetPos.y - startPos.y - 0.5f * Physics.gravity.y * flightTime * flightTime) / flightTime;

                _activePoisonBottles[i] = new RougeThrownBottle
                {
                    Active = true,
                    Position = startPos,
                    Velocity = velocity
                };

                if (_poisonBottleVisuals[i] != null)
                {
                    _poisonBottleVisuals[i].SetActive(true);
                    _poisonBottleVisuals[i].transform.position = startPos;
                    _poisonBottleVisuals[i].transform.localScale = Vector3.one * poisonBottleVisualScale;
                }

                break;
            }
        }

        for (int i = 0; i < MaxPoisonBottles; i++)
        {
            if (!_activePoisonBottles[i].Active)
            {
                if (_poisonBottleVisuals[i] != null)
                {
                    _poisonBottleVisuals[i].SetActive(false);
                }

                continue;
            }

            _activePoisonBottles[i].Velocity += Physics.gravity * context.DeltaTime;
            _activePoisonBottles[i].Position += _activePoisonBottles[i].Velocity * context.DeltaTime;

            if (_poisonBottleVisuals[i] != null)
            {
                _poisonBottleVisuals[i].SetActive(true);
                _poisonBottleVisuals[i].transform.position = _activePoisonBottles[i].Position;
                _poisonBottleVisuals[i].transform.localScale = Vector3.one * poisonBottleVisualScale;
            }

            if (_activePoisonBottles[i].Position.y > renderHeight)
            {
                continue;
            }

            float2 impactPos = PlayerSkillMath.ToPlanar(_activePoisonBottles[i].Position);
            ActivatePoisonZone(impactPos, poisonZoneRadius, poisonZoneDuration, (uint)(Time.frameCount * 131 + i + 1));
            SpawnAOERing(new Vector3(impactPos.x, renderHeight + 0.1f, impactPos.y), poisonZoneRadius * 0.7f, 0.35f, new Color(0.35f, 1f, 0.45f, 1f));

            _activePoisonBottles[i].Active = false;
            if (_poisonBottleVisuals[i] != null)
            {
                _poisonBottleVisuals[i].SetActive(false);
            }
        }

        for (int i = 0; i < MaxPoisonZones; i++)
        {
            if (!_activePoisonZones[i].Active)
            {
                if (_poisonZoneVisuals[i] != null)
                {
                    _poisonZoneVisuals[i].SetActive(false);
                }

                continue;
            }

            _activePoisonZones[i].Timer -= context.DeltaTime;
            if (_activePoisonZones[i].Timer <= 0f)
            {
                _activePoisonZones[i].Active = false;
                if (_poisonZoneVisuals[i] != null)
                {
                    _poisonZoneVisuals[i].SetActive(false);
                }

                continue;
            }

            float normalizedLifetime = 1f - (_activePoisonZones[i].Timer / math.max(0.01f, _activePoisonZones[i].Duration));
            float pulseA = math.sin((_survivalTime + i * 0.73f) * poisonZonePulseSpeed);
            float pulseB = math.cos((_survivalTime + i * 1.17f) * (poisonZonePulseSpeed * 0.85f));
            float xScale = 1f + poisonZonePulseAmplitude * pulseA;
            float zScale = 1f + poisonZonePulseAmplitude * pulseB;

            if (_poisonZoneVisuals[i] != null)
            {
                _poisonZoneVisuals[i].SetActive(true);
                _poisonZoneVisuals[i].transform.position = new Vector3(_activePoisonZones[i].Position.x, renderHeight + 0.025f, _activePoisonZones[i].Position.y);
                _poisonZoneVisuals[i].transform.rotation = Quaternion.Euler(0f, normalizedLifetime * 90f + i * 17f, 0f);
                _poisonZoneVisuals[i].transform.localScale = new Vector3(_activePoisonZones[i].Radius * 2f * xScale, 0.05f, _activePoisonZones[i].Radius * 2f * zScale);
            }

            TryAddSkillArea(new RougeSkillArea
            {
                Type = 9,
                Position = _activePoisonZones[i].Position,
                Radius = _activePoisonZones[i].Radius,
                Length = _activePoisonZones[i].Radius * math.clamp(poisonZoneCoreRatio, 0.2f, 0.95f),
                Damage = 0f,
                PullForce = 0f,
                SpinForce = 0f,
                VerticalForce = 0f,
                AuxA = poisonZoneIrregularity,
                AuxB = 0f,
                AuxC = poisonZoneNoiseScale,
                AuxD = _activePoisonZones[i].Seed
            }, poisonEffects);
        }
    }

    private void ActivatePoisonZone(float2 position, float radius, float duration, uint seed)
    {
        for (int i = 0; i < MaxPoisonZones; i++)
        {
            if (_activePoisonZones[i].Active)
            {
                continue;
            }

            _activePoisonZones[i] = new RougePoisonZoneState
            {
                Active = true,
                Position = position,
                Timer = duration,
                Duration = duration,
                Radius = radius,
                Seed = seed
            };
            return;
        }

        int replaceIndex = 0;
        float shortestTimer = _activePoisonZones[0].Timer;
        for (int i = 1; i < MaxPoisonZones; i++)
        {
            if (_activePoisonZones[i].Timer < shortestTimer)
            {
                shortestTimer = _activePoisonZones[i].Timer;
                replaceIndex = i;
            }
        }

        _activePoisonZones[replaceIndex] = new RougePoisonZoneState
        {
            Active = true,
            Position = position,
            Timer = duration,
            Duration = duration,
            Radius = radius,
            Seed = seed
        };
    }

    private void UpdateDashSkill(SkillUpdateContext context)
    {
        DashSkillConfig dash = skillConfig.Dash;
        int dashLevel = currentLevel;
        ResolvedSkillHitEffectConfig dashEffects = dash.Effects.Resolve(dashLevel, dash.MaxLevel);
        float dashCooldown = dash.GetValue(dash.Cooldown, dashLevel);
        float dashDuration = dash.GetValue(dash.Duration, dashLevel);
        float dashDistance = dash.GetValue(dash.Distance, dashLevel);
        float dashInvincibilityDuration = dash.GetValue(dash.InvincibilityDuration, dashLevel);
        float dashSpinDamage = dash.GetValue(dash.SpinDamage, dashLevel);
        float dashHitRadius = dash.GetValue(dash.HitRadius, dashLevel);
        float dashBladeWidth = dash.GetValue(dash.BladeWidth, dashLevel);
        float dashBladeLength = dash.GetValue(dash.BladeLength, dashLevel);
        float dashBladeThickness = dash.GetValue(dash.BladeThickness, dashLevel);
        float dashMaxSpinRate = dash.GetValue(dash.MaxSpinRate, dashLevel);
        float dashImpactRadius = dash.GetValue(dash.ImpactRadius, dashLevel);
        float dashImpactDamage = dash.GetValue(dash.ImpactDamage, dashLevel);
        float dashPullForce = dash.GetValue(dash.PullForce, dashLevel);
        float dashVerticalForce = dash.GetValue(dash.VerticalForce, dashLevel);
        float dashRingDuration = dash.GetValue(dash.RingDuration, dashLevel);

        if (_dashSpinTimer <= 0f && CanStartMovementSkill(PlayerSkillType.Dash) && RougeInputManager.Instance.WasPressedThisFrame(RougeInputBinding.Dash) && _dashCooldownTimer <= 0f && _jumpState == 0 && player != null && TryStartSkillActivation(PlayerSkillType.Dash))
        {
            _dashCooldownTimer = dashCooldown;
            _dashSpinTimer = dashDuration;
            _dashSpinAngle = 0f;
            _dashStartPosition = player.transform.position;

            float2 startPlanar = new float2(_dashStartPosition.x, _dashStartPosition.z);
            _dashDirection = math.normalizesafe(context.AimDirection, new float2(0f, 1f));
            float2 endPlanar = startPlanar + _dashDirection * dashDistance;
            endPlanar.x = math.clamp(endPlanar.x, -arenaHalfExtent + 1f, arenaHalfExtent - 1f);
            endPlanar.y = math.clamp(endPlanar.y, -arenaHalfExtent + 1f, arenaHalfExtent - 1f);
            _dashTargetPosition = new Vector3(endPlanar.x, _dashStartPosition.y, endPlanar.y);
            _invincibilityTimer = math.max(_invincibilityTimer, dashInvincibilityDuration);
        }

        if (_dashSpinTimer <= 0f)
        {
            if (_dashVisual != null)
            {
                _dashVisual.SetActive(false);
            }

            return;
        }

        float previousTimer = _dashSpinTimer;
        _dashSpinTimer = math.max(0f, _dashSpinTimer - context.DeltaTime);
        float previousProgress = 1f - previousTimer / math.max(0.01f, dashDuration);
        float currentProgress = 1f - _dashSpinTimer / math.max(0.01f, dashDuration);
        float previousTravel = EvaluateWhirlwindTravel(previousProgress);
        float currentTravel = EvaluateWhirlwindTravel(currentProgress);
        Vector3 newPosition = Vector3.Lerp(_dashStartPosition, _dashTargetPosition, currentTravel);
        player.transform.position = newPosition;
        _invincibilityTimer = math.max(_invincibilityTimer, context.DeltaTime + 0.02f);

        float spinFactor = EvaluateWhirlwindSpinFactor(currentProgress);
        _dashSpinAngle += dashMaxSpinRate * spinFactor * context.DeltaTime;
        _meleeHitShake = math.max(_meleeHitShake, 0.014f + spinFactor * 0.018f);
        _cameraFovOffset = math.max(_cameraFovOffset, 0.65f + spinFactor * 1.2f);

        Quaternion spinRotation = Quaternion.AngleAxis(_dashSpinAngle, Vector3.up);
        Vector3 bladeForward = spinRotation * new Vector3(_dashDirection.x, 0f, _dashDirection.y);
        if (bladeForward.sqrMagnitude < 0.0001f)
        {
            bladeForward = Vector3.forward;
        }

        Vector3 bladeCenter = newPosition + bladeForward * (dashBladeLength * 0.45f);
        float bladeScaleBoost = 1f + spinFactor * 0.45f;

        if (_dashVisual != null)
        {
            _dashVisual.SetActive(true);
            _dashVisual.transform.position = new Vector3(bladeCenter.x, renderHeight + 1f, bladeCenter.z);
            _dashVisual.transform.rotation = Quaternion.LookRotation(bladeForward);
            _dashVisual.transform.localScale = new Vector3(dashBladeWidth * bladeScaleBoost, dashBladeThickness, dashBladeLength * bladeScaleBoost);
        }

        TryAddSkillArea(new RougeSkillArea
        {
            Type = 4,
            Position = new float2(bladeCenter.x, bladeCenter.z),
            Direction = math.normalizesafe(new float2(bladeForward.x, bladeForward.z), new float2(0f, 1f)),
            Radius = dashHitRadius,
            Damage = dashSpinDamage * (0.85f + spinFactor * 0.35f),
            PullForce = math.abs(dashPullForce) * (0.85f + spinFactor * 0.25f),
            VerticalForce = dashVerticalForce
        }, dashEffects);

        if (_dashSpinTimer > 0f)
        {
            return;
        }

        float2 endPos = new float2(player.transform.position.x, player.transform.position.z);
        SpawnImpact(endPos, dashImpactRadius, dashImpactRadius, dashRingDuration, new Color(1f, 0.75f, 0.15f, 1f));
        TryAddCircularSkillArea(endPos, dashImpactRadius, dashImpactDamage, math.abs(dashPullForce), dashVerticalForce, dashEffects);

        if (_dashVisual != null)
        {
            _dashVisual.SetActive(false);
        }
    }

    private static float EvaluateWhirlwindTravel(float normalizedTime)
    {
        float t = math.saturate(normalizedTime);
        if (t < 0.14f)
        {
            float p = t / 0.14f;
            return 0.09f * (1f - math.pow(1f - p, 3f));
        }

        if (t < 0.8f)
        {
            float p = (t - 0.14f) / 0.66f;
            return math.lerp(0.09f, 0.9f, p);
        }

        float q = (t - 0.8f) / 0.2f;
        return math.lerp(0.9f, 1f, q * q * (3f - 2f * q));
    }

    private static float EvaluateWhirlwindSpinFactor(float normalizedTime)
    {
        float t = math.saturate(normalizedTime);
        if (t < 0.12f)
        {
            float p = t / 0.12f;
            return math.lerp(0.4f, 1.25f, 1f - math.pow(1f - p, 3f));
        }

        if (t < 0.82f)
        {
            float p = (t - 0.12f) / 0.7f;
            return 1.15f + math.sin(p * math.PI) * 0.35f;
        }

        float q = (t - 0.82f) / 0.18f;
        return math.lerp(0.95f, 0.28f, q * q * (3f - 2f * q));
    }
}