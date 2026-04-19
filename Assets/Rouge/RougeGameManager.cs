using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEditor.SearchService;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

[DefaultExecutionOrder(-50)]
public partial class RougeGameManager : MonoBehaviour
{
    private static readonly int PositionScaleBufferId = Shader.PropertyToID("_PositionScaleBuffer");
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ScaleMultiplierId = Shader.PropertyToID("_ScaleMultiplier");

    [Header("References")]
    [SerializeField] private PlayerBase player;
    [SerializeField] private Mesh enemyMesh;
    [SerializeField] private Material enemyMaterial;

    private Mesh _bulletMesh;
    private Material _bulletMaterial;

    [Header("Population")]
    [SerializeField, Range(1000, 300000)] private int enemyCount = 200000;
    [SerializeField] private float enemyMaxHealth = 20f;
    [SerializeField] private float enemyRadius = 0.3f;
    [SerializeField] private float enemyMaxSpeed = 7f;
    [SerializeField] private float enemyVisualScale = 1.35f;

    [Header("Arena")]
    [SerializeField] private float arenaHalfExtent = 220f;
    [SerializeField] private float spawnRadiusMin = 50f;
    [SerializeField] private float spawnRadiusMax = 180f;
    [SerializeField] private float despawnDistance = 260f;
    [SerializeField] private float renderHeight = 0f;

    [Header("Steering")]
    [SerializeField] private float chaseAcceleration = 22f;
    [SerializeField] private float velocityDamping = 0.96f;
    [SerializeField] private float separationRadius = 1.3f;
    [SerializeField] private float separationStrength = 10f;
    [SerializeField] private float obstaclePadding = 1.5f;
    [SerializeField] private float obstacleLookAhead = 3f;
    [SerializeField] private float obstacleRepulsion = 30f;
    [SerializeField] private float obstacleOrbitStrength = 18f;
    [SerializeField] private LayerMask obstacleLayers = -1;

    [Header("Skill Config")]
    [SerializeField] private PlayerSkillConfigSet skillConfig = new PlayerSkillConfigSet();

    private int maxBullets = 128;
    private float fireInterval = 0.06f;
    private float bulletSpeed = 42f;
    private float bulletRadius = 0.2f;
    private float bulletDamage = 14f;
    private float bulletLifetime = 1.5f;
    private int bulletsPerShot = 1;
    private float spreadAngle = 4f;
    private ResolvedSkillHitEffectConfig _autoShootEffects;
    private ResolvedSkillHitEffectConfig _playerContactEffects;
    private float _playerContactRepulseDamage;
    private float _playerContactRingDuration;
    private bool _playerContactDefeatEnemyOnContact;

    [SerializeField] private Mesh tornadoMesh;
    private KeyCode tornadoKey = KeyCode.Q;
    private float tornadoRadius = 10f;
    private float tornadoPullForce = 55f;
    private float tornadoSpinForce = 85f;
    private float tornadoLiftForce = 35f;
    private float tornadoDuration = 4f;
    private float tornadoCooldown = 6f;
    private float tornadoTravelSpeed = 10f;



    [Header("Job Settings")]
    [SerializeField, Range(64, 4096)] private int sortBatchSize = 2048;
    [SerializeField, Range(64, 2048)] private int simulationBatchSize = 256;
    [SerializeField, Range(0.1f, 2f)] private float fixedSimulationDt = 1f;

    [Header("Player Stats")]
    [SerializeField] private float playerMaxHealth = 100f;
    [SerializeField] private float playerContactDamage = 8f;
    [SerializeField] private float playerHitInvincibilityDuration = 0.33f;
    [SerializeField] private float playerContactPadding = 0.22f;
    [SerializeField] private float playerHitRepulseRadius = 8f;
    [SerializeField] private float playerHitRepulseForce = 220f;
    [SerializeField] private float playerHitRepulseLift = 18f;
    private float playerHealth;
    private float _fps;
    private const float BurnGroundDuration = 2.75f;
    private const float BurnGroundRadius = 5f;
    private const float DeathBurstDuration = 0.45f;

    private NativeArray<float4> _positionsA;
    private NativeArray<float4> _positionsB;
    private NativeArray<float4> _velocitiesA;
    private NativeArray<float4> _velocitiesB;
    private NativeArray<float4> _stateA;
    private NativeArray<float4> _stateB;
    private NativeArray<RougeEnemyEffectState> _effectStateA;
    private NativeArray<RougeEnemyEffectState> _effectStateB;
    private NativeArray<ulong> _enemyKeys;
    private NativeArray<ulong> _tempEnemyKeys;
    private NativeArray<int> _cellOffsets;
    private NativeArray<int> _cellCounts;
    private NativeArray<int2> _neighborOffsets;
    private NativeArray<int> _histograms;
    private NativeArray<int> _binTotals;
    private NativeArray<RougeBullet> _bullets;
    private NativeArray<RougeObstacle> _obstacles;
    private NativeArray<int> _playerDamageCount;
    private NativeArray<int> _enemyKillCount;
    private NativeQueue<float2> _explosionQueue;
    private NativeQueue<RougeSkillEvent> _skillEventQueue;
    private float2 _bulletMin;
    private float2 _bulletMax;

    [Header("Progression")]
    public int totalKills = 0;
    public int currentLevel = 1;

    private GraphicsBuffer _positionBuffer;
    private GraphicsBuffer _stateBuffer;
    private GraphicsBuffer _argsBuffer;
    private readonly uint[] _drawArgs = new uint[5];

    private JobHandle _simulationHandle;
    private bool _initialized;
    private int _hashSize;
    private int _hashMask;
    private int _chunkCount;
    private int _activeBulletCount;
    private float _fireTimer;

    private int _obstacleCount;

    private NativeArray<RougeSkillArea> _skillAreasDb;
    private int _skillAreaCount;
    private float _tornadoCooldownTimer;
    private int _pillarStrikesDone = 999;
    private int _pillarStrikesTotal = 0;
    private float _pillarNextStrikeTimer = 0f;
    private float2 _pillarBasePos;
    private float2 _pillarDirection;

    private GameObject _tornadoVisual;
    private Material _tornadoMat;
    
    // Tornado VFX data
    private const int MaxTornados = 16;
    private int _activeTornadoCount;
    private NativeArray<float4> _tornadoPosData;
    private NativeArray<float4> _tornadoStateData;
    private float[] _tornadoLifeTimers = new float[MaxTornados];
    private float[] _tornadoMaxTimes = new float[MaxTornados];
    private float[] _tornadoRadiusMultipliers = new float[MaxTornados];
    private GraphicsBuffer _tornadoPosBuffer;
    private GraphicsBuffer _tornadoStateBuffer;
    private GraphicsBuffer _tornadoArgsBuffer;
    private uint[] _tornadoDrawArgs = new uint[5];

    // ----- New Skills logic
    private float _bombCooldownTimer;
    private float _laserCooldownTimer;
    private struct RougeBomb 
    {
        public bool Active;
        public Vector3 Position;
        public Vector3 Velocity;
        public int BounceCount;
        public float BaseRadius;
    }
    private const int MaxBombs = 4;
    private RougeBomb[] _activeBombs = new RougeBomb[MaxBombs];
    private GameObject[] _bombVisuals = new GameObject[MaxBombs];
    private GameObject _laserVisual;
    private const int MaxLaserSubBeams = 6;
    private GameObject[] _laserExtraVisuals = new GameObject[MaxLaserSubBeams];
    private Material _laserMat;
    private float _laserTimer;
    private float2 _laserPos;
    private float2 _laserDir;

    private float _meleeCooldownTimer;
    private GameObject _meleeVisual;
    private Material _meleeMat;
    private GameObject _meleeFinisherVisual;
    private Material _meleeFinisherMat;
    private float _meleeTimer;
    private float2 _meleePos;
    private float2 _meleeDir;
    private int _meleeComboStep = 0;
    private float _meleeComboWindow = 0f;
    private float _meleeFinisherSlamTimer;
    private float2 _meleeFinisherPos;
    private float2 _meleeFinisherDir;

    private int _bombBounceCount;
    private float _spikeStartupTimer;
    private float _spikeTimer;
    private float2 _spikePos;
    private float2 _spikeDir;
    private GameObject[] _spikeVisuals = new GameObject[3];
    private Material _spikeMat;

    private float _orbitTimer;
    private System.Collections.Generic.List<GameObject> _orbitVisuals = new System.Collections.Generic.List<GameObject>();
    private Material _orbitMat;

    private int _currentMaxEnemies;
    private float _spawnTimer;

    private float _jumpCooldownTimer;
    private float _jumpTimer;
    private float _invincibilityTimer;
    private int _jumpState; // 0 = idle, 1 = jumping
    private Vector3 _jumpStart;
    private Vector3 _jumpTarget;
    private Vector3 _jumpArcPos;

    // ---- New Skills: Shockwave, Meteor, Ice Zone, Dash ----
    private float _shockwaveCooldownTimer;
    private float _shockwaveTimer;
    private float _shockwaveRadius;
    private float2 _shockwavePos;
    private GameObject _shockwaveVisual;
    private Material _shockwaveMat;

    private float _meteorCooldownTimer;
    private float _meteorTimer;
    private float2 _meteorTargetPos;
    private int _meteorWaveIndex;
    private float _meteorWaveTimer;

    private float _iceZoneCooldownTimer;
    private float _iceZoneTimer;
    private float2 _iceZonePos;
    private GameObject _iceZoneVisual;
    private Material _iceZoneMat;

    private float _poisonCooldownTimer;
    private struct RougeThrownBottle
    {
        public bool Active;
        public Vector3 Position;
        public Vector3 Velocity;
    }
    private struct RougePoisonZoneState
    {
        public bool Active;
        public float2 Position;
        public float Timer;
        public float Duration;
        public float Radius;
        public uint Seed;
    }
    private const int MaxPoisonBottles = 2;
    private const int MaxPoisonZones = 4;
    private RougeThrownBottle[] _activePoisonBottles = new RougeThrownBottle[MaxPoisonBottles];
    private RougePoisonZoneState[] _activePoisonZones = new RougePoisonZoneState[MaxPoisonZones];
    private GameObject[] _poisonBottleVisuals = new GameObject[MaxPoisonBottles];
    private GameObject[] _poisonZoneVisuals = new GameObject[MaxPoisonZones];
    private Material _poisonBottleMat;
    private Material _poisonZoneMat;

    private struct RougeBurnPatchState
    {
        public bool Active;
        public float2 Position;
        public float Radius;
        public float Timer;
        public float Damage;
        public float BurnDuration;
    }

    private const int MaxBurnPatches = 12;
    private RougeBurnPatchState[] _activeBurnPatches = new RougeBurnPatchState[MaxBurnPatches];
    private GameObject[] _burnPatchVisuals = new GameObject[MaxBurnPatches];
    private Material _burnPatchMat;

    private float _dashCooldownTimer;
    private float _dashSpinTimer;
    private float _dashSpinAngle;
    private float2 _dashDirection;
    private Vector3 _dashStartPosition;
    private Vector3 _dashTargetPosition;
    private bool _pendingPlayerHitRepulse;
    private float2 _pendingPlayerHitRepulsePosition;
    private GameObject _dashVisual;
    private Material _dashMat;
    private bool _hasActiveSustainedSkill;
    private PlayerSkillType _activeSustainedSkillType;
    private int _activeSustainedSkillPriority;
    private int _shockwaveState;
    private Vector3 _shockwaveJumpStart;
    private float _cameraLiftOffset;
    private float _cameraFovOffset;
    private float _baseCameraFov = -1f;
    private float _meleeHitShake;
    private readonly Matrix4x4[] _bulletRenderMatrices = new Matrix4x4[1023];

    // VFX buffers for explosions
    private const int MaxExplosions = 128;
    private const int MaxDeathBursts = 256;
    private int _explosionCount;
    private int _deathBurstCount;
    private NativeArray<float4> _expPosData;
    private NativeArray<float4> _expStateData;
    private float[] _expTimers = new float[MaxExplosions];
    private float[] _expMaxScales = new float[MaxExplosions];
    private GraphicsBuffer _expPosBuffer;
    private GraphicsBuffer _expStateBuffer;
    private GraphicsBuffer _expArgsBuffer;
    private uint[] _expDrawArgs = new uint[5];
    private Material _vfxExplosionMat;
    private NativeArray<float4> _deathPosData;
    private NativeArray<float4> _deathStateData;
    private readonly float[] _deathTimers = new float[MaxDeathBursts];
    private readonly float[] _deathDurations = new float[MaxDeathBursts];
    private readonly float[] _deathRiseSpeeds = new float[MaxDeathBursts];
    private GraphicsBuffer _deathPosBuffer;
    private GraphicsBuffer _deathStateBuffer;
    private GraphicsBuffer _deathArgsBuffer;
    private readonly uint[] _deathDrawArgs = new uint[5];
    private Material _vfxDeathMat;
    private Mesh _vfxSphereMesh;

