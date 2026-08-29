using UnityEngine;

public partial class RougeGameManager
{
    private const float TowerDefenseBossArrivalDuration = 1f;
    private const float TowerDefenseBossArrivalHeight = 52f;
    private const float TowerDefenseBossLandingClearRadius = 10f;

    private bool _towerDefenseBossArrivalActive;
    private float _towerDefenseBossArrivalTimer;
    private float _towerDefenseBossLandingShakeRemaining;
    private Vector3 _towerDefenseBossArrivalGroundPosition;

    private bool BeginTowerDefenseBossArrival()
    {
        if (_towerDefenseBossArrivalActive || _bossSpawned) return false;
        if (bossSpawnPoint == null)
            bossSpawnPoint = UnityEngine.Object.FindFirstObjectByType<RougeBossSpawnPoint>();

        Vector3 spawn = bossSpawnPoint != null
            ? bossSpawnPoint.transform.position
            : bossBalance.fallbackSpawnPosition;
        _towerDefenseBossArrivalGroundPosition =
            new Vector3(spawn.x, renderHeight, spawn.z);
        _bossWorldPosition = _towerDefenseBossArrivalGroundPosition;
        _towerDefenseBossArrivalTimer = 0f;
        _towerDefenseBossArrivalActive = true;

        float radius = Mathf.Max(0.5f, bossBalance.radius);
        if (_bossSpriteAnimator != null) Destroy(_bossSpriteAnimator.gameObject);
        _bossSpriteAnimator = RougeBossSpriteAnimator.Create(bossBalance, radius * 4.2f);
        if (_bossSpriteAnimator != null)
        {
            Vector3 airborne = _towerDefenseBossArrivalGroundPosition +
                              Vector3.up * (TowerDefenseBossArrivalHeight +
                                            radius * 1.55f);
            _bossSpriteAnimator.SetWorldState(airborne, Vector3.down * 40f);
        }

        ShowTowerDefenseBossWarning(GetLocalizedBossName(bossBalance.displayName));
        SpawnAOERing(_towerDefenseBossArrivalGroundPosition + Vector3.up * 0.08f,
            TowerDefenseBossLandingClearRadius, 1f,
            new Color(1f, 0.16f, 0.05f, 1f));
        return true;
    }

    private void UpdateTowerDefenseBossArrival(float dt)
    {
        if (!_towerDefenseBossArrivalActive) return;
        _towerDefenseBossArrivalTimer += Mathf.Max(0f, dt);
        float progress = Mathf.Clamp01(
            _towerDefenseBossArrivalTimer / TowerDefenseBossArrivalDuration);
        float fall = progress * progress;
        float radius = Mathf.Max(0.5f, bossBalance.radius);
        Vector3 spriteGround = _towerDefenseBossArrivalGroundPosition +
                               Vector3.up * (radius * 1.55f);
        Vector3 spritePosition = spriteGround +
                                 Vector3.up * (TowerDefenseBossArrivalHeight *
                                               (1f - fall));
        if (_bossSpriteAnimator != null)
            _bossSpriteAnimator.SetWorldState(spritePosition,
                Vector3.down * Mathf.Lerp(8f, 76f, progress));

        RougeCameraFollow follow = RougeCameraFollow.ResolveCamera()?.
            GetComponent<RougeCameraFollow>();
        if (follow != null)
            follow.SetCinematicShake(Mathf.Lerp(0.015f, 0.16f,
                progress * progress));
        if (progress < 1f) return;

        if (!TrySpawnTowerDefenseBoss()) return;
        _towerDefenseBossArrivalActive = false;
        _towerDefenseBossArrivalTimer = 0f;
        _bossWorldPosition = _towerDefenseBossArrivalGroundPosition;

        EliminateEnemiesInsideBossShockwave(
            TowerDefenseBossLandingClearRadius, false);
        SpawnAOERing(_bossWorldPosition + Vector3.up * 0.1f,
            TowerDefenseBossLandingClearRadius, 0.62f,
            new Color(1f, 0.32f, 0.06f, 1f));
        SpawnExplosionVFX(_bossWorldPosition + Vector3.up * 1.4f,
            TowerDefenseBossLandingClearRadius * 0.85f);
        if (follow != null) follow.SetCinematicShake(0.78f);
        _towerDefenseBossLandingShakeRemaining = 0.65f;
    }

    private void UpdateTowerDefenseBossLandingShake(float unscaledDt)
    {
        if (_towerDefenseBossLandingShakeRemaining <= 0f) return;
        _towerDefenseBossLandingShakeRemaining = Mathf.Max(0f,
            _towerDefenseBossLandingShakeRemaining - Mathf.Max(0f, unscaledDt));
        RougeCameraFollow follow = RougeCameraFollow.ResolveCamera()?.
            GetComponent<RougeCameraFollow>();
        if (follow != null)
            follow.SetCinematicShake(0.78f * Mathf.Clamp01(
                _towerDefenseBossLandingShakeRemaining / 0.65f));
    }
}
