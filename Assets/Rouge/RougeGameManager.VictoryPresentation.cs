using System.Collections;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.UI;

public partial class RougeGameManager
{
    private bool _towerDefenseVictorySequenceActive;
    private bool _towerDefenseVictoryResultReady;
    private bool _towerDefenseVictoryHadBoss;
    private bool _towerDefenseVictoryBossDefeatedForScore;
    private bool _towerDefenseVictoryPresentationDisposing;
    private Coroutine _towerDefenseVictoryRoutine;
    private Canvas _towerDefenseVictoryCanvas;
    private CanvasGroup _towerDefenseVictoryDialogueGroup;
    private CanvasGroup _towerDefenseVictoryResultGroup;
    private RectTransform _towerDefenseVictoryResultCard;
    private Image _towerDefenseVictoryPortrait;
    private Text _towerDefenseVictoryDialogueText;
    private Text _towerDefenseVictoryTitleText;
    private Text _towerDefenseVictoryGradeText;
    private Text _towerDefenseVictoryScoreText;
    private Button _towerDefenseVictoryReturnButton;
    private string _towerDefenseVictoryDialogue = string.Empty;
    private string _towerDefenseVictoryScoreBreakdown = string.Empty;
    private readonly List<LineRenderer> _towerDefenseVictoryRings =
        new List<LineRenderer>();

    private void InitializeTowerDefenseVictoryPresentation()
    {
        _towerDefenseVictorySequenceActive = false;
        _towerDefenseVictoryResultReady = false;
        _towerDefenseVictoryHadBoss = false;
        _towerDefenseVictoryBossDefeatedForScore = false;
        _towerDefenseVictoryPresentationDisposing = false;
        _towerDefenseVictoryDialogue = string.Empty;
        _towerDefenseVictoryScoreBreakdown = string.Empty;
    }

    private void DisposeTowerDefenseVictoryPresentation()
    {
        _towerDefenseVictoryPresentationDisposing = true;
        RougeTowerDefenseMapLoader.Active?.CancelVictoryRecall(true);
        if (_towerDefenseVictoryRoutine != null)
            StopCoroutine(_towerDefenseVictoryRoutine);
        _towerDefenseVictoryRoutine = null;
        if (_towerDefenseVictoryCanvas != null)
            Destroy(_towerDefenseVictoryCanvas.gameObject);
        _towerDefenseVictoryCanvas = null;
        _towerDefenseVictoryDialogueGroup = null;
        _towerDefenseVictoryResultGroup = null;
        _towerDefenseVictoryResultCard = null;
        _towerDefenseVictoryPortrait = null;
        _towerDefenseVictoryDialogueText = null;
        _towerDefenseVictoryTitleText = null;
        _towerDefenseVictoryGradeText = null;
        _towerDefenseVictoryScoreText = null;
        _towerDefenseVictoryReturnButton = null;
        for (int i = 0; i < _defenseTowers.Count; i++)
        {
            RougeDefenseTower tower = _defenseTowers[i];
            if (tower == null) continue;
            RougeTowerConstructionEffect dissolve =
                tower.GetComponent<RougeTowerConstructionEffect>();
            if (dissolve != null) dissolve.CancelVictoryDissolve(true);
        }
        ClearTowerDefenseVictoryRings();
        _towerDefenseVictorySequenceActive = false;
        _towerDefenseVictoryResultReady = false;
        _towerDefenseVictoryPresentationDisposing = false;
        RougeCameraFollow follow = RougeCameraFollow.ResolveCamera()?.
            GetComponent<RougeCameraFollow>();
        if (follow != null) follow.SetCinematicShake(0f);
    }

    private void BeginTowerDefenseVictoryPresentation(string reason)
    {
        if (_towerDefenseVictorySequenceActive || _towerDefenseGameOver) return;

        StopTowerDefenseAutoplayForConclusion();
        if (_cameraViewMode != CameraViewMode.Default) ExitDebugUnitView();
        _towerDefenseVictory = true;
        _towerDefenseGameOver = true;
        _towerDefenseGameOverReason = string.IsNullOrWhiteSpace(reason)
            ? "胜利条件已达成"
            : reason;
        _towerDefenseVictoryHadBoss = _bossDeathExplosionTriggered ||
            _bossSpriteAnimator != null;
        _towerDefenseVictoryBossDefeatedForScore =
            _bossDeathExplosionTriggered && _bossEnemyIndex < 0 &&
            _bossCurrentHealth <= 0.001f;
        _towerDefenseVictorySequenceActive = true;
        _towerDefenseVictoryResultReady = false;

        HideTowerDefenseSpawnWarnings();
        StopAllTowerAttackSounds();
        ClearTowerDefenseCombatPresentationForVictory();
        _towerPlacementMode = false;
        TowerDefenseBuildModeActive = false;
        ClearTowerRelocationState();
        SetTowerPlaceVisualsVisible(false);
        RefreshTowerEditHints();
        if (player != null) player.SuppressMovement = true;
        if (_towerPreview != null) _towerPreview.gameObject.SetActive(false);
        Time.timeScale = 0f;

        try
        {
            _towerDefenseVictoryDialogue =
                PickTowerDefenseAutoplayVictoryLine();
            CaptureTowerDefenseVictoryScore();
            BuildTowerDefenseVictoryUi();
            PrepareTowerDefenseVictoryView();
            _towerDefenseVictoryRoutine = StartCoroutine(
                PlayTowerDefenseVictoryPresentation());
        }
        catch (System.Exception exception)
        {
            Debug.LogException(exception, this);
            if (_towerDefenseVictoryCanvas != null)
                Destroy(_towerDefenseVictoryCanvas.gameObject);
            _towerDefenseVictoryCanvas = null;
            _towerDefenseVictorySequenceActive = false;
            _towerDefenseVictoryResultReady = true;
            if (_towerDefenseCanvas != null)
                _towerDefenseCanvas.gameObject.SetActive(true);
            RefreshTowerDefenseUi(true);
        }
    }