    // Meteor visual spheres (falling from sky)
    private const int MeteorVisualMax = 8;
    private GameObject[] _meteorVisuals = new GameObject[MeteorVisualMax];
    private float[] _meteorVisualTimers = new float[MeteorVisualMax];
    private Vector3[] _meteorVisualTargets = new Vector3[MeteorVisualMax];

    // Skill kill tracking: [0]=tornado [1]=bomb/jump [2]=laser [3]=melee/spike [4]=orbit [5]=bullet
    private NativeArray<int> _skillKillCounts;
    private readonly int[] _skillTotalKills = new int[6];
    private readonly int[] _skillLevels = new int[6];
    private float _survivalTime;

    // AOE Ring VFX (shader-based hollow cylinder)
    private const int MaxAOERings = 32;
    private int _aoeRingCount;
    private GameObject[] _aoeRingVisuals = new GameObject[MaxAOERings];
    private float[] _aoeRingTimers = new float[MaxAOERings];
    private float[] _aoeRingMaxTimes = new float[MaxAOERings];
    private float[] _aoeRingMaxRadius = new float[MaxAOERings];
    private Vector3[] _aoeRingPositions = new Vector3[MaxAOERings];
    private Color[] _aoeRingColors = new Color[MaxAOERings];
    private Material _aoeRingMat;
    private MaterialPropertyBlock _aoeRingPropertyBlock;
    private Mesh _cylinderMesh;

    // Shockwave multi-ring system
    private const int ShockwaveRingCount = 5;
    private float[] _shockwaveRingTimers = new float[ShockwaveRingCount];
    private GameObject[] _shockwaveRingVisuals = new GameObject[ShockwaveRingCount];
    private Material _shockwaveRingMat;

    private void OnEnable()
    {
        Initialize();
    }

    private void OnDisable()
    {
        Dispose();
    }

    private void OnValidate()
    {
        ApplySkillConfigValues();
    }

    private UnityEngine.UI.Text _uiText;

    private void LateUpdate()
    {
        if (!_initialized || player == null) return;
        Vector3 pos = player.transform.position;
        
        // Boundary
        pos.x = Mathf.Clamp(pos.x, -arenaHalfExtent + 1f, arenaHalfExtent - 1f);
        pos.z = Mathf.Clamp(pos.z, -arenaHalfExtent + 1f, arenaHalfExtent - 1f);
        
        // Obstacles
        if (_obstacles.IsCreated)
        {
            for (int i = 0; i < _obstacleCount; i++)
            {
                RougeObstacle obs = _obstacles[i];
                if (obs.Type == 0) // AABB
                {
                    float2 minP = obs.Min - new float2(0.5f);
                    float2 maxP = obs.Max + new float2(0.5f);
                    if (pos.x >= minP.x && pos.x <= maxP.x && pos.z >= minP.y && pos.z <= maxP.y)
                    {
                        float dx1 = pos.x - minP.x, dx2 = maxP.x - pos.x;
                        float dy1 = pos.z - minP.y, dy2 = maxP.y - pos.z;
                        float minD = Mathf.Min(Mathf.Min(dx1, dx2), Mathf.Min(dy1, dy2));
                        if (minD == dx1) pos.x = minP.x;
                        else if (minD == dx2) pos.x = maxP.x;
                        else if (minD == dy1) pos.z = minP.y;
                        else pos.z = maxP.y;
                    }
                }
                else // Circle
                {
                    float2 diff = new float2(pos.x, pos.z) - obs.Center;
                    float dist = math.length(diff);
                    float totalR = obs.CircleRadius + obs.Padding + 0.5f;
                    if (dist < totalR && dist > 0.001f)
                    {
                        float2 push = (diff / dist) * totalR;
                        pos.x = obs.Center.x + push.x;
                        pos.z = obs.Center.y + push.y;
                    }
                }
            }
        }
        
        // During leap smash arc, override position after obstacle resolution
        if (_jumpState == 1)
        {
            pos = _jumpArcPos;
        }

        player.transform.position = pos;

        // Melee hit screen shake
        if (_meleeHitShake > 0f)
        {
            _meleeHitShake -= Time.deltaTime;
            Camera gameplayCamera = RougeCameraFollow.ResolveCamera();
            if (gameplayCamera != null)
            {
                float shakeIntensity = _meleeHitShake * 15f;
                gameplayCamera.transform.position += new Vector3(
                    UnityEngine.Random.Range(-shakeIntensity, shakeIntensity),
                    UnityEngine.Random.Range(-shakeIntensity * 0.5f, shakeIntensity * 0.5f),
                    UnityEngine.Random.Range(-shakeIntensity, shakeIntensity));
            }
        }

        ApplyCameraEffects();
    }

    private void Update()
    {
        _fps = math.lerp(_fps, 1f / Time.deltaTime, 5f * Time.deltaTime);

        if (!_initialized)
        {
            return;
        }

        _simulationHandle.Complete();

        if (_enemyKillCount.IsCreated)
        {
            int recentKills = _enemyKillCount[0];
            if (recentKills > 0)
            {
                totalKills += recentKills;
                _enemyKillCount[0] = 0;
                int nextLevel = 1 + (totalKills / 300);
                if (nextLevel > currentLevel)
                {
                    bulletDamage += 5f * (nextLevel - currentLevel);
                    currentLevel = nextLevel;
                    if (currentLevel % 3 == 0) bulletsPerShot++;
                    if (currentLevel % 5 == 0)
                    {
                        maxBullets = Mathf.Min(maxBullets + 64, 2048);
                        NativeArray<RougeBullet> oldBullets = _bullets;
                        _bullets = new NativeArray<RougeBullet>(maxBullets, Allocator.Persistent);
                        for(int i = 0; i < _activeBulletCount; i++) _bullets[i] = oldBullets[i];
                        ReleaseNative(ref oldBullets);
                    }
                }
            }
        }

        // Per-skill kill accumulation
        if (_skillKillCounts.IsCreated)
        {
            for (int sk = 0; sk < 6; sk++)
            {
                int recent = _skillKillCounts[sk];
                if (recent > 0)
                {
                    _skillTotalKills[sk] += recent;
                    _skillKillCounts[sk] = 0;
                    _skillLevels[sk] = Mathf.Min(60, _skillTotalKills[sk] / 150); // increased to max level 60
                }
            }
        }

        _survivalTime += Time.deltaTime;

        if (_invincibilityTimer > 0f) _invincibilityTimer -= Time.deltaTime;

        int damage = _playerDamageCount[0];
        if (damage > 0)
        {
            if (IsPlayerContactEnabled() && _jumpState == 0 && _invincibilityTimer <= 0f)
            {
                playerHealth -= playerContactDamage;
                playerHealth = Mathf.Max(0f, playerHealth);
                _invincibilityTimer = math.max(_invincibilityTimer, playerHitInvincibilityDuration);
                if (player != null && playerHitRepulseRadius > 0f)
                {
                    _pendingPlayerHitRepulse = true;
                    _pendingPlayerHitRepulsePosition = player.PlanarPosition;
                }
            }
            _playerDamageCount[0] = 0;
        }

        if (playerHealth <= 0f)
        {
            Dispose();
            UnityEngine.SceneManagement.SceneManager.LoadScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex);
            return;
        }

        float dt = Mathf.Min(Time.deltaTime, 0.05f) * fixedSimulationDt;
        
        if (Input.GetKeyDown(KeyCode.Equals) || Input.GetKeyDown(KeyCode.Plus)) {
            _currentMaxEnemies = Mathf.Min(enemyCount, _currentMaxEnemies + 10000);
        }
        if (Input.GetKeyDown(KeyCode.Minus)) {
            _currentMaxEnemies = Mathf.Max(10, _currentMaxEnemies - 10000);
        }

        _spawnTimer += dt;
        if (_spawnTimer > 1f) {
            _spawnTimer = 0f;
            if (_currentMaxEnemies < enemyCount) {
                // Ramping up exponentially + flat faster rate to reach 100k cap sensibly
                int growth = 20 + currentLevel * 10 + (int)(_currentMaxEnemies * 0.02f);
                if (Input.GetKey(KeyCode.RightBracket)) growth *= 10;
                _currentMaxEnemies = Mathf.Min(enemyCount, _currentMaxEnemies + growth);
            }
        }

        UpdateSkills(dt);
        ApplyPendingPlayerContactSkill();

     
        while (_explosionQueue.TryDequeue(out float2 expPos)) {
            if (_skillAreaCount < _skillAreasDb.Length) {
                _skillAreasDb[_skillAreaCount++] = new RougeSkillArea {
                    Type = 2,
                    Position = expPos,
                    Radius = 8f,
                    Damage = enemyMaxHealth * (1f + currentLevel * 0.15f) * 0.8f,
                    PullForce = -120f,
                    VerticalForce = 25f
                };
                // Spawn VFX sphere at explosion pos
                SpawnExplosionVFX(new Vector3(expPos.x, renderHeight + 1f, expPos.y), 6f);
                SpawnAOERing(new Vector3(expPos.x, renderHeight, expPos.y), 8f, 0.35f, new Color(1f, 0.4f, 0.1f, 1f));
            }
        }

        while (_skillEventQueue.TryDequeue(out RougeSkillEvent skillEvent))
        {
            RougeSkillEventType eventType = (RougeSkillEventType)skillEvent.Type;
            switch (eventType)
            {
                case RougeSkillEventType.LaunchLandingExplosion:
                case RougeSkillEventType.CurseExplosion:
                    if (_skillAreaCount < _skillAreasDb.Length)
                    {
                        _skillAreasDb[_skillAreaCount++] = new RougeSkillArea
                        {
                            Type = 2,
                            Position = skillEvent.Position,
                            Radius = skillEvent.Radius,
                            Damage = skillEvent.Damage,
                            PullForce = -140f,
                            VerticalForce = eventType == RougeSkillEventType.CurseExplosion ? 16f : 0f
                        };
                    }

                    SpawnExplosionVFX(new Vector3(skillEvent.Position.x, renderHeight + 1f, skillEvent.Position.y), math.max(2f, skillEvent.Radius * 0.45f));
                    SpawnAOERing(
                        new Vector3(skillEvent.Position.x, renderHeight, skillEvent.Position.y),
                        skillEvent.Radius,
                        0.35f,
                        eventType == RougeSkillEventType.CurseExplosion ? new Color(0.12f, 0.12f, 0.12f, 1f) : new Color(1f, 0.7f, 0.22f, 1f));
                    break;
                case RougeSkillEventType.PoisonSpread:
                    if (_skillAreaCount < _skillAreasDb.Length)
                    {
                        _skillAreasDb[_skillAreaCount++] = new RougeSkillArea
                        {
                            Type = 10,
                            Position = skillEvent.Position,
                            Radius = skillEvent.Radius,
                            EffectFlags = (int)SkillHitEffectTag.Poison,
                            EffectPoisonSpreadRadius = skillEvent.Radius
                        };
                    }

                    SpawnAOERing(new Vector3(skillEvent.Position.x, renderHeight + 0.1f, skillEvent.Position.y), skillEvent.Radius, 0.3f, new Color(0.35f, 1f, 0.45f, 1f));
                    break;
                case RougeSkillEventType.BurnGround:
                    ActivateBurnPatch(
                        skillEvent.Position,
                        skillEvent.Radius,
                        BurnGroundDuration,
                        skillEvent.Damage * 0.55f,
                        math.max(0.35f, skillEvent.Duration * 0.55f));
                    SpawnAOERing(new Vector3(skillEvent.Position.x, renderHeight + 0.05f, skillEvent.Position.y), skillEvent.Radius * 0.92f, 0.28f, new Color(1f, 0.4f, 0.08f, 1f));
                    break;
                case RougeSkillEventType.EnemyDeathBurst:
                    SpawnDeathBurstVFX(new Vector3(skillEvent.Position.x, renderHeight + 0.35f, skillEvent.Position.y), math.max(0.8f, skillEvent.Radius * 2.4f));
                    break;
            }
        }

