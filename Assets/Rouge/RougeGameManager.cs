using System;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

[DefaultExecutionOrder(-50)]
public partial class RougeGameManager : MonoBehaviour
{
    private static readonly int PositionScaleBufferId = Shader.PropertyToID("_PositionScaleBuffer");
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ScaleMultiplierId = Shader.PropertyToID("_ScaleMultiplier");
    private static readonly int VariationStrengthId = Shader.PropertyToID("_VariationStrength");
    private static readonly int BreakupScaleId = Shader.PropertyToID("_BreakupScale");
    private static readonly int BreakupStrengthId = Shader.PropertyToID("_BreakupStrength");
    private static readonly int PlayerFocusPositionId = Shader.PropertyToID("_PlayerFocusPosition");
    private static readonly int ShaderRenderHeightId = Shader.PropertyToID("_RenderHeight");
    private static readonly int HologramDissolveProgressId = Shader.PropertyToID("_DissolveProgress");

    [Header("References")]
    [SerializeField] private PlayerBase player;
    [SerializeField] private Mesh enemyMesh;
    [SerializeField] private Material enemyMaterial;
    private bool _ownsEnemyBillboardMaterial;
    private bool _ownsEnemyBillboardMesh;
    [SerializeField] private Material lightPillarBeamMaterial;
    [SerializeField] private Material laserBeamMaterial;

    [Header("Shader References")]
    [SerializeField] private Shader indirectInstancedShader;
    [SerializeField] private Shader vfxInstancedShader;
    [SerializeField] private Shader aoeRingShader;
    [SerializeField] private Shader groundZoneShader;
    [SerializeField] private Shader hologramShader;
    [SerializeField] private Shader techPanelShader;
    [SerializeField] private Shader laserBeamShader;
    [SerializeField] private Shader urpLitShader;

    private Mesh _bulletMesh;
    private Material _bulletMaterial;
    private bool _ownsBulletMaterial;

    [Header("Population")]
    [SerializeField, Range(1000, 500000)] private int enemyCount = 200000;
    [SerializeField] private float enemyMaxHealth = 10f;
    [SerializeField] private float enemyRadius = 0.3f;
    [SerializeField] private float enemyMaxSpeed = 6f;
    [SerializeField] private float enemyVisualScale = 1.35f;
    [SerializeField, Range(0f, 0.4f)] private float enemyVariationStrength = 0.18f;
    [SerializeField, Range(0.5f, 8f)] private float enemyBreakupScale = 3.8f;
    [SerializeField, Range(0f, 0.35f)] private float enemyBreakupStrength = 0.16f;

    [Header("Arena")]
    [SerializeField] private float arenaHalfExtent = 220f;
    [SerializeField] private float spawnRadiusMin = 50f;
    [SerializeField] private float spawnRadiusMax = 180f;
    [SerializeField] private float despawnDistance = 260f;
    [SerializeField] private float renderHeight = 0f;

    [Header("Camera")]
    [SerializeField, Range(0.5f, 5f)] private float cameraZoomMultiplier = 1f;
    [SerializeField, Range(0.02f, 0.5f)] private float cameraZoomScrollStep = 0.15f;

    [Header("Steering")]
    [SerializeField] private float chaseAcceleration = 22f;
    [SerializeField] private float velocityDamping = 0.96f;
    [SerializeField] private float separationRadius = 1.3f;
    [SerializeField] private float separationStrength = 10f;
    [SerializeField] private float crowdReliefRadius = 10f;
    [SerializeField] private float crowdReliefStrength = 16f;
    [SerializeField] private float crowdOrbitStrength = 8f;
    [SerializeField, Range(0f, 4f)] private float denseSeparationBoost = 1.25f;
    [SerializeField, Range(1, 32)] private int denseNeighborThreshold = 6;
    [SerializeField] private float flowFieldCellSize = 1.5f;
    [SerializeField, Range(1, 4)] private int flowFieldIterations = 3;
    [SerializeField, Range(64, 512)] private int flowFieldMaxGridDim = 512;
    [SerializeField, Range(0.05f, 2f)] private float flowFieldRefreshInterval = 0.5f;
    [SerializeField] private float densitySoftThreshold = 1.35f;
    [SerializeField] private float densityRepulsionStrength = 18f;
    [SerializeField] private float densityGradientClamp = 2.5f;
    [SerializeField, Range(0f, 0.6f)] private float densityResponseJitter = 0.18f;
    [SerializeField, Range(0f, 2f)] private float crowdReliefMaxDensityPressure = 0.75f;
    [SerializeField] private float flowFieldObstaclePadding = 1.2f;
    [SerializeField] private float obstaclePadding = 1.5f;
    [SerializeField] private float obstacleLookAhead = 3f;
    [SerializeField] private float obstacleRepulsion = 30f;
    [SerializeField] private float obstacleOrbitStrength = 18f;
    [SerializeField] private LayerMask obstacleLayers = -1;

    [Header("Pathfinding Targets")]
    [Tooltip("除 player 之外的额外寻路目标（体育馆设施、召唤物、诱饵等）。运行时也可用 AddTarget/RemoveTarget API。")]
    [SerializeField] private List<Transform> extraTargets = new List<Transform>();

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
    private NativeArray<int> _bulletCellHeads;
    private NativeArray<int> _bulletCellEntries;
    private NativeArray<int> _bulletCellNext;
    private NativeArray<int> _skillCellHeads;
    private NativeArray<int> _skillCellEntries;
    private NativeArray<int> _skillCellNext;
    private NativeArray<int> _enemyTargetCellHeads;
    private NativeArray<int> _enemyTargetCellNext;
    private NativeArray<RougeTowerTargetRequest> _towerTargetRequests;
    private NativeArray<int> _towerTargetResultIndices;
    private NativeArray<float> _towerTargetResultDistances;
    private NativeArray<int> _densityFieldFixed;
    private NativeArray<float> _flowDistanceField;
    private NativeArray<float> _flowDistanceScratch;
    private NativeArray<float2> _flowDirectionField;
    private NativeArray<byte> _flowBlockedCells;
    private NativeArray<RougeObstacle> _obstacles;
    private int _staticObstacleCount;
    private int _dynamicObstacleCount;
    private NativeArray<byte> _staticBlockedCells;
    private NativeArray<int> _flowGoalIndices;
    private const int MaxFlowGoalCount = 16;
    private static readonly List<RougeDynamicObstacle> s_dynamicObstacles = new List<RougeDynamicObstacle>();
    private readonly List<Transform> _runtimeExtraTargets = new List<Transform>();
    private NativeArray<int> _playerDamageCount;
    private NativeArray<int> _mainTowerDamageCount;
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
    private GraphicsBuffer _velocityRenderBuffer;
    private GraphicsBuffer _enemyKindRenderBuffer;
    private GraphicsBuffer _argsBuffer;
    private readonly uint[] _drawArgs = new uint[5];

    private JobHandle _simulationHandle;
    private bool _initialized;
    private int _hashSize;
    private int _hashMask;
    private int _chunkCount;
    private int _flowGridDim;
    private int _flowGridCellCount;
    private float2 _flowGridOrigin;
    private float _flowFieldRuntimeCellSize;
    private float _flowFieldRefreshCountdown;
    private bool _flowFieldReady;
    private int _activeBulletCount;
    private float _fireTimer;
    private bool _simulationResultBackBufferReady;
    private const int MaxJobifiedTowerCount = 1024;
    private int _towerTargetRequestCount;
    private int _towerTargetScheduledCount;
    private NativeArray<float> _towerLaserDamage;
    private NativeArray<int> _towerLaserDamageFrames;
    private NativeArray<byte> _towerDefenseEnemyKinds;
    private NativeArray<int> _enemyRenderKinds;
    private NativeArray<int> _towerDefenseGoldEarned;
    private NativeArray<float> _towerDamageByType;
    private NativeArray<int> _towerDamageByTypeFrames;
    private NativeArray<long> _towerDamageTotalsFixed;
    private int _towerLaserDamageFrame = 1;

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
    private bool _ownsTornadoMat;
    private MaterialPropertyBlock _hologramPropertyBlock;
    
    // Tornado VFX data
    private const int MaxTornados = 16;
    private int _activeTornadoCount;
    private NativeArray<float4> _tornadoPosData;
    private NativeArray<float4> _tornadoStateData;
    private float[] _tornadoLifeTimers = new float[MaxTornados];
    private float[] _tornadoMaxTimes = new float[MaxTornados];
    private float[] _tornadoRadiusMultipliers = new float[MaxTornados];
    private bool[] _tornadoImpactTriggered = new bool[MaxTornados];
    private float[] _tornadoImpactProgress = new float[MaxTornados];
    private float2[] _tornadoImpactPositions = new float2[MaxTornados];
    private float[] _tornadoImpactRadii = new float[MaxTornados];
    private float[] _tornadoImpactDamages = new float[MaxTornados];
    private float[] _tornadoImpactPullForces = new float[MaxTornados];
    private float[] _tornadoImpactVerticalForces = new float[MaxTornados];
    private float[] _tornadoImpactRingDurations = new float[MaxTornados];
    private Color[] _tornadoImpactRingColors = new Color[MaxTornados];
    private ResolvedSkillHitEffectConfig[] _tornadoImpactEffects = new ResolvedSkillHitEffectConfig[MaxTornados];
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
    private GameObject _laserMuzzleVisual;
    private const int MaxLaserSubBeams = 6;
    private GameObject[] _laserExtraVisuals = new GameObject[MaxLaserSubBeams];
    private Material _laserMat;
    private bool _ownsLaserMat;
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
    private Mesh _spikeMesh;
    private bool _ownsSpikeMesh;

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
    private bool _ownsDashMat;

    // --- Skateboard skill state ---
    private float _skateCooldownTimer;
    private int   _skatePhase;       // 0=idle 1=initJump 2=land 3=riding 4=trick 5=finale
    private float _skatePhaseTimer;
    private float _skateRideTimer;
    private Vector2 _skateBoardVelocity;
    private float _skateBoardRotYaw;
    private Vector3 _skateOriginPos;
    private Vector3 _skateLaunchEnd;
    private Vector3 _skateTrickOrigin;
    private Vector3 _skateTrickEnd;
    private float2  _skateBoardPos;
    private float2 _skateMoveDirection;
    private float2 _skateActionDirection;
    private int _skateTrickVariant;
    private bool  _skatePendingEnd;
    private bool  _skateSlamFired;
    private Vector3 _skateFinaleStart;
    private Vector3 _skateFinaleEnd;
    private GameObject _skateBoardVisual;
    private Material   _skateBoardMat;
    private bool _ownsMeleeMat;
    private bool _ownsMeleeFinisherMat;
    private bool _ownsSpikeMat;
    private bool _ownsSkateboardMat;
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

    // Skill kill tracking: [0]=light pillar [1]=leap/bomb [2]=laser/ice [3]=melee/shockwave [4]=orbit [5]=bullet
    private NativeArray<int> _skillKillCounts;
    private readonly int[] _skillTotalKills = new int[6];
    private readonly int[] _skillLevels = new int[6];
    private float _survivalTime;

    // AOE Ring VFX (shader-based flat ring)
    private const int MaxAOERings = 32;
    private int _aoeRingCount;
    private GameObject[] _aoeRingVisuals = new GameObject[MaxAOERings];
    private float[] _aoeRingTimers = new float[MaxAOERings];
    private float[] _aoeRingMaxTimes = new float[MaxAOERings];
    private float[] _aoeRingMaxRadius = new float[MaxAOERings];
    private Vector3[] _aoeRingPositions = new Vector3[MaxAOERings];
    private Color[] _aoeRingColors = new Color[MaxAOERings];
    private Material[] _aoeRingMaterials = new Material[MaxAOERings];
    private bool[] _aoeRingUseMaterialColor = new bool[MaxAOERings];
    [SerializeField] private Material _aoeRingMat;
    private MaterialPropertyBlock _aoeRingPropertyBlock;
    private Mesh _aoeRingMesh;
    private bool _ownsAoeRingMaterial;
    private bool _ownsAoeRingMesh;

    // Shockwave multi-ring system
    private const int ShockwaveRingCount = 5;
    private float[] _shockwaveRingTimers = new float[ShockwaveRingCount];
    private GameObject[] _shockwaveRingVisuals = new GameObject[ShockwaveRingCount];
    private Material _shockwaveRingMat;

    private void OnEnable()
    {
        if (!Application.isPlaying) return;
        Initialize();
    }

    private void OnDisable()
    {
        if (_initialized) Dispose();
    }

    private void OnValidate()
    {
        EnsureShaderReferenceDefaults();
        ApplySkillConfigValues();
        EnsureTowerDefenseConfigDefaults();
    }

    private UnityEngine.UI.Text _uiText;