    private void PrepareTowerDefenseVictoryView()
    {
        if (IsTiltShiftObservationActive)
            ForceTiltShiftObservationExit(CameraViewMode.Default);
        else if (_cameraViewMode != CameraViewMode.Default)
            ForceCameraViewMode(CameraViewMode.Default);

        if (_towerDefenseCanvas != null)
            _towerDefenseCanvas.gameObject.SetActive(false);
        if (_towerDefenseAutoplayCanvas != null)
            _towerDefenseAutoplayCanvas.gameObject.SetActive(false);
        if (_towerDefenseLevelEventCanvas != null)
            _towerDefenseLevelEventCanvas.gameObject.SetActive(false);
        HideF2MainTowerHealth();

        RougeCameraFollow follow = RougeCameraFollow.ResolveCamera()?.
            GetComponent<RougeCameraFollow>();
        if (follow == null) return;
        RougeCameraFollow.ViewState victoryView =
            ResolveCameraViewPreset(CameraViewMode.TiltShift, follow);
        follow.CancelScriptedView();
        follow.ApplyViewState(victoryView);
        follow.SetCinematicShake(0.025f);
    }

    private void ClearTowerDefenseCombatPresentationForVictory()
    {
        if (_mainTowerDamageCount.IsCreated) _mainTowerDamageCount[0] = 0;
        if (_playerDamageCount.IsCreated) _playerDamageCount[0] = 0;
        _activeBulletCount = 0;
        _skillAreaCount = 0;
        _pendingSkillAreas.Clear();

        for (int i = 0; i < _towerProjectiles.Count; i++)
            if (_towerProjectiles[i].Visual != null)
                Destroy(_towerProjectiles[i].Visual);
        _towerProjectiles.Clear();
        for (int i = 0; i < _towerPersistentCannonZones.Count; i++)
            if (_towerPersistentCannonZones[i].Visual != null)
                Destroy(_towerPersistentCannonZones[i].Visual);
        _towerPersistentCannonZones.Clear();
        for (int i = 0; i < _towerFireZones.Count; i++)
            if (_towerFireZones[i].Visual != null)
                Destroy(_towerFireZones[i].Visual);
        _towerFireZones.Clear();
        for (int i = 0; i < _towerFlameJetVisuals.Count; i++)
            if (_towerFlameJetVisuals[i].Root != null)
                Destroy(_towerFlameJetVisuals[i].Root);
        _towerFlameJetVisuals.Clear();
        for (int i = 0; i < _towerBeamVisuals.Count; i++)
            DestroyTowerBeamVisual(_towerBeamVisuals[i]);
        _towerBeamVisuals.Clear();
        for (int i = 0; i < _activeOrbitSphereAttacks.Count; i++)
            _activeOrbitSphereAttacks[i].Positions = null;
        _activeOrbitSphereAttacks.Clear();
        for (int i = 0; i < _iceSpikeVisuals.Count; i++)
            if (_iceSpikeVisuals[i].Root != null)
                Destroy(_iceSpikeVisuals[i].Root);
        _iceSpikeVisuals.Clear();
        _iceSpikeCandidateCells.Clear();
        _activeRocketBarrageSalvos.Clear();
        _activeRocketBarrageMissiles.Clear();
        for (int i = 0; i < _defenseTowers.Count; i++)
            if (_defenseTowers[i] != null)
                _defenseTowers[i].HideLaserBeams();
        _towerTargetRequestCount = 0;
        _towerTargetScheduledCount = 0;
    }

