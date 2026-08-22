using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public partial class RougeGameManager
{
    // Commander/tactical skills are intentionally kept dormant until their future insertion flow is designed.
    private static readonly bool CommanderSkillsEnabled = false;

    private enum TacticalSkillSelectionState
    {
        None,
        WindmillPoint,
        WindmillDirection,
        BlackHolePoint,
        MissileBarragePoint
    }

    private struct ActiveWindmillSkill
    {
        public float2 Position;
        public float2 Direction;
        public float PhaseTimer;
        public float Remaining;
        public float TickTimer;
        public int Phase;
        public GameObject Visual;
        public Transform Spinner;
        public float DamageMultiplier;
    }

    private struct ActiveBlackHoleSkill
    {
        public float2 Position;
        public float Remaining;
        public float TickTimer;
        public GameObject Visual;
        public float DamageMultiplier;
    }

    private struct ActiveMissileBarrageSkill
    {
        public float2 Position;
        public float Remaining;
        public float SpawnTimer;
    }

    private struct ActiveFallingMissile
    {
        public Vector3 Start;
        public Vector3 End;
        public float Elapsed;
        public float Duration;
        public GameObject Visual;
    }

    private TacticalSkillSelectionState _tacticalSkillSelection;
    private float2 _tacticalSkillPoint;
    private bool _tacticalSkillPointValid;
    private int _windmillSkillCost;
    private int _blackHoleSkillCost;
    private int _overclockSkillCost;
    private float _windmillSkillCooldown;
    private float _blackHoleSkillCooldown;
    private float _overclockSkillCooldown;
    private float _missileBarrageSkillCooldown;
    private float _windmillDamageMultiplier;
    private float _blackHoleDamageMultiplier;
    private LineRenderer _tacticalSkillCircle;
    private LineRenderer _tacticalSkillDirection;
    private readonly List<ActiveWindmillSkill> _activeWindmillSkills = new List<ActiveWindmillSkill>();
    private readonly List<ActiveBlackHoleSkill> _activeBlackHoleSkills = new List<ActiveBlackHoleSkill>();
    private readonly List<ActiveMissileBarrageSkill> _activeMissileBarrageSkills = new List<ActiveMissileBarrageSkill>();
    private readonly List<ActiveFallingMissile> _activeFallingMissiles = new List<ActiveFallingMissile>();
    private readonly Button[] _tacticalSkillButtons = new Button[4];
    private readonly Text[] _tacticalSkillButtonTexts = new Text[4];
    private Material _tacticalBlackHoleMaterial;
    private Material _tacticalIndicatorMaterial;

    private bool HasTacticalSkillSelection => _tacticalSkillSelection != TacticalSkillSelectionState.None;

    private void InitializeTacticalSkills()
    {
        tacticalSkillBalance ??= new RougeTacticalSkillBalanceConfig();
        tacticalSkillBalance.EnsureDefaults();
        _windmillSkillCost = Mathf.Max(0, tacticalSkillBalance.windmill.initialCost);
        _blackHoleSkillCost = Mathf.Max(0, tacticalSkillBalance.blackHole.initialCost);
        _overclockSkillCost = Mathf.Max(0, tacticalSkillBalance.overclock.initialCost);
        _windmillSkillCooldown = 0f;
        _blackHoleSkillCooldown = 0f;
        _overclockSkillCooldown = 0f;
        _missileBarrageSkillCooldown = 0f;
        _windmillDamageMultiplier = 1f;
        _blackHoleDamageMultiplier = 1f;
        _tacticalSkillSelection = TacticalSkillSelectionState.None;
        EnsureTacticalSkillIndicators();
        HideTacticalSkillIndicators();
    }

    private void DisposeTacticalSkills()
    {
        ClearTacticalSkillSelection();
        for (int i = 0; i < _activeWindmillSkills.Count; i++)
        {
            if (_activeWindmillSkills[i].Visual != null) Destroy(_activeWindmillSkills[i].Visual);
        }
        _activeWindmillSkills.Clear();
        for (int i = 0; i < _activeBlackHoleSkills.Count; i++)
        {
            if (_activeBlackHoleSkills[i].Visual != null) Destroy(_activeBlackHoleSkills[i].Visual);
        }
        _activeBlackHoleSkills.Clear();
        _activeMissileBarrageSkills.Clear();
        for (int i = 0; i < _activeFallingMissiles.Count; i++)
        {
            if (_activeFallingMissiles[i].Visual != null) Destroy(_activeFallingMissiles[i].Visual);
        }
        _activeFallingMissiles.Clear();
        if (_tacticalSkillCircle != null) Destroy(_tacticalSkillCircle.gameObject);
        if (_tacticalSkillDirection != null) Destroy(_tacticalSkillDirection.gameObject);
        _tacticalSkillCircle = null;
        _tacticalSkillDirection = null;
        if (_tacticalBlackHoleMaterial != null) Destroy(_tacticalBlackHoleMaterial);
        _tacticalBlackHoleMaterial = null;
        if (_tacticalIndicatorMaterial != null) Destroy(_tacticalIndicatorMaterial);
        _tacticalIndicatorMaterial = null;
    }

    private void EnsureTacticalSkillIndicators()
    {
        if (_tacticalSkillCircle == null)
        {
            _tacticalSkillCircle = TowerDefenseVisuals.CreateCircleRenderer("Tactical Skill AOE Indicator", transform);
            _tacticalSkillCircle.widthMultiplier = 0.28f;
            _tacticalSkillCircle.sharedMaterial = GetTacticalIndicatorMaterial();
            _tacticalSkillCircle.sortingOrder = 32000;
        }
        if (_tacticalSkillDirection == null)
        {
            _tacticalSkillDirection = TowerDefenseVisuals.CreateBeamRenderer("Tactical Skill Direction Indicator", transform, 0.5f);
            _tacticalSkillDirection.positionCount = 3;
            _tacticalSkillDirection.sharedMaterial = GetTacticalIndicatorMaterial();
            _tacticalSkillDirection.sortingOrder = 32000;
        }
    }

    private Material GetTacticalIndicatorMaterial()
    {
        if (_tacticalIndicatorMaterial != null) return _tacticalIndicatorMaterial;
        Shader shader = Shader.Find("Hidden/Internal-Colored");
        if (shader == null) shader = Shader.Find("Sprites/Default");
        _tacticalIndicatorMaterial = new Material(shader)
        {
            name = "Tactical Indicator Always Visible",
            hideFlags = HideFlags.HideAndDontSave,
            renderQueue = 5000
        };
        _tacticalIndicatorMaterial.SetInt("_ZWrite", 0);
        _tacticalIndicatorMaterial.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
        _tacticalIndicatorMaterial.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
        return _tacticalIndicatorMaterial;
    }

    private void HideTacticalSkillIndicators()
    {
        if (_tacticalSkillCircle != null) _tacticalSkillCircle.enabled = false;
        if (_tacticalSkillDirection != null) _tacticalSkillDirection.enabled = false;
    }

    private void BeginWindmillSkillSelection()
    {
        if (_towerDefenseGameOver || _windmillSkillCooldown > 0f || _towerDefenseGold < _windmillSkillCost) return;
        BeginTacticalSkillSelection(TacticalSkillSelectionState.WindmillPoint);
    }

    private void BeginBlackHoleSkillSelection()
    {
        if (_towerDefenseGameOver || _blackHoleSkillCooldown > 0f || _towerDefenseGold < _blackHoleSkillCost) return;
        BeginTacticalSkillSelection(TacticalSkillSelectionState.BlackHolePoint);
    }

    private void BeginOverclockSkillSelection()
    {
        if (_towerDefenseGameOver || _overclockSkillCooldown > 0f ||
            _towerDefenseGold < _overclockSkillCost) return;
        CastOverclockSkill();
    }

    private void BeginMissileBarrageSkillSelection()
    {
        if (_towerDefenseGameOver || _missileBarrageSkillCooldown > 0f) return;
        BeginTacticalSkillSelection(TacticalSkillSelectionState.MissileBarragePoint);
    }

    private void BeginTacticalSkillSelection(TacticalSkillSelectionState state)
    {
        _towerBuildSelectionActive = false;
        _previewValid = false;
        if (_towerPreview != null) Destroy(_towerPreview.gameObject);
        _towerPreview = null;
        SelectPlacedTower(null);
        _tacticalSkillSelection = state;
        _tacticalSkillPointValid = false;
        EnsureTacticalSkillIndicators();
        if (!_towerPlacementMode) SetTowerPlacementMode(true);
        RefreshTowerDefenseUi(true);
    }

    private void CancelTacticalSkillSelection(bool exitEditMode)
    {
        ClearTacticalSkillSelection();
        if (exitEditMode && _towerPlacementMode) SetTowerPlacementMode(false);
        else RefreshTowerDefenseUi(true);
    }

    private void ClearTacticalSkillSelection()
    {
        _tacticalSkillSelection = TacticalSkillSelectionState.None;
        _tacticalSkillPointValid = false;
        HideTacticalSkillIndicators();
    }

    private bool UpdateTacticalSkillInput(Mouse mouse, bool pointerOverUi)
    {
        if (!HasTacticalSkillSelection) return false;
        if (mouse == null) return true;

        if (mouse.rightButton.wasPressedThisFrame)
        {
            CancelTacticalSkillSelection(true);
            return true;
        }

        bool hasPoint = TryGetTacticalMousePoint(out Vector3 worldPoint);
        float radius = _tacticalSkillSelection switch
        {
            TacticalSkillSelectionState.BlackHolePoint => tacticalSkillBalance.blackHole.pullRadius,
            TacticalSkillSelectionState.MissileBarragePoint => tacticalSkillBalance.missileBarrage.selectionRadius,
            _ => tacticalSkillBalance.windmill.impactRadius
        };
        bool forbidTowerPlace = _tacticalSkillSelection == TacticalSkillSelectionState.WindmillPoint;
        bool valid = hasPoint && IsValidTacticalSkillPoint(new float2(worldPoint.x, worldPoint.z), forbidTowerPlace);

        if (_tacticalSkillSelection == TacticalSkillSelectionState.WindmillDirection)
        {
            float2 mousePosition = new float2(worldPoint.x, worldPoint.z);
            float2 direction = mousePosition - _tacticalSkillPoint;
            valid = hasPoint && math.lengthsq(direction) > 0.25f;
            Vector3 center = new Vector3(_tacticalSkillPoint.x, renderHeight, _tacticalSkillPoint.y);
            TowerDefenseVisuals.UpdateCircle(_tacticalSkillCircle, center,
                tacticalSkillBalance.windmill.radius, new Color(0.2f, 0.9f, 1f, 0.95f), true);
            UpdateTacticalDirectionIndicator(center, direction, valid);
        }
        else
        {
            _tacticalSkillPointValid = valid;
            Color color = valid ? new Color(0.18f, 1f, 0.5f, 0.95f) : new Color(1f, 0.15f, 0.12f, 0.95f);
            TowerDefenseVisuals.UpdateCircle(_tacticalSkillCircle, worldPoint, radius, color, hasPoint);
            if (_tacticalSkillDirection != null) _tacticalSkillDirection.enabled = false;
        }

        if (!mouse.leftButton.wasPressedThisFrame || pointerOverUi) return true;

        switch (_tacticalSkillSelection)
        {
            case TacticalSkillSelectionState.WindmillPoint:
                if (!valid) return true;
                _tacticalSkillPoint = new float2(worldPoint.x, worldPoint.z);
                _tacticalSkillSelection = TacticalSkillSelectionState.WindmillDirection;
                RefreshTowerDefenseUi(true);
                break;

            case TacticalSkillSelectionState.WindmillDirection:
                if (!valid || _windmillSkillCooldown > 0f || _towerDefenseGold < _windmillSkillCost) return true;
                float2 direction = math.normalizesafe(new float2(worldPoint.x, worldPoint.z) - _tacticalSkillPoint, new float2(0f, 1f));
                CastWindmillSkill(_tacticalSkillPoint, direction);
                break;

            case TacticalSkillSelectionState.BlackHolePoint:
                if (!valid || _blackHoleSkillCooldown > 0f || _towerDefenseGold < _blackHoleSkillCost) return true;
                CastBlackHoleSkill(new float2(worldPoint.x, worldPoint.z));
                break;

            case TacticalSkillSelectionState.MissileBarragePoint:
                if (!valid || _missileBarrageSkillCooldown > 0f) return true;
                CastMissileBarrageSkill(new float2(worldPoint.x, worldPoint.z));
                break;
        }
        return true;
    }

    private bool TryGetTacticalMousePoint(out Vector3 worldPoint)
    {
        worldPoint = default;
        Camera camera = RougeCameraFollow.ResolveCamera();
        Mouse mouse = Mouse.current;
        if (camera == null || mouse == null) return false;
        Vector2 pointer = mouse.position.ReadValue();
        return PlayerSkillMath.TryGetMouseGroundPoint(camera, new Vector3(pointer.x, pointer.y, 0f), renderHeight, out worldPoint);
    }

    private bool IsValidTacticalSkillPoint(float2 point, bool forbidTowerPlace)
    {
        if (math.abs(point.x) > arenaHalfExtent || math.abs(point.y) > arenaHalfExtent) return false;
        if (IsTacticalPointBlocked(point, 0.35f)) return false;
        if (!forbidTowerPlace) return true;

        int layer = LayerMask.NameToLayer("TowerPlace");
        if (layer < 0) return true;
        Vector3 position = new Vector3(point.x, renderHeight + 0.25f, point.y);
        return !Physics.CheckSphere(position, 0.3f, 1 << layer, QueryTriggerInteraction.Collide);
    }

    private bool IsTacticalPointBlocked(float2 point, float padding)
    {
        if (!_obstacles.IsCreated) return false;
        for (int i = 0; i < _obstacleCount; i++)
        {
            if (RougeObstacleMath.ContainsPoint(_obstacles[i], point, padding)) return true;
        }
        return false;
    }

    private void UpdateTacticalDirectionIndicator(Vector3 center, float2 direction, bool valid)
    {
        if (_tacticalSkillDirection == null) return;
        _tacticalSkillDirection.enabled = true;
        _tacticalSkillDirection.positionCount = 3;
        _tacticalSkillDirection.widthMultiplier = 0.5f;
        Color color = valid ? new Color(0.15f, 0.9f, 1f, 1f) : new Color(1f, 0.15f, 0.12f, 1f);
        _tacticalSkillDirection.startColor = color;
        _tacticalSkillDirection.endColor = color;
        Vector2 normalized = ((Vector2)direction).normalized;
        Vector3 forward = new Vector3(normalized.x, 0f, normalized.y);
        Vector3 side = new Vector3(-normalized.y, 0f, normalized.x);
        float length = Mathf.Max(8f, tacticalSkillBalance.windmill.radius * 1.5f);
        Vector3 tip = center + forward * length + Vector3.up * 0.3f;
        _tacticalSkillDirection.SetPosition(0, center + Vector3.up * 0.3f);
        _tacticalSkillDirection.SetPosition(1, tip);
        _tacticalSkillDirection.SetPosition(2, tip - forward * 2.5f + side * 1.5f);
    }

    private void CastWindmillSkill(float2 position, float2 direction)
    {
        _towerDefenseGold -= _windmillSkillCost;
        _windmillSkillCost = GetNextTacticalSkillCost(_windmillSkillCost, tacticalSkillBalance.windmill.costMultiplier);
        _windmillSkillCooldown = tacticalSkillBalance.windmill.cooldown;
        GameObject visual = CreateWindmillVisual(position, out Transform spinner);
        _activeWindmillSkills.Add(new ActiveWindmillSkill
        {
            Position = position,
            Direction = direction,
            PhaseTimer = tacticalSkillBalance.windmill.fallDuration,
            Remaining = tacticalSkillBalance.windmill.duration,
            TickTimer = 0f,
            Phase = 0,
            Visual = visual,
            Spinner = spinner,
            DamageMultiplier = _windmillDamageMultiplier
        });
        _windmillDamageMultiplier *= GetNextTacticalDamageMultiplier();
        ClearTacticalSkillSelection();
        SetTowerPlacementMode(false);
        RefreshTowerDefenseUi(true);
    }

    private void CastBlackHoleSkill(float2 position)
    {
        _towerDefenseGold -= _blackHoleSkillCost;
        _blackHoleSkillCost = GetNextTacticalSkillCost(_blackHoleSkillCost, tacticalSkillBalance.blackHole.costMultiplier);
        _blackHoleSkillCooldown = tacticalSkillBalance.blackHole.cooldown;
        _activeBlackHoleSkills.Add(new ActiveBlackHoleSkill
        {
            Position = position,
            Remaining = tacticalSkillBalance.blackHole.duration,
            TickTimer = 0f,
            Visual = CreateBlackHoleVisual(position),
            DamageMultiplier = _blackHoleDamageMultiplier
        });
        _blackHoleDamageMultiplier *= GetNextTacticalDamageMultiplier();
        ClearTacticalSkillSelection();
        SetTowerPlacementMode(false);
        RefreshTowerDefenseUi(true);
    }

    private void CastOverclockSkill()
    {
        if (_overclockSkillCooldown > 0f || _towerDefenseGold < _overclockSkillCost || _defenseTowers.Count == 0) return;
        RougeOverclockTacticalSkillConfig config = tacticalSkillBalance.overclock;
        int affected = 0;
        for (int i = 0; i < _defenseTowers.Count; i++)
        {
            RougeDefenseTower tower = _defenseTowers[i];
            if (tower == null) continue;
            tower.ActivateOverclock(config.duration, config.attackSpeedMultiplier, config.damageMultiplier);
            affected++;
        }
        if (affected == 0) return;
        _towerDefenseGold -= _overclockSkillCost;
        _overclockSkillCost = GetNextTacticalSkillCost(_overclockSkillCost,
            tacticalSkillBalance.overclock.costMultiplier);
        _overclockSkillCooldown = tacticalSkillBalance.overclock.cooldown;
        RefreshTowerDefenseUi(true);
    }

    private void CastMissileBarrageSkill(float2 position)
    {
        RougeMissileBarrageTacticalSkillConfig config = tacticalSkillBalance.missileBarrage;
        _missileBarrageSkillCooldown = config.cooldown;
        _activeMissileBarrageSkills.Add(new ActiveMissileBarrageSkill
        {
            Position = position,
            Remaining = config.duration,
            SpawnTimer = 0f
        });
        SpawnAOERing(new Vector3(position.x, renderHeight + 0.05f, position.y), config.selectionRadius,
            0.45f, new Color(1f, 0.28f, 0.08f, 1f));
        ClearTacticalSkillSelection();
        SetTowerPlacementMode(false);
        RefreshTowerDefenseUi(true);
    }

    private float GetNextTacticalDamageMultiplier()
    {
        return 1f + Mathf.Max(0f, tacticalSkillBalance.damageGrowthPerCast);
    }

    private static int GetNextTacticalSkillCost(int currentCost, float multiplier)
    {
        double next = System.Math.Ceiling(System.Math.Max(0, currentCost) * System.Math.Max(1d, multiplier));
        return next >= int.MaxValue ? int.MaxValue : (int)next;
    }

    private void UpdateTacticalSkills(float dt)
    {
        _windmillSkillCooldown = Mathf.Max(0f, _windmillSkillCooldown - dt);
        _blackHoleSkillCooldown = Mathf.Max(0f, _blackHoleSkillCooldown - dt);
        _overclockSkillCooldown = Mathf.Max(0f, _overclockSkillCooldown - dt);
        _missileBarrageSkillCooldown = Mathf.Max(0f, _missileBarrageSkillCooldown - dt);
        UpdateActiveWindmillSkills(dt);
        UpdateActiveBlackHoleSkills(dt);
        UpdateActiveMissileBarrageSkills(dt);
        UpdateFallingMissiles(dt);
    }

    private void UpdateActiveWindmillSkills(float dt)
    {
        RougeWindmillTacticalSkillConfig config = tacticalSkillBalance.windmill;
        for (int i = _activeWindmillSkills.Count - 1; i >= 0; i--)
        {
            ActiveWindmillSkill skill = _activeWindmillSkills[i];
            if (skill.Visual == null)
            {
                _activeWindmillSkills.RemoveAt(i);
                continue;
            }

            if (skill.Phase == 0)
            {
                skill.PhaseTimer -= dt;
                float progress = 1f - Mathf.Clamp01(skill.PhaseTimer / Mathf.Max(0.01f, config.fallDuration));
                float height = Mathf.Lerp(config.fallHeight, 2.5f, progress * progress);
                skill.Visual.transform.position = new Vector3(skill.Position.x, renderHeight + height, skill.Position.y);
                if (skill.PhaseTimer <= 0f)
                {
                    AddTacticalDamagePulse(skill.Position, config.impactRadius,
                        config.impactDamage * skill.DamageMultiplier, false, 0f);
                    SpawnExplosionVFX(new Vector3(skill.Position.x, renderHeight + 0.5f, skill.Position.y), Mathf.Max(4f, config.impactRadius * 0.28f));
                    SpawnAOERing(new Vector3(skill.Position.x, renderHeight + 0.05f, skill.Position.y), config.impactRadius, 0.4f,
                        new Color(0.18f, 0.9f, 1f, 1f));
                    skill.Phase = 1;
                    skill.PhaseTimer = config.startDelay;
                    skill.Visual.transform.position = new Vector3(skill.Position.x, renderHeight + 2.5f, skill.Position.y);
                }
            }
            else if (skill.Phase == 1)
            {
                skill.PhaseTimer -= dt;
                if (skill.PhaseTimer <= 0f)
                {
                    skill.Phase = 2;
                    skill.TickTimer = 0f;
                    if (skill.Spinner != null) skill.Spinner.gameObject.SetActive(true);
                }
            }
            else
            {
                skill.Remaining -= dt;
                skill.TickTimer -= dt;
                float distance = config.moveSpeed * dt;
                skill.Position = AdvanceTacticalSkillUntilBlocked(skill.Position, skill.Direction, distance, config.obstaclePadding, out bool blocked);
                if (blocked) skill.Direction = float2.zero;
                skill.Visual.transform.position = new Vector3(skill.Position.x, renderHeight + 2.5f, skill.Position.y);
                if (skill.Spinner != null) skill.Spinner.Rotate(0f, 0f, 1080f * dt, Space.Self);
                while (skill.TickTimer <= 0f && skill.Remaining > 0f)
                {
                    AddTacticalDamagePulse(skill.Position, config.radius,
                        config.tickDamage * skill.DamageMultiplier, true, config.killLaunchHeight);
                    skill.TickTimer += Mathf.Max(0.05f, config.tickInterval);
                }
                if (skill.Remaining <= 0f)
                {
                    Destroy(skill.Visual);
                    _activeWindmillSkills.RemoveAt(i);
                    continue;
                }
            }
            _activeWindmillSkills[i] = skill;
        }
    }

    private void UpdateActiveBlackHoleSkills(float dt)
    {
        RougeBlackHoleTacticalSkillConfig config = tacticalSkillBalance.blackHole;
        for (int i = _activeBlackHoleSkills.Count - 1; i >= 0; i--)
        {
            ActiveBlackHoleSkill skill = _activeBlackHoleSkills[i];
            skill.Remaining -= dt;
            skill.TickTimer -= dt;
            if (skill.Visual != null)
            {
                skill.Visual.transform.Rotate(0f, 150f * dt, 0f, Space.World);
                float pulse = 0.92f + Mathf.Sin(Time.time * 8f) * 0.08f;
                float scale = Mathf.Max(1f, config.pullRadius * 0.22f) * pulse;
                skill.Visual.transform.localScale = Vector3.one * scale;
            }
            while (skill.TickTimer <= 0f && skill.Remaining > 0f)
            {
                TryAddSkillArea(new RougeSkillArea
                {
                    Type = 18,
                    Position = skill.Position,
                    Radius = config.pullRadius,
                    PullForce = config.pullSpeed
                });
                skill.TickTimer += Mathf.Max(0.05f, config.tickInterval);
            }
            if (skill.Remaining <= 0f)
            {
                AddTacticalDamagePulse(skill.Position, config.explosionRadius,
                    config.explosionDamage * skill.DamageMultiplier, true, config.killLaunchHeight);
                SpawnExplosionVFX(new Vector3(skill.Position.x, renderHeight + 0.5f, skill.Position.y), config.explosionRadius);
                SpawnAOERing(new Vector3(skill.Position.x, renderHeight + 0.05f, skill.Position.y), config.explosionRadius, 0.45f,
                    new Color(0.72f, 0.18f, 1f, 1f));
                if (skill.Visual != null) Destroy(skill.Visual);
                _activeBlackHoleSkills.RemoveAt(i);
                continue;
            }
            _activeBlackHoleSkills[i] = skill;
        }
    }

    private void UpdateActiveMissileBarrageSkills(float dt)
    {
        RougeMissileBarrageTacticalSkillConfig config = tacticalSkillBalance.missileBarrage;
        float minimumInterval = Mathf.Max(0.01f, Mathf.Min(config.minimumInterval, config.maximumInterval));
        float maximumInterval = Mathf.Max(minimumInterval, Mathf.Max(config.minimumInterval, config.maximumInterval));
        for (int i = _activeMissileBarrageSkills.Count - 1; i >= 0; i--)
        {
            ActiveMissileBarrageSkill skill = _activeMissileBarrageSkills[i];
            skill.Remaining -= dt;
            skill.SpawnTimer -= dt;
            int catchUp = 0;
            while (skill.SpawnTimer <= 0f && skill.Remaining > 0f && catchUp < 4)
            {
                Vector2 random = UnityEngine.Random.insideUnitCircle * config.selectionRadius;
                Vector3 end = new Vector3(skill.Position.x + random.x, renderHeight + 0.12f,
                    skill.Position.y + random.y);
                float2 clamped = new float2(
                    Mathf.Clamp(end.x, -arenaHalfExtent, arenaHalfExtent),
                    Mathf.Clamp(end.z, -arenaHalfExtent, arenaHalfExtent));
                end.x = clamped.x;
                end.z = clamped.y;
                Vector2 drift = UnityEngine.Random.insideUnitCircle * 4f;
                Vector3 start = end + new Vector3(drift.x, Mathf.Max(1f, config.fallHeight), drift.y);
                _activeFallingMissiles.Add(new ActiveFallingMissile
                {
                    Start = start,
                    End = end,
                    Elapsed = 0f,
                    Duration = Mathf.Max(0.05f, config.fallDuration),
                    Visual = CreateFallingMissileVisual(start)
                });
                skill.SpawnTimer += UnityEngine.Random.Range(minimumInterval, maximumInterval);
                catchUp++;
            }
            if (skill.Remaining <= 0f) _activeMissileBarrageSkills.RemoveAt(i);
            else _activeMissileBarrageSkills[i] = skill;
        }
    }

    private void UpdateFallingMissiles(float dt)
    {
        RougeMissileBarrageTacticalSkillConfig config = tacticalSkillBalance.missileBarrage;
        for (int i = _activeFallingMissiles.Count - 1; i >= 0; i--)
        {
            ActiveFallingMissile missile = _activeFallingMissiles[i];
            missile.Elapsed += dt;
            float progress = Mathf.Clamp01(missile.Elapsed / Mathf.Max(0.05f, missile.Duration));
            float eased = progress * progress;
            if (missile.Visual != null)
            {
                missile.Visual.transform.position = Vector3.LerpUnclamped(missile.Start, missile.End, eased);
                float pulse = 0.85f + Mathf.Sin(Time.time * 28f + i) * 0.15f;
                missile.Visual.transform.localScale = Vector3.one * pulse;
            }
            if (progress < 1f)
            {
                _activeFallingMissiles[i] = missile;
                continue;
            }

            float2 impact = new float2(missile.End.x, missile.End.z);
            AddTacticalDamagePulse(impact, config.impactRadius, config.impactDamage, false, 0f);
            SpawnExplosionVFX(missile.End + Vector3.up * 0.35f, Mathf.Max(1.5f, config.impactRadius * 0.7f));
            SpawnAOERing(missile.End, config.impactRadius, 0.28f, new Color(1f, 0.32f, 0.06f, 1f));
            if (missile.Visual != null) Destroy(missile.Visual);
            _activeFallingMissiles.RemoveAt(i);
        }
    }

    private GameObject CreateFallingMissileVisual(Vector3 position)
    {
        GameObject root = new GameObject("Tactical Falling Missile");
        root.transform.position = position;
        root.AddComponent<RougeBillboard>();
        SpriteRenderer missile = RougeSpriteAssets.CreateRenderer("Missile Sprite", root.transform,
            RougeSpriteAssets.Load("Sprites/projectile_energy"), Vector3.zero, 0.7f, 90,
            new Color(1f, 0.38f, 0.06f, 1f));
        missile.transform.localScale = new Vector3(0.55f, 1.7f, 1f);
        return root;
    }

    private void AddTacticalDamagePulse(float2 position, float radius, float damage, bool launchKilled, float launchHeight)
    {
        TryAddSkillArea(new RougeSkillArea
        {
            Type = 17,
            Position = position,
            Radius = Mathf.Max(0f, radius),
            Damage = Mathf.Max(0f, damage),
            AuxA = launchKilled ? 1f : 0f,
            EffectLaunchHeight = Mathf.Max(0f, launchHeight),
            EffectLaunchLandingRadius = 0f
        });
    }

    private float2 AdvanceTacticalSkillUntilBlocked(float2 position, float2 direction, float distance, float padding, out bool blocked)
    {
        blocked = false;
        if (distance <= 0f || math.lengthsq(direction) <= 0.0001f) return position;
        direction = math.normalizesafe(direction);
        int steps = Mathf.Max(1, Mathf.CeilToInt(distance / 0.5f));
        float stepDistance = distance / steps;
        for (int i = 0; i < steps; i++)
        {
            float2 candidate = position + direction * stepDistance;
            if (math.abs(candidate.x) > arenaHalfExtent || math.abs(candidate.y) > arenaHalfExtent || IsTacticalPointBlocked(candidate, padding))
            {
                blocked = true;
                return position;
            }
            position = candidate;
        }
        return position;
    }

    private GameObject CreateWindmillVisual(float2 position, out Transform spinner)
    {
        GameObject root = new GameObject("Tactical Windmill Skill");
        root.transform.position = new Vector3(position.x, renderHeight + tacticalSkillBalance.windmill.fallHeight, position.y);
        root.AddComponent<RougeBillboard>();
        RougeSpriteAssets.CreateRenderer("Falling Hero", root.transform, RougeSpriteAssets.Load("Sprites/player_hero"),
            Vector3.zero, 1.05f, 75, Color.white);
        GameObject spin = new GameObject("Windmill Spinner");
        spin.transform.SetParent(root.transform, false);
        spinner = spin.transform;
        Sprite bladeSprite = RougeSpriteAssets.Load("Sprites/projectile_energy");
        for (int i = 0; i < 4; i++)
        {
            float angle = i * Mathf.PI * 0.5f;
            Vector3 offset = new Vector3(Mathf.Cos(angle) * 3.5f, Mathf.Sin(angle) * 3.5f, 0f);
            SpriteRenderer blade = RougeSpriteAssets.CreateRenderer("Energy Blade " + i, spin.transform, bladeSprite,
                offset, 0.8f, 74, new Color(0.15f, 0.9f, 1f, 0.95f));
            blade.transform.localRotation = Quaternion.Euler(0f, 0f, -angle * Mathf.Rad2Deg);
            blade.transform.localScale = new Vector3(2.8f, 0.55f, 1f);
        }
        spin.SetActive(false);
        return root;
    }

    private GameObject CreateBlackHoleVisual(float2 position)
    {
        GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        visual.name = "Tactical Black Hole";
        Collider collider = visual.GetComponent<Collider>();
        if (collider != null) Destroy(collider);
        visual.transform.position = new Vector3(position.x, renderHeight + 1.2f, position.y);
        Shader shader = Shader.Find("Custom/BlackHole");
        if (_tacticalBlackHoleMaterial == null)
        {
            _tacticalBlackHoleMaterial = shader != null
                ? new Material(shader) { name = "Tactical Black Hole Material" }
                : CreateRuntimeMaterial("Universal Render Pipeline/Unlit", "Tactical Black Hole", false);
            if (_tacticalBlackHoleMaterial.HasProperty("_HaloColor"))
                _tacticalBlackHoleMaterial.SetColor("_HaloColor", new Color(0.55f, 0.08f, 1f, 1f));
            ApplyBaseColor(_tacticalBlackHoleMaterial, new Color(0.08f, 0.01f, 0.14f, 1f));
        }
        visual.GetComponent<MeshRenderer>().sharedMaterial = _tacticalBlackHoleMaterial;
        visual.transform.localScale = Vector3.one * Mathf.Max(1f, tacticalSkillBalance.blackHole.pullRadius * 0.22f);
        return visual;
    }

    private void BuildTacticalSkillUi(Transform canvasTransform)
    {
        GameObject panel = CreateUiPanel("Tactical Skills Panel", canvasTransform, new Color(0.025f, 0.04f, 0.07f, 0.92f));
        RectTransform panelRect = panel.GetComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0.5f, 0f);
        panelRect.anchorMax = new Vector2(0.5f, 0f);
        panelRect.pivot = new Vector2(0.5f, 0f);
        panelRect.anchoredPosition = new Vector2(0f, 290f);
        panelRect.sizeDelta = new Vector2(790f, 126f);

        Text title = CreateUiText("Tactical Skills Title", panel.transform, 18, TextAnchor.UpperCenter);
        RectTransform titleRect = title.rectTransform;
        titleRect.anchorMin = new Vector2(0f, 1f);
        titleRect.anchorMax = new Vector2(1f, 1f);
        titleRect.pivot = new Vector2(0.5f, 1f);
        titleRect.anchoredPosition = new Vector2(0f, -5f);
        titleRect.sizeDelta = new Vector2(-20f, 28f);
        title.text = "TACTICAL SKILLS  |  RMB CANCEL";

        CreateTacticalSkillButton(panel.transform, 0, -292.5f, new Color(0.08f, 0.55f, 0.78f, 1f), BeginWindmillSkillSelection);
        CreateTacticalSkillButton(panel.transform, 1, -97.5f, new Color(0.42f, 0.08f, 0.68f, 1f), BeginBlackHoleSkillSelection);
        CreateTacticalSkillButton(panel.transform, 2, 97.5f, new Color(0.95f, 0.48f, 0.08f, 1f), BeginOverclockSkillSelection);
        CreateTacticalSkillButton(panel.transform, 3, 292.5f, new Color(0.72f, 0.16f, 0.06f, 1f), BeginMissileBarrageSkillSelection);
    }

    private void CreateTacticalSkillButton(Transform parent, int index, float x, Color color, UnityEngine.Events.UnityAction action)
    {
        Button button = CreateUiButton("Tactical Skill " + (index + 1), parent, string.Empty, color);
        RectTransform rect = button.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0f);
        rect.anchorMax = new Vector2(0.5f, 0f);
        rect.anchoredPosition = new Vector2(x, 14f);
        rect.sizeDelta = new Vector2(180f, 78f);
        if (action != null) button.onClick.AddListener(action);
        else button.interactable = false;
        _tacticalSkillButtons[index] = button;
        _tacticalSkillButtonTexts[index] = button.GetComponentInChildren<Text>();
    }

    private void RefreshTacticalSkillUi()
    {
        if (_tacticalSkillButtonTexts[0] != null)
        {
            _tacticalSkillButtonTexts[0].text = _windmillSkillCooldown > 0f
                ? $"WINDMILL\nCD {_windmillSkillCooldown:0.0}s"
                : $"WINDMILL\n${_windmillSkillCost}  DMG ×{_windmillDamageMultiplier:0.##}";
            bool available = _windmillSkillCooldown <= 0f && _towerDefenseGold >= _windmillSkillCost &&
                _tacticalSkillSelection != TacticalSkillSelectionState.WindmillDirection;
            SetPurchaseButtonAvailability(_tacticalSkillButtons[0], _tacticalSkillButtonTexts[0], available);
        }
        if (_tacticalSkillButtonTexts[1] != null)
        {
            _tacticalSkillButtonTexts[1].text = _blackHoleSkillCooldown > 0f
                ? $"BLACK HOLE\nCD {_blackHoleSkillCooldown:0.0}s"
                : $"BLACK HOLE\n${_blackHoleSkillCost}  DMG ×{_blackHoleDamageMultiplier:0.##}";
            bool available = _blackHoleSkillCooldown <= 0f && _towerDefenseGold >= _blackHoleSkillCost;
            SetPurchaseButtonAvailability(_tacticalSkillButtons[1], _tacticalSkillButtonTexts[1], available);
        }
        if (_tacticalSkillButtonTexts[2] != null)
        {
            _tacticalSkillButtonTexts[2].text = _overclockSkillCooldown > 0f
                ? $"OVERCLOCK\nCD {_overclockSkillCooldown:0.0}s"
                : $"OVERCLOCK\n${_overclockSkillCost}  ALL TOWERS";
            bool available = _overclockSkillCooldown <= 0f && _towerDefenseGold >= _overclockSkillCost &&
                _defenseTowers.Count > 0;
            SetPurchaseButtonAvailability(_tacticalSkillButtons[2], _tacticalSkillButtonTexts[2], available);
        }
        if (_tacticalSkillButtonTexts[3] != null)
        {
            _tacticalSkillButtonTexts[3].text = _missileBarrageSkillCooldown > 0f
                ? $"MISSILE RAIN\nCD {_missileBarrageSkillCooldown:0.0}s"
                : "MISSILE RAIN\nAREA R50  10s";
            bool available = _missileBarrageSkillCooldown <= 0f &&
                _tacticalSkillSelection != TacticalSkillSelectionState.MissileBarragePoint;
            SetPurchaseButtonAvailability(_tacticalSkillButtons[3], _tacticalSkillButtonTexts[3], available);
        }
    }

    private string GetTacticalSkillModeText()
    {
        return _tacticalSkillSelection switch
        {
            TacticalSkillSelectionState.WindmillPoint => "WINDMILL 1/2  |  CHOOSE IMPACT POINT  |  RED AREA IS INVALID",
            TacticalSkillSelectionState.WindmillDirection => "WINDMILL 2/2  |  CHOOSE TRAVEL DIRECTION",
            TacticalSkillSelectionState.BlackHolePoint => "BLACK HOLE  |  CHOOSE CENTER POINT",
            TacticalSkillSelectionState.MissileBarragePoint => "MISSILE RAIN  |  CHOOSE AREA CENTER  |  RADIUS 50",
            _ => string.Empty
        };
    }
}