        UpdateBurnPatches(dt);

        // Compact active VFX so we only upload and draw live instances.
        _explosionCount = 0;
        for (int vi = 0; vi < MaxExplosions; vi++)
        {
            if (_expTimers[vi] <= 0f)
            {
                continue;
            }

            _expTimers[vi] = math.max(0f, _expTimers[vi] - dt);
            if (_expTimers[vi] <= 0f)
            {
                _expStateData[vi] = new float4(0f, 0f, 0f, 1f);
                continue;
            }

            float progress = 1f - math.saturate(_expTimers[vi] / 0.35f);
            float currentRadius = _expMaxScales[vi] * math.saturate(progress);
            _expStateData[vi] = new float4(currentRadius, currentRadius * 0.72f, currentRadius, progress);

            if (_explosionCount != vi)
            {
                _expPosData[_explosionCount] = _expPosData[vi];
                _expStateData[_explosionCount] = _expStateData[vi];
                _expTimers[_explosionCount] = _expTimers[vi];
                _expMaxScales[_explosionCount] = _expMaxScales[vi];
                _expTimers[vi] = 0f;
                _expStateData[vi] = new float4(0f, 0f, 0f, 1f);
            }

            _explosionCount++;
        }

        for (int vi = _explosionCount; vi < MaxExplosions; vi++)
        {
            _expStateData[vi] = new float4(0f, 0f, 0f, 1f);
        }

        _deathBurstCount = 0;
        for (int vi = 0; vi < MaxDeathBursts; vi++)
        {
            if (_deathTimers[vi] <= 0f)
            {
                continue;
            }

            _deathTimers[vi] = math.max(0f, _deathTimers[vi] - dt);
            if (_deathTimers[vi] <= 0f)
            {
                _deathStateData[vi] = new float4(0f, 0f, 0f, 1f);
                continue;
            }

            float duration = math.max(0.01f, _deathDurations[vi]);
            float progress = 1f - (_deathTimers[vi] / duration);
            float scale = math.lerp(0.3f, 1f, math.saturate(math.pow(progress, 0.7f)));

            float4 pos = _deathPosData[vi];
            pos.y += _deathRiseSpeeds[vi] * dt;
            _deathPosData[vi] = pos;

            float baseRadius = pos.w;
            _deathStateData[vi] = new float4(baseRadius * scale, baseRadius * 0.55f * scale, baseRadius * scale, math.saturate(progress));

            if (_deathBurstCount != vi)
            {
                _deathPosData[_deathBurstCount] = _deathPosData[vi];
                _deathStateData[_deathBurstCount] = _deathStateData[vi];
                _deathTimers[_deathBurstCount] = _deathTimers[vi];
                _deathDurations[_deathBurstCount] = _deathDurations[vi];
                _deathRiseSpeeds[_deathBurstCount] = _deathRiseSpeeds[vi];
                _deathTimers[vi] = 0f;
                _deathStateData[vi] = new float4(0f, 0f, 0f, 1f);
            }

            _deathBurstCount++;
        }

        for (int vi = _deathBurstCount; vi < MaxDeathBursts; vi++)
        {
            _deathStateData[vi] = new float4(0f, 0f, 0f, 1f);
        }

        UpdateAOERings(dt);
        UpdateBullets(dt);
        RenderBullets();
        RenderAOERings();
        RenderEnemies();
        RenderExplosions();
        RenderDeathBursts();
        RenderTornados();
        // Light Pillar Array replaces Tornado instancing
        ScheduleSimulation(math.max(dt, 0.0001f));