    private IEnumerator PlayTowerDefenseVictoryPresentation()
    {
        bool completed = false;
        try
        {
            yield return PlayTowerDefenseVictoryPresentationCore();
            completed = true;
        }
        finally
        {
            ClearTowerDefenseVictoryRings();
            RougeCameraFollow follow = RougeCameraFollow.ResolveCamera()?.
                GetComponent<RougeCameraFollow>();
            if (follow != null)
            {
                follow.EndCinematicFocus();
                follow.SetCinematicShake(0f);
            }
            if (!_towerDefenseVictoryPresentationDisposing)
            {
                // A missing runtime object or third-party shader must never leave the
                // run frozen behind a result gate that can no longer be reached.
                if (!completed && _towerDefenseVictoryResultGroup != null)
                {
                    _towerDefenseVictoryResultGroup.alpha = 1f;
                    _towerDefenseVictoryResultGroup.blocksRaycasts = true;
                }
                if (_towerDefenseVictoryReturnButton != null)
                    _towerDefenseVictoryReturnButton.interactable = true;
                _towerDefenseVictoryResultReady = true;
                _towerDefenseVictorySequenceActive = false;
                _towerDefenseVictoryRoutine = null;
            }
        }
    }

    private IEnumerator PlayTowerDefenseVictoryPresentationCore()
    {
        Vector3 anchor = mainTower != null
            ? mainTower.transform.position
            : Vector3.zero;
        RougeCameraFollow follow = RougeCameraFollow.ResolveCamera()?.
            GetComponent<RougeCameraFollow>();

        // Boss victories already arrive here after the dedicated shatter and its
        // three outward kill waves. Maps without a Boss receive an equally readable
        // base-origin super pulse before the shared teardown begins.
        if (!_towerDefenseVictoryHadBoss)
            yield return PlayTowerDefenseBaseVictoryShockwave(anchor, follow);
        else
        {
            if (_bossSpriteAnimator != null && !_bossDeathExplosionTriggered)
                yield return PlayTowerDefenseActiveBossVictoryShatter(follow);
            yield return PlayTowerDefenseRemainingEnemyPurge(
                _bossWorldPosition, follow, 0.72f);
        }

        yield return PlayTowerDefenseVictoryTowerDissolve(anchor, follow);

        RougeTowerDefenseMapLoader loader = RougeTowerDefenseMapLoader.Active;
        if (loader != null && loader.CanPlayVictoryRecall)
            yield return loader.PlayVictoryRecallToAnchor(
                anchor, mainTower, 2.1f, 0.88f);
        else
            yield return PlayTowerDefenseVictoryBaseFallback(anchor, follow);

        if (follow != null)
        {
            follow.EndCinematicFocus();
            follow.SetCinematicShake(0f);
        }
        yield return RevealTowerDefenseVictoryResult();
    }

    private IEnumerator PlayTowerDefenseActiveBossVictoryShatter(
        RougeCameraFollow follow)
    {
        Vector3 origin = _bossWorldPosition;
        if (_bossSpriteAnimator != null) _bossSpriteAnimator.BeginDeath();
        float elapsed = 0f;
        const float shatterTime = 0.82f;
        while (elapsed < shatterTime)
        {
            elapsed += Mathf.Min(Time.unscaledDeltaTime, 1f / 30f);
            float progress = Mathf.Clamp01(elapsed / shatterTime);
            if (_bossSpriteAnimator != null)
                _bossSpriteAnimator.SetDeathShake(progress);
            if (follow != null)
            {
                follow.BeginCinematicFocus(origin);
                follow.SetCinematicShake(Mathf.Lerp(0.04f, 0.28f,
                    progress * progress));
            }
            yield return null;
        }

        if (_bossSpriteAnimator != null)
            _bossSpriteAnimator.ExplodeIntoShards(8.8f);
        _bossDeathExplosionTriggered = true;
        LineRenderer ring = CreateTowerDefenseVictoryRing(
            "Boss Victory Shatter", 32404);
        _towerDefenseVictoryRings.Add(ring);
        elapsed = 0f;
        const float burstDuration = 0.68f;
        while (elapsed < burstDuration)
        {
            elapsed += Mathf.Min(Time.unscaledDeltaTime, 1f / 30f);
            float progress = Mathf.Clamp01(elapsed / burstDuration);
            float radius = Mathf.Lerp(1f, Mathf.Max(36f, bossBalance.radius * 4f),
                1f - Mathf.Pow(1f - progress, 3f));
            Color color = Color.Lerp(
                new Color(1.35f, 0.52f, 0.08f, 1f),
                new Color(0.32f, 0.92f, 1.42f, 0f), progress);
            TowerDefenseVisuals.UpdateCircle(ring,
                origin + Vector3.up * 0.18f, radius, color,
                progress < 0.998f);
            if (follow != null)
                follow.SetCinematicShake(Mathf.Lerp(0.62f, 0.08f, progress));
            yield return null;
        }
        ClearTowerDefenseVictoryRings();
        ReleaseDefeatedBossSlot();
        _bossEnemyIndex = -1;
        _bossCurrentHealth = 0f;
        _bossSpawned = false;
        _activeBossEncounter = null;
        if (_bossSpriteAnimator != null)
            Destroy(_bossSpriteAnimator.gameObject);
        _bossSpriteAnimator = null;
    }