    private void LateUpdate()
    {
        if (!_initialized) return;
        if (UsesTowerDefenseSpawners())
        {
            ApplyCameraEffects();
            return;
        }
        if (player == null) return;
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
                float extraPadding = obs.Type == RougeObstacle.CircleType ? obs.Padding + 0.5f : 0.5f;
                float2 resolved = RougeObstacleMath.ResolvePointOutside(obs, new float2(pos.x, pos.z), extraPadding);
                pos.x = resolved.x;
                pos.z = resolved.y;
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
        if (Time.unscaledDeltaTime > 0.00001f)
        {
            _fps = math.lerp(_fps, 1f / Time.unscaledDeltaTime, 5f * Time.unscaledDeltaTime);
        }

        if (!_initialized)
        {
            return;
        }

        _simulationHandle.Complete();
        FinalizeCompletedSimulationBuffers();
        EnsureTowerDefenseInitialized();
        UpdateTowerDefenseInput(Time.unscaledDeltaTime);

        if (_enemyKillCount.IsCreated)
        {
            int recentKills = _enemyKillCount[0];
            if (recentKills > 0)
            {
                totalKills += recentKills;
                AddTowerDefenseGoldForKills(recentKills);
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
                        ResizeBulletStorage(maxBullets);
                    }
                }
            }
        }

        if (_skillKillCounts.IsCreated)
        {
            for (int sk = 0; sk < 6; sk++)
            {
                int recent = _skillKillCounts[sk];
                if (recent > 0)
                {
                    _skillKillCounts[sk] = 0;
                    LevelScaledSkillConfig progressionConfig = GetSkillProgressionConfig(sk);
                    if (progressionConfig == null || progressionConfig.DisableLevelUp)
                    {
                        continue;
                    }

                    _skillTotalKills[sk] += recent;
                    _skillLevels[sk] = progressionConfig.EvaluateProgressionLevel(_skillTotalKills[sk]);
                }
            }
        }

        _survivalTime += Time.deltaTime;

        if (_invincibilityTimer > 0f)
        {
            _invincibilityTimer -= Time.deltaTime;
        }

        int damage = _playerDamageCount[0];
        if (damage > 0)
        {
            RemoveTowerDefenseAliveEstimate(damage);
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

        ApplyMainTowerContactDamage();

        if ((!UsesTowerDefenseSpawners() && playerHealth <= 0f) || IsMainTowerDestroyed())
        {
            TriggerTowerDefenseGameOver(!UsesTowerDefenseSpawners() && playerHealth <= 0f
                ? "PLAYER DOWN"
                : "MAIN TOWER DESTROYED");
            return;
        }

        if (IsTowerDefenseSimulationPaused())
        {
            RenderEnemies();
            RenderOrbitSphereVisuals();
            RenderTowerDefensePausedFrame();
            UpdateHudIfNeeded();
            return;
        }

        float dt = Mathf.Min(Time.deltaTime, 0.05f) * fixedSimulationDt;
        HandleCameraZoomInput();

        if (Input.GetKeyDown(KeyCode.Equals) || Input.GetKeyDown(KeyCode.Plus))
        {
            _currentMaxEnemies = Mathf.Min(enemyCount, _currentMaxEnemies + 10000);
        }

        if (Input.GetKeyDown(KeyCode.Minus))
        {
            _currentMaxEnemies = Mathf.Max(10, _currentMaxEnemies - 10000);
        }

        _spawnTimer += dt;
        if (!UsesTowerDefenseSpawners() && _spawnTimer > 1f)
        {
            _spawnTimer = 0f;
            if (_currentMaxEnemies < enemyCount)
            {
                int growth = 20 + currentLevel * 10 + (int)(_currentMaxEnemies * 0.02f);
                if (Input.GetKey(KeyCode.RightBracket)) growth *= 10;
                _currentMaxEnemies = Mathf.Min(enemyCount, _currentMaxEnemies + growth);
            }
        }

        if (UsesTowerDefenseSpawners())
        {
            _skillAreaCount = 0;
            _activeBulletCount = 0;
            _bulletMin = float2.zero;
            _bulletMax = float2.zero;
        }
        else
        {
            UpdateSkills(dt);
            ApplyPendingPlayerContactSkill();
        }

        while (_explosionQueue.TryDequeue(out float2 expPos))
        {
            if (_skillAreaCount < _skillAreasDb.Length)
            {
                _skillAreasDb[_skillAreaCount++] = new RougeSkillArea
                {
                    Type = 2,
                    Position = expPos,
                    Radius = 8f,
                    Damage = enemyMaxHealth * (1f + currentLevel * 0.15f) * 0.8f,
                    PullForce = -120f,
                    VerticalForce = 25f
                };

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
        UpdateTowerDefenseSimulation(dt);
        UpdateBullets(dt);
        RenderBullets();
        RenderAOERings();
        RenderEnemies();
        RenderOrbitSphereVisuals();
        RenderExplosions();
        RenderDeathBursts();
        RenderTornados();
        ScheduleSimulation(math.max(dt, 0.0001f));

        if (_uiText != null)
        {
            UpdateHudIfNeeded();
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
        enemyMesh = CreateFallbackQuad();
        _ownsEnemyBillboardMesh = true;
        enemyMaterial = CreateRuntimeMaterial("Rouge/EnemyBillboard", "Enemy 2D Billboard", true);
        _ownsEnemyBillboardMaterial = true;
        ApplyEnemySpriteSheetTextures();
        cameraZoomMultiplier = Mathf.Clamp(cameraZoomMultiplier, 0.5f, 5f);

        _bulletMesh = enemyMesh;
        _bulletMaterial = CreateRuntimeMaterial("Rouge/SpriteInstanced", "Bullet 2D Sprite", true);
        _ownsBulletMaterial = true;
        _bulletMaterial.SetTexture("_MainTex", Resources.Load<Texture2D>("Sprites/projectile_energy"));
        ApplyBaseColor(_bulletMaterial, Color.white);
        _bulletMaterial.enableInstancing = true;

        _hashSize = Mathf.NextPowerOfTwo(Mathf.Max(enemyCount * 2, 65536));
        _hashMask = _hashSize - 1;
        _chunkCount = Mathf.CeilToInt(enemyCount / (float)sortBatchSize);
        _flowFieldRuntimeCellSize = math.max(flowFieldCellSize, 0.5f);
        int maxFlowGridDim = math.clamp(flowFieldMaxGridDim, 64, 512);
        int computedFlowGridDim = Mathf.NextPowerOfTwo(Mathf.CeilToInt((arenaHalfExtent * 2f + _flowFieldRuntimeCellSize * 2f) / _flowFieldRuntimeCellSize));
        while (computedFlowGridDim > maxFlowGridDim)
        {
            _flowFieldRuntimeCellSize *= 1.15f;
            computedFlowGridDim = Mathf.NextPowerOfTwo(Mathf.CeilToInt((arenaHalfExtent * 2f + _flowFieldRuntimeCellSize * 2f) / _flowFieldRuntimeCellSize));
        }

        _flowGridDim = Mathf.Clamp(computedFlowGridDim, 32, maxFlowGridDim);
        _flowGridCellCount = _flowGridDim * _flowGridDim;
        float gridSpan = _flowGridDim * _flowFieldRuntimeCellSize;
        _flowGridOrigin = new float2(-gridSpan * 0.5f, -gridSpan * 0.5f);

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
        _bulletCellHeads = new NativeArray<int>(_flowGridCellCount, Allocator.Persistent);
        _skillCellHeads = new NativeArray<int>(_flowGridCellCount, Allocator.Persistent);
        _enemyTargetCellHeads = new NativeArray<int>(_flowGridCellCount, Allocator.Persistent);
        _enemyTargetCellNext = new NativeArray<int>(enemyCount, Allocator.Persistent);
        _towerTargetRequests = new NativeArray<RougeTowerTargetRequest>(MaxJobifiedTowerCount, Allocator.Persistent);
        _towerTargetResultIndices = new NativeArray<int>(
            MaxJobifiedTowerCount * FindTowerTargetsJob.MaxTargetsPerTower, Allocator.Persistent);
        _towerTargetResultDistances = new NativeArray<float>(
            MaxJobifiedTowerCount * FindTowerTargetsJob.MaxTargetsPerTower, Allocator.Persistent);
        _towerLaserDamage = new NativeArray<float>(enemyCount, Allocator.Persistent);
        _towerLaserDamageFrames = new NativeArray<int>(enemyCount, Allocator.Persistent);
        _towerDefenseEnemyKinds = new NativeArray<byte>(enemyCount, Allocator.Persistent);
        _enemyRenderKinds = new NativeArray<int>(enemyCount, Allocator.Persistent);
        _towerDefenseGoldEarned = new NativeArray<int>(1, Allocator.Persistent);
        _towerDamageByType = new NativeArray<float>(enemyCount * TowerDefenseVisuals.TowerTypeCount, Allocator.Persistent);
        _towerDamageByTypeFrames = new NativeArray<int>(enemyCount * TowerDefenseVisuals.TowerTypeCount, Allocator.Persistent);
        _towerDamageTotalsFixed = new NativeArray<long>(TowerDefenseVisuals.TowerTypeCount, Allocator.Persistent);
        _neighborOffsets = new NativeArray<int2>(9, Allocator.Persistent);
        _histograms = new NativeArray<int>(math.max(_chunkCount * 256, 256), Allocator.Persistent);
        _binTotals = new NativeArray<int>(256, Allocator.Persistent);
        _densityFieldFixed = new NativeArray<int>(_flowGridCellCount, Allocator.Persistent);
        _flowDistanceField = new NativeArray<float>(_flowGridCellCount, Allocator.Persistent);
        _flowDistanceScratch = new NativeArray<float>(_flowGridCellCount, Allocator.Persistent);
        _flowDirectionField = new NativeArray<float2>(_flowGridCellCount, Allocator.Persistent);
        _flowBlockedCells = new NativeArray<byte>(_flowGridCellCount, Allocator.Persistent);
        _staticBlockedCells = new NativeArray<byte>(_flowGridCellCount, Allocator.Persistent);
        _flowGoalIndices = new NativeArray<int>(MaxFlowGoalCount, Allocator.Persistent);
        ResizeBulletStorage(maxBullets);
        _playerDamageCount = new NativeArray<int>(1, Allocator.Persistent);
        _mainTowerDamageCount = new NativeArray<int>(1, Allocator.Persistent);
        _enemyKillCount = new NativeArray<int>(1, Allocator.Persistent);
        _explosionQueue = new NativeQueue<float2>(Allocator.Persistent);
        _skillEventQueue = new NativeQueue<RougeSkillEvent>(Allocator.Persistent);
        _enemyKillCount[0] = 0;
        totalKills = 0;
        currentLevel = 1;
        _skillKillCounts = new NativeArray<int>(6, Allocator.Persistent);
        _survivalTime = 0f;
        _flowFieldRefreshCountdown = 0f;
        _flowFieldReady = false;
        ResetHudRefreshState();
        System.Array.Clear(_skillTotalKills, 0, 6);
        System.Array.Clear(_skillLevels, 0, 6);
        for (int skillIndex = 0; skillIndex < _skillLevels.Length; skillIndex++)
        {
            _skillLevels[skillIndex] = GetInitialSkillProgressionLevel(skillIndex);
        }

        playerHealth = playerMaxHealth;

        PrepareTowerDefenseSceneBeforeNavigation();
        CaptureObstacles();
        BuildNeighborOffsets();
        SeedEnemies();

        _skillAreasDb = new NativeArray<RougeSkillArea>(1024, Allocator.Persistent);
        ResizeSkillAreaGridStorage(_skillAreasDb.Length);

        _positionBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, enemyCount, UnsafeUtility.SizeOf<float4>());
        _stateBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, enemyCount, UnsafeUtility.SizeOf<float4>());
        _velocityRenderBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, enemyCount, UnsafeUtility.SizeOf<float4>());
        _enemyKindRenderBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, enemyCount, sizeof(int));
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
        _simulationResultBackBufferReady = false;
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
            _vfxExplosionMat = CreateRuntimeMaterial("Rouge/VFXInstanced", "Explosion VFX", true);
            ApplyBaseColor(_vfxExplosionMat, new Color(1f, 0.4f, 0.1f, 0.7f));
            _vfxExplosionMat.enableInstancing = true;
        }

        if (_vfxDeathMat == null)
        {
            _vfxDeathMat = CreateRuntimeMaterial("Rouge/VFXInstanced", "Death VFX", true);
            ApplyBaseColor(_vfxDeathMat, new Color(1f, 0.92f, 0.84f, 0.42f));
            _vfxDeathMat.enableInstancing = true;
        }
        
        if (_tornadoMat == null)
        {
            Material resolvedLightPillarBeamMaterial = lightPillarBeamMaterial != null
                ? lightPillarBeamMaterial
                : skillConfig != null && skillConfig.LightPillar != null
                    ? skillConfig.LightPillar.BeamVisualMaterial
                    : null;
            _ownsTornadoMat = resolvedLightPillarBeamMaterial == null;
            if (resolvedLightPillarBeamMaterial != null)
            {
                _tornadoMat = resolvedLightPillarBeamMaterial;
            }
            else
            {
                _tornadoMat = CreateRuntimeMaterial("Rouge/VFXInstanced", "Light Pillar Tornado", true);
                ApplyBaseColor(_tornadoMat, new Color(1f, 0.98f, 0.86f, 0.82f));
                _tornadoMat.enableInstancing = true;
            }
        }

        // AOE Ring material
        if (_aoeRingMat == null)
        {
            Shader ringShader = Shader.Find("Rouge/AOERing");
            if (ringShader != null)
            {
                _aoeRingMat = new Material(ringShader);
                _ownsAoeRingMaterial = true;
                _aoeRingMat.SetColor("_Color", new Color(1f, 0.5f, 0.1f, 0.8f));
                _aoeRingMat.renderQueue = 2450;
            }
        }

        if (_aoeRingPropertyBlock == null)
        {
            _aoeRingPropertyBlock = new MaterialPropertyBlock();
        }

        if (_aoeRingMesh == null)
        {
            _aoeRingMesh = CreateAoERingMesh(96);
            _ownsAoeRingMesh = true;
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
                    _bombVisuals[b].GetComponent<MeshRenderer>().material = CreateRuntimeMaterial("Universal Render Pipeline/Lit", "Bomb Visual", false);
                    ApplyBaseColor(_bombVisuals[b].GetComponent<MeshRenderer>().sharedMaterial, Color.red);
                }
                _bombVisuals[b].SetActive(false);
            }
        }

        if (_laserVisual == null)
        {
            _laserVisual = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Destroy(_laserVisual.GetComponent<Collider>());
            _laserVisual.name = "Laser Visual";

            Shader laserShader = Shader.Find("Rouge/LaserBeam");
            if (laserBeamMaterial != null)
            {
                _laserMat = laserBeamMaterial;
                _ownsLaserMat = false;
            }
            else if (laserShader != null)
            {
                _laserMat = new Material(laserShader);
                _ownsLaserMat = true;
                _laserMat.SetColor("_CoreColor", new Color(2.8f, 3.2f, 3.5f, 1f));
                _laserMat.SetColor("_BeamColor", new Color(0.08f, 1.25f, 2.8f, 1f));
                _laserMat.SetColor("_GlowColor", new Color(0.12f, 0.25f, 2.2f, 1f));
                _laserMat.SetFloat("_CoreRadius", 0.18f);
                _laserMat.SetFloat("_BeamRadius", 0.52f);
                _laserMat.SetFloat("_GlowSoftness", 0.28f);
                _laserMat.SetFloat("_FlowScale", 28f);
                _laserMat.SetFloat("_FlowSpeed", 18f);
                _laserMat.SetFloat("_RibbonScale", 11f);
                _laserMat.SetFloat("_RibbonSpeed", 9f);
                _laserMat.SetFloat("_RibbonIntensity", 1.45f);
                _laserMat.SetFloat("_NoiseStrength", 0.24f);
                _laserMat.SetFloat("_PulseSpeed", 6f);
                _laserMat.SetFloat("_FresnelPower", 2.2f);
                _laserMat.SetFloat("_FresnelStrength", 1.25f);
                _laserMat.SetFloat("_EndFade", 0.08f);
                _laserMat.SetFloat("_Alpha", 1f);
            }
            else
            {
                _laserMat = CreateRuntimeMaterial("Universal Render Pipeline/Lit", "Laser Beam Fallback", false);
                _ownsLaserMat = true;
                ApplyBaseColor(_laserMat, new Color(0.1f, 1f, 1f, 0.9f));
                ApplyFloatIfPresent(_laserMat, "_Surface", 1f);
                ApplyFloatIfPresent(_laserMat, "_Blend", 0f);
                ApplyEmissionColor(_laserMat, new Color(0.2f, 0.8f, 1f, 1f) * 4f);
            }

            _laserVisual.GetComponent<MeshRenderer>().sharedMaterial = _laserMat;
            _laserVisual.SetActive(false);
        }

        if (_laserMuzzleVisual == null)
        {
            _laserMuzzleVisual = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(_laserMuzzleVisual.GetComponent<Collider>());
            _laserMuzzleVisual.name = "Laser Muzzle";
            _laserMuzzleVisual.GetComponent<MeshRenderer>().sharedMaterial = _laserMat;
            _laserMuzzleVisual.SetActive(false);
        }

        for (int li = 0; li < MaxLaserSubBeams; li++)
        {
            if (_laserExtraVisuals[li] == null)
            {
                _laserExtraVisuals[li] = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                Destroy(_laserExtraVisuals[li].GetComponent<Collider>());
                _laserExtraVisuals[li].name = "Laser Extra " + li;
                _laserExtraVisuals[li].GetComponent<MeshRenderer>().sharedMaterial = _laserMat;
                _laserExtraVisuals[li].SetActive(false);
            }
        }

        if (_meleeVisual == null)
        {
            _meleeVisual = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(_meleeVisual.GetComponent<Collider>());
            _meleeVisual.name = "Melee Slash";
            _ownsMeleeMat = skillConfig.MeleeSlash.SlashVisualMaterial == null;
            _meleeMat = skillConfig.MeleeSlash.SlashVisualMaterial != null
                ? skillConfig.MeleeSlash.SlashVisualMaterial
                : CreateFallbackHologramMaterial(new Color(0.16f, 0.95f, 1f, 1f), new Color(0.95f, 0.98f, 1f, 1f), 0.62f, 20f, 2.3f);
            _meleeVisual.GetComponent<MeshRenderer>().sharedMaterial = _meleeMat;
            _meleeVisual.SetActive(false);
        }

        MeshRenderer meleeRenderer = _meleeVisual != null ? _meleeVisual.GetComponent<MeshRenderer>() : null;
        if (meleeRenderer != null && meleeRenderer.sharedMaterial != _meleeMat)
        {
            meleeRenderer.sharedMaterial = _meleeMat;
        }

        if (_meleeFinisherVisual == null)
        {
            _meleeFinisherVisual = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(_meleeFinisherVisual.GetComponent<Collider>());
            _meleeFinisherVisual.name = "Melee Finisher Slam";
            _ownsMeleeFinisherMat = skillConfig.MeleeSlash.FinisherVisualMaterial == null;
            _meleeFinisherMat = skillConfig.MeleeSlash.FinisherVisualMaterial != null
                ? skillConfig.MeleeSlash.FinisherVisualMaterial
                : CreateFallbackHologramMaterial(new Color(1f, 0.74f, 0.22f, 1f), new Color(1f, 0.98f, 0.78f, 1f), 0.74f, 16f, 2.8f);
            _meleeFinisherVisual.GetComponent<MeshRenderer>().sharedMaterial = _meleeFinisherMat;
            _meleeFinisherVisual.SetActive(false);
        }

        MeshRenderer meleeFinisherRenderer = _meleeFinisherVisual != null ? _meleeFinisherVisual.GetComponent<MeshRenderer>() : null;
        if (meleeFinisherRenderer != null && meleeFinisherRenderer.sharedMaterial != _meleeFinisherMat)
        {
            meleeFinisherRenderer.sharedMaterial = _meleeFinisherMat;
        }

        if (_spikeMat == null)
        {
            _ownsSpikeMat = skillConfig.MeleeSlash.SpikeVisualMaterial == null;
            _spikeMat = skillConfig.MeleeSlash.SpikeVisualMaterial != null
                ? skillConfig.MeleeSlash.SpikeVisualMaterial
                : CreateFallbackHologramMaterial(new Color(0.18f, 1f, 0.86f, 1f), new Color(0.94f, 1f, 0.94f, 1f), 0.8f, 24f, 2.45f);
        }

        if (_spikeMesh == null)
        {
            _spikeMesh = CreateConeMesh(18);
            _ownsSpikeMesh = true;
        }

        for (int iSpkI = 0; iSpkI < 3; iSpkI++)
        {
            if (_spikeVisuals[iSpkI] == null)
            {
                _spikeVisuals[iSpkI] = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                Destroy(_spikeVisuals[iSpkI].GetComponent<Collider>());
                _spikeVisuals[iSpkI].name = "Spike " + iSpkI;
                MeshFilter spikeMeshFilter = _spikeVisuals[iSpkI].GetComponent<MeshFilter>();
                if (spikeMeshFilter != null && _spikeMesh != null)
                {
                    spikeMeshFilter.sharedMesh = _spikeMesh;
                }

                _spikeVisuals[iSpkI].GetComponent<MeshRenderer>().sharedMaterial = _spikeMat;
                _spikeVisuals[iSpkI].SetActive(false);
            }

            MeshFilter reboundSpikeMeshFilter = _spikeVisuals[iSpkI] != null ? _spikeVisuals[iSpkI].GetComponent<MeshFilter>() : null;
            if (reboundSpikeMeshFilter != null && reboundSpikeMeshFilter.sharedMesh != _spikeMesh)
            {
                reboundSpikeMeshFilter.sharedMesh = _spikeMesh;
            }

            MeshRenderer spikeRenderer = _spikeVisuals[iSpkI] != null ? _spikeVisuals[iSpkI].GetComponent<MeshRenderer>() : null;
            if (spikeRenderer != null && spikeRenderer.sharedMaterial != _spikeMat)
            {
                spikeRenderer.sharedMaterial = _spikeMat;
            }
        }

        if (_orbitMat == null)
        {
            _orbitMat = CreateRuntimeMaterial("Universal Render Pipeline/Lit", "Orbit Ball", false);
            ApplyBaseColor(_orbitMat, new Color(0.8f, 0.1f, 0.8f, 0.8f));
            ApplyFloatIfPresent(_orbitMat, "_Surface", 1f);
            ApplyEmissionColor(_orbitMat, new Color(0.8f, 0.1f, 0.8f, 1f) * 2f);
        }

        if (_shockwaveVisual == null)
        {
            _shockwaveVisual = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Destroy(_shockwaveVisual.GetComponent<Collider>());
            _shockwaveVisual.name = "Shockwave Visual";
            _shockwaveMat = CreateRuntimeMaterial("Universal Render Pipeline/Lit", "Shockwave", false);
            ApplyBaseColor(_shockwaveMat, new Color(1f, 0.6f, 0.0f, 0.5f));
            ApplyFloatIfPresent(_shockwaveMat, "_Surface", 1f);
            ApplyEmissionColor(_shockwaveMat, new Color(1f, 0.6f, 0.0f, 1f) * 3f);
            _shockwaveVisual.GetComponent<MeshRenderer>().material = _shockwaveMat;
            _shockwaveVisual.SetActive(false);
        }

        if (_iceZoneVisual == null)
        {
            _iceZoneVisual = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Destroy(_iceZoneVisual.GetComponent<Collider>());
            _iceZoneVisual.name = "Ice Zone Visual";
            _iceZoneMat = CreateRuntimeMaterial("Rouge/GroundZone", "Ice Zone", false);
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
            ConfigureGroundAoEVisual(_iceZoneVisual.GetComponent<MeshRenderer>(), _iceZoneMat);
            _iceZoneVisual.SetActive(false);
        }

        if (_poisonBottleMat == null)
        {
            _poisonBottleMat = CreateRuntimeMaterial("Universal Render Pipeline/Lit", "Poison Bottle", false);
            ApplyBaseColor(_poisonBottleMat, new Color(0.25f, 0.95f, 0.25f, 0.85f));
            ApplyFloatIfPresent(_poisonBottleMat, "_Surface", 1f);
            ApplyEmissionColor(_poisonBottleMat, new Color(0.2f, 1f, 0.3f, 1f) * 2.5f);
        }

        if (_poisonZoneMat == null)
        {
            _poisonZoneMat = CreateRuntimeMaterial("Rouge/GroundZone", "Poison Zone", false);
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
            _burnPatchMat = CreateRuntimeMaterial("Rouge/GroundZone", "Burn Patch", false);
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
            Material dashVisualMaterial = skillConfig != null && skillConfig.Dash != null
                ? skillConfig.Dash.BladeVisualMaterial
                : null;
            _ownsDashMat = dashVisualMaterial == null;
            _dashMat = dashVisualMaterial != null
                ? dashVisualMaterial
                : CreateFallbackTechPanelMaterial(new Color(0.2f, 0.86f, 1f, 1f), new Color(0.88f, 0.98f, 1f, 1f), 0.62f, 18f, 1.35f);
            _dashVisual.GetComponent<MeshRenderer>().sharedMaterial = _dashMat;
            _dashVisual.SetActive(false);
        }

        MeshRenderer dashRenderer = _dashVisual != null ? _dashVisual.GetComponent<MeshRenderer>() : null;
        if (dashRenderer != null && dashRenderer.sharedMaterial != _dashMat)
        {
            dashRenderer.sharedMaterial = _dashMat;
        }



        Material meteorMat = CreateRuntimeMaterial("Universal Render Pipeline/Lit", "Meteor", false);
        ApplyBaseColor(meteorMat, new Color(1f, 0.3f, 0.0f, 0.9f));
        ApplyEmissionColor(meteorMat, new Color(1f, 0.4f, 0.0f, 1f) * 5f);
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

        InitializeTowerDefense();
        _initialized = true;
        _currentMaxEnemies = UsesTowerDefenseSpawners() ? 0 : 10;
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
            RougeCameraFollow.SetRuntimeEffects(_cameraLiftOffset, _cameraFovOffset, cameraZoomMultiplier);
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

    private void HandleCameraZoomInput()
    {
        float scroll = Input.mouseScrollDelta.y;
        if (math.abs(scroll) <= 0.001f)
        {
            return;
        }

        cameraZoomMultiplier = Mathf.Clamp(cameraZoomMultiplier - scroll * cameraZoomScrollStep, 0.5f, 5f);
    }

    private int GetInitialSkillProgressionLevel(int progressionIndex)
    {
        LevelScaledSkillConfig progressionConfig = GetSkillProgressionConfig(progressionIndex);
        return progressionConfig != null ? progressionConfig.GetInitialSkillLevel() : 1;
    }

    private LevelScaledSkillConfig GetSkillProgressionConfig(int progressionIndex)
    {
        if (skillConfig == null)
        {
            return null;
        }

        for (int i = 0; i < PlayerSkillCatalog.ProgressionBindings.Length; i++)
        {
            PlayerSkillProgressBinding binding = PlayerSkillCatalog.ProgressionBindings[i];
            if (binding.ProgressionIndex == progressionIndex)
            {
                return skillConfig.GetLevelScaledConfig(binding.Type);
            }
        }

        return null;
    }

    private bool IsSkillProgressionLocked(int progressionIndex)
    {
        LevelScaledSkillConfig progressionConfig = GetSkillProgressionConfig(progressionIndex);
        return progressionConfig != null && progressionConfig.DisableLevelUp;
    }

    private string GetSkillProgressSummary(int progressionIndex)
    {
        LevelScaledSkillConfig progressionConfig = GetSkillProgressionConfig(progressionIndex);
        int totalSkillKills = progressionIndex >= 0 && progressionIndex < _skillTotalKills.Length ? _skillTotalKills[progressionIndex] : 0;
        if (progressionConfig == null)
        {
            return totalSkillKills.ToString();
        }

        if (progressionConfig.DisableLevelUp)
        {
            return $"{totalSkillKills} | LOCK";
        }

        int remainingKills = progressionConfig.GetRemainingKillsToNextLevel(totalSkillKills);
        if (remainingKills <= 0)
        {
            return $"{totalSkillKills} | MAX";
        }

        return $"{totalSkillKills} | NEXT {remainingKills}";
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

    private void FinalizeCompletedSimulationBuffers()
    {
        if (!_simulationResultBackBufferReady)
        {
            return;
        }

        SwapNativeArrays(ref _positionsA, ref _positionsB);
        SwapNativeArrays(ref _velocitiesA, ref _velocitiesB);
        SwapNativeArrays(ref _stateA, ref _stateB);
        SwapNativeArrays(ref _effectStateA, ref _effectStateB);
        _simulationResultBackBufferReady = false;
    }

    private static void SwapNativeArrays<T>(ref NativeArray<T> left, ref NativeArray<T> right) where T : struct
    {
        NativeArray<T> temp = left;
        left = right;
        right = temp;
    }

    private void CaptureObstacles()
    {
        RougeDynamicObstacle[] dynamicObstacles = UnityEngine.Object.FindObjectsByType<RougeDynamicObstacle>(FindObjectsSortMode.None);
        for (int i = 0; i < dynamicObstacles.Length; i++)
        {
            if (dynamicObstacles[i] != null && dynamicObstacles[i].isActiveAndEnabled)
            {
                RegisterDynamicObstacle(dynamicObstacles[i]);
            }
        }

        Collider[] colliders = UnityEngine.Object.FindObjectsByType<Collider>(FindObjectsSortMode.None);
        float capturedObstaclePadding = math.min(math.max(obstaclePadding, 0f), 0.18f);
        int count = 0;
        for (int i = 0; i < colliders.Length; i++)
        {
            Collider collider = colliders[i];
            if (collider == null || !collider.enabled || !collider.gameObject.activeInHierarchy) continue;
            if (collider.GetComponentInParent<RougeMainTower>() != null) continue;
            if ((obstacleLayers.value & (1 << collider.gameObject.layer)) == 0) continue;
            if (player != null && collider.transform == player.transform) continue;
            if (collider.bounds.size.y < 0.2f) continue;
            // 排除已挂 RougeDynamicObstacle 的 collider（它们走动态注册路径）
            if (collider.GetComponent<RougeDynamicObstacle>() != null) continue;
            if (RougeDynamicObstacle.TryCreateObstacleFromCollider(collider, capturedObstaclePadding, out _))
            {
                count++;
            }
        }

        // 预留动态障碍空间：静态 + 动态扩展容量（运行时还可再扩）
        int dynamicCapacity = math.max(s_dynamicObstacles.Count * 2, 32);
        int totalCapacity = math.max(1, count + dynamicCapacity);
        _obstacles = new NativeArray<RougeObstacle>(totalCapacity, Allocator.Persistent);

        int obstacleIndex = 0;
        for (int i = 0; i < colliders.Length; i++)
        {
            Collider collider = colliders[i];
            if (collider == null || !collider.enabled || !collider.gameObject.activeInHierarchy) continue;
            if (collider.GetComponentInParent<RougeMainTower>() != null) continue;
            if ((obstacleLayers.value & (1 << collider.gameObject.layer)) == 0) continue;
            if (player != null && collider.transform == player.transform) continue;
            if (collider.bounds.size.y < 0.2f || collider.bounds.size.x > 80f) continue;
            if (collider.GetComponent<RougeDynamicObstacle>() != null) continue;

            if (RougeDynamicObstacle.TryCreateObstacleFromCollider(collider, capturedObstaclePadding, out RougeObstacle obstacle))
            {
                _obstacles[obstacleIndex++] = obstacle;
            }
        }

        _staticObstacleCount = obstacleIndex;
        _dynamicObstacleCount = 0;
        _obstacleCount = _staticObstacleCount;

        // 一次性把静态障碍栅格化进 _staticBlockedCells，运行时只需 memcpy + 仅 dynamic 段重栅格化
        BakeStaticBlockedCells();
    }

    private void BakeStaticBlockedCells()
    {
        if (!_staticBlockedCells.IsCreated || !_obstacles.IsCreated) return;
        // 清零
        unsafe
        {
            UnsafeUtility.MemClear(_staticBlockedCells.GetUnsafePtr(), _staticBlockedCells.Length * sizeof(byte));
        }
        if (_staticObstacleCount <= 0) return;

        float invCellSize = 1f / math.max(_flowFieldRuntimeCellSize, 0.001f);
        // 同步执行（一次性）
        new RasterizeObstacleGridJob
        {
            Obstacles = _obstacles,
            StartIndex = 0,
            ObstacleCount = _staticObstacleCount,
            BlockedCells = _staticBlockedCells,
            GridOrigin = _flowGridOrigin,
            InvCellSize = invCellSize,
            GridDim = _flowGridDim,
            ExtraPadding = flowFieldObstaclePadding
        }.Run();
    }

    /// <summary>每帧把已注册的动态障碍刷新到 _obstacles 的动态后缀段。</summary>
    private void RefreshDynamicObstacleSnapshot()
    {
        // 清理无效引用
        for (int i = s_dynamicObstacles.Count - 1; i >= 0; i--)
        {
            if (s_dynamicObstacles[i] == null)
            {
                s_dynamicObstacles.RemoveAt(i);
            }
        }

        int needed = s_dynamicObstacles.Count;
        int requiredCapacity = _staticObstacleCount + needed;

        // 容量不够时扩张（极少发生：CaptureObstacles 已预留余量）
        if (!_obstacles.IsCreated || _obstacles.Length < requiredCapacity)
        {
            int newCapacity = math.max(requiredCapacity, math.max(_obstacles.IsCreated ? _obstacles.Length * 2 : 32, 32));
            NativeArray<RougeObstacle> grown = new NativeArray<RougeObstacle>(newCapacity, Allocator.Persistent);
            if (_obstacles.IsCreated && _staticObstacleCount > 0)
            {
                NativeArray<RougeObstacle>.Copy(_obstacles, grown, _staticObstacleCount);
            }
            if (_obstacles.IsCreated) _obstacles.Dispose();
            _obstacles = grown;
        }

        int written = 0;
        for (int i = 0; i < needed; i++)
        {
            RougeDynamicObstacle src = s_dynamicObstacles[i];
            if (src == null || !src.isActiveAndEnabled) continue;
            if (src.GetComponentInParent<RougeMainTower>() != null) continue;
            _obstacles[_staticObstacleCount + written] = src.Snapshot();
            written++;
        }

        _dynamicObstacleCount = written;
        _obstacleCount = _staticObstacleCount + _dynamicObstacleCount;
    }

    /// <summary>静态障碍发生变化（如可破坏地形拆除）时调用，重新扫描场景并烘焙静态阻挡 mask。</summary>
    public void RebuildStaticPathfinding()
    {
        if (!_initialized) return;
        if (_obstacles.IsCreated) _obstacles.Dispose();
        CaptureObstacles();
        _flowFieldReady = false;
    }

    /// <summary>由 RougeDynamicObstacle.OnEnable 调用。</summary>
    public static void RegisterDynamicObstacle(RougeDynamicObstacle obstacle)
    {
        if (obstacle == null) return;
        if (obstacle.GetComponentInParent<RougeMainTower>() != null) return;
        if (!s_dynamicObstacles.Contains(obstacle))
        {
            s_dynamicObstacles.Add(obstacle);
        }
    }

    /// <summary>由 RougeDynamicObstacle.OnDisable 调用。</summary>
    public static void UnregisterDynamicObstacle(RougeDynamicObstacle obstacle)
    {
        if (obstacle == null) return;
        s_dynamicObstacles.Remove(obstacle);
    }

    /// <summary>运行时增加额外寻路目标（除 player 外）。</summary>
    public void AddTarget(Transform target)
    {
        if (target == null) return;
        if (!_runtimeExtraTargets.Contains(target))
        {
            _runtimeExtraTargets.Add(target);
        }
    }

    /// <summary>运行时移除额外寻路目标。</summary>
    public void RemoveTarget(Transform target)
    {
        if (target == null) return;
        _runtimeExtraTargets.Remove(target);
    }

    /// <summary>把 player + extraTargets + 运行时目标解析为非阻挡 cell index 写入 _flowGoalIndices；返回有效目标数。</summary>
    private int ResolveFlowGoals(float2 playerPos, float invCellSize, bool primaryOnly = false)
    {
        int written = 0;

        // 主目标 = player
        _flowGoalIndices[written++] = ResolveFlowGoalIndex(playerPos, invCellSize);

        if (primaryOnly)
        {
            return written;
        }

        // serialized extraTargets
        if (extraTargets != null)
        {
            for (int i = 0; i < extraTargets.Count && written < MaxFlowGoalCount; i++)
            {
                Transform t = extraTargets[i];
                if (t == null || !t.gameObject.activeInHierarchy) continue;
                Vector3 wp = t.position;
                _flowGoalIndices[written++] = ResolveFlowGoalIndex(new float2(wp.x, wp.z), invCellSize);
            }
        }

        // runtime AddTarget
        for (int i = 0; i < _runtimeExtraTargets.Count && written < MaxFlowGoalCount;)
        {
            Transform t = _runtimeExtraTargets[i];
            if (t == null) { _runtimeExtraTargets.RemoveAt(i); continue; }
            if (!t.gameObject.activeInHierarchy) { i++; continue; }
            Vector3 wp = t.position;
            _flowGoalIndices[written++] = ResolveFlowGoalIndex(new float2(wp.x, wp.z), invCellSize);
            i++;
        }

        return written;
    }

    private int ResolveFlowGoalIndex(float2 playerPos, float invCellSize)
    {
        int2 goalCell = RougeMortonGridUtility.WorldToGrid(playerPos, _flowGridOrigin, invCellSize, _flowGridDim);
        int bestIndex = RougeMortonGridUtility.EncodeMorton(goalCell.x, goalCell.y);
        if (!IsFlowCellBlocked(goalCell.x, goalCell.y))
        {
            return bestIndex;
        }

        float bestDistSq = float.MaxValue;
        for (int ring = 1; ring < _flowGridDim; ring++)
        {
            bool found = false;
            int minX = math.max(goalCell.x - ring, 0);
            int maxX = math.min(goalCell.x + ring, _flowGridDim - 1);
            int minY = math.max(goalCell.y - ring, 0);
            int maxY = math.min(goalCell.y + ring, _flowGridDim - 1);

            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    if (x != minX && x != maxX && y != minY && y != maxY)
                    {
                        continue;
                    }

                    if (IsFlowCellBlocked(x, y))
                    {
                        continue;
                    }

                    float2 cellCenter = _flowGridOrigin + (new float2(x + 0.5f, y + 0.5f) * _flowFieldRuntimeCellSize);
                    float distSq = math.lengthsq(cellCenter - playerPos);
                    if (distSq < bestDistSq)
                    {
                        bestDistSq = distSq;
                        bestIndex = RougeMortonGridUtility.EncodeMorton(x, y);
                        found = true;
                    }
                }
            }

            if (found)
            {
                return bestIndex;
            }
        }

        return bestIndex;
    }

    private bool IsFlowCellBlocked(int x, int y)
    {
        if (!_obstacles.IsCreated || _obstacleCount <= 0)
        {
            return false;
        }

        float2 cellCenter = _flowGridOrigin + (new float2(x + 0.5f, y + 0.5f) * _flowFieldRuntimeCellSize);
        float navPadding = math.max(flowFieldObstaclePadding, 0f);
        for (int obstacleIndex = 0; obstacleIndex < _obstacleCount; obstacleIndex++)
        {
            RougeObstacle obstacle = _obstacles[obstacleIndex];
            if (obstacle.Type == RougeObstacle.CircleType)
            {
                float paddedRadius = obstacle.CircleRadius + navPadding;
                if (math.lengthsq(cellCenter - obstacle.Center) <= paddedRadius * paddedRadius)
                {
                    return true;
                }

                continue;
            }

            if (RougeObstacleMath.ContainsPoint(obstacle, cellCenter, navPadding))
            {
                return true;
            }
        }

        return false;
    }

    private void SeedEnemies()
    {
        float2 center = player != null ? player.PlanarPosition : float2.zero;
        float safeSpawnRadiusMax = math.max(spawnRadiusMin, math.min(spawnRadiusMax, math.max(8f, GetSafeSpawnRadius(center))));
        float safeSpawnRadiusMin = math.min(spawnRadiusMin, safeSpawnRadiusMax * 0.78f);
        for (int i = 0; i < enemyCount; i++)
        {
            uint hash = math.hash(new uint2((uint)i + 1u, 0x9E3779B9u));
            float angle = ((hash & 0xFFFFu) / 65535f) * math.PI * 2f;
            float distance = math.lerp(safeSpawnRadiusMin, safeSpawnRadiusMax, ((hash >> 16) & 0xFFFFu) / 65535f);
            float speedScale = math.lerp(0.9f, 1.15f, ((hash >> 8) & 0xFFu) / 255f);
            float2 pos = center + new float2(math.cos(angle), math.sin(angle)) * distance;
            pos.x = math.clamp(pos.x, -arenaHalfExtent + 2f, arenaHalfExtent - 2f);
            pos.y = math.clamp(pos.y, -arenaHalfExtent + 2f, arenaHalfExtent - 2f);
            _positionsA[i] = new float4(pos.x, renderHeight, pos.y, enemyRadius);
            _velocitiesA[i] = float4.zero;
            _stateA[i] = new float4(enemyMaxHealth, enemyRadius, enemyMaxSpeed * speedScale, 0f);
            _effectStateA[i] = default;
            if (_enemyRenderKinds.IsCreated) _enemyRenderKinds[i] = 0;
        }
    }

    private float GetSafeSpawnRadius(float2 center)
    {
        float marginX = arenaHalfExtent - math.abs(center.x) - 2f;
        float marginY = arenaHalfExtent - math.abs(center.y) - 2f;
        return math.max(0f, math.min(marginX, marginY));
    }

    private void ResizeBulletStorage(int bulletCapacity)
    {
        NativeArray<RougeBullet> previousBullets = _bullets;
        _bullets = new NativeArray<RougeBullet>(bulletCapacity, Allocator.Persistent);
        if (previousBullets.IsCreated)
        {
            int copyCount = math.min(_activeBulletCount, previousBullets.Length);
            if (copyCount > 0)
            {
                NativeArray<RougeBullet>.Copy(previousBullets, _bullets, copyCount);
            }

            ReleaseNative(ref previousBullets);
        }

        int entryCapacity = math.max(bulletCapacity * 32, 256);
        ReleaseNative(ref _bulletCellEntries);
        ReleaseNative(ref _bulletCellNext);
        _bulletCellEntries = new NativeArray<int>(entryCapacity, Allocator.Persistent);
        _bulletCellNext = new NativeArray<int>(entryCapacity, Allocator.Persistent);
    }

    private void ResizeSkillAreaGridStorage(int skillAreaCapacity)
    {
        int entryCapacity = math.max(skillAreaCapacity * math.max(_flowGridDim, 16), _flowGridCellCount);
        ReleaseNative(ref _skillCellEntries);
        ReleaseNative(ref _skillCellNext);
        _skillCellEntries = new NativeArray<int>(entryCapacity, Allocator.Persistent);
        _skillCellNext = new NativeArray<int>(entryCapacity, Allocator.Persistent);
    }

    private void UpdateBullets(float dt)
    {
        if (UsesTowerDefenseSpawners() || !IsSkillEnabled(PlayerSkillType.AutoShoot))
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
    float targetPadding = math.max(enemyRadius * 2f, 0.5f);
    float2 bulletMin = new float2(float.MaxValue, float.MaxValue);
    float2 bulletMax = new float2(float.MinValue, float.MinValue);

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

            _bullets[i] = bullet;
            float expandedRadius = bullet.Radius + targetPadding;
            bulletMin = math.min(bulletMin, math.min(bullet.Previous, bullet.Current) - expandedRadius);
            bulletMax = math.max(bulletMax, math.max(bullet.Previous, bullet.Current) + expandedRadius);
            i++;
        }

        if (_activeBulletCount <= 0)
        {
            _bulletMin = float2.zero;
            _bulletMax = float2.zero;
        }
        else
        {
            _bulletMin = bulletMin;
            _bulletMax = bulletMax;
        }
    }

    private void RenderBullets()
    {
        if (_activeBulletCount <= 0 || _bulletMesh == null || _bulletMaterial == null) return;

        Camera camera = RougeCameraFollow.ResolveCamera();
        Quaternion facing = camera != null
            ? Quaternion.LookRotation(-camera.transform.forward, camera.transform.up)
            : Quaternion.Euler(90f, 0f, 0f);

        for (int startIndex = 0; startIndex < _activeBulletCount; startIndex += _bulletRenderMatrices.Length)
        {
            int batchCount = Mathf.Min(_bulletRenderMatrices.Length, _activeBulletCount - startIndex);
            for (int i = 0; i < batchCount; i++)
            {
                RougeBullet bullet = _bullets[startIndex + i];
                Vector3 pos = new Vector3(bullet.Current.x, renderHeight + 0.5f, bullet.Current.y);
                Vector3 worldDirection = new Vector3(bullet.Velocity.x, 0f, bullet.Velocity.y);
                Vector3 localDirection = Quaternion.Inverse(facing) * worldDirection;
                float angle = Mathf.Atan2(-localDirection.x, localDirection.y) * Mathf.Rad2Deg;
                Quaternion rotation = facing * Quaternion.Euler(0f, 0f, angle);
                Vector3 scale = new Vector3(bullet.Radius * 3.2f, bullet.Radius * 5.5f, 1f);
                _bulletRenderMatrices[i] = Matrix4x4.TRS(pos, rotation, scale);
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
        if (_explosionCount <= 0 || _expPosBuffer == null || _expStateBuffer == null || _expArgsBuffer == null || _vfxSphereMesh == null || _vfxExplosionMat == null) return;

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
            ShadowCastingMode.On,
            true,
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
        _velocityRenderBuffer.SetData(_velocitiesA, 0, 0, drawCount);
        _enemyKindRenderBuffer.SetData(_enemyRenderKinds, 0, 0, drawCount);

        enemyMaterial.SetBuffer(PositionScaleBufferId, _positionBuffer);
        enemyMaterial.SetBuffer("_StateBuffer", _stateBuffer);
        enemyMaterial.SetBuffer("_VelocityBuffer", _velocityRenderBuffer);
        enemyMaterial.SetBuffer("_EnemyKindBuffer", _enemyKindRenderBuffer);
       // enemyMaterial.SetColor(BaseColorId, new Color(0.88f, 0.18f, 0.18f, 1f));
        enemyMaterial.SetFloat(ScaleMultiplierId, enemyVisualScale);
        enemyMaterial.SetFloat(VariationStrengthId, enemyVariationStrength);
        enemyMaterial.SetFloat(BreakupScaleId, enemyBreakupScale);
        enemyMaterial.SetFloat(BreakupStrengthId, enemyBreakupStrength);
        enemyMaterial.SetVector(PlayerFocusPositionId, player != null ? (Vector4)player.transform.position : (Vector4)transform.position);
        enemyMaterial.SetFloat(ShaderRenderHeightId, renderHeight);

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
        if (_bossDeathSequenceActive)
        {
            _simulationHandle = default;
            _simulationResultBackBufferReady = false;
            _towerTargetScheduledCount = 0;
            return;
        }
        int activeEnemyCount = Mathf.Clamp(_currentMaxEnemies, 0, enemyCount);
        if (activeEnemyCount <= 0)
        {
            _simulationHandle = default;
            _simulationResultBackBufferReady = false;
            _towerTargetScheduledCount = 0;
            return;
        }

        float invCellSize = 1f / math.max(_flowFieldRuntimeCellSize, 0.001f);
        int gridBatchSize = 1024;
        int flowIterationCount = math.clamp(flowFieldIterations, 1, 4);
        float2 playerPos = player != null ? player.PlanarPosition : float2.zero;
        float2 enemyGoalPos = GetEnemyTowerDefenseGoal(playerPos);
        float2 enemySpawnCenter = GetEnemyTowerDefenseSpawnCenter(playerPos);
        _flowFieldRefreshCountdown -= math.max(dt, 0f);
        bool refreshFlowField = !_flowFieldReady || _flowFieldRefreshCountdown <= 0f;
        if (refreshFlowField)
        {
            _flowFieldReady = true;
            float effectiveRefreshInterval = UsesTowerDefenseSpawners()
                ? math.max(flowFieldRefreshInterval, 1f)
                : math.max(flowFieldRefreshInterval, 0.05f);
            _flowFieldRefreshCountdown = effectiveRefreshInterval;
        }

        // 1) 把动态障碍快照刷到 _obstacles 后缀；每帧仅 List<Transform> 走读，不做 FindObjectsByType
        RefreshDynamicObstacleSnapshot();

        // 2) 把启动时烘焙好的 _staticBlockedCells memcpy 进工作 buffer，并清零 density
        JobHandle clearGridHandle = new CopyStaticBlockedMaskJob
        {
            StaticBlockedCells = _staticBlockedCells,
            BlockedCells = _flowBlockedCells,
            DensityFieldFixed = _densityFieldFixed
        }.ScheduleBatch(_flowGridCellCount, gridBatchSize);

        // 3) 仅栅格化动态障碍段（_dynamicObstacleCount 通常很小）
        JobHandle obstacleHandle = clearGridHandle;
        if (refreshFlowField && _dynamicObstacleCount > 0)
        {
            obstacleHandle = new RasterizeObstacleGridJob
            {
                Obstacles = _obstacles,
                StartIndex = _staticObstacleCount,
                ObstacleCount = _dynamicObstacleCount,
                BlockedCells = _flowBlockedCells,
                GridOrigin = _flowGridOrigin,
                InvCellSize = invCellSize,
                GridDim = _flowGridDim,
                ExtraPadding = flowFieldObstaclePadding
            }.Schedule(clearGridHandle);
        }

        JobHandle densityHandle = new BuildEnemyDensityFieldJob
        {
            PositionScaleIn = _positionsA,
            StateIn = _stateA,
            DensityFieldFixed = _densityFieldFixed,
            GridOrigin = _flowGridOrigin,
            InvCellSize = invCellSize,
            GridDim = _flowGridDim,
            RenderHeight = renderHeight
        }.ScheduleBatch(activeEnemyCount, simulationBatchSize, clearGridHandle);

        JobHandle flowDirectionHandle = default;
        if (refreshFlowField)
        {
            // 塔防模式下所有怪物以主塔为唯一目标。
            int goalCount = ResolveFlowGoals(enemyGoalPos, invCellSize, UsesTowerDefenseSpawners());

            // Fast sweeping propagates the goal distance across the entire grid each pass.
            JobHandle flowSolveHandle = new SolveFlowFieldJob
            {
                BlockedCells = _flowBlockedCells,
                FlowDistances = _flowDistanceField,
                GoalIndices = _flowGoalIndices,
                GoalCount = goalCount,
                GridDim = _flowGridDim,
                CellSize = _flowFieldRuntimeCellSize,
                IterationCount = flowIterationCount
            }.Schedule(obstacleHandle);

            flowDirectionHandle = new BuildFlowFieldDirectionsJob
            {
                BlockedCells = _flowBlockedCells,
                FlowDistances = _flowDistanceField,
                FlowDirections = _flowDirectionField,
                GridDim = _flowGridDim
            }.ScheduleBatch(_flowGridCellCount, gridBatchSize, flowSolveHandle);
        }

        JobHandle clearBulletHandle = new ClearBulletGridHeadsJob
        {
            CellHeads = _bulletCellHeads
        }.ScheduleBatch(_flowGridCellCount, gridBatchSize);

        JobHandle bulletHandle = new BuildBulletGridJob
        {
            Bullets = _bullets,
            BulletCount = _activeBulletCount,
            CellHeads = _bulletCellHeads,
            CellEntries = _bulletCellEntries,
            CellNext = _bulletCellNext,
            EntryCapacity = _bulletCellEntries.Length,
            GridOrigin = _flowGridOrigin,
            InvCellSize = invCellSize,
            GridDim = _flowGridDim,
            TargetRadiusPadding = math.max(enemyRadius * 2f, 0.5f)
        }.Schedule(clearBulletHandle);

        JobHandle clearSkillHandle = new ClearBulletGridHeadsJob
        {
            CellHeads = _skillCellHeads
        }.ScheduleBatch(_flowGridCellCount, gridBatchSize);

        JobHandle skillAreaHandle = new BuildSkillAreaGridJob
        {
            SkillAreas = _skillAreasDb,
            SkillAreaCount = _skillAreaCount,
            CellHeads = _skillCellHeads,
            CellEntries = _skillCellEntries,
            CellNext = _skillCellNext,
            EntryCapacity = _skillCellEntries.Length,
            GridOrigin = _flowGridOrigin,
            InvCellSize = invCellSize,
            GridDim = _flowGridDim
        }.Schedule(clearSkillHandle);

        JobHandle handle = new SimulateEnemiesFlowFieldJob
        {
            PositionScaleIn = _positionsA,
            VelocityIn = _velocitiesA,
            StateIn = _stateA,
            EffectStateIn = _effectStateA,
            PositionScaleOut = _positionsB,
            VelocityOut = _velocitiesB,
            StateOut = _stateB,
            EffectStateOut = _effectStateB,
            DensityFieldFixed = _densityFieldFixed,
            FlowDirections = _flowDirectionField,
            Bullets = _bullets,
            BulletCellHeads = _bulletCellHeads,
            BulletCellEntries = _bulletCellEntries,
            BulletCellNext = _bulletCellNext,
            SkillCellHeads = _skillCellHeads,
            SkillCellEntries = _skillCellEntries,
            SkillCellNext = _skillCellNext,
            BulletCount = _activeBulletCount,
            Obstacles = _obstacles,
            ObstacleCount = _obstacleCount,
            PlayerPos = playerPos,
            GoalPos = enemyGoalPos,
            SpawnCenter = enemySpawnCenter,
            PlayerDamageCount = _playerDamageCount,
            MainTowerDamageCount = _mainTowerDamageCount,
            EnemyKillCount = _enemyKillCount,
            EnemyKinds = _towerDefenseEnemyKinds,
            TowerDefenseGoldEarned = _towerDefenseGoldEarned,
            TowerDefenseRewardsEnabled = UsesTowerDefenseSpawners(),
            NormalKillGold = Mathf.Max(0, enemyBalance.normalKillGold),
            EliteKillGold = Mathf.Max(0, enemyBalance.eliteKillGold),
            TowerLaserDamage = _towerLaserDamage,
            TowerLaserDamageFrames = _towerLaserDamageFrames,
            TowerLaserDamageFrame = _towerLaserDamageFrame,
            TowerDamageByType = _towerDamageByType,
            TowerDamageByTypeFrames = _towerDamageByTypeFrames,
            TowerDamageTotalsFixed = _towerDamageTotalsFixed,
            BossShieldActive = _bossSpawned && _bossShieldActive,
            BossShieldPosition = new float2(_bossWorldPosition.x, _bossWorldPosition.z),
            BossShieldRadius = Mathf.Max(0f, bossBalance.shieldRadius),
            BossShieldDamageMultiplier = Mathf.Clamp(bossBalance.shieldDamageMultiplier, 0.01f, 1f),
            BossShieldMinimumDamage = Mathf.Max(1f, bossBalance.minimumShieldedDamage),
            BossEnemyIndex = _bossSpawned ? _bossEnemyIndex : -1,
            BossNavigationRadius = Mathf.Max(0.1f, bossBalance.navigationRadius),
            ExplosionQueue = _explosionQueue.AsParallelWriter(),
            SkillEventQueue = _skillEventQueue.AsParallelWriter(),
            EnemyMaxHealth = UsesTowerDefenseSpawners() ? GetTowerDefenseEnemyHealth() : enemyMaxHealth * (1f + currentLevel * 0.15f),
            EnemyRadius = Mathf.Min(enemyRadius*2f, enemyRadius * (0.8f+ currentLevel * 0.0001f)),
            EnemyMaxSpeed = UsesTowerDefenseSpawners() ? GetTowerDefenseEnemySpeed() : enemyMaxSpeed * math.min(1f + currentLevel * 0.02f, 1.8f),
            ArenaHalfExtent = arenaHalfExtent,
            SpawnRadiusMin = spawnRadiusMin,
            SpawnRadiusMax = spawnRadiusMax,
            DespawnDistanceSq = despawnDistance * despawnDistance,
            ChaseAcceleration = chaseAcceleration,
            CurrentMaxEnemies = _currentMaxEnemies,
            VelocityDamping = velocityDamping,
            SeparationRadius = separationRadius,
            SeparationStrength = separationStrength,
            CrowdReliefRadius = crowdReliefRadius,
            CrowdReliefStrength = crowdReliefStrength,
            CrowdOrbitStrength = crowdOrbitStrength,
            DenseSeparationBoost = denseSeparationBoost,
            DenseNeighborThreshold = denseNeighborThreshold,
            ObstacleLookAhead = obstacleLookAhead,
            ObstacleRepulsion = obstacleRepulsion,
            ObstacleOrbitStrength = obstacleOrbitStrength,
            KnockbackResist = math.max(0.1f, 1f - currentLevel * 0.0002f),
            PlayerContactEnabled = !UsesTowerDefenseSpawners() && IsPlayerContactEnabled(),
            DefeatEnemyOnPlayerContact = _playerContactDefeatEnemyOnContact,
            PlayerContactPadding = playerContactPadding,
            MainTowerContactRadius = GetMainTowerContactRadius(),
            MainTowerContactEnabled = HasLivingMainTower(),
            ExternalSpawning = UsesTowerDefenseSpawners(),
            SkillAreas = _skillAreasDb,
            SkillAreaCount = _skillAreaCount,
            BulletMin = _bulletMin,
            BulletMax = _bulletMax,
            RenderHeight = renderHeight,
            DeltaTime = dt,
            GridOrigin = _flowGridOrigin,
            GridCellSize = _flowFieldRuntimeCellSize,
            GridInvCellSize = invCellSize,
            GridDim = _flowGridDim,
            DensitySoftThreshold = densitySoftThreshold,
            DensityRepulsionStrength = densityRepulsionStrength,
            DensityGradientClamp = densityGradientClamp,
            DensityResponseJitter = densityResponseJitter,
            CrowdReliefMaxDensityPressure = crowdReliefMaxDensityPressure,
            FrameSeed = (uint)(Time.frameCount * 1664525 + 1013904223),
            SkillKillCounts = _skillKillCounts,
            BombDmgMult   = math.clamp(0.3f + _skillLevels[1] * 0.035f, 0.3f, 2.0f),
            LaserDmgMult  = math.clamp(0.3f + _skillLevels[2] * 0.035f, 0.3f, 2.0f),
            MeleeDmgMult  = math.clamp(0.3f + _skillLevels[3] * 0.035f, 0.3f, 2.0f),
            OrbitDmgMult  = math.clamp(2.0f + _skillLevels[4] * 0.5f, 2.0f, 15.0f),
            BulletDmgMult = math.clamp(0.3f + _skillLevels[5] * 0.035f, 0.3f, 2.0f)
        }.ScheduleBatch(
            activeEnemyCount,
            simulationBatchSize,
            JobHandle.CombineDependencies(
                JobHandle.CombineDependencies(densityHandle, flowDirectionHandle),
                JobHandle.CombineDependencies(bulletHandle, skillAreaHandle)));

        int scheduledTowerCount = math.min(_towerTargetRequestCount, MaxJobifiedTowerCount);
        if (scheduledTowerCount > 0)
        {
            JobHandle clearEnemyTargetGridHandle = new ClearBulletGridHeadsJob
            {
                CellHeads = _enemyTargetCellHeads
            }.ScheduleBatch(_flowGridCellCount, gridBatchSize);

            JobHandle buildEnemyTargetGridHandle = new BuildEnemyTargetGridJob
            {
                Positions = _positionsB,
                States = _stateB,
                CellHeads = _enemyTargetCellHeads,
                CellNext = _enemyTargetCellNext,
                GridOrigin = _flowGridOrigin,
                InvCellSize = invCellSize,
                GridDim = _flowGridDim
            }.ScheduleBatch(
                activeEnemyCount,
                simulationBatchSize,
                JobHandle.CombineDependencies(handle, clearEnemyTargetGridHandle));

            handle = new FindTowerTargetsJob
            {
                Requests = _towerTargetRequests,
                EnemyPositions = _positionsB,
                EnemyStates = _stateB,
                EnemyKinds = _towerDefenseEnemyKinds,
                FlowDistances = _flowDistanceField,
                CellHeads = _enemyTargetCellHeads,
                CellNext = _enemyTargetCellNext,
                ResultIndices = _towerTargetResultIndices,
                ResultDistances = _towerTargetResultDistances,
                GridOrigin = _flowGridOrigin,
                InvCellSize = invCellSize,
                GridDim = _flowGridDim
            }.Schedule(scheduledTowerCount, 1, buildEnemyTargetGridHandle);
        }

        _towerTargetScheduledCount = scheduledTowerCount;
        _simulationHandle = handle;
        _simulationResultBackBufferReady = true;
    }

    private JobHandle ScheduleRadixSort(JobHandle dependency, int activeEnemyCount, int activeChunkCount)
    {
        // 只排序hashSize实际用到的位数：对10w-30w敌人hashSize最大2^20，只需3趟而非固定4趟
      //  int numPasses = Mathf.Max(1, Mathf.CeilToInt(Mathf.Log(_hashSize, 2) / 8f));
        JobHandle handle = dependency;
        for (int pass = 0; pass < 3; pass++)
        {
            int shift = 32 + pass * 8;
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

    private void SpawnAOERing(Vector3 center, float radius, float duration, Color color = default, Material material = null, bool useMaterialColor = false)
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
                _aoeRingMaterials[i] = material;
                _aoeRingUseMaterialColor[i] = useMaterialColor;
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
                    _aoeRingMaterials[i] = null;
                    _aoeRingUseMaterialColor[i] = false;
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
        if (_aoeRingMesh == null || _aoeRingMat == null || _aoeRingPropertyBlock == null)
        {
            return;
        }

        for (int i = 0; i < MaxAOERings; i++)
        {
            if (_aoeRingTimers[i] <= 0f)
            {
                continue;
            }

            Material ringMaterial = _aoeRingMaterials[i] != null ? _aoeRingMaterials[i] : _aoeRingMat;
            if (ringMaterial == null)
            {
                continue;
            }

            float progress = 1f - math.max(0f, _aoeRingTimers[i] / math.max(0.01f, _aoeRingMaxTimes[i]));
            float travel = progress * progress * (3f - 2f * progress);
            float currentRadius = _aoeRingMaxRadius[i] * math.lerp(0.72f, 1f, travel);
            float ringHeight = math.lerp(0.08f, 0.2f, math.sin(progress * math.PI * 0.5f));
            float alpha = math.saturate((1f - progress * progress * 0.82f) * (0.72f + travel * 0.28f));
            Vector3 center = _aoeRingPositions[i];
            center.y = math.max(center.y, renderHeight + 0.045f);
            Matrix4x4 matrix = Matrix4x4.TRS(center, Quaternion.identity, new Vector3(currentRadius * 2f, ringHeight, currentRadius * 2f));

            _aoeRingPropertyBlock.Clear();
            if (ringMaterial.HasProperty("_InnerRadiusRatio"))
            {
                _aoeRingPropertyBlock.SetFloat("_InnerRadiusRatio", math.lerp(0.9f, 0.965f, progress));
            }

            Color color = _aoeRingColors[i];
            color.a *= alpha;
            if (_aoeRingUseMaterialColor[i])
            {
                if (ringMaterial.HasProperty("_AlphaMultiplier"))
                {
                    _aoeRingPropertyBlock.SetFloat("_AlphaMultiplier", color.a);
                }
            }
            else if (ringMaterial.HasProperty("_Color"))
            {
                _aoeRingPropertyBlock.SetColor("_Color", color);
            }

            Graphics.DrawMesh(_aoeRingMesh, matrix, ringMaterial, gameObject.layer, null, 0, _aoeRingPropertyBlock, UnityEngine.Rendering.ShadowCastingMode.Off, false, null, false);
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
                float beamMaxRadius = math.max(0.12f, _tornadoPosData[i].w);
                float impactProgress = math.clamp(_tornadoImpactProgress[i], 0.08f, 0.6f);
                float preImpact = math.saturate(progress / impactProgress);
                float postImpact = math.saturate((progress - impactProgress) / math.max(0.001f, 1f - impactProgress));
                float beamHeight = math.max(28f, math.max(_tornadoImpactRadii[i] * 3.2f, 34f));
                float beamStartOffset = math.max(14f, beamHeight * 0.88f);
                float bottomOffset = math.lerp(beamStartOffset, 0f, preImpact * preImpact * (3f - 2f * preImpact));
                float thinRadius = math.max(0.08f, beamMaxRadius * 0.16f);
                float currentRadius = postImpact > 0f
                    ? math.lerp(thinRadius, beamMaxRadius, postImpact * postImpact)
                    : thinRadius;
                float desiredAlpha = postImpact > 0f
                    ? math.lerp(0.98f, 0f, postImpact * postImpact)
                    : math.lerp(0.18f, 0.98f, preImpact);

                _tornadoPosData[i] = new float4(_tornadoPosData[i].x, renderHeight + bottomOffset + beamHeight * 0.5f, _tornadoPosData[i].z, beamMaxRadius);
                _tornadoStateData[i] = new float4(currentRadius * 2f, beamHeight, currentRadius * 2f * 0.94f, 1f - desiredAlpha);

                if (!_tornadoImpactTriggered[i] && progress >= impactProgress)
                {
                    _tornadoImpactTriggered[i] = true;
                    if (TryAddCircularSkillArea(
                        _tornadoImpactPositions[i],
                        _tornadoImpactRadii[i],
                        _tornadoImpactDamages[i],
                        _tornadoImpactPullForces[i],
                        _tornadoImpactVerticalForces[i],
                        _tornadoImpactEffects[i]))
                    {
                        Vector3 impactCenter = new Vector3(_tornadoImpactPositions[i].x, renderHeight + 0.04f, _tornadoImpactPositions[i].y);
                        SpawnAOERing(
                            impactCenter,
                            _tornadoImpactRadii[i],
                            _tornadoImpactRingDurations[i],
                            _tornadoImpactRingColors[i],
                            skillConfig != null ? skillConfig.LightPillar.ImpactRingMaterial : null,
                            skillConfig != null && skillConfig.LightPillar != null && skillConfig.LightPillar.ImpactRingMaterial != null);
                        SpawnExplosionVFX(impactCenter + Vector3.up * 0.3f, math.max(0.8f, _tornadoImpactRadii[i] * 0.22f));
                    }
                }
                
                if (_activeTornadoCount != i)
                {
                    _tornadoPosData[_activeTornadoCount] = _tornadoPosData[i];
                    _tornadoStateData[_activeTornadoCount] = _tornadoStateData[i];
                    _tornadoLifeTimers[_activeTornadoCount] = _tornadoLifeTimers[i];
                    _tornadoMaxTimes[_activeTornadoCount] = _tornadoMaxTimes[i];
                    _tornadoRadiusMultipliers[_activeTornadoCount] = _tornadoRadiusMultipliers[i];
                    _tornadoImpactTriggered[_activeTornadoCount] = _tornadoImpactTriggered[i];
                    _tornadoImpactProgress[_activeTornadoCount] = _tornadoImpactProgress[i];
                    _tornadoImpactPositions[_activeTornadoCount] = _tornadoImpactPositions[i];
                    _tornadoImpactRadii[_activeTornadoCount] = _tornadoImpactRadii[i];
                    _tornadoImpactDamages[_activeTornadoCount] = _tornadoImpactDamages[i];
                    _tornadoImpactPullForces[_activeTornadoCount] = _tornadoImpactPullForces[i];
                    _tornadoImpactVerticalForces[_activeTornadoCount] = _tornadoImpactVerticalForces[i];
                    _tornadoImpactRingDurations[_activeTornadoCount] = _tornadoImpactRingDurations[i];
                    _tornadoImpactRingColors[_activeTornadoCount] = _tornadoImpactRingColors[i];
                    _tornadoImpactEffects[_activeTornadoCount] = _tornadoImpactEffects[i];
                    _tornadoLifeTimers[i] = 0f;
                    _tornadoRadiusMultipliers[i] = 0f;
                    _tornadoImpactTriggered[i] = false;
                    _tornadoImpactProgress[i] = 0f;
                }
                _activeTornadoCount++;
            }
        }

        for (int ti = _activeTornadoCount; ti < MaxTornados; ti++)
        {
            _tornadoPosData[ti] = new float4(99999f, -999f, 99999f, 0f);
            _tornadoStateData[ti] = float4.zero;
            _tornadoRadiusMultipliers[ti] = 0f;
            _tornadoImpactTriggered[ti] = false;
            _tornadoImpactProgress[ti] = 0f;
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
        FinalizeCompletedSimulationBuffers();
        _simulationHandle = default;
        DisposeTowerDefense();
        _initialized = false;
        _activeBulletCount = 0;
        _hasActiveSustainedSkill = false;
        _activeSustainedSkillType = default;
        _activeSustainedSkillPriority = 0;

        if (_tornadoVisual) Destroy(_tornadoVisual);
        if (_bombVisuals != null)
            for (int i=0; i<MaxBombs; i++) if (_bombVisuals[i]) { Destroy(_bombVisuals[i]); _bombVisuals[i] = null; }
        if (_laserVisual) Destroy(_laserVisual);
        if (_laserMuzzleVisual) Destroy(_laserMuzzleVisual);
        if (_laserExtraVisuals != null)
            for (int li = 0; li < _laserExtraVisuals.Length; li++)
                if (_laserExtraVisuals[li] != null) { Destroy(_laserExtraVisuals[li]); _laserExtraVisuals[li] = null; }
        if (_tornadoMat && _ownsTornadoMat) Destroy(_tornadoMat);
        if (_laserMat && _ownsLaserMat) Destroy(_laserMat);
        if (_meleeMat && _ownsMeleeMat) Destroy(_meleeMat);
        if (_meleeVisual) Destroy(_meleeVisual);
        if (_meleeFinisherMat && _ownsMeleeFinisherMat) Destroy(_meleeFinisherMat);
        if (_meleeFinisherVisual) Destroy(_meleeFinisherVisual);
        if (_spikeMat && _ownsSpikeMat) Destroy(_spikeMat);
        if (_spikeMesh && _ownsSpikeMesh) Destroy(_spikeMesh);
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
        if (_dashMat && _ownsDashMat) Destroy(_dashMat);
        if (_aoeRingMesh && _ownsAoeRingMesh) Destroy(_aoeRingMesh);
        if (_skateBoardMat && _ownsSkateboardMat) Destroy(_skateBoardMat);
        if (_skateBoardVisual) Destroy(_skateBoardVisual);
        if (_poisonBottleMat) Destroy(_poisonBottleMat);
        if (_poisonZoneMat) Destroy(_poisonZoneMat);
        if (_burnPatchMat) Destroy(_burnPatchMat);
        for (int i = 0; i < MaxPoisonBottles; i++)
            if (_poisonBottleVisuals[i] != null) { Destroy(_poisonBottleVisuals[i]); _poisonBottleVisuals[i] = null; }
        for (int i = 0; i < MaxPoisonZones; i++)
            if (_poisonZoneVisuals[i] != null) { Destroy(_poisonZoneVisuals[i]); _poisonZoneVisuals[i] = null; }
        for (int i = 0; i < MaxBurnPatches; i++)
            if (_burnPatchVisuals[i] != null) { Destroy(_burnPatchVisuals[i]); _burnPatchVisuals[i] = null; }
        if (_aoeRingMat && _ownsAoeRingMaterial) Destroy(_aoeRingMat);
        if (_ownsAoeRingMaterial) _aoeRingMat = null;
        _ownsAoeRingMaterial = false;
        if (_shockwaveRingMat) Destroy(_shockwaveRingMat);
        for (int ri = 0; ri < MaxAOERings; ri++)
            if (_aoeRingVisuals[ri] != null) { Destroy(_aoeRingVisuals[ri]); _aoeRingVisuals[ri] = null; }
        for (int si = 0; si < ShockwaveRingCount; si++)
            if (_shockwaveRingVisuals[si] != null) { Destroy(_shockwaveRingVisuals[si]); _shockwaveRingVisuals[si] = null; }
        for (int mi = 0; mi < MeteorVisualMax; mi++)
            if (_meteorVisuals[mi] != null) { Destroy(_meteorVisuals[mi]); _meteorVisuals[mi] = null; }

        if (_ownsBulletMaterial && _bulletMaterial != null) Destroy(_bulletMaterial);
        _bulletMaterial = null;
        _ownsBulletMaterial = false;
        _bulletMesh = null;
        if (_ownsEnemyBillboardMaterial && enemyMaterial != null) Destroy(enemyMaterial);
        enemyMaterial = null;
        _ownsEnemyBillboardMaterial = false;
        if (_ownsEnemyBillboardMesh && enemyMesh != null) Destroy(enemyMesh);
        enemyMesh = null;
        _ownsEnemyBillboardMesh = false;

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
        ReleaseNative(ref _bulletCellHeads);
        ReleaseNative(ref _bulletCellEntries);
        ReleaseNative(ref _bulletCellNext);
        ReleaseNative(ref _skillCellHeads);
        ReleaseNative(ref _skillCellEntries);
        ReleaseNative(ref _skillCellNext);
        ReleaseNative(ref _enemyTargetCellHeads);
        ReleaseNative(ref _enemyTargetCellNext);
        ReleaseNative(ref _towerTargetRequests);
        ReleaseNative(ref _towerTargetResultIndices);
        ReleaseNative(ref _towerTargetResultDistances);
        ReleaseNative(ref _towerLaserDamage);
        ReleaseNative(ref _towerLaserDamageFrames);
        ReleaseNative(ref _towerDefenseEnemyKinds);
        ReleaseNative(ref _enemyRenderKinds);
        ReleaseNative(ref _towerDefenseGoldEarned);
        ReleaseNative(ref _towerDamageByType);
        ReleaseNative(ref _towerDamageByTypeFrames);
        ReleaseNative(ref _towerDamageTotalsFixed);
        ReleaseNative(ref _densityFieldFixed);
        ReleaseNative(ref _flowDistanceField);
        ReleaseNative(ref _flowDistanceScratch);
        ReleaseNative(ref _flowDirectionField);
        ReleaseNative(ref _flowBlockedCells);
        ReleaseNative(ref _staticBlockedCells);
        ReleaseNative(ref _flowGoalIndices);
        ReleaseNative(ref _neighborOffsets);
        ReleaseNative(ref _histograms);
        ReleaseNative(ref _binTotals);
        ReleaseNative(ref _bullets);
        ReleaseNative(ref _obstacles);
        ReleaseNative(ref _playerDamageCount);
        ReleaseNative(ref _mainTowerDamageCount);
        ReleaseNative(ref _enemyKillCount);
        if (_explosionQueue.IsCreated) _explosionQueue.Dispose();
        if (_skillEventQueue.IsCreated) _skillEventQueue.Dispose();

        _positionBuffer?.Release();
        _positionBuffer = null;
        _stateBuffer?.Release();
        _stateBuffer = null;
        _velocityRenderBuffer?.Release();
        _velocityRenderBuffer = null;
        _enemyKindRenderBuffer?.Release();
        _enemyKindRenderBuffer = null;
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
            name = "RougeSpriteQuad"
        };
        Vector3[] vertices = {
            new Vector3(-0.5f, -0.5f, 0f),
            new Vector3(0.5f, -0.5f, 0f),
            new Vector3(0.5f, 0.5f, 0f),
            new Vector3(-0.5f, 0.5f, 0f)
        };
        int[] triangles = { 0, 2, 1, 0, 3, 2 };
        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.uv = new[]
        {
            new Vector2(0f, 0f),
            new Vector2(1f, 0f),
            new Vector2(1f, 1f),
            new Vector2(0f, 1f)
        };
        mesh.RecalculateBounds();
        return mesh;
    }

    private void EnsureShaderReferenceDefaults()
    {
        AssignShaderReferenceIfMissing(ref indirectInstancedShader, "Rouge/IndirectInstancedURP");
        AssignShaderReferenceIfMissing(ref vfxInstancedShader, "Rouge/VFXInstanced");
        AssignShaderReferenceIfMissing(ref aoeRingShader, "Rouge/AOERing");
        AssignShaderReferenceIfMissing(ref groundZoneShader, "Rouge/GroundZone");
        AssignShaderReferenceIfMissing(ref hologramShader, "Rouge/Hologram");
        AssignShaderReferenceIfMissing(ref techPanelShader, "Rouge/TechPanel");
        AssignShaderReferenceIfMissing(ref laserBeamShader, "Rouge/LaserBeam");
        AssignShaderReferenceIfMissing(ref urpLitShader, "Universal Render Pipeline/Lit");
    }

    private static void AssignShaderReferenceIfMissing(ref Shader shaderField, string shaderName)
    {
        if (shaderField == null)
        {
            shaderField = Shader.Find(shaderName);
        }
    }

    private Material CreateFallbackMaterial()
    {
        Material material = CreateRuntimeMaterial("Rouge/IndirectInstancedURP", "Enemy Fallback", true);
        material.enableInstancing = true;
        material.hideFlags = HideFlags.DontSave;
        return material;
    }

    private Material CreateRuntimeMaterial(string preferredShaderName, string context, bool enableInstancing)
    {
        Shader shader = ResolveRuntimeShader(preferredShaderName, context);
        Material material = new Material(shader)
        {
            hideFlags = HideFlags.DontSave
        };
        material.enableInstancing = enableInstancing;
        return material;
    }

    private Shader ResolveRuntimeShader(string preferredShaderName, string context)
    {
        Shader shader = FindRuntimeShader(preferredShaderName);
        if (shader != null)
        {
            return shader;
        }

        string[] fallbacks =
        {
            "Universal Render Pipeline/Lit",
            "Universal Render Pipeline/Simple Lit",
            "Standard",
            "Sprites/Default",
            "Unlit/Color"
        };

        for (int i = 0; i < fallbacks.Length; i++)
        {
            shader = FindRuntimeShader(fallbacks[i]);
            if (shader != null)
            {
                Debug.LogWarning($"[RougeGameManager] Missing shader '{preferredShaderName}' for {context}. Fallback to '{fallbacks[i]}'.");
                return shader;
            }
        }

        throw new InvalidOperationException($"No runtime shader available for {context}. Preferred shader: {preferredShaderName}");
    }

    private Shader FindRuntimeShader(string shaderName)
    {
        if (string.IsNullOrEmpty(shaderName))
        {
            return null;
        }

        Shader configuredShader = GetConfiguredShader(shaderName);
        return configuredShader != null ? configuredShader : Shader.Find(shaderName);
    }

    private Shader GetConfiguredShader(string shaderName)
    {
        switch (shaderName)
        {
            case "Rouge/IndirectInstancedURP":
                return indirectInstancedShader;
            case "Rouge/VFXInstanced":
                return vfxInstancedShader;
            case "Rouge/AOERing":
                return aoeRingShader;
            case "Rouge/GroundZone":
                return groundZoneShader;
            case "Rouge/Hologram":
                return hologramShader;
            case "Rouge/TechPanel":
                return techPanelShader;
            case "Rouge/LaserBeam":
                return laserBeamShader;
            case "Universal Render Pipeline/Lit":
                return urpLitShader;
            default:
                return null;
        }
    }

    private static void ApplyBaseColor(Material material, Color color)
    {
        if (material == null)
        {
            return;
        }

        if (material.HasProperty("_BaseColor"))
        {
            material.SetColor("_BaseColor", color);
        }

        if (material.HasProperty("_Color"))
        {
            material.SetColor("_Color", color);
        }
    }

    private static void ApplyEmissionColor(Material material, Color color)
    {
        if (material != null && material.HasProperty("_EmissionColor"))
        {
            material.SetColor("_EmissionColor", color);
        }
    }

    private static void ApplyFloatIfPresent(Material material, string propertyName, float value)
    {
        if (material != null && material.HasProperty(propertyName))
        {
            material.SetFloat(propertyName, value);
        }
    }

    private Material CreateFallbackHologramMaterial(Color baseColor, Color accentColor, float alpha, float scanlineDensity, float glowStrength)
    {
        Shader shader = FindRuntimeShader("Rouge/Hologram");
        Material material;
        if (shader != null)
        {
            material = new Material(shader)
            {
                hideFlags = HideFlags.DontSave
            };
            material.SetColor("_BaseColor", baseColor);
            material.SetColor("_AccentColor", accentColor);
            material.SetFloat("_Alpha", alpha);
            material.SetFloat("_ScanlineDensity", scanlineDensity);
            material.SetFloat("_ScanlineSpeed", 2.2f);
            material.SetFloat("_FresnelPower", 2.4f);
            material.SetFloat("_GlowStrength", glowStrength);
            material.SetFloat("_NoiseStrength", 0.16f);
            material.SetFloat("_DissolveProgress", 1f);
            material.SetFloat("_GridDensity", 9f);
            material.SetFloat("_DissolveEdgeWidth", 0.14f);
            material.SetFloat("_DissolveGlow", 1.45f);
            return material;
        }

        material = CreateRuntimeMaterial("Universal Render Pipeline/Lit", "Hologram Fallback", false);
        ApplyBaseColor(material, new Color(baseColor.r, baseColor.g, baseColor.b, alpha));
        ApplyFloatIfPresent(material, "_Surface", 1f);
        ApplyEmissionColor(material, accentColor * math.max(1f, glowStrength));
        return material;
    }

    private Material CreateFallbackTechPanelMaterial(Color baseColor, Color accentColor, float alpha, float lineDensity, float glowStrength)
    {
        Shader shader = FindRuntimeShader("Rouge/TechPanel");
        Material material;
        if (shader != null)
        {
            material = new Material(shader)
            {
                hideFlags = HideFlags.DontSave
            };
            material.SetColor("_BaseColor", baseColor);
            material.SetColor("_EdgeColor", accentColor);
            material.SetFloat("_Alpha", alpha);
            material.SetFloat("_LineDensity", lineDensity);
            material.SetFloat("_SweepSpeed", 1.5f);
            material.SetFloat("_FresnelPower", 2.1f);
            material.SetFloat("_GlowStrength", glowStrength);
            material.SetFloat("_NoiseStrength", 0.1f);
            return material;
        }

        material = CreateRuntimeMaterial("Universal Render Pipeline/Lit", "Tech Panel Fallback", false);
        ApplyBaseColor(material, new Color(baseColor.r, baseColor.g, baseColor.b, alpha));
        ApplyFloatIfPresent(material, "_Surface", 1f);
        ApplyEmissionColor(material, accentColor * math.max(1f, glowStrength));
        return material;
    }

    private static float EvaluateHologramReveal(float normalizedProgress, float fadeWindow)
    {
        float safeFadeWindow = math.max(0.01f, fadeWindow);
        float progress = math.saturate(normalizedProgress);
        float fadeIn = math.saturate(progress / safeFadeWindow);
        float fadeOut = math.saturate((1f - progress) / safeFadeWindow);
        float reveal = math.min(fadeIn, fadeOut);
        return reveal * reveal * (3f - 2f * reveal);
    }

    private void ApplyHologramLifecycle(GameObject visual, float normalizedProgress, float fadeWindow = 0.22f)
    {
        if (visual == null)
        {
            return;
        }

        Renderer renderer = visual.GetComponent<Renderer>();
        if (renderer == null || renderer.sharedMaterial == null || !renderer.sharedMaterial.HasProperty(HologramDissolveProgressId))
        {
            return;
        }

        if (_hologramPropertyBlock == null)
        {
            _hologramPropertyBlock = new MaterialPropertyBlock();
        }

        renderer.GetPropertyBlock(_hologramPropertyBlock);
        _hologramPropertyBlock.SetFloat(HologramDissolveProgressId, EvaluateHologramReveal(normalizedProgress, fadeWindow));
        renderer.SetPropertyBlock(_hologramPropertyBlock);
    }

    private static Mesh CreateConeMesh(int segmentCount = 20)
    {
        int safeSegmentCount = Mathf.Max(3, segmentCount);
        Mesh mesh = new Mesh
        {
            name = "RougeCone"
        };

        Vector3[] vertices = new Vector3[safeSegmentCount * 2 + 2];
        Vector3[] normals = new Vector3[vertices.Length];
        Vector2[] uv = new Vector2[vertices.Length];
        int[] triangles = new int[safeSegmentCount * 6 + safeSegmentCount * 3];

        vertices[0] = new Vector3(0f, 1f, 0f);
        normals[0] = Vector3.up;
        uv[0] = new Vector2(0.5f, 1f);

        vertices[1] = new Vector3(0f, -1f, 0f);
        normals[1] = Vector3.down;
        uv[1] = new Vector2(0.5f, 0.5f);

        for (int i = 0; i < safeSegmentCount; i++)
        {
            float angle = i / (float)safeSegmentCount * math.PI * 2f;
            float x = math.cos(angle);
            float z = math.sin(angle);
            int sideIndex = 2 + i;
            int capIndex = 2 + safeSegmentCount + i;
            Vector3 ringVertex = new Vector3(x, -1f, z);

            vertices[sideIndex] = ringVertex;
            normals[sideIndex] = new Vector3(x, 0.55f, z).normalized;
            uv[sideIndex] = new Vector2(i / (float)safeSegmentCount, 0f);

            vertices[capIndex] = ringVertex;
            normals[capIndex] = Vector3.down;
            uv[capIndex] = new Vector2((x + 1f) * 0.5f, (z + 1f) * 0.5f);
        }

        int triangleIndex = 0;
        for (int i = 0; i < safeSegmentCount; i++)
        {
            int next = (i + 1) % safeSegmentCount;
            int sideCurrent = 2 + i;
            int sideNext = 2 + next;
            triangles[triangleIndex++] = 0;
            triangles[triangleIndex++] = sideNext;
            triangles[triangleIndex++] = sideCurrent;
        }

        for (int i = 0; i < safeSegmentCount; i++)
        {
            int next = (i + 1) % safeSegmentCount;
            int capCurrent = 2 + safeSegmentCount + i;
            int capNext = 2 + safeSegmentCount + next;
            triangles[triangleIndex++] = 1;
            triangles[triangleIndex++] = capCurrent;
            triangles[triangleIndex++] = capNext;
        }

        mesh.vertices = vertices;
        mesh.normals = normals;
        mesh.uv = uv;
        mesh.triangles = triangles;
        mesh.RecalculateBounds();
        return mesh;
    }

    private static Mesh CreateAoERingMesh(int segmentCount = 64)
    {
        int safeSegmentCount = Mathf.Max(8, segmentCount);
        Mesh mesh = new Mesh
        {
            name = "RougeAoERing"
        };

        Vector3[] vertices = new Vector3[safeSegmentCount * 2];
        Vector3[] normals = new Vector3[vertices.Length];
        Vector2[] uv = new Vector2[vertices.Length];
        int[] triangles = new int[safeSegmentCount * 6];

        for (int i = 0; i < safeSegmentCount; i++)
        {
            float angle = i / (float)safeSegmentCount * math.PI * 2f;
            float x = math.cos(angle);
            float z = math.sin(angle);
            int outerIndex = i * 2;
            int innerIndex = outerIndex + 1;

            vertices[outerIndex] = new Vector3(x * 0.5f, 0f, z * 0.5f);
            vertices[innerIndex] = new Vector3(x * 0.02f, 0f, z * 0.02f);
            normals[outerIndex] = Vector3.up;
            normals[innerIndex] = Vector3.up;
            uv[outerIndex] = new Vector2((x + 1f) * 0.5f, (z + 1f) * 0.5f);
            uv[innerIndex] = new Vector2(0.5f, 0.5f);
        }

        int triangleIndex = 0;
        for (int i = 0; i < safeSegmentCount; i++)
        {
            int next = (i + 1) % safeSegmentCount;
            int outerCurrent = i * 2;
            int innerCurrent = outerCurrent + 1;
            int outerNext = next * 2;
            int innerNext = outerNext + 1;

            triangles[triangleIndex++] = outerCurrent;
            triangles[triangleIndex++] = outerNext;
            triangles[triangleIndex++] = innerNext;
            triangles[triangleIndex++] = outerCurrent;
            triangles[triangleIndex++] = innerNext;
            triangles[triangleIndex++] = innerCurrent;
        }

        mesh.vertices = vertices;
        mesh.normals = normals;
        mesh.uv = uv;
        mesh.triangles = triangles;
        mesh.RecalculateBounds();
        return mesh;
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
    public float EffectFreezeDuration;
    public float EffectCurseExplosionDamage;
    public float EffectCurseExplosionRadius;
    public float EffectBurnDamage;
    public float EffectBurnDuration;
    public int SourceTowerTypePlusOne;
}

public struct RougeEnemyEffectState
{
    public float PoisonTimer;
    public float PoisonTickTimer;
    public float PoisonSpreadRadius;
    public float SlowPercent;
    public float SlowTimer;
    public float SlowStacks;
    public float FreezeTimer;
    public float BurnTimer;
    public float BurnTickTimer;
    public float BurnDamage;
    public float BurnReapplyCooldown;
    public float CurseExplosionDamage;
    public float CurseExplosionRadius;
    public float LaunchLandingDamage;
    public float LaunchLandingRadius;
    public float LaunchMotionTimer;
    public float LaunchStackTimer;
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

// 子弹按左端 X 升序排列，配合 Job 内二分查找初筛
struct BulletMinXComparer : IComparer<RougeBullet>
{
    public int Compare(RougeBullet a, RougeBullet b)
    {
        float keyA = Unity.Mathematics.math.min(a.Previous.x, a.Current.x) - a.Radius;
        float keyB = Unity.Mathematics.math.min(b.Previous.x, b.Current.x) - b.Radius;
        return keyA.CompareTo(keyB);
    }
}