        if (_uiText != null)
        {
            UpdateHud();
        }
    }

    private void Initialize()
    {
        Dispose();
        ApplySkillConfigValues();

        if (player == null)
        {
            player = UnityEngine.Object.FindFirstObjectByType<PlayerBase>();
        }
        
        if (_uiText == null)
        {
            var existingCanvas = GameObject.Find("RougeCanvas");
            if (existingCanvas != null) Destroy(existingCanvas);
            
            GameObject canvasGo = new GameObject("RougeCanvas");
            Canvas canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvasGo.AddComponent<UnityEngine.UI.CanvasScaler>();
            canvasGo.AddComponent<UnityEngine.UI.GraphicRaycaster>();
            
            GameObject textGo = new GameObject("RougeText");
            textGo.transform.SetParent(canvasGo.transform, false);
            _uiText = textGo.AddComponent<UnityEngine.UI.Text>();
            _uiText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _uiText.fontSize = 24;
            _uiText.color = Color.white;
            _uiText.alignment = TextAnchor.UpperLeft;
            
            RectTransform rt = _uiText.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0, 1);
            rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = new Vector2(30, -30);
            rt.sizeDelta = new Vector2(860, 960);
        }

        enemyCount = Mathf.Max(enemyCount, 1024);
        maxBullets = Mathf.Max(maxBullets, 1);
        spawnRadiusMax = Mathf.Max(spawnRadiusMax, spawnRadiusMin + 1f);
        despawnDistance = Mathf.Max(despawnDistance, spawnRadiusMax + 20f);
        enemyMesh = enemyMesh != null ? enemyMesh : CreateFallbackQuad();
        enemyMaterial = enemyMaterial != null ? enemyMaterial : CreateFallbackMaterial();

        if (_bulletMesh == null)
        {
            GameObject tempSphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            _bulletMesh = tempSphere.GetComponent<MeshFilter>().sharedMesh;
            Destroy(tempSphere);
        }

        if (_bulletMaterial == null)
        {
            _bulletMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            _bulletMaterial.SetColor("_BaseColor", Color.yellow);
            _bulletMaterial.enableInstancing = true;
        }

        _hashSize = Mathf.NextPowerOfTwo(Mathf.Max(enemyCount * 2, 65536));
        _hashMask = _hashSize - 1;
        _chunkCount = Mathf.CeilToInt(enemyCount / (float)sortBatchSize);

        _positionsA = new NativeArray<float4>(enemyCount, Allocator.Persistent);
        _positionsB = new NativeArray<float4>(enemyCount, Allocator.Persistent);
        _velocitiesA = new NativeArray<float4>(enemyCount, Allocator.Persistent);
        _velocitiesB = new NativeArray<float4>(enemyCount, Allocator.Persistent);
        _stateA = new NativeArray<float4>(enemyCount, Allocator.Persistent);
        _stateB = new NativeArray<float4>(enemyCount, Allocator.Persistent);
        _effectStateA = new NativeArray<RougeEnemyEffectState>(enemyCount, Allocator.Persistent);
        _effectStateB = new NativeArray<RougeEnemyEffectState>(enemyCount, Allocator.Persistent);
        _enemyKeys = new NativeArray<ulong>(enemyCount, Allocator.Persistent);
        _tempEnemyKeys = new NativeArray<ulong>(enemyCount, Allocator.Persistent);
        _cellOffsets = new NativeArray<int>(_hashSize, Allocator.Persistent);
        _cellCounts = new NativeArray<int>(_hashSize, Allocator.Persistent);
        _neighborOffsets = new NativeArray<int2>(9, Allocator.Persistent);
        _histograms = new NativeArray<int>(math.max(_chunkCount * 256, 256), Allocator.Persistent);
        _binTotals = new NativeArray<int>(256, Allocator.Persistent);
        _bullets = new NativeArray<RougeBullet>(maxBullets, Allocator.Persistent);
        _playerDamageCount = new NativeArray<int>(1, Allocator.Persistent);
        _enemyKillCount = new NativeArray<int>(1, Allocator.Persistent);
        _explosionQueue = new NativeQueue<float2>(Allocator.Persistent);
        _skillEventQueue = new NativeQueue<RougeSkillEvent>(Allocator.Persistent);
        _enemyKillCount[0] = 0;
        totalKills = 0;
        currentLevel = 1;
        _skillKillCounts = new NativeArray<int>(6, Allocator.Persistent);
        _survivalTime = 0f;
        System.Array.Clear(_skillTotalKills, 0, 6);
        System.Array.Clear(_skillLevels, 0, 6);

        playerHealth = playerMaxHealth;

        CaptureObstacles();
        BuildNeighborOffsets();
        SeedEnemies();

        _skillAreasDb = new NativeArray<RougeSkillArea>(1024, Allocator.Persistent);

        _positionBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, enemyCount, UnsafeUtility.SizeOf<float4>());
        _stateBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, enemyCount, UnsafeUtility.SizeOf<float4>());
        _argsBuffer = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments, 1, sizeof(uint) * 5);
        _drawArgs[0] = enemyMesh.GetIndexCount(0);
        _drawArgs[1] = (uint)enemyCount;
        _drawArgs[2] = enemyMesh.GetIndexStart(0);
        _drawArgs[3] = enemyMesh.GetBaseVertex(0);
        _drawArgs[4] = 0;
        _argsBuffer.SetData(_drawArgs);

        _expPosData = new NativeArray<float4>(MaxExplosions, Allocator.Persistent);
        _expStateData = new NativeArray<float4>(MaxExplosions, Allocator.Persistent);
        _expPosBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, MaxExplosions, UnsafeUtility.SizeOf<float4>());
        _expStateBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, MaxExplosions, UnsafeUtility.SizeOf<float4>());
        _expArgsBuffer = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments, 1, sizeof(uint) * 5);
        _deathPosData = new NativeArray<float4>(MaxDeathBursts, Allocator.Persistent);
        _deathStateData = new NativeArray<float4>(MaxDeathBursts, Allocator.Persistent);
        _deathPosBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, MaxDeathBursts, UnsafeUtility.SizeOf<float4>());
        _deathStateBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, MaxDeathBursts, UnsafeUtility.SizeOf<float4>());
        _deathArgsBuffer = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments, 1, sizeof(uint) * 5);
        
        _tornadoPosData = new NativeArray<float4>(MaxTornados, Allocator.Persistent);
        _tornadoStateData = new NativeArray<float4>(MaxTornados, Allocator.Persistent);
        _tornadoPosBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, MaxTornados, UnsafeUtility.SizeOf<float4>());
        _tornadoStateBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, MaxTornados, UnsafeUtility.SizeOf<float4>());
        _tornadoArgsBuffer = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments, 1, sizeof(uint) * 5);
        _argsBuffer.SetData(_drawArgs);

        _simulationHandle = default;
        _fireTimer = 0f;
        _tornadoCooldownTimer = 0f;
        _pillarStrikesDone = 999;
        
        _bombCooldownTimer = 0f;
        for (int i=0; i<MaxBombs; i++) _activeBombs[i].Active = false;
        _spikeTimer = 0f;
        _spikeStartupTimer = 0f;
        
        _laserTimer = 0f;
        _laserCooldownTimer = 0f;
        _meleeCooldownTimer = 0f;
        _meleeFinisherSlamTimer = 0f;

        if (tornadoMesh == null)
        {
            GameObject tempCylinder = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            tornadoMesh = tempCylinder.GetComponent<MeshFilter>().sharedMesh;
            Destroy(tempCylinder);
        }
        
        if (_vfxSphereMesh == null)
        {
            GameObject tmpS = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            _vfxSphereMesh = tmpS.GetComponent<MeshFilter>().sharedMesh;
            Destroy(tmpS);
        }
        
        if (_vfxExplosionMat == null)
        {
            Shader sh = Shader.Find("Rouge/VFXInstanced");
            _vfxExplosionMat = new Material(sh);
            _vfxExplosionMat.SetColor("_BaseColor", new Color(1f, 0.4f, 0.1f, 0.7f));
            _vfxExplosionMat.enableInstancing = true;
        }

        if (_vfxDeathMat == null)
        {
            Shader sh = Shader.Find("Rouge/VFXInstanced");
            _vfxDeathMat = new Material(sh);
            _vfxDeathMat.SetColor("_BaseColor", new Color(1f, 0.92f, 0.84f, 0.42f));
            _vfxDeathMat.enableInstancing = true;
        }
        
        if (_tornadoMat == null)
        {
            Shader tsh = Shader.Find("Rouge/VFXInstanced");
            _tornadoMat = new Material(tsh);
            _tornadoMat.SetColor("_BaseColor", new Color(1f, 0.9f, 0.2f, 0.8f)); // glowing golden-yellow
            _tornadoMat.enableInstancing = true;
        }

        // AOE Ring material
        if (_aoeRingMat == null)
        {
            Shader ringShader = Shader.Find("Rouge/AOERing");
            if (ringShader != null)
            {
                _aoeRingMat = new Material(ringShader);
                _aoeRingMat.SetColor("_Color", new Color(1f, 0.5f, 0.1f, 0.8f));
                _aoeRingMat.renderQueue = 2450;
            }
        }

        if (_aoeRingPropertyBlock == null)
        {
            _aoeRingPropertyBlock = new MaterialPropertyBlock();
        }

        if (_cylinderMesh == null)
        {
            GameObject tmpCyl = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            _cylinderMesh = tmpCyl.GetComponent<MeshFilter>().sharedMesh;
            Destroy(tmpCyl);
        }

        // Shockwave ring material (yellow-orange glow)
        if (_shockwaveRingMat == null)
        {
            Shader ringShader = Shader.Find("Rouge/AOERing");
            if (ringShader != null)
            {
                _shockwaveRingMat = new Material(ringShader);
                _shockwaveRingMat.SetColor("_Color", new Color(1f, 0.8f, 0.0f, 0.9f));
                _shockwaveRingMat.SetFloat("_GlowIntensity", 4f);
            }
        }

        _expDrawArgs[0] = _vfxSphereMesh.GetIndexCount(0);
        _expDrawArgs[1] = 0;
        _expDrawArgs[2] = _vfxSphereMesh.GetIndexStart(0);
        _expDrawArgs[3] = _vfxSphereMesh.GetBaseVertex(0);
        _expDrawArgs[4] = 0;
        _expArgsBuffer.SetData(_expDrawArgs);

        _tornadoDrawArgs[0] = tornadoMesh.GetIndexCount(0);
        _tornadoDrawArgs[1] = 0;
        _tornadoDrawArgs[2] = tornadoMesh.GetIndexStart(0);
        _tornadoDrawArgs[3] = tornadoMesh.GetBaseVertex(0);
        _tornadoDrawArgs[4] = 0;
        _tornadoArgsBuffer.SetData(_tornadoDrawArgs);

        // Create tornado visual object
        if (_tornadoVisual == null)
        {
            _tornadoVisual = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Destroy(_tornadoVisual.GetComponent<Collider>());
            _tornadoVisual.name = "Tornado Visual";
            _tornadoVisual.GetComponent<MeshRenderer>().material = _tornadoMat;
            _tornadoVisual.SetActive(false);
        }

        for (int ri = 0; ri < MaxAOERings; ri++)
        {
            _aoeRingTimers[ri] = 0f;
        }

        for (int si = 0; si < ShockwaveRingCount; si++)
        {
            _shockwaveRingTimers[si] = 0f;
        }

        for (int b = 0; b < MaxBombs; b++)
        {
            if (_bombVisuals[b] == null)
            {
                _bombVisuals[b] = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                Destroy(_bombVisuals[b].GetComponent<Collider>());
                _bombVisuals[b].name = "Bomb Visual " + b;
                Shader bhShader = Shader.Find("Custom/BlackHole");
                if (bhShader != null)
                {
                    _bombVisuals[b].GetComponent<MeshRenderer>().material = new Material(bhShader);
                    _bombVisuals[b].GetComponent<MeshRenderer>().sharedMaterial.SetColor("_HaloColor", new Color(0.8f, 0.2f, 0.0f, 1f));
                }
                else
                {
                    _bombVisuals[b].GetComponent<MeshRenderer>().material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                    _bombVisuals[b].GetComponent<MeshRenderer>().sharedMaterial.color = Color.red;
                }
                _bombVisuals[b].SetActive(false);
            }
        }

        if (_laserVisual == null)
        {
            _laserVisual = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Destroy(_laserVisual.GetComponent<Collider>());
            _laserVisual.name = "Laser Visual";
            _laserMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            _laserMat.color = new Color(0.1f, 1f, 1f, 0.9f);
            _laserMat.SetFloat("_Surface", 1f); // Transparent
            _laserMat.SetFloat("_Blend", 0f); // Alpha blend
            _laserMat.SetColor("_EmissionColor", new Color(0.2f, 0.8f, 1f, 1f) * 4f); // Brighter
            _laserVisual.GetComponent<MeshRenderer>().material = _laserMat;
            _laserVisual.SetActive(false);
        }

        for (int li = 0; li < MaxLaserSubBeams; li++)
        {
            if (_laserExtraVisuals[li] == null)
            {
                _laserExtraVisuals[li] = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                Destroy(_laserExtraVisuals[li].GetComponent<Collider>());
                _laserExtraVisuals[li].name = "Laser Extra " + li;
                _laserExtraVisuals[li].GetComponent<MeshRenderer>().material = _laserMat;
                _laserExtraVisuals[li].SetActive(false);
            }
        }

        if (_meleeVisual == null)
        {
            _meleeVisual = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(_meleeVisual.GetComponent<Collider>());
            _meleeVisual.name = "Melee Slash";
            _meleeMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            _meleeMat.color = new Color(1f, 0.3f, 0.1f, 0.6f);
            _meleeMat.SetFloat("_Surface", 1f); // Transparent
            _meleeMat.SetColor("_EmissionColor", new Color(1f, 0.3f, 0.1f, 1f) * 4f);
            _meleeVisual.GetComponent<MeshRenderer>().material = _meleeMat;
            _meleeVisual.SetActive(false);
        }

        if (_meleeFinisherVisual == null)
        {
            _meleeFinisherVisual = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(_meleeFinisherVisual.GetComponent<Collider>());
            _meleeFinisherVisual.name = "Melee Finisher Slam";
            _meleeFinisherMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            _meleeFinisherMat.color = new Color(1f, 0.85f, 0.45f, 0.75f);
            _meleeFinisherMat.SetFloat("_Surface", 1f);
            _meleeFinisherMat.SetColor("_EmissionColor", new Color(1f, 0.7f, 0.2f, 1f) * 4f);
            _meleeFinisherVisual.GetComponent<MeshRenderer>().material = _meleeFinisherMat;
            _meleeFinisherVisual.SetActive(false);
        }

        if (_spikeMat == null)
        {
            _spikeMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            _spikeMat.color = new Color(0.65f, 0.52f, 0.3f, 1f);
            _spikeMat.SetColor("_EmissionColor", new Color(0.9f, 0.6f, 0.2f, 1f) * 2f);
        }
        for (int iSpkI = 0; iSpkI < 3; iSpkI++)
        {
            if (_spikeVisuals[iSpkI] == null)
            {
                _spikeVisuals[iSpkI] = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                Destroy(_spikeVisuals[iSpkI].GetComponent<Collider>());
                _spikeVisuals[iSpkI].name = "Spike " + iSpkI;
                _spikeVisuals[iSpkI].GetComponent<MeshRenderer>().material = _spikeMat;
                _spikeVisuals[iSpkI].SetActive(false);
            }
        }

        if (_orbitMat == null)
        {
            _orbitMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            _orbitMat.color = new Color(0.8f, 0.1f, 0.8f, 0.8f);
            _orbitMat.SetFloat("_Surface", 1f); // Transparent
            _orbitMat.SetColor("_EmissionColor", new Color(0.8f, 0.1f, 0.8f, 1f) * 2f);
        }

        if (_shockwaveVisual == null)
        {
            _shockwaveVisual = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Destroy(_shockwaveVisual.GetComponent<Collider>());
            _shockwaveVisual.name = "Shockwave Visual";
            _shockwaveMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            _shockwaveMat.color = new Color(1f, 0.6f, 0.0f, 0.5f);
            _shockwaveMat.SetFloat("_Surface", 1f);
            _shockwaveMat.SetColor("_EmissionColor", new Color(1f, 0.6f, 0.0f, 1f) * 3f);
            _shockwaveVisual.GetComponent<MeshRenderer>().material = _shockwaveMat;
            _shockwaveVisual.SetActive(false);
        }

        if (_iceZoneVisual == null)
        {
            _iceZoneVisual = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Destroy(_iceZoneVisual.GetComponent<Collider>());
            _iceZoneVisual.name = "Ice Zone Visual";
            _iceZoneMat = new Material(Shader.Find("Rouge/GroundZone"));
            ConfigureGroundZoneMaterial(
                _iceZoneMat,
                new Color(0.35f, 0.82f, 1f, 0.78f),
                new Color(0.88f, 0.98f, 1f, 0.42f),
                1f,
                1.55f,
                0.08f,
                2.2f,
                0.22f,
                1.25f,
                1.05f,
                1.35f);
            _iceZoneVisual.GetComponent<MeshRenderer>().material = _iceZoneMat;
            ConfigureGroundAoEVisual(_iceZoneVisual.GetComponent<MeshRenderer>(), _iceZoneMat);
            _iceZoneVisual.SetActive(false);
        }

        if (_poisonBottleMat == null)
        {
            _poisonBottleMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            _poisonBottleMat.color = new Color(0.25f, 0.95f, 0.25f, 0.85f);
            _poisonBottleMat.SetFloat("_Surface", 1f);
            _poisonBottleMat.SetColor("_EmissionColor", new Color(0.2f, 1f, 0.3f, 1f) * 2.5f);
        }

        if (_poisonZoneMat == null)
        {
            _poisonZoneMat = new Material(Shader.Find("Rouge/GroundZone"));
            ConfigureGroundZoneMaterial(
                _poisonZoneMat,
                new Color(0.26f, 1f, 0.36f, 0.78f),
                new Color(0.05f, 0.22f, 0.08f, 0.32f),
                0f,
                1.9f,
                0.24f,
                1.8f,
                0.7f,
                0.95f,
                1.15f,
                0.9f);
        }

        if (_burnPatchMat == null)
        {
            _burnPatchMat = new Material(Shader.Find("Rouge/GroundZone"));
            ConfigureGroundZoneMaterial(
                _burnPatchMat,
                new Color(1f, 0.48f, 0.08f, 0.85f),
                new Color(0.32f, 0.04f, 0.01f, 0.35f),
                2f,
                2.35f,
                0.18f,
                5.2f,
                1.45f,
                1.9f,
                1.2f,
                1.15f);
        }

        for (int i = 0; i < MaxPoisonBottles; i++)
        {
            if (_poisonBottleVisuals[i] == null)
            {
                _poisonBottleVisuals[i] = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                Destroy(_poisonBottleVisuals[i].GetComponent<Collider>());
                _poisonBottleVisuals[i].name = "Poison Bottle " + i;
                _poisonBottleVisuals[i].GetComponent<MeshRenderer>().material = _poisonBottleMat;
                _poisonBottleVisuals[i].SetActive(false);
            }

            _activePoisonBottles[i].Active = false;
        }

        for (int i = 0; i < MaxPoisonZones; i++)
        {
            if (_poisonZoneVisuals[i] == null)
            {
                _poisonZoneVisuals[i] = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                Destroy(_poisonZoneVisuals[i].GetComponent<Collider>());
                _poisonZoneVisuals[i].name = "Poison Zone " + i;
                _poisonZoneVisuals[i].GetComponent<MeshRenderer>().material = _poisonZoneMat;
                ConfigureGroundAoEVisual(_poisonZoneVisuals[i].GetComponent<MeshRenderer>(), _poisonZoneMat);
                _poisonZoneVisuals[i].SetActive(false);
            }

            _activePoisonZones[i].Active = false;
        }

        for (int i = 0; i < MaxBurnPatches; i++)
        {
            if (_burnPatchVisuals[i] == null)
            {
                _burnPatchVisuals[i] = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                Destroy(_burnPatchVisuals[i].GetComponent<Collider>());
                _burnPatchVisuals[i].name = "Burn Patch " + i;
                _burnPatchVisuals[i].GetComponent<MeshRenderer>().material = _burnPatchMat;
                ConfigureGroundAoEVisual(_burnPatchVisuals[i].GetComponent<MeshRenderer>(), _burnPatchMat);
                _burnPatchVisuals[i].SetActive(false);
            }

            _activeBurnPatches[i].Active = false;
        }

        _shockwaveCooldownTimer = 0f;
        _meteorCooldownTimer = 0f;
        _iceZoneCooldownTimer = 0f;
        _poisonCooldownTimer = 0f;
        _dashCooldownTimer = 0f;
        _dashSpinTimer = 0f;
        _dashSpinAngle = 0f;
        _hasActiveSustainedSkill = false;
        _activeSustainedSkillType = default;
        _activeSustainedSkillPriority = 0;

        if (_dashVisual == null)
        {
            _dashVisual = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(_dashVisual.GetComponent<Collider>());
            _dashVisual.name = "Whirlwind Visual";
            _dashMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            _dashMat.color = new Color(1f, 0.88f, 0.35f, 0.55f);
            _dashMat.SetFloat("_Surface", 1f);
            _dashMat.SetColor("_EmissionColor", new Color(1f, 0.75f, 0.2f, 1f) * 4f);
            _dashVisual.GetComponent<MeshRenderer>().material = _dashMat;
            _dashVisual.SetActive(false);
        }



        Material meteorMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        meteorMat.color = new Color(1f, 0.3f, 0.0f, 0.9f);
        meteorMat.SetColor("_EmissionColor", new Color(1f, 0.4f, 0.0f, 1f) * 5f);
        for (int mi = 0; mi < MeteorVisualMax; mi++)
        {
            if (_meteorVisuals[mi] == null)
            {
                _meteorVisuals[mi] = new GameObject("Meteor_" + mi);
                _meteorVisuals[mi].AddComponent<MeshFilter>().sharedMesh = _vfxSphereMesh;
                _meteorVisuals[mi].AddComponent<MeshRenderer>().material = meteorMat;
                _meteorVisuals[mi].SetActive(false);
            }
            _meteorVisualTimers[mi] = 0f;
        }

        _initialized = true;
        _currentMaxEnemies = 10;
        _spawnTimer = 0f;
        ScheduleSimulation(0.016f);
    }

    private void ConfigureGroundAoEVisual(Renderer renderer, Material material)
    {
        if (renderer == null)
        {
            return;
        }

        if (material != null)
        {
            material.renderQueue = 2450;
            if (material.HasProperty("_ZWrite"))
            {
                material.SetFloat("_ZWrite", 0f);
            }
        }

        renderer.sortingOrder = -50;
        renderer.receiveShadows = false;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    private void ConfigureGroundZoneMaterial(
        Material material,
        Color primaryColor,
        Color secondaryColor,
        float zoneType,
        float noiseScale,
        float edgeIrregularity,
        float pulseSpeed,
        float flowSpeed,
        float emissionStrength,
        float coreStrength,
        float rimStrength)
    {
        if (material == null)
        {
            return;
        }

        material.color = primaryColor;
        material.renderQueue = 2450;
        material.SetColor("_Color", primaryColor);
        material.SetColor("_SecondaryColor", secondaryColor);
        material.SetFloat("_ZoneType", zoneType);
        material.SetFloat("_NoiseScale", noiseScale);
        material.SetFloat("_EdgeIrregularity", edgeIrregularity);
        material.SetFloat("_PulseSpeed", pulseSpeed);
        material.SetFloat("_FlowSpeed", flowSpeed);
        material.SetFloat("_EmissionStrength", emissionStrength);
        material.SetFloat("_CoreStrength", coreStrength);
        material.SetFloat("_RimStrength", rimStrength);
    }

    private void ApplyCameraEffects()
    {
        Camera camera = RougeCameraFollow.ResolveCamera();
        if (camera == null)
        {
            return;
        }

        RougeCameraFollow cameraFollow = camera.GetComponent<RougeCameraFollow>();
        if (cameraFollow != null)
        {
            RougeCameraFollow.SetRuntimeEffects(_cameraLiftOffset, _cameraFovOffset);
            _cameraLiftOffset = Mathf.Lerp(_cameraLiftOffset, 0f, 8f * Time.deltaTime);
            _cameraFovOffset = Mathf.Lerp(_cameraFovOffset, 0f, 7f * Time.deltaTime);
            return;
        }

        if (_cameraLiftOffset != 0f)
        {
            camera.transform.position += Vector3.up * _cameraLiftOffset;
        }

        if (!camera.orthographic)
        {
            if (_baseCameraFov < 1f)
            {
                _baseCameraFov = camera.fieldOfView;
            }

            float targetFov = _baseCameraFov + _cameraFovOffset;
            camera.fieldOfView = Mathf.Lerp(camera.fieldOfView, targetFov, 14f * Time.deltaTime);
        }

        _cameraLiftOffset = Mathf.Lerp(_cameraLiftOffset, 0f, 8f * Time.deltaTime);
        _cameraFovOffset = Mathf.Lerp(_cameraFovOffset, 0f, 7f * Time.deltaTime);
    }

    private void BuildNeighborOffsets()
    {
        int index = 0;
        for (int y = -1; y <= 1; y++)
        {
            for (int x = -1; x <= 1; x++)
            {
                _neighborOffsets[index++] = new int2(x * 73856093, y * 19349663);
            }
        }
    }

    private void CaptureObstacles()
    {
        Collider[] colliders = UnityEngine.Object.FindObjectsByType<Collider>(FindObjectsSortMode.None);
        int count = 0;
        for (int i = 0; i < colliders.Length; i++)
        {
            Collider collider = colliders[i];
            if (collider == null || !collider.enabled || !collider.gameObject.activeInHierarchy) continue;
            if ((obstacleLayers.value & (1 << collider.gameObject.layer)) == 0) continue;
            if (player != null && collider.transform == player.transform) continue;
            if (collider.bounds.size.y < 0.2f || collider.bounds.size.x > 80f) continue;
            count++;
        }

        _obstacleCount = count;
        _obstacles = new NativeArray<RougeObstacle>(Mathf.Max(1, count), Allocator.Persistent);

        int obstacleIndex = 0;
        for (int i = 0; i < colliders.Length; i++)
        {
            Collider collider = colliders[i];
            if (collider == null || !collider.enabled || !collider.gameObject.activeInHierarchy) continue;
            if ((obstacleLayers.value & (1 << collider.gameObject.layer)) == 0) continue;
            if (player != null && collider.transform == player.transform) continue;
            if (collider.bounds.size.y < 0.2f || collider.bounds.size.x > 80f) continue;

            if (collider is SphereCollider sphere)
            {
                float2 center = new float2(sphere.transform.position.x, sphere.transform.position.z);
                float r = sphere.radius * Mathf.Max(sphere.transform.lossyScale.x, sphere.transform.lossyScale.z);
                _obstacles[obstacleIndex++] = new RougeObstacle { Type = 1, Center = center, CircleRadius = r, Padding = obstaclePadding };
            }
            else if (collider is CapsuleCollider capsule)
            {
                float2 center = new float2(capsule.transform.position.x, capsule.transform.position.z);
                float r = capsule.radius * Mathf.Max(capsule.transform.lossyScale.x, capsule.transform.lossyScale.z);
                _obstacles[obstacleIndex++] = new RougeObstacle { Type = 1, Center = center, CircleRadius = r, Padding = obstaclePadding };
            }
            else
            {
                Bounds bounds = collider.bounds;
                float2 min = new float2(bounds.min.x, bounds.min.z);
                float2 max = new float2(bounds.max.x, bounds.max.z);
                _obstacles[obstacleIndex++] = new RougeObstacle { Type = 0, Min = min, Max = max, Padding = obstaclePadding };
            }
        }
    }

    private void SeedEnemies()
    {
        float2 center = player != null ? player.PlanarPosition : float2.zero;
        for (int i = 0; i < enemyCount; i++)
        {
            uint hash = math.hash(new uint2((uint)i + 1u, 0x9E3779B9u));
            float angle = ((hash & 0xFFFFu) / 65535f) * math.PI * 2f;
            float distance = math.lerp(spawnRadiusMin, spawnRadiusMax, ((hash >> 16) & 0xFFFFu) / 65535f);
            float speedScale = math.lerp(0.9f, 1.15f, ((hash >> 8) & 0xFFu) / 255f);
            float2 pos = center + new float2(math.cos(angle), math.sin(angle)) * distance;
            pos.x = math.clamp(pos.x, -arenaHalfExtent + 2f, arenaHalfExtent - 2f);
            pos.y = math.clamp(pos.y, -arenaHalfExtent + 2f, arenaHalfExtent - 2f);
            _positionsA[i] = new float4(pos.x, renderHeight, pos.y, enemyRadius);
            _velocitiesA[i] = float4.zero;
            _stateA[i] = new float4(enemyMaxHealth, enemyRadius, enemyMaxSpeed * speedScale, 0f);
            _effectStateA[i] = default;
        }
    }

    private void UpdateBullets(float dt)
    {
        if (!IsSkillEnabled(PlayerSkillType.AutoShoot))
        {
            _fireTimer = 0f;
            _activeBulletCount = 0;
            _bulletMin = float2.zero;
            _bulletMax = float2.zero;
            return;
        }

        _fireTimer -= dt;
        if (_fireTimer <= 0f)
        {
            FireBullets();
            _fireTimer += fireInterval;
        }

        float2 playerPos = player != null ? player.PlanarPosition : float2.zero;
        float maxDistanceSq = (arenaHalfExtent + 40f) * (arenaHalfExtent + 40f);

        float2 minB = new float2(float.MaxValue, float.MaxValue);
        float2 maxB = new float2(float.MinValue, float.MinValue);

        for (int i = 0; i < _activeBulletCount;)
        {
            RougeBullet bullet = _bullets[i];
            bullet.Previous = bullet.Current;
            bullet.Current += bullet.Velocity * dt;
            bullet.Life -= dt;

            if (bullet.Life <= 0f || math.lengthsq(bullet.Current - playerPos) > maxDistanceSq)
            {
                int last = _activeBulletCount - 1;
                if (i != last)
                {
                    _bullets[i] = _bullets[last];
                }
                _activeBulletCount--;
                continue;
            }

            minB = math.min(minB, math.min(bullet.Previous, bullet.Current));
            maxB = math.max(maxB, math.max(bullet.Previous, bullet.Current));

            _bullets[i] = bullet;
            i++;
        }

        _bulletMin = minB - new float2(bulletRadius + 2f, bulletRadius + 2f);
        _bulletMax = maxB + new float2(bulletRadius + 2f, bulletRadius + 2f);
    }

    private void RenderBullets()
    {
        if (_activeBulletCount <= 0 || _bulletMesh == null || _bulletMaterial == null) return;

        for (int startIndex = 0; startIndex < _activeBulletCount; startIndex += _bulletRenderMatrices.Length)
        {
            int batchCount = Mathf.Min(_bulletRenderMatrices.Length, _activeBulletCount - startIndex);
            for (int i = 0; i < batchCount; i++)
            {
                RougeBullet bullet = _bullets[startIndex + i];
                Vector3 pos = new Vector3(bullet.Current.x, renderHeight + 0.5f, bullet.Current.y);
                Vector3 scale = Vector3.one * (bullet.Radius * 2f);
                _bulletRenderMatrices[i] = Matrix4x4.TRS(pos, Quaternion.identity, scale);
            }

            Graphics.DrawMeshInstanced(_bulletMesh, 0, _bulletMaterial, _bulletRenderMatrices, batchCount);
        }
    }

    private void FireBullets()
    {
        if (!IsSkillEnabled(PlayerSkillType.AutoShoot))
        {
            return;
        }

        if (player == null)
        {
            return;
        }

        float2 origin = player.PlanarPosition;
        Vector3 aim = player.AimDirection;
        float2 baseDir = math.normalizesafe(new float2(aim.x, aim.z), new float2(0f, 1f));
        int shotCount = math.min(bulletsPerShot, maxBullets - _activeBulletCount);
        if (shotCount <= 0)
        {
            return;
        }

        for (int i = 0; i < shotCount; i++)
        {
            float t = shotCount == 1 ? 0.5f : i / (float)(shotCount - 1);
            float angleOffset = math.lerp(-spreadAngle, spreadAngle, t) * math.PI / 180f;
            float2 dir = Rotate(baseDir, angleOffset);
            _bullets[_activeBulletCount++] = new RougeBullet
            {
                Previous = origin,
                Current = origin + dir * 0.25f,
                Velocity = dir * bulletSpeed,
                Radius = bulletRadius,
                Damage = bulletDamage,
                Life = bulletLifetime,
                EffectFlags = (int)_autoShootEffects.Tags,
                EffectKnockbackCenter = (int)_autoShootEffects.KnockbackCenter,
                EffectKnockbackForce = _autoShootEffects.KnockbackForce,
                EffectLaunchHeight = _autoShootEffects.LaunchHeight,
                EffectLaunchLandingRadius = _autoShootEffects.LaunchLandingRadius,
                EffectPoisonSpreadRadius = _autoShootEffects.PoisonSpreadRadius,
                EffectSlowPercent = _autoShootEffects.SlowPercent,
                EffectSlowDuration = _autoShootEffects.SlowDuration,
                EffectCurseExplosionDamage = _autoShootEffects.CurseExplosionDamage,
                EffectCurseExplosionRadius = _autoShootEffects.CurseExplosionRadius,
                EffectBurnDamage = _autoShootEffects.BurnDamage,
                EffectBurnDuration = _autoShootEffects.BurnDuration
            };
        }
    }

    private void RenderExplosions()
    {
        if (_explosionCount <= 0 || _expPosBuffer == null || _vfxSphereMesh == null || _vfxExplosionMat == null) return;

        _expPosBuffer.SetData(_expPosData, 0, 0, _explosionCount);
        _expStateBuffer.SetData(_expStateData, 0, 0, _explosionCount);

        _vfxExplosionMat.SetBuffer(PositionScaleBufferId, _expPosBuffer);
        _vfxExplosionMat.SetBuffer("_StateBuffer", _expStateBuffer);
        _vfxExplosionMat.SetFloat(ScaleMultiplierId, 1f);

        _expDrawArgs[1] = (uint)_explosionCount;
        _expArgsBuffer.SetData(_expDrawArgs);

        Bounds bounds = new Bounds(transform.position, new Vector3(1000f, 100f, 1000f));
        Graphics.DrawMeshInstancedIndirect(
            _vfxSphereMesh,
            0,
            _vfxExplosionMat,
            bounds,
            _expArgsBuffer,
            0,
            null,
            ShadowCastingMode.Off,
            false,
            gameObject.layer);
    }

    private void RenderDeathBursts()
    {
        if (_deathBurstCount <= 0 || _deathPosBuffer == null || _deathStateBuffer == null || _deathArgsBuffer == null || _vfxSphereMesh == null || _vfxDeathMat == null) return;

        _deathPosBuffer.SetData(_deathPosData, 0, 0, _deathBurstCount);
        _deathStateBuffer.SetData(_deathStateData, 0, 0, _deathBurstCount);

        _vfxDeathMat.SetBuffer(PositionScaleBufferId, _deathPosBuffer);
        _vfxDeathMat.SetBuffer("_StateBuffer", _deathStateBuffer);
        _vfxDeathMat.SetFloat(ScaleMultiplierId, 1f);

        _deathDrawArgs[0] = _vfxSphereMesh.GetIndexCount(0);
        _deathDrawArgs[1] = (uint)_deathBurstCount;
        _deathDrawArgs[2] = _vfxSphereMesh.GetIndexStart(0);
        _deathDrawArgs[3] = _vfxSphereMesh.GetBaseVertex(0);
        _deathDrawArgs[4] = 0;
        _deathArgsBuffer.SetData(_deathDrawArgs);

        Bounds bounds = new Bounds(transform.position, new Vector3(1000f, 100f, 1000f));
        Graphics.DrawMeshInstancedIndirect(
            _vfxSphereMesh,
            0,
            _vfxDeathMat,
            bounds,
            _deathArgsBuffer,
            0,
            null,
            ShadowCastingMode.Off,
            false,
            gameObject.layer);
    }

    private void RenderEnemies()
    {
        if (_positionBuffer == null || enemyMesh == null || enemyMaterial == null) return;

        int drawCount = Mathf.Clamp(_currentMaxEnemies, 0, enemyCount);
        if (drawCount <= 0)
        {
            return;
        }

        _positionBuffer.SetData(_positionsA, 0, 0, drawCount);
        _stateBuffer.SetData(_stateA, 0, 0, drawCount);

        enemyMaterial.SetBuffer(PositionScaleBufferId, _positionBuffer);
        enemyMaterial.SetBuffer("_StateBuffer", _stateBuffer);
        enemyMaterial.SetColor(BaseColorId, new Color(0.88f, 0.18f, 0.18f, 1f));
        enemyMaterial.SetFloat(ScaleMultiplierId, enemyVisualScale);

        Vector3 center = player != null ? player.transform.position : transform.position;
        float extent = math.max(arenaHalfExtent, despawnDistance) * 2f;
        Bounds bounds = new Bounds(center, new Vector3(extent, 32f, extent));

        _drawArgs[1] = (uint)drawCount;
        _argsBuffer.SetData(_drawArgs);

        Graphics.DrawMeshInstancedIndirect(
            enemyMesh,
            0,
            enemyMaterial,
            bounds,
            _argsBuffer,
            0,
            null,
            ShadowCastingMode.Off,
            false,
            gameObject.layer);
    }

    private void ScheduleSimulation(float dt)
    {
        int activeEnemyCount = Mathf.Clamp(_currentMaxEnemies, 0, enemyCount);
        if (activeEnemyCount <= 0)
        {
            _simulationHandle = default;
            return;
        }

        int activeChunkCount = Mathf.Max(1, Mathf.CeilToInt(activeEnemyCount / (float)sortBatchSize));
        float cellSize = math.max(separationRadius, enemyRadius * 2.5f);
        float invCellSize = 1f / math.max(cellSize, 0.001f);

        JobHandle handle = new BuildEnemyKeysJob
        {
            PositionScaleIn = _positionsA,
            EnemyKeys = _enemyKeys,
            InvCellSize = invCellSize,
            HashMask = _hashMask
        }.ScheduleBatch(activeEnemyCount, sortBatchSize);

        handle = ScheduleRadixSort(handle, activeEnemyCount, activeChunkCount);

        handle = new ClearGridJob
        {
            CellCounts = _cellCounts,
            CellOffsets = _cellOffsets
        }.ScheduleBatch(_hashSize, sortBatchSize, handle);

        handle = new BuildCellOffsetsJob
        {
            SortedKeys = _enemyKeys,
            CellOffsets = _cellOffsets,
            CellCounts = _cellCounts
        }.ScheduleBatch(activeEnemyCount, sortBatchSize, handle);

        handle = new ReorderEnemiesJob
        {
            SortedKeys = _enemyKeys,
            PositionScaleIn = _positionsA,
            VelocityIn = _velocitiesA,
            StateIn = _stateA,
            EffectStateIn = _effectStateA,
            PositionScaleOut = _positionsB,
            VelocityOut = _velocitiesB,
            StateOut = _stateB,
            EffectStateOut = _effectStateB
        }.ScheduleBatch(activeEnemyCount, sortBatchSize, handle);

        float2 playerPos = player != null ? player.PlanarPosition : float2.zero;
        handle = new SimulateEnemiesJob
        {
            SortedKeys = _enemyKeys,
            PositionScaleIn = _positionsB,
            VelocityIn = _velocitiesB,
            StateIn = _stateB,
            EffectStateIn = _effectStateB,
            PositionScaleOut = _positionsA,
            VelocityOut = _velocitiesA,
            StateOut = _stateA,
            EffectStateOut = _effectStateA,
            CellOffsets = _cellOffsets,
            CellCounts = _cellCounts,
            NeighborOffsets = _neighborOffsets,
            Bullets = _bullets,
            BulletCount = _activeBulletCount,
            Obstacles = _obstacles,
            ObstacleCount = _obstacleCount,
            PlayerPos = playerPos,
            PlayerDamageCount = _playerDamageCount,
            EnemyKillCount = _enemyKillCount,
            ExplosionQueue = _explosionQueue.AsParallelWriter(),
            SkillEventQueue = _skillEventQueue.AsParallelWriter(),
            EnemyMaxHealth = enemyMaxHealth * (1f + currentLevel * 0.15f),
            EnemyRadius = math.min(enemyRadius * (1f + currentLevel * 0.05f), enemyRadius * 2.5f),
            EnemyMaxSpeed = enemyMaxSpeed * math.min(1f + currentLevel * 0.02f, 1.8f),
            ArenaHalfExtent = arenaHalfExtent,
            SpawnRadiusMin = spawnRadiusMin,
            SpawnRadiusMax = spawnRadiusMax,
            DespawnDistanceSq = despawnDistance * despawnDistance,
            ChaseAcceleration = chaseAcceleration,
            CurrentMaxEnemies = _currentMaxEnemies,
            VelocityDamping = velocityDamping,
            SeparationRadius = separationRadius,
            SeparationStrength = separationStrength,
            ObstacleLookAhead = obstacleLookAhead,
            ObstacleRepulsion = obstacleRepulsion,
            ObstacleOrbitStrength = obstacleOrbitStrength,
            KnockbackResist = math.max(0.1f, 1f - currentLevel * 0.0002f),
            PlayerContactEnabled = IsPlayerContactEnabled(),
            DefeatEnemyOnPlayerContact = _playerContactDefeatEnemyOnContact,
            PlayerContactPadding = playerContactPadding,
            SkillAreas = _skillAreasDb,
            SkillAreaCount = _skillAreaCount,
            BulletMin = _bulletMin,
            BulletMax = _bulletMax,
            RenderHeight = renderHeight,
            DeltaTime = dt,
            InvCellSize = invCellSize,
            HashMask = _hashMask,
            FrameSeed = (uint)(Time.frameCount * 1664525 + 1013904223),
            SkillKillCounts = _skillKillCounts,
            BombDmgMult   = math.clamp(0.3f + _skillLevels[1] * 0.035f, 0.3f, 2.0f),
            LaserDmgMult  = math.clamp(0.3f + _skillLevels[2] * 0.035f, 0.3f, 2.0f),
            MeleeDmgMult  = math.clamp(0.3f + _skillLevels[3] * 0.035f, 0.3f, 2.0f),
            OrbitDmgMult  = math.clamp(2.0f + _skillLevels[4] * 0.5f, 2.0f, 15.0f),
            BulletDmgMult = math.clamp(0.3f + _skillLevels[5] * 0.035f, 0.3f, 2.0f)
        }.ScheduleBatch(activeEnemyCount, simulationBatchSize, handle);

        _simulationHandle = handle;
    }

    private JobHandle ScheduleRadixSort(JobHandle dependency, int activeEnemyCount, int activeChunkCount)
    {
        JobHandle handle = dependency;
        for (int shift = 32; shift < 64; shift += 8)
        {
            handle = new LocalHistogramJob
            {
                Keys = _enemyKeys,
                Histograms = _histograms,
                BatchSize = sortBatchSize,
                Shift = shift,
                ChunkCount = activeChunkCount
            }.ScheduleBatch(activeEnemyCount, sortBatchSize, handle);

            handle = new BinLocalPrefixSumBatchJob
            {
                Histograms = _histograms,
                BinTotals = _binTotals,
                ChunkCount = activeChunkCount
            }.ScheduleBatch(256, 64, handle);

            handle = new GlobalBinSumJob
            {
                BinTotals = _binTotals
            }.Schedule(handle);

            handle = new ApplyGlobalOffsetBatchJob
            {
                Histograms = _histograms,
                BinTotals = _binTotals,
                ChunkCount = activeChunkCount
            }.ScheduleBatch(256, 64, handle);

            handle = new ScatterJob
            {
                SrcKeys = _enemyKeys,
                DstKeys = _tempEnemyKeys,
                Histograms = _histograms,
                BatchSize = sortBatchSize,
                Shift = shift,
                ChunkCount = activeChunkCount
            }.ScheduleBatch(activeEnemyCount, sortBatchSize, handle);

            handle = new CopyArrayJob
            {
                Src = _tempEnemyKeys,
                Dst = _enemyKeys
            }.ScheduleBatch(activeEnemyCount, sortBatchSize, handle);
        }

        return handle;
    }

    private void SpawnAOERing(Vector3 center, float radius, float duration, Color color = default)
    {
        if (color == default) color = new Color(1f, 0.5f, 0f, 1f); // default orange
        for (int i = 0; i < MaxAOERings; i++)
        {
            if (_aoeRingTimers[i] <= 0f)
            {
                _aoeRingTimers[i] = duration;
                _aoeRingMaxTimes[i] = duration;
                _aoeRingMaxRadius[i] = radius;
                _aoeRingPositions[i] = center;
                _aoeRingColors[i] = color;
                return;
            }
        }
    }

    private void UpdateAOERings(float dt)
    {
        for (int i = 0; i < MaxAOERings; i++)
        {
            if (_aoeRingTimers[i] > 0f)
            {
                _aoeRingTimers[i] -= dt;
                if (_aoeRingTimers[i] <= 0f)
                {
                    _aoeRingTimers[i] = 0f;
                }
            }
        }
    }

    private void UpdateBurnPatches(float dt)
    {
        for (int i = 0; i < MaxBurnPatches; i++)
        {
            if (!_activeBurnPatches[i].Active)
            {
                if (_burnPatchVisuals[i] != null)
                {
                    _burnPatchVisuals[i].SetActive(false);
                }

                continue;
            }

            _activeBurnPatches[i].Timer -= dt;
            if (_activeBurnPatches[i].Timer <= 0f)
            {
                _activeBurnPatches[i].Active = false;
                if (_burnPatchVisuals[i] != null)
                {
                    _burnPatchVisuals[i].SetActive(false);
                }

                continue;
            }

            if (_burnPatchVisuals[i] != null)
            {
                float normalizedLifetime = 1f - (_activeBurnPatches[i].Timer / math.max(0.01f, BurnGroundDuration));
                float pulse = 1f + math.sin((_survivalTime + i * 0.41f) * 6.5f) * 0.12f;
                float drift = math.sin((_survivalTime + i * 0.63f) * 1.7f) * 0.18f;
                _burnPatchVisuals[i].SetActive(true);
                _burnPatchVisuals[i].transform.position = new Vector3(_activeBurnPatches[i].Position.x + drift, renderHeight + 0.03f, _activeBurnPatches[i].Position.y - drift * 0.45f);
                _burnPatchVisuals[i].transform.rotation = Quaternion.Euler(0f, normalizedLifetime * 110f, 0f);
                _burnPatchVisuals[i].transform.localScale = new Vector3(_activeBurnPatches[i].Radius * 2f * pulse, 0.08f, _activeBurnPatches[i].Radius * 2f / math.max(pulse, 0.01f));
            }

            TryAddSkillArea(new RougeSkillArea
            {
                Type = 11,
                Position = _activeBurnPatches[i].Position,
                Radius = _activeBurnPatches[i].Radius,
                EffectFlags = (int)SkillHitEffectTag.Burn,
                EffectBurnDamage = _activeBurnPatches[i].Damage,
                EffectBurnDuration = _activeBurnPatches[i].BurnDuration
            });
        }
    }

    private void ActivateBurnPatch(float2 position, float radius, float duration, float damage, float burnDuration)
    {
        for (int i = 0; i < MaxBurnPatches; i++)
        {
            if (_activeBurnPatches[i].Active)
            {
                continue;
            }

            _activeBurnPatches[i] = new RougeBurnPatchState
            {
                Active = true,
                Position = position,
                Radius = math.max(1f, radius),
                Timer = duration,
                Damage = damage,
                BurnDuration = burnDuration
            };
            return;
        }

        int replaceIndex = 0;
        float shortestTimer = _activeBurnPatches[0].Timer;
        for (int i = 1; i < MaxBurnPatches; i++)
        {
            if (_activeBurnPatches[i].Timer < shortestTimer)
            {
                shortestTimer = _activeBurnPatches[i].Timer;
                replaceIndex = i;
            }
        }

        _activeBurnPatches[replaceIndex] = new RougeBurnPatchState
        {
            Active = true,
            Position = position,
            Radius = math.max(1f, radius),
            Timer = duration,
            Damage = damage,
            BurnDuration = burnDuration
        };
    }

    private void RenderAOERings()
    {
        if (_cylinderMesh == null || _aoeRingMat == null || _aoeRingPropertyBlock == null)
        {
            return;
        }

        for (int i = 0; i < MaxAOERings; i++)
        {
            if (_aoeRingTimers[i] <= 0f)
            {
                continue;
            }

            float progress = 1f - math.max(0f, _aoeRingTimers[i] / math.max(0.01f, _aoeRingMaxTimes[i]));
            float currentRadius = _aoeRingMaxRadius[i] * math.sqrt(progress);
            float ringHeight = 0.18f + math.sin(progress * math.PI) * 0.4f;
            float alpha = math.sin(progress * math.PI);
            Vector3 center = _aoeRingPositions[i];
            center.y = math.max(center.y, renderHeight + 0.045f);
            Matrix4x4 matrix = Matrix4x4.TRS(center, Quaternion.identity, new Vector3(currentRadius * 2f, ringHeight, currentRadius * 2f));

            _aoeRingPropertyBlock.Clear();
            _aoeRingPropertyBlock.SetFloat("_InnerRadiusRatio", math.lerp(0.68f, 0.92f, progress));
            Color color = _aoeRingColors[i];
            color.a *= alpha;
            _aoeRingPropertyBlock.SetColor("_Color", color);

            Graphics.DrawMesh(_cylinderMesh, matrix, _aoeRingMat, gameObject.layer, null, 0, _aoeRingPropertyBlock, UnityEngine.Rendering.ShadowCastingMode.Off, false, null, false);
        }
    }

    private void RenderTornados()
    {
        if (_tornadoPosBuffer == null || tornadoMesh == null || _tornadoMat == null) return;
        
        _activeTornadoCount = 0;
        float dt = Time.deltaTime;
        
        for (int i = 0; i < MaxTornados; i++)
        {
            if (_tornadoLifeTimers[i] > 0f)
            {
                _tornadoLifeTimers[i] -= dt;
                float progress = 1f - math.max(0f, _tornadoLifeTimers[i] / _tornadoMaxTimes[i]);
                float maxRadius = _tornadoPosData[i].w;
                float currentRadius = math.lerp(maxRadius, 0.1f, math.pow(progress, 3f)); // Shrink to center quickly near end
                
                _tornadoStateData[i] = new float4(currentRadius * 2f, 100f, currentRadius * 2f, 1f - progress);
                
                if (_activeTornadoCount != i)
                {
                    _tornadoPosData[_activeTornadoCount] = _tornadoPosData[i];
                    _tornadoStateData[_activeTornadoCount] = _tornadoStateData[i];
                    _tornadoLifeTimers[_activeTornadoCount] = _tornadoLifeTimers[i];
                    _tornadoMaxTimes[_activeTornadoCount] = _tornadoMaxTimes[i];
                    _tornadoLifeTimers[i] = 0f;
                }
                _activeTornadoCount++;
            }
        }

        for (int ti = _activeTornadoCount; ti < MaxTornados; ti++)
        {
            _tornadoPosData[ti] = new float4(99999f, -999f, 99999f, 0f);
            _tornadoStateData[ti] = float4.zero;
        }

        _tornadoPosBuffer.SetData(_tornadoPosData);
        _tornadoStateBuffer.SetData(_tornadoStateData);

        _tornadoMat.SetBuffer(PositionScaleBufferId, _tornadoPosBuffer);
        _tornadoMat.SetBuffer("_StateBuffer", _tornadoStateBuffer);
        _tornadoMat.SetFloat(ScaleMultiplierId, 1f);

        _tornadoDrawArgs[1] = (uint)_activeTornadoCount;
        _tornadoArgsBuffer.SetData(_tornadoDrawArgs);

        if (_activeTornadoCount > 0)
        {
            Bounds bounds = new Bounds(transform.position, new Vector3(1000f, 100f, 1000f));
            Graphics.DrawMeshInstancedIndirect(
                tornadoMesh,
                0,
                _tornadoMat,
                bounds,
                _tornadoArgsBuffer,
                0,
                null,
                ShadowCastingMode.Off,
                false,
                gameObject.layer);
        }
    }

    private void Dispose()
    {
        _simulationHandle.Complete();
        _simulationHandle = default;
        _initialized = false;
        _activeBulletCount = 0;
        _hasActiveSustainedSkill = false;
        _activeSustainedSkillType = default;
        _activeSustainedSkillPriority = 0;

        if (_tornadoVisual) Destroy(_tornadoVisual);
        if (_bombVisuals != null)
            for (int i=0; i<MaxBombs; i++) if (_bombVisuals[i]) { Destroy(_bombVisuals[i]); _bombVisuals[i] = null; }
        if (_laserVisual) Destroy(_laserVisual);
        if (_laserExtraVisuals != null)
            for (int li = 0; li < _laserExtraVisuals.Length; li++)
                if (_laserExtraVisuals[li] != null) { Destroy(_laserExtraVisuals[li]); _laserExtraVisuals[li] = null; }
        if (_tornadoMat) Destroy(_tornadoMat);
        if (_laserMat) Destroy(_laserMat);
        if (_meleeMat) Destroy(_meleeMat);
        if (_meleeVisual) Destroy(_meleeVisual);
        if (_meleeFinisherMat) Destroy(_meleeFinisherMat);
        if (_meleeFinisherVisual) Destroy(_meleeFinisherVisual);
        if (_spikeMat) Destroy(_spikeMat);
        if (_spikeVisuals != null)
            for (int iSpkD = 0; iSpkD < _spikeVisuals.Length; iSpkD++)
                if (_spikeVisuals[iSpkD] != null) { Destroy(_spikeVisuals[iSpkD]); _spikeVisuals[iSpkD] = null; }
        if (_orbitMat) Destroy(_orbitMat);
        if (_orbitVisuals != null)
        {
            for (int i = 0; i < _orbitVisuals.Count; i++) Destroy(_orbitVisuals[i]);
            _orbitVisuals.Clear();
        }
        if (_shockwaveVisual) Destroy(_shockwaveVisual);
        if (_shockwaveMat) Destroy(_shockwaveMat);
        if (_iceZoneVisual) Destroy(_iceZoneVisual);
        if (_iceZoneMat) Destroy(_iceZoneMat);
        if (_dashVisual) Destroy(_dashVisual);
        if (_dashMat) Destroy(_dashMat);
        if (_poisonBottleMat) Destroy(_poisonBottleMat);
        if (_poisonZoneMat) Destroy(_poisonZoneMat);
        if (_burnPatchMat) Destroy(_burnPatchMat);
        for (int i = 0; i < MaxPoisonBottles; i++)
            if (_poisonBottleVisuals[i] != null) { Destroy(_poisonBottleVisuals[i]); _poisonBottleVisuals[i] = null; }
        for (int i = 0; i < MaxPoisonZones; i++)
            if (_poisonZoneVisuals[i] != null) { Destroy(_poisonZoneVisuals[i]); _poisonZoneVisuals[i] = null; }
        for (int i = 0; i < MaxBurnPatches; i++)
            if (_burnPatchVisuals[i] != null) { Destroy(_burnPatchVisuals[i]); _burnPatchVisuals[i] = null; }
        if (_aoeRingMat) Destroy(_aoeRingMat);
        if (_shockwaveRingMat) Destroy(_shockwaveRingMat);
        for (int ri = 0; ri < MaxAOERings; ri++)
            if (_aoeRingVisuals[ri] != null) { Destroy(_aoeRingVisuals[ri]); _aoeRingVisuals[ri] = null; }
        for (int si = 0; si < ShockwaveRingCount; si++)
            if (_shockwaveRingVisuals[si] != null) { Destroy(_shockwaveRingVisuals[si]); _shockwaveRingVisuals[si] = null; }
        for (int mi = 0; mi < MeteorVisualMax; mi++)
            if (_meteorVisuals[mi] != null) { Destroy(_meteorVisuals[mi]); _meteorVisuals[mi] = null; }

        ReleaseNative(ref _expPosData);
        ReleaseNative(ref _expStateData);
        _expPosBuffer?.Release(); _expPosBuffer = null;
        _expStateBuffer?.Release(); _expStateBuffer = null;
        _expArgsBuffer?.Release(); _expArgsBuffer = null;
        ReleaseNative(ref _deathPosData);
        ReleaseNative(ref _deathStateData);
        _deathPosBuffer?.Release(); _deathPosBuffer = null;
        _deathStateBuffer?.Release(); _deathStateBuffer = null;
        _deathArgsBuffer?.Release(); _deathArgsBuffer = null;
        ReleaseNative(ref _tornadoPosData);
        ReleaseNative(ref _tornadoStateData);
        _tornadoPosBuffer?.Release(); _tornadoPosBuffer = null;
        _tornadoStateBuffer?.Release(); _tornadoStateBuffer = null;
        _tornadoArgsBuffer?.Release(); _tornadoArgsBuffer = null;

        ReleaseNative(ref _skillAreasDb);
        ReleaseNative(ref _skillKillCounts);

        ReleaseNative(ref _positionsA);
        ReleaseNative(ref _positionsB);
        ReleaseNative(ref _velocitiesA);
        ReleaseNative(ref _velocitiesB);
        ReleaseNative(ref _stateA);
        ReleaseNative(ref _stateB);
        ReleaseNative(ref _effectStateA);
        ReleaseNative(ref _effectStateB);
        ReleaseNative(ref _enemyKeys);
        ReleaseNative(ref _tempEnemyKeys);
        ReleaseNative(ref _cellOffsets);
        ReleaseNative(ref _cellCounts);
        ReleaseNative(ref _neighborOffsets);
        ReleaseNative(ref _histograms);
        ReleaseNative(ref _binTotals);
        ReleaseNative(ref _bullets);
        ReleaseNative(ref _obstacles);
        ReleaseNative(ref _playerDamageCount);
        ReleaseNative(ref _enemyKillCount);
        if (_explosionQueue.IsCreated) _explosionQueue.Dispose();
        if (_skillEventQueue.IsCreated) _skillEventQueue.Dispose();

        _positionBuffer?.Release();
        _positionBuffer = null;
        _stateBuffer?.Release();
        _stateBuffer = null;
        _argsBuffer?.Release();
        _argsBuffer = null;
    }

    private static void ReleaseNative<T>(ref NativeArray<T> array) where T : struct
    {
        if (array.IsCreated)
        {
            array.Dispose();
        }
    }

    private static Mesh CreateFallbackQuad()
    {
        Mesh mesh = new Mesh
        {
            name = "RougeQuad"
        };
        Vector3[] vertices = {
            new Vector3(-0.5f, -0.5f, -0.5f), new Vector3(0.5f, -0.5f, -0.5f), new Vector3(0.5f, 0.5f, -0.5f), new Vector3(-0.5f, 0.5f, -0.5f),
            new Vector3(-0.5f, -0.5f, 0.5f), new Vector3(0.5f, -0.5f, 0.5f), new Vector3(0.5f, 0.5f, 0.5f), new Vector3(-0.5f, 0.5f, 0.5f) 
        };
        int[] triangles = {
            0, 2, 1, 0, 3, 2,  2, 3, 6, 3, 7, 6,
            1, 2, 5, 2, 6, 5,  0, 1, 4, 1, 5, 4,
            0, 4, 3, 4, 7, 3,  5, 6, 4, 6, 7, 4 
        };
        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    private static Material CreateFallbackMaterial()
    {
        Shader shader = Shader.Find("Rouge/IndirectInstancedURP");
        if (shader == null)
        {
            throw new InvalidOperationException("Missing Rouge/IndirectInstancedURP shader.");
        }

        Material material = new Material(shader)
        {
            enableInstancing = true,
            hideFlags = HideFlags.DontSave
        };
        return material;
    }

    private static float2 Rotate(float2 value, float angle)
    {
        float sin = math.sin(angle);
        float cos = math.cos(angle);
        return new float2(value.x * cos - value.y * sin, value.x * sin + value.y * cos);
    }

    private void EnsureSkillConfigInitialized()
    {
        if (skillConfig == null)
        {
            skillConfig = PlayerSkillConfigSet.CreateDefault();
        }

        skillConfig.EnsureInitialized();
        RougeInputManager.Instance.ApplySkillPresentationDefaults(skillConfig);
    }

    private void ApplySkillConfigValues()
    {
        EnsureSkillConfigInitialized();
        MigrateLegacySkillConfig();

        maxBullets = Mathf.Max(1, skillConfig.AutoShoot.GetIntValue(skillConfig.AutoShoot.MaxBullets, 0));
        fireInterval = Mathf.Max(0.01f, skillConfig.AutoShoot.GetValue(skillConfig.AutoShoot.FireInterval, 0));
        bulletSpeed = Mathf.Max(0.1f, skillConfig.AutoShoot.GetValue(skillConfig.AutoShoot.BulletSpeed, 0));
        bulletRadius = Mathf.Max(0.01f, skillConfig.AutoShoot.GetValue(skillConfig.AutoShoot.BulletRadius, 0));
        bulletDamage = Mathf.Max(0.1f, skillConfig.AutoShoot.GetValue(skillConfig.AutoShoot.BulletDamage, 0));
        bulletLifetime = Mathf.Max(0.05f, skillConfig.AutoShoot.GetValue(skillConfig.AutoShoot.BulletLifetime, 0));
        bulletsPerShot = Mathf.Max(1, skillConfig.AutoShoot.GetIntValue(skillConfig.AutoShoot.BulletsPerShot, 0));
        spreadAngle = Mathf.Max(0f, skillConfig.AutoShoot.GetValue(skillConfig.AutoShoot.SpreadAngle, 0));
                _autoShootEffects = skillConfig.AutoShoot.Effects.Resolve(currentLevel, 60);
                ApplyPlayerContactSkillConfigValues();
      //  tornadoPullForce = skillConfig.LightPillar.GetValue(skillConfig.LightPillar.PullForce, 0);
       // tornadoSpinForce = 85f;
       // tornadoLiftForce = skillConfig.LightPillar.GetValue(skillConfig.LightPillar.VerticalForce, 0);
      //  tornadoDuration = skillConfig.LightPillar.GetValue(skillConfig.LightPillar.VisualDuration, 0);
      //  tornadoCooldown = skillConfig.LightPillar.GetValue(skillConfig.LightPillar.Cooldown, 0);
       // tornadoTravelSpeed = skillConfig.LightPillar.GetValue(skillConfig.LightPillar.DistanceStep, 0);
    }

    private void MigrateLegacySkillConfig()
    {
        if (Mathf.Approximately(skillConfig.Shockwave.GetBaseValue(skillConfig.Shockwave.Duration), 1.8f) && Mathf.Approximately(skillConfig.Shockwave.GetBaseValue(skillConfig.Shockwave.RingStartRadius), 2f))
        {
            skillConfig.Shockwave.Presentation.DisplayName = "Shockwave";
            skillConfig.Shockwave.Duration = PlayerSkillScaling.Constant(0.6f);
            skillConfig.Shockwave.LaunchDuration = PlayerSkillScaling.Constant(0.18f);
            skillConfig.Shockwave.SlamDuration = PlayerSkillScaling.Constant(0.12f);
            skillConfig.Shockwave.JumpHeight = PlayerSkillScaling.Constant(12f);
            skillConfig.Shockwave.RingStartRadius = PlayerSkillScaling.Constant(8f);
            skillConfig.Shockwave.RingEndRadius = PlayerSkillScaling.Constant(48f);
            skillConfig.Shockwave.ImpactRadius = PlayerSkillScaling.Constant(38f);
            skillConfig.Shockwave.ImpactRingCount = PlayerSkillScaling.Constant(5f);
            skillConfig.Shockwave.RingThickness = PlayerSkillScaling.Constant(7f);
            skillConfig.Shockwave.ImpactDamage = PlayerSkillScaling.Constant(2400f);
     //       skillConfig.Shockwave.PullForce = PlayerSkillScaling.Constant(-240f);
    //        skillConfig.Shockwave.VerticalForce = PlayerSkillScaling.Constant(125f);
            skillConfig.Shockwave.CameraLift = PlayerSkillScaling.Constant(1.35f);
            skillConfig.Shockwave.CameraFovKick = PlayerSkillScaling.Constant(8f);
            skillConfig.Shockwave.LandingShake = PlayerSkillScaling.Constant(0.26f);
        }

        if (Mathf.Approximately(skillConfig.Dash.GetBaseValue(skillConfig.Dash.Distance), 12f) && Mathf.Approximately(skillConfig.Dash.GetBaseValue(skillConfig.Dash.InvincibilityDuration), 0.33f))
        {
            skillConfig.Dash.Presentation.DisplayName = "Whirlwind";
            skillConfig.Dash.Duration = PlayerSkillScaling.Constant(1.5f);
            skillConfig.Dash.Distance = PlayerSkillScaling.Constant(21f);
            skillConfig.Dash.InvincibilityDuration = PlayerSkillScaling.Constant(1.5f);
            skillConfig.Dash.SpinDamage = PlayerSkillScaling.Constant(9f);
            skillConfig.Dash.HitRadius = PlayerSkillScaling.Constant(8f);
            skillConfig.Dash.BladeWidth = PlayerSkillScaling.Constant(4f);
            skillConfig.Dash.BladeLength = PlayerSkillScaling.Constant(11f);
            skillConfig.Dash.BladeThickness = PlayerSkillScaling.Constant(0.75f);
            skillConfig.Dash.MaxSpinRate = PlayerSkillScaling.Constant(3000f);
            skillConfig.Dash.ImpactRadius = PlayerSkillScaling.Constant(10f);
            skillConfig.Dash.ImpactDamage = PlayerSkillScaling.Constant(260f);
      //      skillConfig.Dash.PullForce = PlayerSkillScaling.Constant(320f);
      //      skillConfig.Dash.VerticalForce = PlayerSkillScaling.Constant(90f);
        }

        if (Mathf.Approximately(skillConfig.MeleeSlash.GetBaseValue(skillConfig.MeleeSlash.SlashVerticalForce), 18f))
        {
            skillConfig.MeleeSlash.SlashVerticalForce = PlayerSkillScaling.Constant(80f);
            skillConfig.MeleeSlash.ThrustVerticalForce = PlayerSkillScaling.Constant(90f);
            skillConfig.MeleeSlash.CenterSpikeVerticalForce = PlayerSkillScaling.Constant(90f);
            skillConfig.MeleeSlash.SideSpikeVerticalForce = PlayerSkillScaling.Constant(65f);
        }

        // if (Mathf.Approximately(skillConfig.LightPillar.GetBaseValue(skillConfig.LightPillar.VerticalForce), 45f))
        // {
        //     skillConfig.LightPillar.VerticalForce = PlayerSkillScaling.Constant(70f);
        // }
    }

    private void SpawnExplosionVFX(Vector3 worldPos, float radius)
    {
        for (int i = 0; i < MaxExplosions; i++)
        {
            if (_expTimers[i] <= 0f)
            {
                _expPosData[i] = new float4(worldPos.x, worldPos.y, worldPos.z, 0f);
                _expStateData[i] = new float4(0f, 0f, 0f, 0f);
                _expMaxScales[i] = radius;
                _expTimers[i] = 0.35f;
                return;
            }
        }
    }

    private void SpawnDeathBurstVFX(Vector3 worldPos, float radius)
    {
        for (int i = 0; i < MaxDeathBursts; i++)
        {
            if (_deathTimers[i] > 0f)
            {
                continue;
            }

            _deathPosData[i] = new float4(worldPos.x, worldPos.y, worldPos.z, radius);
            _deathStateData[i] = float4.zero;
            _deathTimers[i] = DeathBurstDuration;
            _deathDurations[i] = DeathBurstDuration;
            _deathRiseSpeeds[i] = math.max(2.5f, radius * 1.6f);
            return;
        }
    }

}

public struct RougeSkillArea
{
    public int Type;
    public float2 Position;
    public float2 Direction;
    public float Radius;
    public float Length;
    public float Damage;
    public float PullForce;
    public float VerticalForce;
    public float AuxA;
    public float AuxB;
    public float AuxC;
    public float AuxD;
    public int EffectFlags;
    public int EffectKnockbackCenter;
    public float EffectKnockbackForce;
    public float EffectLaunchHeight;
    public float EffectLaunchLandingRadius;
    public float EffectPoisonSpreadRadius;
    public float EffectSlowPercent;
    public float EffectSlowDuration;
    public float EffectCurseExplosionDamage;
    public float EffectCurseExplosionRadius;
    public float EffectBurnDamage;
    public float EffectBurnDuration;
}

public struct RougeEnemyEffectState
{
    public float PoisonTimer;
    public float PoisonTickTimer;
    public float PoisonSpreadRadius;
    public float SlowPercent;
    public float SlowTimer;
    public float BurnTimer;
    public float BurnTickTimer;
    public float BurnDamage;
    public float BurnReapplyCooldown;
    public float CurseExplosionDamage;
    public float CurseExplosionRadius;
    public float LaunchLandingDamage;
    public float LaunchLandingRadius;
    public float BurnDuration;
}

public enum RougeSkillEventType
{
    LaunchLandingExplosion = 1,
    PoisonSpread = 2,
    CurseExplosion = 3,
    BurnGround = 4,
    EnemyDeathBurst = 5
}

public struct RougeSkillEvent
{
    public int Type;
    public float2 Position;
    public float Radius;
    public float Damage;
    public float Duration;
}

public struct RougeBullet
{
    public float2 Previous;
    public float2 Current;
    public float2 Velocity;
    public float Radius;
    public float Damage;
    public float Life;
    
    public int EffectFlags;
    public int EffectKnockbackCenter;
    public float EffectKnockbackForce;
    public float EffectLaunchHeight;
    public float EffectLaunchLandingRadius;
    public float EffectPoisonSpreadRadius;
    public float EffectSlowPercent;
    public float EffectSlowDuration;
    public float EffectCurseExplosionDamage;
    public float EffectCurseExplosionRadius;
    public float EffectBurnDamage;
    public float EffectBurnDuration;
}