    private IEnumerator PlayTowerDefenseBaseVictoryShockwave(Vector3 anchor,
        RougeCameraFollow follow)
    {
        Transform baseTransform = mainTower != null ? mainTower.transform : null;
        Vector3 originalScale = baseTransform != null
            ? baseTransform.localScale
            : Vector3.one;
        try
        {
            const float chargeDuration = 0.62f;
            float elapsed = 0f;
            while (elapsed < chargeDuration)
            {
                elapsed += Mathf.Min(Time.unscaledDeltaTime, 1f / 30f);
                float progress = Mathf.Clamp01(elapsed / chargeDuration);
                float pulse = Mathf.Sin(progress * Mathf.PI * 3f) *
                              (0.025f + progress * 0.055f);
                if (baseTransform != null)
                    baseTransform.localScale = originalScale * (1f + pulse);
                if (follow != null)
                {
                    follow.BeginCinematicFocus(anchor);
                    follow.SetCinematicShake(Mathf.Lerp(0.02f, 0.16f,
                        progress * progress));
                }
                yield return null;
            }

            ClearTowerDefenseVictoryRings();
            for (int i = 0; i < 3; i++)
                _towerDefenseVictoryRings.Add(CreateTowerDefenseVictoryRing(
                    "Base Super Pulse " + (i + 1), 32400 + i));

            float maxRadius = Mathf.Max(150f, arenaHalfExtent * 3.2f);
            const float waveDuration = 1.62f;
            elapsed = 0f;
            while (elapsed < waveDuration)
            {
                float dt = Mathf.Min(Time.unscaledDeltaTime, 1f / 30f);
                elapsed += dt;
                float leadingRadius = 0f;
                for (int i = 0; i < _towerDefenseVictoryRings.Count; i++)
                {
                    float local = Mathf.Clamp01((elapsed - i * 0.16f) / 1.05f);
                    float eased = 1f - Mathf.Pow(1f - local, 3f);
                    float radius = Mathf.Lerp(0.4f, maxRadius, eased);
                    leadingRadius = Mathf.Max(leadingRadius, radius);
                    Color color = Color.Lerp(
                        new Color(0.18f, 1.18f, 1.65f, 1f),
                        new Color(0.72f, 0.24f, 1.2f, 0f), local);
                    TowerDefenseVisuals.UpdateCircle(
                        _towerDefenseVictoryRings[i],
                        anchor + Vector3.up * (0.12f + i * 0.09f),
                        radius, color, local < 0.998f);
                }
                EliminateTowerDefenseVictoryEnemies(anchor, leadingRadius, false);
                RenderEnemies();
                if (follow != null)
                    follow.SetCinematicShake(Mathf.Lerp(0.58f, 0.025f,
                        Mathf.Clamp01(elapsed / waveDuration)));
                yield return null;
            }
            EliminateTowerDefenseVictoryEnemies(anchor, float.MaxValue, true);
            RenderEnemies();
        }
        finally
        {
            if (baseTransform != null) baseTransform.localScale = originalScale;
            ClearTowerDefenseVictoryRings();
        }
    }

