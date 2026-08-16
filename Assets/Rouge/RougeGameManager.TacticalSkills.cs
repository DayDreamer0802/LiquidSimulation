using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public partial class RougeGameManager
{
    private enum TacticalSkillSelectionState
    {
        None,
        WindmillPoint,
        WindmillDirection,
        BlackHolePoint,
        DimensionalSlashDraw
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

    private sealed class ActiveDimensionalSlashSkill
    {
        public float2[] Points;
        public int SegmentIndex;
        public float SegmentTimer;
        public float DamageMultiplier;
    }

    private struct ActiveDimensionalSlashVisual
    {
        public LineRenderer Renderer;
        public float Remaining;
        public float Duration;
    }

    private TacticalSkillSelectionState _tacticalSkillSelection;
    private float2 _tacticalSkillPoint;
    private bool _tacticalSkillPointValid;
    private int _windmillSkillCost;
    private int _blackHoleSkillCost;
    private int _dimensionalSlashSkillCost;
    private float _windmillSkillCooldown;
    private float _blackHoleSkillCooldown;
    private float _dimensionalSlashSkillCooldown;
    private float _windmillDamageMultiplier;
    private float _blackHoleDamageMultiplier;
    private float _dimensionalSlashDamageMultiplier;
    private readonly List<float2> _dimensionalSlashPoints = new List<float2>(9);
    private float _dimensionalSlashDrawnLength;
    private LineRenderer _tacticalSkillCircle;
    private LineRenderer _tacticalSkillDirection;
    private readonly List<ActiveWindmillSkill> _activeWindmillSkills = new List<ActiveWindmillSkill>();
    private readonly List<ActiveBlackHoleSkill> _activeBlackHoleSkills = new List<ActiveBlackHoleSkill>();
    private readonly List<ActiveDimensionalSlashSkill> _activeDimensionalSlashSkills = new List<ActiveDimensionalSlashSkill>();
    private readonly List<ActiveDimensionalSlashVisual> _activeDimensionalSlashVisuals = new List<ActiveDimensionalSlashVisual>();
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
        _dimensionalSlashSkillCost = Mathf.Max(0, tacticalSkillBalance.dimensionalSlash.initialCost);
        _windmillSkillCooldown = 0f;
        _blackHoleSkillCooldown = 0f;
        _dimensionalSlashSkillCooldown = 0f;
        _windmillDamageMultiplier = 1f;
        _blackHoleDamageMultiplier = 1f;
        _dimensionalSlashDamageMultiplier = 1f;
        _dimensionalSlashPoints.Clear();
        _dimensionalSlashDrawnLength = 0f;
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
        _activeDimensionalSlashSkills.Clear();
        for (int i = 0; i < _activeDimensionalSlashVisuals.Count; i++)
        {
            if (_activeDimensionalSlashVisuals[i].Renderer != null)
                Destroy(_activeDimensionalSlashVisuals[i].Renderer.gameObject);
        }
        _activeDimensionalSlashVisuals.Clear();
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

    private void BeginDimensionalSlashSkillSelection()
    {
        if (_towerDefenseGameOver || _dimensionalSlashSkillCooldown > 0f ||
            _towerDefenseGold < _dimensionalSlashSkillCost) return;
        _dimensionalSlashPoints.Clear();
        _dimensionalSlashDrawnLength = 0f;
        BeginTacticalSkillSelection(TacticalSkillSelectionState.DimensionalSlashDraw);
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
        _dimensionalSlashPoints.Clear();
        _dimensionalSlashDrawnLength = 0f;
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
        if (_tacticalSkillSelection == TacticalSkillSelectionState.DimensionalSlashDraw)
        {
            return UpdateDimensionalSlashDrawing(mouse, pointerOverUi, hasPoint, worldPoint);
        }
        float radius = _tacticalSkillSelection == TacticalSkillSelectionState.BlackHolePoint
            ? tacticalSkillBalance.blackHole.pullRadius
            : tacticalSkillBalance.windmill.impactRadius;
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
        }
        return true;
    }

    private bool UpdateDimensionalSlashDrawing(Mouse mouse, bool pointerOverUi, bool hasPoint, Vector3 worldPoint)
    {
        RougeDimensionalSlashTacticalSkillConfig config = tacticalSkillBalance.dimensionalSlash;
        float totalLength = Mathf.Max(10f, config.totalLength);
        float minimumLength = Mathf.Clamp(config.minimumSegmentLength, 0.1f, totalLength);
        float2 cursor = new float2(worldPoint.x, worldPoint.z);
        bool valid = hasPoint && IsValidTacticalSkillPoint(cursor, false);
        float2 previewPoint = cursor;

        if (_dimensionalSlashPoints.Count == 0)
        {
            if (_tacticalSkillDirection != null) _tacticalSkillDirection.enabled = false;
        }
        else
        {
            float2 start = _dimensionalSlashPoints[_dimensionalSlashPoints.Count - 1];
            float2 delta = cursor - start;
            float rawLength = math.length(delta);
            float remaining = Mathf.Max(0f, totalLength - _dimensionalSlashDrawnLength);
            float previewLength = Mathf.Min(rawLength, remaining);
            float2 direction = math.normalizesafe(delta, new float2(0f, 1f));
            previewPoint = start + direction * previewLength;
            float leftover = remaining - previewLength;
            bool leavesUsableSegment = leftover <= 0.02f || leftover + 0.001f >= minimumLength;
            valid = hasPoint && rawLength + 0.001f >= minimumLength &&
                previewLength + 0.001f >= minimumLength && leavesUsableSegment &&
                IsValidTacticalSkillPoint(previewPoint, false);
            UpdateDimensionalSlashIndicator(previewPoint, valid);
        }

        _tacticalSkillPointValid = valid;
        Color color = valid ? new Color(0.72f, 0.25f, 1f, 1f) : new Color(1f, 0.12f, 0.18f, 1f);
        TowerDefenseVisuals.UpdateCircle(_tacticalSkillCircle,
            new Vector3(previewPoint.x, renderHeight, previewPoint.y), config.aoeRadius, color, hasPoint);

        if (!mouse.leftButton.wasPressedThisFrame || pointerOverUi || !valid) return true;
        if (_dimensionalSlashPoints.Count == 0)
        {
            _dimensionalSlashPoints.Add(cursor);
            UpdateDimensionalSlashIndicator(cursor, true);
            RefreshTowerDefenseUi(true);
            return true;
        }

        float2 previous = _dimensionalSlashPoints[_dimensionalSlashPoints.Count - 1];
        float acceptedLength = math.distance(previous, previewPoint);
        _dimensionalSlashPoints.Add(previewPoint);
        _dimensionalSlashDrawnLength += acceptedLength;
        if (_dimensionalSlashDrawnLength + 0.02f >= totalLength)
        {
            CastDimensionalSlashSkill();
        }
        else
        {
            UpdateDimensionalSlashIndicator(previewPoint, true);
            RefreshTowerDefenseUi(true);
        }
        return true;
    }

    private void UpdateDimensionalSlashIndicator(float2 previewPoint, bool valid)
    {
        if (_tacticalSkillDirection == null || _dimensionalSlashPoints.Count == 0) return;
        RougeDimensionalSlashTacticalSkillConfig config = tacticalSkillBalance.dimensionalSlash;
        _tacticalSkillDirection.enabled = true;
        _tacticalSkillDirection.positionCount = _dimensionalSlashPoints.Count + 1;
        Color color = valid ? new Color(0.72f, 0.25f, 1f, 1f) : new Color(1f, 0.12f, 0.18f, 1f);
        _tacticalSkillDirection.startColor = color;
        _tacticalSkillDirection.endColor = color;
        _tacticalSkillDirection.widthMultiplier = Mathf.Max(0.04f, config.visualWidth);
        for (int i = 0; i < _dimensionalSlashPoints.Count; i++)
        {
            float2 point = _dimensionalSlashPoints[i];
            _tacticalSkillDirection.SetPosition(i, new Vector3(point.x, renderHeight + 0.35f, point.y));
        }
        _tacticalSkillDirection.SetPosition(_dimensionalSlashPoints.Count,
            new Vector3(previewPoint.x, renderHeight + 0.35f, previewPoint.y));
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

    private void CastDimensionalSlashSkill()
    {
        if (_dimensionalSlashPoints.Count < 2 || _dimensionalSlashSkillCooldown > 0f ||
            _towerDefenseGold < _dimensionalSlashSkillCost) return;
        _towerDefenseGold -= _dimensionalSlashSkillCost;
        _dimensionalSlashSkillCost = GetNextTacticalSkillCost(_dimensionalSlashSkillCost,
            tacticalSkillBalance.dimensionalSlash.costMultiplier);
        _dimensionalSlashSkillCooldown = tacticalSkillBalance.dimensionalSlash.cooldown;
        _activeDimensionalSlashSkills.Add(new ActiveDimensionalSlashSkill
        {
            Points = _dimensionalSlashPoints.ToArray(),
            SegmentIndex = 0,
            SegmentTimer = 0f,
            DamageMultiplier = _dimensionalSlashDamageMultiplier
        });
        _dimensionalSlashDamageMultiplier *= GetNextTacticalDamageMultiplier();
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
        _dimensionalSlashSkillCooldown = Mathf.Max(0f, _dimensionalSlashSkillCooldown - dt);
        UpdateActiveWindmillSkills(dt);
        UpdateActiveBlackHoleSkills(dt);
        UpdateActiveDimensionalSlashSkills(dt);
        UpdateDimensionalSlashVisuals(dt);
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

    private void UpdateActiveDimensionalSlashSkills(float dt)
    {
        RougeDimensionalSlashTacticalSkillConfig config = tacticalSkillBalance.dimensionalSlash;
        for (int i = _activeDimensionalSlashSkills.Count - 1; i >= 0; i--)
        {
            ActiveDimensionalSlashSkill skill = _activeDimensionalSlashSkills[i];
            skill.SegmentTimer -= dt;
            while (skill.SegmentTimer <= 0f && skill.SegmentIndex < skill.Points.Length - 1)
            {
                float2 start = skill.Points[skill.SegmentIndex];
                float2 end = skill.Points[skill.SegmentIndex + 1];
                AddTacticalLineDamage(start, end, config.aoeRadius,
                    config.damage * skill.DamageMultiplier);
                CreateDimensionalSlashVisual(start, end, config);
                skill.SegmentIndex++;
                skill.SegmentTimer += Mathf.Max(0.01f, config.segmentInterval);
            }
            if (skill.SegmentIndex >= skill.Points.Length - 1)
                _activeDimensionalSlashSkills.RemoveAt(i);
        }
    }

    private void AddTacticalLineDamage(float2 start, float2 end, float radius, float damage)
    {
        float2 delta = end - start;
        float length = math.length(delta);
        if (length <= 0.001f) return;
        TryAddSkillArea(new RougeSkillArea
        {
            Type = 19,
            Position = start,
            Direction = delta / length,
            Length = length,
            Radius = Mathf.Max(0.1f, radius),
            Damage = Mathf.Max(0f, damage)
        });
    }

    private void CreateDimensionalSlashVisual(float2 start, float2 end,
        RougeDimensionalSlashTacticalSkillConfig config)
    {
        LineRenderer line = TowerDefenseVisuals.CreateBeamRenderer("Dimensional Slash", transform,
            Mathf.Max(0.035f, config.visualWidth));
        line.sharedMaterial = TowerDefenseVisuals.GetDimensionalSlashMaterial();
        line.sortingOrder = 32020;
        line.textureMode = LineTextureMode.Stretch;
        line.numCapVertices = 2;
        line.positionCount = 2;
        line.SetPosition(0, new Vector3(start.x, renderHeight + 0.65f, start.y));
        line.SetPosition(1, new Vector3(end.x, renderHeight + 0.65f, end.y));
        Color color = new Color(0.78f, 0.24f, 1f, 1f);
        line.startColor = color;
        line.endColor = new Color(0.18f, 0.9f, 1f, 1f);
        float duration = Mathf.Max(0.01f, config.visualDuration);
        _activeDimensionalSlashVisuals.Add(new ActiveDimensionalSlashVisual
        {
            Renderer = line,
            Remaining = duration,
            Duration = duration
        });
    }

    private void UpdateDimensionalSlashVisuals(float dt)
    {
        for (int i = _activeDimensionalSlashVisuals.Count - 1; i >= 0; i--)
        {
            ActiveDimensionalSlashVisual visual = _activeDimensionalSlashVisuals[i];
            visual.Remaining -= dt;
            if (visual.Renderer == null || visual.Remaining <= 0f)
            {
                if (visual.Renderer != null) Destroy(visual.Renderer.gameObject);
                _activeDimensionalSlashVisuals.RemoveAt(i);
                continue;
            }
            float alpha = Mathf.Clamp01(visual.Remaining / Mathf.Max(0.01f, visual.Duration));
            visual.Renderer.startColor = new Color(0.78f, 0.24f, 1f, alpha);
            visual.Renderer.endColor = new Color(0.18f, 0.9f, 1f, alpha);
            _activeDimensionalSlashVisuals[i] = visual;
        }
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
        CreateTacticalSkillButton(panel.transform, 2, 97.5f, new Color(0.58f, 0.12f, 0.72f, 1f), BeginDimensionalSlashSkillSelection);
        CreateTacticalSkillButton(panel.transform, 3, 292.5f, new Color(0.14f, 0.17f, 0.22f, 1f), null);
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
            _tacticalSkillButtons[0].interactable = _windmillSkillCooldown <= 0f && _towerDefenseGold >= _windmillSkillCost &&
                _tacticalSkillSelection != TacticalSkillSelectionState.WindmillDirection;
        }
        if (_tacticalSkillButtonTexts[1] != null)
        {
            _tacticalSkillButtonTexts[1].text = _blackHoleSkillCooldown > 0f
                ? $"BLACK HOLE\nCD {_blackHoleSkillCooldown:0.0}s"
                : $"BLACK HOLE\n${_blackHoleSkillCost}  DMG ×{_blackHoleDamageMultiplier:0.##}";
            _tacticalSkillButtons[1].interactable = _blackHoleSkillCooldown <= 0f && _towerDefenseGold >= _blackHoleSkillCost;
        }
        if (_tacticalSkillButtonTexts[2] != null)
        {
            _tacticalSkillButtonTexts[2].text = _dimensionalSlashSkillCooldown > 0f
                ? $"DIMENSION SLASH\nCD {_dimensionalSlashSkillCooldown:0.0}s"
                : $"DIMENSION SLASH\n${_dimensionalSlashSkillCost}  DMG ×{_dimensionalSlashDamageMultiplier:0.##}";
            _tacticalSkillButtons[2].interactable = _dimensionalSlashSkillCooldown <= 0f &&
                _towerDefenseGold >= _dimensionalSlashSkillCost;
        }
        if (_tacticalSkillButtonTexts[3] != null) _tacticalSkillButtonTexts[3].text = "LOCKED\nSKILL IV";
    }

    private string GetTacticalSkillModeText()
    {
        return _tacticalSkillSelection switch
        {
            TacticalSkillSelectionState.WindmillPoint => "WINDMILL 1/2  |  CHOOSE IMPACT POINT  |  RED AREA IS INVALID",
            TacticalSkillSelectionState.WindmillDirection => "WINDMILL 2/2  |  CHOOSE TRAVEL DIRECTION",
            TacticalSkillSelectionState.BlackHolePoint => "BLACK HOLE  |  CHOOSE CENTER POINT",
            TacticalSkillSelectionState.DimensionalSlashDraw =>
                $"DIMENSION SLASH  |  DRAW {_dimensionalSlashDrawnLength:0.#}/{tacticalSkillBalance.dimensionalSlash.totalLength:0.#}m  |  EACH SEGMENT ≥ {tacticalSkillBalance.dimensionalSlash.minimumSegmentLength:0.#}m",
            _ => string.Empty
        };
    }
}