    private IEnumerator PlayTowerDefenseRemainingEnemyPurge(Vector3 origin,
        RougeCameraFollow follow, float duration)
    {
        duration = Mathf.Max(0.3f, duration);
        LineRenderer ring = CreateTowerDefenseVictoryRing(
            "Victory Residual Purge", 32405);
        _towerDefenseVictoryRings.Add(ring);
        float maxRadius = Mathf.Max(150f, arenaHalfExtent * 3.2f);
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Mathf.Min(Time.unscaledDeltaTime, 1f / 30f);
            float progress = Mathf.Clamp01(elapsed / duration);
            float radius = Mathf.Lerp(3f, maxRadius,
                1f - Mathf.Pow(1f - progress, 3f));
            Color color = Color.Lerp(
                new Color(1.2f, 0.62f, 0.12f, 0.86f),
                new Color(0.18f, 0.92f, 1.42f, 0f), progress);
            TowerDefenseVisuals.UpdateCircle(ring,
                origin + Vector3.up * 0.12f, radius, color,
                progress < 0.998f);
            EliminateTowerDefenseVictoryEnemies(origin, radius, false);
            RenderEnemies();
            if (follow != null)
                follow.SetCinematicShake(Mathf.Sin(progress * Mathf.PI) * 0.08f);
            yield return null;
        }
        EliminateTowerDefenseVictoryEnemies(origin, float.MaxValue, true);
        RenderEnemies();
        ClearTowerDefenseVictoryRings();
    }

    private void EliminateTowerDefenseVictoryEnemies(Vector3 origin,
        float radius, bool eliminateAll)
    {
        if (!_stateA.IsCreated || !_positionsA.IsCreated) return;
        float radiusSq = radius >= 100000f
            ? float.MaxValue
            : radius * radius;
        int removed = 0;
        int limit = Mathf.Min(_currentMaxEnemies, _stateA.Length);
        for (int i = 0; i < limit; i++)
        {
            if (i == _bossEnemyIndex) continue;
            float4 state = _stateA[i];
            if (state.x <= 0f) continue;
            float4 position = _positionsA[i];
            float dx = position.x - origin.x;
            float dz = position.z - origin.z;
            if (!eliminateAll && dx * dx + dz * dz > radiusSq) continue;
            state.x = 0f;
            state.w = 20.99f;
            position.y = -1000f;
            _stateA[i] = state;
            _positionsA[i] = position;
            if (_stateB.IsCreated && i < _stateB.Length) _stateB[i] = state;
            if (_positionsB.IsCreated && i < _positionsB.Length)
                _positionsB[i] = position;
            if (_towerDefenseEnemyKinds.IsCreated &&
                i < _towerDefenseEnemyKinds.Length)
                _towerDefenseEnemyKinds[i] = 0;
            if (_enemyRenderKinds.IsCreated && i < _enemyRenderKinds.Length)
                _enemyRenderKinds[i] = 0;
            removed++;
        }
        _towerDefenseAliveEstimate = Mathf.Max(0,
            _towerDefenseAliveEstimate - removed);
    }

    private IEnumerator PlayTowerDefenseVictoryTowerDissolve(Vector3 anchor,
        RougeCameraFollow follow)
    {
        float farthest = 0.01f;
        for (int i = 0; i < _defenseTowers.Count; i++)
        {
            RougeDefenseTower tower = _defenseTowers[i];
            if (tower == null || !tower.gameObject.activeInHierarchy) continue;
            Vector3 delta = tower.transform.position - anchor;
            delta.y = 0f;
            farthest = Mathf.Max(farthest, delta.magnitude);
        }

        RougeTowerDefenseMap map = RougeTowerDefenseMapLoader.ActiveMap;
        float cellSize = map != null ? map.CellSize : 8f;
        float maximumDelay = 0f;
        for (int i = 0; i < _defenseTowers.Count; i++)
        {
            RougeDefenseTower tower = _defenseTowers[i];
            if (tower == null || !tower.gameObject.activeInHierarchy) continue;
            tower.StopAttackSounds();
            StopPiercingLaserAttacksForTower(tower);
            StopOrbitSphereAttacksForTower(tower);
            Vector3 delta = tower.transform.position - anchor;
            delta.y = 0f;
            float normalizedDistance = Mathf.Clamp01(delta.magnitude / farthest);
            // Outer defenses answer the recall first; the wave then closes on the base.
            float delay = (1f - normalizedDistance) * 0.62f;
            maximumDelay = Mathf.Max(maximumDelay, delay);
            RougeTowerConstructionEffect.PlayVictoryDissolve(tower,
                hologramShader, cellSize, delay);
        }

        LineRenderer recallRing = CreateTowerDefenseVictoryRing(
            "Tower Lattice Recall", 32410);
        _towerDefenseVictoryRings.Add(recallRing);
        float duration = RougeTowerConstructionEffect.VictoryDissolveDuration +
                         maximumDelay + 0.14f;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Mathf.Min(Time.unscaledDeltaTime, 1f / 30f);
            float progress = Mathf.Clamp01(elapsed / duration);
            float radius = Mathf.Lerp(farthest + cellSize, 0.25f,
                Mathf.SmoothStep(0f, 1f, progress));
            Color color = Color.Lerp(
                new Color(0.72f, 0.28f, 1.25f, 0.72f),
                new Color(0.12f, 1.08f, 1.55f, 0f), progress);
            TowerDefenseVisuals.UpdateCircle(recallRing,
                anchor + Vector3.up * 0.18f, radius, color,
                progress < 0.998f);
            if (follow != null)
            {
                follow.BeginCinematicFocus(anchor);
                follow.SetCinematicShake(
                    Mathf.Sin(progress * Mathf.PI) * 0.045f);
            }
            yield return null;
        }
        ClearTowerDefenseVictoryRings();
    }

    private IEnumerator PlayTowerDefenseVictoryBaseFallback(Vector3 anchor,
        RougeCameraFollow follow)
    {
        if (mainTower == null) yield break;
        Transform root = mainTower.transform;
        Vector3 originalScale = root.localScale;
        Vector3 originalPosition = root.localPosition;
        bool committed = false;
        try
        {
            float elapsed = 0f;
            const float duration = 0.88f;
            while (elapsed < duration)
            {
                elapsed += Mathf.Min(Time.unscaledDeltaTime, 1f / 30f);
                float progress = Mathf.Clamp01(elapsed / duration);
                float smooth = Mathf.SmoothStep(0f, 1f, progress);
                root.localScale = originalScale * Mathf.Max(0.01f, 1f - smooth);
                root.localPosition = originalPosition + Vector3.up * smooth;
                if (follow != null)
                    follow.SetCinematicShake(Mathf.Sin(progress * Mathf.PI) * 0.05f);
                yield return null;
            }
            committed = true;
            mainTower.gameObject.SetActive(false);
        }
        finally
        {
            if (!committed && root != null)
            {
                root.localScale = originalScale;
                root.localPosition = originalPosition;
            }
        }
    }

    private LineRenderer CreateTowerDefenseVictoryRing(string objectName,
        int sortingOrder)
    {
        LineRenderer ring = TowerDefenseVisuals.CreateCircleRenderer(
            objectName, transform);
        ring.widthMultiplier = 0.42f;
        ring.sharedMaterial = GetTacticalIndicatorMaterial();
        ring.sortingOrder = sortingOrder;
        ring.enabled = false;
        return ring;
    }

    private void ClearTowerDefenseVictoryRings()
    {
        for (int i = 0; i < _towerDefenseVictoryRings.Count; i++)
            if (_towerDefenseVictoryRings[i] != null)
                Destroy(_towerDefenseVictoryRings[i].gameObject);
        _towerDefenseVictoryRings.Clear();
    }

    private void CaptureTowerDefenseVictoryScore()
    {
        double totalDamage = 0d;
        double topDamage = 0d;
        RougeTowerType topTowerType = RougeTowerType.MachineGun;
        if (_towerDamageTotalsFixed.IsCreated)
        {
            for (int i = 0; i < _towerDamageTotalsFixed.Length; i++)
            {
                double damage = System.Math.Max(0L,
                    _towerDamageTotalsFixed[i]) / 1000d;
                totalDamage += damage;
                if (damage <= topDamage) continue;
                topDamage = damage;
                topTowerType = (RougeTowerType)i;
            }
        }

        float healthRatio = mainTower != null ? mainTower.HealthNormalized : 0f;
        RougeTowerDefenseMap.ScoringRules scoreRules =
            GetActiveTowerDefenseScoringRules();
        long killPoints = scoreRules.GetKillPoints(totalKills);
        long damagePoints = scoreRules.GetDamagePoints(totalDamage);
        long healthPoints = scoreRules.GetMainTowerHealthPoints(healthRatio);
        long economyPoints = scoreRules.GetRemainingGoldPoints(_towerDefenseGold);
        long bossPoints = scoreRules.GetBossDefeatPoints(
            _towerDefenseVictoryBossDefeatedForScore);
        long score = killPoints + damagePoints + healthPoints +
                     economyPoints + bossPoints;

        string grade = scoreRules.GetGrade(score);
        string mvp = topDamage > 0.001d
            ? TowerDefenseVisuals.GetTowerName(topTowerType) + "  " +
              FormatCompactDamage(topDamage)
            : "暂无输出记录";
        _towerDefenseVictoryScoreBreakdown =
            $"{grade}|{score:N0}|" +
            $"<b>{_towerDefenseGameOverReason}</b>\n\n" +
            $"作战用时    {FormatGameTime(_survivalTime)}\n" +
            $"击杀总数    {totalKills:N0}    <color=#72E9FF>+{killPoints:N0}</color>\n" +
            $"总伤害      {FormatCompactDamage(totalDamage)}    <color=#72E9FF>+{damagePoints:N0}</color>\n" +
            $"主塔完整度  {healthRatio * 100f:0.0}%    <color=#72E9FF>+{healthPoints:N0}</color>\n" +
            $"剩余金币    {_towerDefenseGold:N0}    <color=#72E9FF>+{economyPoints:N0}</color>\n" +
            (_towerDefenseVictoryBossDefeatedForScore
                ? $"首领击破    完成    <color=#FFD66B>+{bossPoints:N0}</color>\n"
                : string.Empty) +
            $"\n<color=#FFD66B><b>MVP  {mvp}</b></color>";
    }

    private static RougeTowerDefenseMap.ScoringRules
        GetActiveTowerDefenseScoringRules()
    {
        RougeTowerDefenseMap map = RougeTowerDefenseMapLoader.ActiveMap;
        return map != null
            ? map.ScoreRules
            : new RougeTowerDefenseMap.ScoringRules();
    }

    private void BuildTowerDefenseVictoryUi()
    {
        if (_towerDefenseVictoryCanvas != null)
            Destroy(_towerDefenseVictoryCanvas.gameObject);
        GameObject canvasObject = new GameObject("Victory Debrief Canvas");
        canvasObject.transform.SetParent(transform, false);
        _towerDefenseVictoryCanvas = canvasObject.AddComponent<Canvas>();
        _towerDefenseVictoryCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _towerDefenseVictoryCanvas.sortingOrder = 130;
        CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
        RougeTowerDefenseUiLayout.ConfigureCanvasScaler(scaler);
        canvasObject.AddComponent<GraphicRaycaster>();

        GameObject dialogueLayer = new GameObject("Victory Transmission Layer",
            typeof(RectTransform));
        dialogueLayer.transform.SetParent(canvasObject.transform, false);
        StretchRect(dialogueLayer.GetComponent<RectTransform>(), 0f, 0f, 0f, 0f);
        _towerDefenseVictoryDialogueGroup =
            dialogueLayer.AddComponent<CanvasGroup>();
        _towerDefenseVictoryDialogueGroup.alpha = 1f;
        _towerDefenseVictoryDialogueGroup.blocksRaycasts = false;

        _towerDefenseVictoryPortrait = CreateUiImage(
            "Victory " + TowerDefenseAutoplayCharacterName,
            dialogueLayer.transform, Color.white);
        _towerDefenseVictoryPortrait.sprite = TowerDefenseAutoplayCommander
            .ResolvePortraitSprite(RougeAutoplayCommanderPortraitEmotion.Calm,
                RougeAutoplayCommanderPortraitVariant.Base);
        _towerDefenseVictoryPortrait.preserveAspect = true;
        _towerDefenseVictoryPortrait.raycastTarget = false;
        RectTransform portraitRect = _towerDefenseVictoryPortrait.rectTransform;
        portraitRect.anchorMin = Vector2.zero;
        portraitRect.anchorMax = Vector2.zero;
        portraitRect.pivot = Vector2.zero;
        portraitRect.anchoredPosition = new Vector2(12f, 8f);
        portraitRect.sizeDelta = new Vector2(310f, 476f);

        _towerDefenseVictoryDialogueText = CreateUiText(
            "Victory Transmission", dialogueLayer.transform, 34,
            TextAnchor.LowerRight);
        _towerDefenseVictoryDialogueText.supportRichText = false;
        _towerDefenseVictoryDialogueText.fontStyle = FontStyle.Bold;
        _towerDefenseVictoryDialogueText.color =
            RemapCommanderInterfaceColor(new Color(0.82f, 1f, 0.9f, 1f));
        RectTransform dialogueRect =
            _towerDefenseVictoryDialogueText.rectTransform;
        dialogueRect.anchorMin = new Vector2(1f, 0f);
        dialogueRect.anchorMax = new Vector2(1f, 0f);
        dialogueRect.pivot = new Vector2(1f, 0f);
        dialogueRect.anchoredPosition = new Vector2(-64f, 84f);
        dialogueRect.sizeDelta = new Vector2(820f, 190f);
        Shadow dialogueShadow = _towerDefenseVictoryDialogueText.gameObject
            .AddComponent<Shadow>();
        dialogueShadow.effectColor = new Color(0f, 0f, 0f, 0.92f);
        dialogueShadow.effectDistance = new Vector2(3f, -3f);
        _towerDefenseVictoryDialogueText.text = _towerDefenseVictoryDialogue;

        Image shade = CreateUiImage("Victory Debrief Shade",
            canvasObject.transform, new Color(0.002f, 0.012f, 0.026f, 0.94f));
        StretchRect(shade.rectTransform, 0f, 0f, 0f, 0f);
        _towerDefenseVictoryResultGroup = shade.gameObject.
            AddComponent<CanvasGroup>();
        _towerDefenseVictoryResultGroup.alpha = 0f;
        _towerDefenseVictoryResultGroup.blocksRaycasts = false;

        GameObject card = CreateUiPanel("Victory Debrief Card", shade.transform,
            new Color(0.012f, 0.055f, 0.085f, 0.94f));
        _towerDefenseVictoryResultCard = card.GetComponent<RectTransform>();
        _towerDefenseVictoryResultCard.anchorMin = new Vector2(0.5f, 0.5f);
        _towerDefenseVictoryResultCard.anchorMax = new Vector2(0.5f, 0.5f);
        _towerDefenseVictoryResultCard.pivot = new Vector2(0.5f, 0.5f);
        _towerDefenseVictoryResultCard.anchoredPosition = Vector2.zero;
        _towerDefenseVictoryResultCard.sizeDelta = new Vector2(960f, 650f);
        AddHudPanelChrome(card, new Color(0.1f, 0.9f, 1f, 1f));

        _towerDefenseVictoryTitleText = CreateUiText("Victory Title",
            card.transform, 48, TextAnchor.MiddleLeft);
        RectTransform titleRect = _towerDefenseVictoryTitleText.rectTransform;
        titleRect.anchorMin = new Vector2(0f, 1f);
        titleRect.anchorMax = new Vector2(1f, 1f);
        titleRect.pivot = new Vector2(0.5f, 1f);
        titleRect.anchoredPosition = new Vector2(0f, -34f);
        titleRect.sizeDelta = new Vector2(-96f, 78f);
        _towerDefenseVictoryTitleText.fontStyle = FontStyle.Bold;
        _towerDefenseVictoryTitleText.color =
            new Color(0.28f, 1f, 0.76f, 1f);
        _towerDefenseVictoryTitleText.text = "作战完成  //  MISSION CLEARED";

        string[] scoreParts = _towerDefenseVictoryScoreBreakdown.Split('|');
        _towerDefenseVictoryGradeText = CreateUiText("Victory Grade",
            card.transform, 164, TextAnchor.MiddleCenter);
        RectTransform gradeRect = _towerDefenseVictoryGradeText.rectTransform;
        gradeRect.anchorMin = new Vector2(0f, 0.5f);
        gradeRect.anchorMax = new Vector2(0f, 0.5f);
        gradeRect.pivot = new Vector2(0f, 0.5f);
        gradeRect.anchoredPosition = new Vector2(78f, -18f);
        gradeRect.sizeDelta = new Vector2(250f, 280f);
        _towerDefenseVictoryGradeText.fontStyle = FontStyle.Bold;
        _towerDefenseVictoryGradeText.color =
            new Color(1f, 0.78f, 0.24f, 1f);
        _towerDefenseVictoryGradeText.text = scoreParts.Length > 0
            ? scoreParts[0]
            : "A";

        Text totalScore = CreateUiText("Victory Total Score", card.transform,
            27, TextAnchor.UpperCenter);
        RectTransform totalRect = totalScore.rectTransform;
        totalRect.anchorMin = new Vector2(0f, 0f);
        totalRect.anchorMax = new Vector2(0f, 0f);
        totalRect.pivot = new Vector2(0f, 0f);
        totalRect.anchoredPosition = new Vector2(62f, 58f);
        totalRect.sizeDelta = new Vector2(280f, 100f);
        totalScore.color = new Color(0.55f, 0.84f, 0.92f, 1f);
        totalScore.text = "作战评分\n<b>" +
            (scoreParts.Length > 1 ? scoreParts[1] : "0") + "</b>";

        _towerDefenseVictoryScoreText = CreateUiText("Victory Score Breakdown",
            card.transform, 25, TextAnchor.UpperLeft);
        RectTransform scoreRect = _towerDefenseVictoryScoreText.rectTransform;
        scoreRect.anchorMin = new Vector2(0f, 0f);
        scoreRect.anchorMax = new Vector2(1f, 1f);
        scoreRect.offsetMin = new Vector2(370f, 112f);
        scoreRect.offsetMax = new Vector2(-54f, -132f);
        _towerDefenseVictoryScoreText.supportRichText = true;
        _towerDefenseVictoryScoreText.color =
            new Color(0.84f, 0.96f, 1f, 1f);
        _towerDefenseVictoryScoreText.text = scoreParts.Length > 2
            ? scoreParts[2]
            : _towerDefenseGameOverReason;

        _towerDefenseVictoryReturnButton = CreateUiButton(
            "Return To Commander Selection", card.transform,
            "[R / ENTER]  返回指挥官选择",
            new Color(0.06f, 0.58f, 0.72f, 0.96f));
        RectTransform returnRect = _towerDefenseVictoryReturnButton.
            GetComponent<RectTransform>();
        returnRect.anchorMin = new Vector2(1f, 0f);
        returnRect.anchorMax = new Vector2(1f, 0f);
        returnRect.pivot = new Vector2(1f, 0f);
        returnRect.anchoredPosition = new Vector2(-54f, 38f);
        returnRect.sizeDelta = new Vector2(330f, 58f);
        _towerDefenseVictoryReturnButton.interactable = false;
        _towerDefenseVictoryReturnButton.onClick.AddListener(
            ReloadTowerDefenseScene);

        ApplyActiveCommanderUiTheme(canvasObject);
    }

    private IEnumerator RevealTowerDefenseVictoryResult()
    {
        if (_towerDefenseVictoryResultGroup == null) yield break;
        float elapsed = 0f;
        const float duration = 0.72f;
        Vector3 originalScale = _towerDefenseVictoryResultCard != null
            ? _towerDefenseVictoryResultCard.localScale
            : Vector3.one;
        while (elapsed < duration)
        {
            elapsed += Mathf.Min(Time.unscaledDeltaTime, 1f / 30f);
            float progress = Mathf.Clamp01(elapsed / duration);
            float smooth = 1f - Mathf.Pow(1f - progress, 3f);
            _towerDefenseVictoryResultGroup.alpha = smooth;
            if (_towerDefenseVictoryDialogueGroup != null)
                _towerDefenseVictoryDialogueGroup.alpha = 1f - smooth;
            if (_towerDefenseVictoryResultCard != null)
                _towerDefenseVictoryResultCard.localScale = originalScale *
                    Mathf.Lerp(0.94f, 1f, smooth);
            yield return null;
        }
        _towerDefenseVictoryResultGroup.alpha = 1f;
        _towerDefenseVictoryResultGroup.blocksRaycasts = true;
        if (_towerDefenseVictoryDialogueGroup != null)
            _towerDefenseVictoryDialogueGroup.gameObject.SetActive(false);
        if (_towerDefenseVictoryReturnButton != null)
            _towerDefenseVictoryReturnButton.interactable = true;
    }
}
