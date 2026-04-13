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
public class RougeGameManager : MonoBehaviour
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

    [Header("Projectile Attack")]
    [SerializeField, Range(1, 2048)] private int maxBullets = 128;
    [SerializeField] private float fireInterval = 0.06f;
    [SerializeField] private float bulletSpeed = 42f;
    [SerializeField] private float bulletRadius = 0.2f;
    [SerializeField] private float bulletDamage = 14f;
    [SerializeField] private float bulletLifetime = 1.5f;
    [SerializeField] private int bulletsPerShot = 1;
    [SerializeField] private float spreadAngle = 4f;

    [Header("Tornado")]
    [SerializeField] private KeyCode tornadoKey = KeyCode.Q;
    [SerializeField] private Mesh tornadoMesh;
    [SerializeField] private float tornadoRadius = 10f;
    [SerializeField] private float tornadoPullForce = 55f;
    [SerializeField] private float tornadoSpinForce = 85f;
    [SerializeField] private float tornadoLiftForce = 35f;
    [SerializeField] private float tornadoDuration = 4f;
    [SerializeField] private float tornadoCooldown = 6f;
    [SerializeField] private float tornadoTravelSpeed = 10f;



    [Header("Job Settings")]
    [SerializeField, Range(64, 4096)] private int sortBatchSize = 2048;
    [SerializeField, Range(64, 2048)] private int simulationBatchSize = 256;
    [SerializeField, Range(0.1f, 2f)] private float fixedSimulationDt = 1f;

    [Header("Player Stats")]
    [SerializeField] private float playerMaxHealth = 100f;
    private float playerHealth;
    private float _fps;

    private NativeArray<float4> _positionsA;
    private NativeArray<float4> _positionsB;
    private NativeArray<float4> _velocitiesA;
    private NativeArray<float4> _velocitiesB;
    private NativeArray<float4> _stateA;
    private NativeArray<float4> _stateB;
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
    private Material _laserMat;
    private float _laserTimer;
    private float2 _laserPos;
    private float2 _laserDir;

    private float _meleeCooldownTimer;
    private GameObject _meleeVisual;
    private Material _meleeMat;
    private float _meleeTimer;
    private float2 _meleePos;
    private float2 _meleeDir;
    private int _meleeComboStep = 0;
    private float _meleeComboWindow = 0f;

    private int _bombBounceCount;
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

    private float _dashCooldownTimer;
    private float _meleeHitShake;

    // VFX buffers for explosions
    private const int MaxExplosions = 128;
    private int _explosionCount;
    private NativeArray<float4> _expPosData;
    private NativeArray<float4> _expStateData;
    private float[] _expTimers = new float[MaxExplosions];
    private float[] _expMaxScales = new float[MaxExplosions];
    private GraphicsBuffer _expPosBuffer;
    private GraphicsBuffer _expStateBuffer;
    private GraphicsBuffer _expArgsBuffer;
    private uint[] _expDrawArgs = new uint[5];
    private Material _vfxExplosionMat;
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
        
        player.transform.position = pos;

        // Melee hit screen shake
        if (_meleeHitShake > 0f)
        {
            _meleeHitShake -= Time.deltaTime;
            if (Camera.main != null)
            {
                float shakeIntensity = _meleeHitShake * 15f;
                Camera.main.transform.position += new Vector3(
                    UnityEngine.Random.Range(-shakeIntensity, shakeIntensity),
                    UnityEngine.Random.Range(-shakeIntensity * 0.5f, shakeIntensity * 0.5f),
                    UnityEngine.Random.Range(-shakeIntensity, shakeIntensity));
            }
        }
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
            if (_jumpState == 0 && _invincibilityTimer <= 0f)
            {
                playerHealth -= damage * 2f;
                playerHealth = Mathf.Max(0f, playerHealth);
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
        
        _spawnTimer += dt;
        if (_spawnTimer > 1f) {
            _spawnTimer = 0f;
            if (_currentMaxEnemies < enemyCount) {
                // Ramping up exponentially + flat faster rate to reach 100k cap sensibly
                int growth = 20 + currentLevel * 10 + (int)(_currentMaxEnemies * 0.02f);
                _currentMaxEnemies = Mathf.Min(enemyCount, _currentMaxEnemies + growth);
            }
        }

        UpdateSkills(dt);

     
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

        // Animate VFX spheres without active objects
        for (int vi = 0; vi < MaxExplosions; vi++)
        {
            if (_expTimers[vi] > 0f)
            {
                _expTimers[vi] -= dt;
                float p = 1f - math.max(0, _expTimers[vi] / 0.35f);
                float currentRadius = _expMaxScales[vi] * p;
                float4 pos = _expPosData[vi];
                pos.w = currentRadius;
                _expPosData[vi] = pos;
            }
            else
            {
                float4 pos = _expPosData[vi];
                pos.w = 0f;
                _expPosData[vi] = pos;
            }
        }
        UpdateAOERings(dt);
        UpdateBullets(dt);
        RenderBullets();
        RenderEnemies();
        RenderExplosions();
        RenderTornados();
        // Light Pillar Array replaces Tornado instancing
        ScheduleSimulation(math.max(dt, 0.0001f));

        if (_uiText != null)
        {
            int mm = Mathf.FloorToInt(_survivalTime / 60);
            int ss = Mathf.FloorToInt(_survivalTime % 60);
            string[] skillNames = { "TRN", "BMB", "LSR", "MLR", "ORB", "BLT" };
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"FPS: {Mathf.RoundToInt(_fps)}  |  SURVIVAL: {mm:D2}:{ss:D2}");
            sb.AppendLine($"LEVEL: {currentLevel} | KILLS: {totalKills}");
            sb.AppendLine($"ACTIVE ENEMIES: {_currentMaxEnemies} / {enemyCount}");
            sb.AppendLine($"PLAYER HP: {Mathf.RoundToInt(playerHealth)} / {playerMaxHealth}");
            sb.AppendLine();
            sb.AppendLine("SKILLS (Lv / Kills):");
            for (int sk = 0; sk < 6; sk++)
                sb.Append($"{skillNames[sk]}:Lv{_skillLevels[sk]}({_skillTotalKills[sk]})  ");
            sb.AppendLine();
            sb.AppendLine();
            sb.AppendLine("SKILLS:");
            sb.AppendLine("AUTO SHOOT: Passive");
            sb.AppendLine($"SPACE: Leap Smash (CD: {Mathf.Max(0, _jumpCooldownTimer):F1}s)");
            sb.AppendLine($"Q: Light Pillar Strike (CD: {Mathf.Max(0, _tornadoCooldownTimer):F1}s)");
            sb.AppendLine($"E: Bomb Throw (CD: {Mathf.Max(0, _bombCooldownTimer):F1}s)");
            sb.AppendLine($"R: Laser Beam (CD: {Mathf.Max(0, _laserCooldownTimer):F1}s)");
            sb.AppendLine($"MOUSE L-CLICK: Melee Slash (CD: {Mathf.Max(0, _meleeCooldownTimer):F1}s)");
            sb.AppendLine($"V: Shockwave (CD: {Mathf.Max(0, _shockwaveCooldownTimer):F1}s)");
            sb.AppendLine($"T: Meteor Rain (CD: {Mathf.Max(0, _meteorCooldownTimer):F1}s)");
            sb.AppendLine($"C: Ice Zone (CD: {Mathf.Max(0, _iceZoneCooldownTimer):F1}s)");
            sb.AppendLine($"L-SHIFT: Dash (CD: {Mathf.Max(0, _dashCooldownTimer):F1}s)");
            int numOrbBalls = math.min(8, 1 + _skillLevels[4] / 4);
            sb.AppendLine($"Orbit Ball x{numOrbBalls}/8 (Passive)");
            _uiText.text = sb.ToString();
        }
    }

    private void Initialize()
    {
        Dispose();

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
            rt.sizeDelta = new Vector2(600, 550);
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
        
        _laserTimer = 0f;
        _laserCooldownTimer = 0f;
        _meleeCooldownTimer = 0f;

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
            }
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

        // Pre-create AOE ring visuals
        for (int ri = 0; ri < MaxAOERings; ri++)
        {
            if (_aoeRingVisuals[ri] == null && _cylinderMesh != null && _aoeRingMat != null)
            {
                _aoeRingVisuals[ri] = new GameObject("AoeRing_" + ri);
                _aoeRingVisuals[ri].AddComponent<MeshFilter>().sharedMesh = _cylinderMesh;
                _aoeRingVisuals[ri].AddComponent<MeshRenderer>().material = _aoeRingMat;
                _aoeRingVisuals[ri].SetActive(false);
            }
            _aoeRingTimers[ri] = 0f;
        }

        // Pre-create shockwave ring visuals  
        for (int si = 0; si < ShockwaveRingCount; si++)
        {
            if (_shockwaveRingVisuals[si] == null && _cylinderMesh != null && _shockwaveRingMat != null)
            {
                _shockwaveRingVisuals[si] = new GameObject("ShockwaveRing_" + si);
                _shockwaveRingVisuals[si].AddComponent<MeshFilter>().sharedMesh = _cylinderMesh;
                _shockwaveRingVisuals[si].AddComponent<MeshRenderer>().material = _shockwaveRingMat;
                _shockwaveRingVisuals[si].SetActive(false);
            }
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
            _iceZoneMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            _iceZoneMat.color = new Color(0.3f, 0.7f, 1f, 0.4f);
            _iceZoneMat.SetFloat("_Surface", 1f);
            _iceZoneMat.SetColor("_EmissionColor", new Color(0.2f, 0.5f, 1f, 1f) * 2f);
            _iceZoneVisual.GetComponent<MeshRenderer>().material = _iceZoneMat;
            _iceZoneVisual.SetActive(false);
        }

        _shockwaveCooldownTimer = 0f;
        _meteorCooldownTimer = 0f;
        _iceZoneCooldownTimer = 0f;
        _dashCooldownTimer = 0f;



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
        }
    }

    private void UpdateSkills(float dt)
    {
        if (_jumpCooldownTimer > 0f) _jumpCooldownTimer -= dt;
        if (_tornadoCooldownTimer > 0f) _tornadoCooldownTimer -= dt;
        if (_bombCooldownTimer > 0f) _bombCooldownTimer -= dt;
        if (_laserCooldownTimer > 0f) _laserCooldownTimer -= dt;
        if (_meleeCooldownTimer > 0f) _meleeCooldownTimer -= dt;

        float2 playerPos = player != null ? player.PlanarPosition : float2.zero;
        Vector3 aim = player != null ? player.AimDirection : Vector3.forward;
        float2 aimDir = math.normalizesafe(new float2(aim.x, aim.z), new float2(0f, 1f));

        _skillAreaCount = 0;

        // Jump (Leap Smash)
        if (Input.GetKeyDown(KeyCode.Space) && _jumpCooldownTimer <= 0f && _jumpState == 0)
        {
            _jumpCooldownTimer = 8f;
            _jumpState = 1;
            _jumpTimer = 0.5f; // half second air time
            _jumpStart = player != null ? player.transform.position : new Vector3(playerPos.x, renderHeight, playerPos.y);
            
            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            float distToGround = (renderHeight - ray.origin.y) / ray.direction.y;
            Vector3 hitPoint = ray.origin + ray.direction * distToGround;
            
            Vector3 diff = hitPoint - _jumpStart;
            diff.y = 0;
            float maxDist = 20f;
            if (diff.magnitude > maxDist) hitPoint = _jumpStart + diff.normalized * maxDist;
            
            _jumpTarget = hitPoint;
            _jumpTarget.y = renderHeight;
            if (player != null) player.enabled = false;
        }

        if (_jumpState == 1)
        {
            _jumpTimer -= dt;
            float progress = 1f - math.max(0, _jumpTimer / 0.5f);
            if (progress >= 1f)
            {
                _jumpState = 0;
                _invincibilityTimer = 0.5f; // invincible for 0.5s after landing
                if (player != null) 
                {
                    player.transform.position = _jumpTarget;
                    player.enabled = true;
                }
                
                // Explode AoE upon landing
                _skillAreasDb[_skillAreaCount++] = new RougeSkillArea {
                    Type = 2,
                    Position = new float2(_jumpTarget.x, _jumpTarget.z),
                    Radius = 18f,
                    Damage = 1500f,
                    PullForce = -300f, // huge knockback
                    VerticalForce = 60f
                };
                SpawnExplosionVFX(new Vector3(_jumpTarget.x, renderHeight + 1f, _jumpTarget.z), 8f);
                SpawnAOERing(new Vector3(_jumpTarget.x, renderHeight, _jumpTarget.z), 18f, 0.5f, new Color(1f, 0.6f, 0.1f, 1f));
            }
            else
            {
                Vector3 cur = Vector3.Lerp(_jumpStart, _jumpTarget, progress);
                cur.y += math.sin(progress * math.PI) * 8f; // arch height = 8
                if (player != null) player.transform.position = cur;
                playerPos = new float2(cur.x, cur.z); // update virtual pos for this frame
            }
        }

        // Light Pillar Array (Q)
        if (Input.GetKeyDown(tornadoKey) && _tornadoCooldownTimer <= 0f)        
        {
            _tornadoCooldownTimer = 10f; // base cd
            _pillarStrikesTotal = 4 + (currentLevel / 5); // 4 base strikes + 1 per 5 levels
            _pillarStrikesDone = 0;
            _pillarNextStrikeTimer = 0f;
            _pillarBasePos = playerPos;
            _pillarDirection = aimDir;
            if (_tornadoVisual) _tornadoVisual.SetActive(false); // Make sure old stand-in is off
        }

        if (_pillarStrikesDone < _pillarStrikesTotal)
        {
            _pillarNextStrikeTimer -= dt;
            if (_pillarNextStrikeTimer <= 0f)
            {
                float dist = 6f + _pillarStrikesDone * 14f; 
                float2 strikePos = _pillarBasePos + _pillarDirection * dist;
                float strikeRadius = math.min(10f + currentLevel * 0.1f, 25f); // grow with max cap
                
                // Spawn visual pillar (using Tornado instanced rendering arrays)
                for(int i = 0; i < MaxTornados; i++) {
                    if (_tornadoLifeTimers[i] <= 0f) {
                        _tornadoPosData[i] = new float4(strikePos.x, renderHeight + 30f, strikePos.y, strikeRadius); // Pos + max radius
                        _tornadoStateData[i] = new float4(strikeRadius, 60f, strikeRadius, 1f); // XZ scale, Y scale, Alpha
                        _tornadoLifeTimers[i] = 0.5f; // duration
                        _tornadoMaxTimes[i] = 0.5f;
                        break;
                    }
                }

                // Apply Physical Area Damage + physics Knockups
                if (_skillAreaCount < _skillAreasDb.Length)
                {
                    _skillAreasDb[_skillAreaCount++] = new RougeSkillArea {
                        Type = 2, // Map to ProcessBomb (Circular Explosion & vertical knockup logic)
                        Position = strikePos,
                        Radius = strikeRadius,
                        Damage = 400f + currentLevel * 40f,
                        PullForce = -120f,
                        VerticalForce = 45f
                    };
                    SpawnExplosionVFX(new Vector3(strikePos.x, renderHeight + 1f, strikePos.y), strikeRadius);
                    SpawnAOERing(new Vector3(strikePos.x, renderHeight, strikePos.y), strikeRadius, 0.4f, new Color(1f, 0.9f, 0.2f, 1f));
                }

                _pillarStrikesDone++;
                _pillarNextStrikeTimer = 0.15f; 
            }
        }

        // Bomb (Parabolic to mouse)
        bool hasActiveBomb = false;
        for (int i=0; i<MaxBombs; i++) if (_activeBombs[i].Active) { hasActiveBomb = true; break; }
        
        if (Input.GetKeyDown(KeyCode.E) && _bombCooldownTimer <= 0f && !hasActiveBomb)    
        {
            _bombCooldownTimer = 3f;
            
            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            float distToGround = (renderHeight - ray.origin.y) / ray.direction.y;
            Vector3 hitPoint = ray.origin + ray.direction * distToGround;
            
            Vector3 startPos = player != null ? player.transform.position + Vector3.up * 2f : new Vector3(playerPos.x, renderHeight + 2f, playerPos.y);
            Vector3 targetPos = hitPoint;
            
            // Limit distance
            float maxDist = 30f;
            Vector3 diff = targetPos - startPos;
            diff.y = 0;
            if (diff.magnitude > maxDist) targetPos = startPos + diff.normalized * maxDist + Vector3.up * (targetPos.y - startPos.y);
            
            float flightTime = 0.8f;
            Vector3 vel = (targetPos - startPos) / flightTime;
            vel.y = (targetPos.y - startPos.y - 0.5f * Physics.gravity.y * flightTime * flightTime) / flightTime;
            
            _activeBombs[0] = new RougeBomb { Active = true, Position = startPos, Velocity = vel, BounceCount = 0, BaseRadius = 15f };
            if (_bombVisuals[0]) _bombVisuals[0].SetActive(true);
        }

        for (int i = 0; i < MaxBombs; i++)
        {
            if (!_activeBombs[i].Active) continue;
            
            _activeBombs[i].Velocity += Physics.gravity * dt;
            _activeBombs[i].Position += _activeBombs[i].Velocity * dt;
            
            if (_bombVisuals[i]) 
            {
                _bombVisuals[i].transform.position = _activeBombs[i].Position;
                _bombVisuals[i].transform.localScale = Vector3.one * math.max(_activeBombs[i].BaseRadius * 0.5f - _activeBombs[i].BounceCount * 1.5f, 3f); 
            }
            
            if (_activeBombs[i].Position.y <= renderHeight)
            {
                // Explosion upon ground contact
                float bounceRadius = math.max(_activeBombs[i].BaseRadius - _activeBombs[i].BounceCount * 2.5f, 4f);
                float bounceDamage = math.max(350f * (float)math.pow(0.65, _activeBombs[i].BounceCount), 60f);

                if (_skillAreaCount < _skillAreasDb.Length)
                {
                    _skillAreasDb[_skillAreaCount++] = new RougeSkillArea {
                        Type = 2,
                        Position = new float2(_activeBombs[i].Position.x, _activeBombs[i].Position.z),
                        Radius = bounceRadius,
                        Damage = bounceDamage,
                        PullForce = -150f,
                        VerticalForce = 50f
                    };
                    SpawnExplosionVFX(new Vector3(_activeBombs[i].Position.x, renderHeight + 1f, _activeBombs[i].Position.z), bounceRadius * 0.5f);
                    SpawnAOERing(new Vector3(_activeBombs[i].Position.x, renderHeight, _activeBombs[i].Position.z), bounceRadius, 0.4f, new Color(1f, 0.5f, 0f, 1f));
                }

                // If this is the main bomb and level is high, spawn split fragments
                if (i == 0 && _activeBombs[i].BounceCount == 0 && currentLevel >= 5)
                {
                    float angleBase = math.atan2(_activeBombs[i].Velocity.z, _activeBombs[i].Velocity.x);
                    int splitCount = math.min(10, 2 + currentLevel / 10);
                    int spawned = 0;
                    for (int s = 1; s < MaxBombs && spawned < splitCount; s++) 
                    {
                        if (!_activeBombs[s].Active)
                        {
                            float spreadOffset = (spawned * (360f / splitCount)) * math.PI / 180f;
                            Vector3 scatterVel = new Vector3(
                                math.cos(angleBase + spreadOffset) * 12f,
                                14f, // Pop up into air
                                math.sin(angleBase + spreadOffset) * 12f
                            );
                            _activeBombs[s] = new RougeBomb { Active = true, Position = _activeBombs[i].Position, Velocity = scatterVel, BounceCount = 1, BaseRadius = 10f };
                            if (_bombVisuals[s]) _bombVisuals[s].SetActive(true);
                            spawned++;
                        }
                    }
                }

                _activeBombs[i].BounceCount++;
                float2 hVel = new float2(_activeBombs[i].Velocity.x, _activeBombs[i].Velocity.z);
                
                if (_activeBombs[i].BounceCount >= 4 || math.lengthsq(hVel) < 4f)
                {
                    _activeBombs[i].Active = false;
                    if (_bombVisuals[i]) _bombVisuals[i].SetActive(false);
                }
                else
                {
                    // Bounce
                    _activeBombs[i].Position = new Vector3(_activeBombs[i].Position.x, renderHeight + 0.1f, _activeBombs[i].Position.z);
                    float bounceUpVel = math.max(-_activeBombs[i].Velocity.y * 0.85f, 12f - _activeBombs[i].BounceCount * 2f);
                    _activeBombs[i].Velocity = new Vector3(
                        _activeBombs[i].Velocity.x * 0.75f,
                        bounceUpVel,
                        _activeBombs[i].Velocity.z * 0.75f
                    );
                }
            }
        }

        // Laser
        if (Input.GetKeyDown(KeyCode.R) && _laserCooldownTimer <= 0f)    
        {
            _laserCooldownTimer = 6f;
            _laserTimer = 0.5f + (_skillLevels[2] / 30f) * 1.5f; // duration max 2s
            _laserPos = playerPos;
            _laserDir = aimDir;
        }
        
        if (_laserTimer > 0f)
        {
            float laserDur = 0.5f + (_skillLevels[2] / 30f) * 1.5f;
            _laserTimer -= dt;
            float p = 1f - math.max(0f, _laserTimer / laserDur);
            
            float sweepP = math.saturate(p * 3f); // reaches full length at 33% of duration
            float sweepEased = 1f - (1f - sweepP) * (1f - sweepP); // ease-out
            float lgth = 150f * sweepEased; // increased length from 100 to 150

            // New Laser Visuals: Pulsing inner core + fading wider beam
            float laserWidth = 14f * math.min(1f, 2f * p) * (1f - p); // Swells up, then thins out towards end

            if (_laserVisual)
            {
                _laserVisual.SetActive(true);
                _laserVisual.transform.position = new Vector3(_laserPos.x + _laserDir.x * (lgth*0.5f), renderHeight + 1f, _laserPos.y + _laserDir.y * (lgth*0.5f));
                _laserVisual.transform.rotation = Quaternion.LookRotation(new Vector3(_laserDir.x, 0, _laserDir.y)) * Quaternion.Euler(90, 0, 0);
                _laserVisual.transform.localScale = new Vector3(laserWidth, lgth * 0.5f, laserWidth * 0.4f); // Flatter oval shape
            }
            
            _skillAreasDb[_skillAreaCount++] = new RougeSkillArea {
                Type = 3,
                Position = _laserPos,
                Direction = _laserDir,
                Length = lgth,
                Radius = laserWidth,
                Damage = 400f,
                PullForce = 50f,
                VerticalForce = 0f
            };
        } 
        else if (_laserVisual) _laserVisual.SetActive(false);

        if (_meleeComboWindow > 0f) _meleeComboWindow -= dt;
        else _meleeComboStep = 0;

        // Melee (Left Click)
        if (Input.GetMouseButtonDown(0) && _meleeCooldownTimer <= 0f)
        {
            _meleePos = playerPos;
            _meleeDir = aimDir;
            
            _meleeComboStep++;
            if (_meleeComboStep > 5) _meleeComboStep = 1;

            if (_meleeComboStep == 5) // Ground Spike Finisher
            {
                _spikeTimer = 0.28f;
                _spikePos = playerPos;
                _spikeDir = aimDir;
                _meleeComboStep = 0;
                _meleeComboWindow = 0f;
                _meleeCooldownTimer = 1.5f;
                _meleeTimer = 0f; // no melee animation after spike finisher
                // Spike finisher: screen shake instead of AOE ring
                _meleeHitShake = 0.15f;
            }
            else
            {
                _meleeCooldownTimer = 0.22f; // faster combo speed for better feel
                _meleeComboWindow = 1.5f;
                _meleeTimer = 0.22f; // faster swing animation
                // Stronger forward lunge on each hit
                float lungeAmount = _meleeComboStep == 3 ? 4f : 3f;
                if (_meleeComboStep == 4) lungeAmount = 1.5f; // spin stays closer
                if (player != null)
                    player.transform.position += new Vector3(aimDir.x, 0f, aimDir.y) * lungeAmount;
                // Screen shake on hit
                _meleeHitShake = 0.08f;
            }
        }
        
        if (_meleeTimer > 0f)
        {
            _meleeTimer -= dt;
            float progress = 1f - math.max(0, _meleeTimer / 0.22f); // faster animation
            // Ease-out curve for snappy feel: fast start, decelerating end
            float easedProgress = 1f - (1f - progress) * (1f - progress);
            
            float angle = 0f;
            float radius = 8f;
            Vector3 scale = new Vector3(radius * 2f, 0.5f, 3f);
            
            if (_meleeComboStep == 1) // Left to right
            {
                angle = math.lerp(-70f, 70f, easedProgress) * math.PI / 180f;
            }
            else if (_meleeComboStep == 2) // Right to left
            {
                angle = math.lerp(70f, -70f, easedProgress) * math.PI / 180f;
            }
            else if (_meleeComboStep == 3) // Thrust/Stab
            {
                angle = 0f;
                radius = math.lerp(3f, 10f, math.sin(easedProgress * math.PI));
                scale = new Vector3(3f, 0.5f, 8f);
                if (player != null)
                {
                    player.transform.position += new Vector3(_meleeDir.x, 0f, _meleeDir.y) * (20f * dt);
                }
            }
            else if (_meleeComboStep == 4) // Circular Spin
            {
                angle = math.lerp(0f, 360f, easedProgress) * math.PI / 180f;
                scale = new Vector3(radius * 2f, 0.5f, radius * 2f);
            }
            
            float2 swingDir = Rotate(_meleeDir, angle);
            Vector3 centerP = new Vector3(playerPos.x, renderHeight + 1f, playerPos.y) + new Vector3(swingDir.x, 0, swingDir.y) * (radius * 0.4f);
            
            if (_meleeVisual)
            {
                _meleeVisual.SetActive(true);
                _meleeVisual.transform.position = centerP;
                _meleeVisual.transform.rotation = Quaternion.LookRotation(new Vector3(swingDir.x, 0, swingDir.y));
                _meleeVisual.transform.localScale = scale;
            }

            _skillAreasDb[_skillAreaCount++] = new RougeSkillArea {
                Type = 4,
                Position = new float2(centerP.x, centerP.z),
                Direction = swingDir,
                Radius = radius,
                Damage = _meleeComboStep == 4 ? 1200f : 800f, 
                PullForce = 120f,
                VerticalForce = _meleeComboStep == 3 ? 25f : 18f
            };
        }
        else if (_meleeVisual) _meleeVisual.SetActive(false);

        // 地刺终结技（连击第5击）
        float spikeDuration = 0.28f;  // �?_spikeTimer 初始值保持一�?
        float spikeRiseRatio = 0.25f; // 更快进入攻击阶段
        if (_spikeTimer > 0f)
        {
            _spikeTimer -= dt;
            float normalizedT = 1f - _spikeTimer / spikeDuration; // 0..1
            float heightFactor = math.saturate(math.sin(normalizedT * math.PI));

            float[] spikeMaxH  = { 14f, 10f, 10f };  // 更高的刺
            float[] spikeRad   = { 7f,  5f,  5f  };  // 更宽的范�?
            float[] spikeDist  = { 14f, 12f, 12f };  // 更远的覆�?
            float[] spikeAngleRad = { 0f, -28f * math.PI / 180f, 28f * math.PI / 180f };

            bool risingPhase = normalizedT < spikeRiseRatio + 0.12f;

            for (int iSpk = 0; iSpk < 3; iSpk++)
            {
                float2 spikeBase = _spikePos + Rotate(_spikeDir, spikeAngleRad[iSpk]) * spikeDist[iSpk];
                float h = spikeMaxH[iSpk] * heightFactor;
                float r = spikeRad[iSpk];

                if (_spikeVisuals[iSpk] != null)
                {
                    bool vis = h > 0.05f;
                    _spikeVisuals[iSpk].SetActive(vis);
                    if (vis)
                    {
                        // 圆柱底部贴地：中心y = renderHeight + h/2
                        _spikeVisuals[iSpk].transform.position = new Vector3(spikeBase.x, renderHeight + h * 0.5f, spikeBase.y);
                        _spikeVisuals[iSpk].transform.localScale = new Vector3(r * 2f, h * 0.5f, r * 2f);
                    }
                }

                // 上升阶段发出技能区域撞飞敌�?
                if (risingPhase && h > 0.3f && _skillAreaCount < _skillAreasDb.Length)
                {
                    _skillAreasDb[_skillAreaCount++] = new RougeSkillArea {
                        Type = 6,
                        Position = spikeBase,
                        Radius = r + 3f,   // 更宽的冲击范�?
                        VerticalForce = iSpk == 0 ? 65f : 45f,  // 降低就高防止视线遮挡
                        PullForce = 40f,   // 向外散射冲击
                        Damage = 0f // 死亡由落地条件处�?
                    };
                }
            }

            if (_spikeTimer <= 0f)
                for (int iSpk = 0; iSpk < 3; iSpk++)
                    if (_spikeVisuals[iSpk] != null) _spikeVisuals[iSpk].SetActive(false);
        }

        // Orbit Balls (Passive) �?�?50级解�?个，最�?2个，2圈�?个分�?
        int numBalls = math.min(8, 1 + _skillLevels[4] / 4);
        if (numBalls > 0)
        {
            _orbitTimer += dt * 2.5f;
            float[] ringRadii  = { 6f, 10f };
            float[] ringSpeeds = { 1.0f, 0.75f };

            while (_orbitVisuals.Count < numBalls)
            {
                var ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                Destroy(ball.GetComponent<Collider>());
                ball.name = "Orbit Ball " + _orbitVisuals.Count;
                ball.GetComponent<MeshRenderer>().material = _orbitMat;
                _orbitVisuals.Add(ball);
            }

            for (int objIdx = _orbitVisuals.Count - 1; objIdx >= numBalls; objIdx--)
            {
                Destroy(_orbitVisuals[objIdx]);
                _orbitVisuals.RemoveAt(objIdx);
            }

            for (int b = 0; b < numBalls; b++)
            {
                int ring        = b / 4;
                int ballInRing  = b % 4;
                float orbRadius = ringRadii[math.min(ring, ringRadii.Length - 1)];
                float speedMult = ringSpeeds[math.min(ring, ringSpeeds.Length - 1)];
                float offset    = (float)ballInRing / 4f * (math.PI * 2f);
                float oTime     = _orbitTimer * speedMult + offset;
                float2 orbitPos = playerPos + new float2(math.cos(oTime), math.sin(oTime)) * orbRadius;

                if (_orbitVisuals[b] != null)
                {
                    _orbitVisuals[b].transform.position = new Vector3(orbitPos.x, renderHeight + 1.5f, orbitPos.y);
                    _orbitVisuals[b].transform.localScale = Vector3.one * 3f;
                }

                if (_skillAreaCount < _skillAreasDb.Length)
                    _skillAreasDb[_skillAreaCount++] = new RougeSkillArea {
                        Type = 5,
                        Position = orbitPos,
                        Radius = 2f,
                        Damage = 45f + (currentLevel * 2f),
                        PullForce = 15f,
                        VerticalForce = 3f
                    };
            }
        }
        else
        {
            for (int objIdx = _orbitVisuals.Count - 1; objIdx >= 0; objIdx--)
            {
                Destroy(_orbitVisuals[objIdx]);
                _orbitVisuals.RemoveAt(objIdx);
            }
        }

        // ---- Shockwave (V) ---- 多圈圆环依次抬起+消失，抬起敌人落地秒杀
        if (_shockwaveCooldownTimer > 0f) _shockwaveCooldownTimer -= dt;
        if (Input.GetKeyDown(KeyCode.V) && _shockwaveCooldownTimer <= 0f)
        {
            _shockwaveCooldownTimer = 30f;
            _shockwaveTimer = 1.8f; // longer for multi-ring effect
            _shockwaveRadius = 0f;
            _shockwavePos = playerPos;
            // Stagger ring launches
            for (int sr = 0; sr < ShockwaveRingCount; sr++)
                _shockwaveRingTimers[sr] = 0.8f + sr * 0.12f; // rings appear in sequence, outward
        }

        if (_shockwaveTimer > 0f)
        {
            _shockwaveTimer -= dt;

            // Animate each ring independently
            for (int sr = 0; sr < ShockwaveRingCount; sr++)
            {
                if (_shockwaveRingTimers[sr] > 0f)
                {
                    _shockwaveRingTimers[sr] -= dt;
                    float ringProg = 1f - math.max(0, _shockwaveRingTimers[sr] / 0.8f);
                    float ringRadius = math.lerp(2f + sr * 6f, 10f + sr * 6f, ringProg);
                    float ringHeight = math.sin(ringProg * math.PI) * (6f - sr * 0.5f); // rises and falls
                    float ringAlpha = math.sin(ringProg * math.PI); // fade in then out

                    if (_shockwaveRingVisuals[sr] != null)
                    {
                        _shockwaveRingVisuals[sr].SetActive(true);
                        _shockwaveRingVisuals[sr].transform.position = new Vector3(_shockwavePos.x, renderHeight + ringHeight, _shockwavePos.y);
                        _shockwaveRingVisuals[sr].transform.localScale = new Vector3(ringRadius * 2f, 1.5f * ringAlpha + 0.2f, ringRadius * 2f);
                        var rend = _shockwaveRingVisuals[sr].GetComponent<MeshRenderer>();
                        if (rend != null && rend.material != null)
                        {
                            rend.material.SetFloat("_InnerRadiusRatio", 0.82f);
                            rend.material.SetColor("_Color", new Color(1f, 0.8f, 0.1f, ringAlpha));
                        }
                    }

                    if (_skillAreaCount < _skillAreasDb.Length && ringProg > 0.05f && ringProg < 0.95f)
                    {
                        _skillAreasDb[_skillAreaCount++] = new RougeSkillArea {
                            Type = 7,
                            Position = _shockwavePos,
                            Radius = ringRadius,
                            Length = 3f,
                            Damage = 0f,
                            PullForce = -80f,
                            VerticalForce = 85f
                        };
                    }
                }
                else if (_shockwaveRingVisuals[sr] != null)
                {
                    _shockwaveRingVisuals[sr].SetActive(false);
                }
            }
        }
        else 
        {
            if (_shockwaveVisual) _shockwaveVisual.SetActive(false);
            for (int sr = 0; sr < ShockwaveRingCount; sr++)
                if (_shockwaveRingVisuals[sr] != null) _shockwaveRingVisuals[sr].SetActive(false);
        }

        // ---- Meteor Rain (T) ----
        if (_meteorCooldownTimer > 0f) _meteorCooldownTimer -= dt;
        if (Input.GetKeyDown(KeyCode.T) && _meteorCooldownTimer <= 0f)
        {
            _meteorCooldownTimer = 8f;
            _meteorTimer = 2.0f;
            _meteorWaveIndex = 0;
            _meteorWaveTimer = 0f;
            
            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            float distToGround = (renderHeight - ray.origin.y) / ray.direction.y;
            Vector3 hitPoint = ray.origin + ray.direction * distToGround;
            _meteorTargetPos = new float2(hitPoint.x, hitPoint.z);
        }

        if (_meteorTimer > 0f)
        {
            _meteorTimer -= dt;
            _meteorWaveTimer -= dt;
            if (_meteorWaveTimer <= 0f && _meteorWaveIndex < 8)
            {
                _meteorWaveTimer = 0.2f;
                uint mHash = math.hash(new uint2((uint)_meteorWaveIndex + 1u, (uint)(Time.frameCount)));
                float mAngle = ((mHash & 0xFFFFu) / 65535f) * math.PI * 2f;
                float mDist = ((mHash >> 16) & 0xFFFFu) / 65535f * 20f;
                float2 impactPos = _meteorTargetPos + new float2(math.cos(mAngle), math.sin(mAngle)) * mDist;
                
                // Spawn meteor visual: ball falling from sky to impact
                if (_meteorWaveIndex < MeteorVisualMax)
                {
                    _meteorVisualTimers[_meteorWaveIndex] = 0.5f;
                    _meteorVisualTargets[_meteorWaveIndex] = new Vector3(impactPos.x, renderHeight, impactPos.y);
                    if (_meteorVisuals[_meteorWaveIndex] != null)
                        _meteorVisuals[_meteorWaveIndex].SetActive(true);
                }
                _meteorWaveIndex++;
            }
        }

        // Animate meteor visual spheres
        for (int mi = 0; mi < MeteorVisualMax; mi++)
        {
            if (_meteorVisualTimers[mi] > 0f)
            {
                float prevTimer = _meteorVisualTimers[mi];
                _meteorVisualTimers[mi] -= dt;
                float mp = 1f - math.max(0f, _meteorVisualTimers[mi] / 0.5f);
                Vector3 target = _meteorVisualTargets[mi];
                Vector3 mPos = target + new Vector3(10f - mp * 10f, 60f * (1f - mp), 5f - mp * 5f); // diagonal drop
                float mScale = math.lerp(6f, 3f, mp);
                if (_meteorVisuals[mi] != null)
                {
                    _meteorVisuals[mi].transform.position = mPos;
                    _meteorVisuals[mi].transform.localScale = new Vector3(mScale, mScale * 2f, mScale); // stretch to look like falling
                    _meteorVisuals[mi].transform.rotation = Quaternion.LookRotation((target - mPos).normalized);
                    if (mp >= 1f) _meteorVisuals[mi].SetActive(false);
                }

                // impact occurs when it reaches 0
                if (_meteorVisualTimers[mi] <= 0f && prevTimer > 0f)
                {
                    if (_skillAreaCount < _skillAreasDb.Length)
                        _skillAreasDb[_skillAreaCount++] = new RougeSkillArea {
                            Type = 2,
                            Position = new float2(target.x, target.z),
                            Radius = 15f,
                            Damage = 900f, // enhanced damage
                            PullForce = -220f,
                            VerticalForce = 55f
                        };
                    SpawnExplosionVFX(new Vector3(target.x, renderHeight + 1f, target.z), 15f);
                    SpawnAOERing(new Vector3(target.x, renderHeight, target.z), 15f, 0.45f, new Color(1f, 0.2f, 0f, 1f));
                }
            }
        }

        // ---- Ice Zone (C) ----
        if (_iceZoneCooldownTimer > 0f) _iceZoneCooldownTimer -= dt;
        if (Input.GetKeyDown(KeyCode.C) && _iceZoneCooldownTimer <= 0f)
        {
            _iceZoneCooldownTimer = 10f;
            _iceZoneTimer = 4.0f;
            
            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            float distToGround = (renderHeight - ray.origin.y) / ray.direction.y;
            Vector3 hitPoint = ray.origin + ray.direction * distToGround;
            _iceZonePos = new float2(hitPoint.x, hitPoint.z);
        }

        if (_iceZoneTimer > 0f)
        {
            float prevIceTimer = _iceZoneTimer;
            _iceZoneTimer -= dt;
            float iceRadius = math.lerp(8f, 25f, math.min(1f, currentLevel / 500f));

            if (_iceZoneVisual)
            {
                _iceZoneVisual.SetActive(true);
                _iceZoneVisual.transform.position = new Vector3(_iceZonePos.x, renderHeight + 0.2f, _iceZonePos.y);
                _iceZoneVisual.transform.localScale = new Vector3(iceRadius * 2f, 0.1f, iceRadius * 2f);
                float pulse = 0.3f + 0.1f * math.sin(_survivalTime * 6f);
                if (_iceZoneMat != null)
                    _iceZoneMat.color = new Color(0.3f, 0.7f, 1f, pulse);
            }

            if (_skillAreaCount < _skillAreasDb.Length)
                _skillAreasDb[_skillAreaCount++] = new RougeSkillArea {
                    Type = 8,
                    Position = _iceZonePos,
                    Radius = iceRadius,
                    Damage = 80f,
                    PullForce = 30f,
                    VerticalForce = 0f
                };

            // End burst: when timer crosses zero
            if (prevIceTimer > 0f && _iceZoneTimer <= 0f)
            {
                if (_skillAreaCount < _skillAreasDb.Length)
                    _skillAreasDb[_skillAreaCount++] = new RougeSkillArea {
                        Type = 2,
                        Position = _iceZonePos,
                        Radius = iceRadius + 5f,
                        Damage = 800f,
                        PullForce = -250f,
                        VerticalForce = 40f
                    };
                SpawnExplosionVFX(new Vector3(_iceZonePos.x, renderHeight + 2f, _iceZonePos.y), iceRadius);
                SpawnAOERing(new Vector3(_iceZonePos.x, renderHeight, _iceZonePos.y), iceRadius + 5f, 0.6f, new Color(0.3f, 0.7f, 1f, 1f));
            }
        }
        else if (_iceZoneVisual) _iceZoneVisual.SetActive(false);

        // ---- Dash (Left Shift) ----
        if (_dashCooldownTimer > 0f) _dashCooldownTimer -= dt;
        if (Input.GetKeyDown(KeyCode.LeftShift) && _dashCooldownTimer <= 0f && _jumpState == 0 && player != null)
        {
            _dashCooldownTimer = 3f;
            float dashDist = 18f;
            Vector3 startP = player.transform.position;
            Vector3 endP = startP + new Vector3(aimDir.x, 0f, aimDir.y) * dashDist;
            endP.x = Mathf.Clamp(endP.x, -arenaHalfExtent + 1f, arenaHalfExtent - 1f);
            endP.z = Mathf.Clamp(endP.z, -arenaHalfExtent + 1f, arenaHalfExtent - 1f);
            player.transform.position = endP;
            _invincibilityTimer = 0.3f;

            float2 dashCenter = (new float2(startP.x, startP.z) + new float2(endP.x, endP.z)) * 0.5f;
            if (_skillAreaCount < _skillAreasDb.Length)
                _skillAreasDb[_skillAreaCount++] = new RougeSkillArea {
                    Type = 3,
                    Position = new float2(startP.x, startP.z),
                    Direction = aimDir,
                    Length = dashDist,
                    Radius = 5f,
                    Damage = 500f,
                    PullForce = 80f,
                    VerticalForce = 10f
                };
        }
    }

    private void UpdateBullets(float dt)
    {
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

        Matrix4x4[] matrices = new Matrix4x4[_activeBulletCount];
        for (int i = 0; i < _activeBulletCount; i++)
        {
            RougeBullet bullet = _bullets[i];
            Vector3 pos = new Vector3(bullet.Current.x, renderHeight + 0.5f, bullet.Current.y);
            Vector3 scale = Vector3.one * (bullet.Radius * 2f);
            matrices[i] = Matrix4x4.TRS(pos, Quaternion.identity, scale);
        }

        Graphics.DrawMeshInstanced(_bulletMesh, 0, _bulletMaterial, matrices, _activeBulletCount);
    }

    private void FireBullets()
    {
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
                Life = bulletLifetime
            };
        }
    }

    private void RenderExplosions()
    {
        if (_expPosBuffer == null || _vfxSphereMesh == null || _vfxExplosionMat == null) return;
        
        int drawCount = MaxExplosions;
        
        _expPosBuffer.SetData(_expPosData);
        _expStateBuffer.SetData(_expStateData);

        _vfxExplosionMat.SetBuffer(PositionScaleBufferId, _expPosBuffer);
        _vfxExplosionMat.SetBuffer("_StateBuffer", _expStateBuffer);
        _vfxExplosionMat.SetFloat(ScaleMultiplierId, 1f);

        _expDrawArgs[1] = (uint)drawCount;
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

    private void RenderEnemies()
    {
        if (_positionBuffer == null || enemyMesh == null || enemyMaterial == null) return;

        _positionBuffer.SetData(_positionsA);
        _stateBuffer.SetData(_stateA);

        enemyMaterial.SetBuffer(PositionScaleBufferId, _positionBuffer);
        enemyMaterial.SetBuffer("_StateBuffer", _stateBuffer);
        enemyMaterial.SetColor(BaseColorId, new Color(0.88f, 0.18f, 0.18f, 1f));
        enemyMaterial.SetFloat(ScaleMultiplierId, enemyVisualScale);

        Vector3 center = player != null ? player.transform.position : transform.position;
        float extent = math.max(arenaHalfExtent, despawnDistance) * 2f;
        Bounds bounds = new Bounds(center, new Vector3(extent, 32f, extent));

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
        float cellSize = math.max(separationRadius, enemyRadius * 2.5f);
        float invCellSize = 1f / math.max(cellSize, 0.001f);

        JobHandle handle = new BuildEnemyKeysJob
        {
            PositionScaleIn = _positionsA,
            EnemyKeys = _enemyKeys,
            InvCellSize = invCellSize,
            HashMask = _hashMask
        }.ScheduleBatch(enemyCount, sortBatchSize);

        handle = ScheduleRadixSort(handle);

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
        }.ScheduleBatch(enemyCount, sortBatchSize, handle);

        handle = new ReorderEnemiesJob
        {
            SortedKeys = _enemyKeys,
            PositionScaleIn = _positionsA,
            VelocityIn = _velocitiesA,
            StateIn = _stateA,
            PositionScaleOut = _positionsB,
            VelocityOut = _velocitiesB,
            StateOut = _stateB
        }.ScheduleBatch(enemyCount, sortBatchSize, handle);

        float2 playerPos = player != null ? player.PlanarPosition : float2.zero;
        handle = new SimulateEnemiesJob
        {
            SortedKeys = _enemyKeys,
            PositionScaleIn = _positionsB,
            VelocityIn = _velocitiesB,
            StateIn = _stateB,
            PositionScaleOut = _positionsA,
            VelocityOut = _velocitiesA,
            StateOut = _stateA,
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
        }.ScheduleBatch(enemyCount, simulationBatchSize, handle);

        _simulationHandle = handle;
    }

    private JobHandle ScheduleRadixSort(JobHandle dependency)
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
                ChunkCount = _chunkCount
            }.ScheduleBatch(enemyCount, sortBatchSize, handle);

            handle = new BinLocalPrefixSumBatchJob
            {
                Histograms = _histograms,
                BinTotals = _binTotals,
                ChunkCount = _chunkCount
            }.ScheduleBatch(256, 64, handle);

            handle = new GlobalBinSumJob
            {
                BinTotals = _binTotals
            }.Schedule(handle);

            handle = new ApplyGlobalOffsetBatchJob
            {
                Histograms = _histograms,
                BinTotals = _binTotals,
                ChunkCount = _chunkCount
            }.ScheduleBatch(256, 64, handle);

            handle = new ScatterJob
            {
                SrcKeys = _enemyKeys,
                DstKeys = _tempEnemyKeys,
                Histograms = _histograms,
                BatchSize = sortBatchSize,
                Shift = shift,
                ChunkCount = _chunkCount
            }.ScheduleBatch(enemyCount, sortBatchSize, handle);

            handle = new CopyArrayJob
            {
                Src = _tempEnemyKeys,
                Dst = _enemyKeys
            }.ScheduleBatch(enemyCount, sortBatchSize, handle);
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
                float progress = 1f - math.max(0, _aoeRingTimers[i] / _aoeRingMaxTimes[i]);
                // Ring expands outward quickly, then fades
                float currentRadius = _aoeRingMaxRadius[i] * math.sqrt(progress); // sqrt for fast initial expansion
                float ringHeight = math.sin(progress * math.PI) * 3f; // rises then falls
                float alpha = math.sin(progress * math.PI); // fade in/out

                if (_aoeRingVisuals[i] != null)
                {
                    _aoeRingVisuals[i].SetActive(true);
                    _aoeRingVisuals[i].transform.position = _aoeRingPositions[i] + Vector3.up * ringHeight * 0.3f;
                    _aoeRingVisuals[i].transform.localScale = new Vector3(currentRadius * 2f, ringHeight + 0.3f, currentRadius * 2f);
                    var rend = _aoeRingVisuals[i].GetComponent<MeshRenderer>();
                    if (rend != null && rend.material != null)
                    {
                        rend.material.SetFloat("_InnerRadiusRatio", math.lerp(0.7f, 0.92f, progress));
                        Color c = _aoeRingColors[i];
                        c.a = alpha;
                        rend.material.SetColor("_Color", c);
                    }
                }

                if (_aoeRingTimers[i] <= 0f && _aoeRingVisuals[i] != null)
                    _aoeRingVisuals[i].SetActive(false);
            }
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

        if (_tornadoVisual) Destroy(_tornadoVisual);
        if (_bombVisuals != null)
            for (int i=0; i<MaxBombs; i++) if (_bombVisuals[i]) { Destroy(_bombVisuals[i]); _bombVisuals[i] = null; }
        if (_laserVisual) Destroy(_laserVisual);
        if (_tornadoMat) Destroy(_tornadoMat);
        if (_laserMat) Destroy(_laserMat);
        if (_meleeMat) Destroy(_meleeMat);
        if (_meleeVisual) Destroy(_meleeVisual);
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

    private void SpawnExplosionVFX(Vector3 worldPos, float radius)
    {
        for (int i = 0; i < MaxExplosions; i++)
        {
            if (_expTimers[i] <= 0f)
            {
                _expPosData[i] = new float4(worldPos.x, worldPos.y, worldPos.z, radius);
                _expStateData[i] = new float4(1f, 0f, 0f, 0f);
                _expMaxScales[i] = radius;
                _expTimers[i] = 0.35f;
                return;
            }
        }
    }
}

public struct RougeSkillArea
{
    public int Type; // 1: Tornado, 2: Bomb, 3: Laser
    public float2 Position;
    public float2 Direction;
    public float Radius;
    public float Length;
    public float Damage;
    public float PullForce;
    public float SpinForce;
    public float VerticalForce;
}

public struct RougeBullet
{
    public float2 Previous;
    public float2 Current;
    public float2 Velocity;
    public float Radius;
    public float Damage;
    public float Life;
}

public struct RougeObstacle
{
    public int Type; // 0=AABB, 1=Circle
    public float2 Min;
    public float2 Max;
    public float2 Center;
    public float CircleRadius;
    public float Padding;
}

[BurstCompile(FloatMode = FloatMode.Fast, FloatPrecision = FloatPrecision.Standard)]
public unsafe struct BuildEnemyKeysJob : IJobParallelForBatch
{
    [ReadOnly] public NativeArray<float4> PositionScaleIn;
    [NativeDisableParallelForRestriction] public NativeArray<ulong> EnemyKeys;
    public float InvCellSize;
    public int HashMask;

    public void Execute(int startIndex, int count)
    {
        float4* posPtr = (float4*)PositionScaleIn.GetUnsafeReadOnlyPtr();
        ulong* keysPtr = (ulong*)EnemyKeys.GetUnsafePtr();
        int end = startIndex + count;
        for (int i = startIndex; i < end; i++)
        {
            int2 cell = (int2)math.floor(posPtr[i].xz * InvCellSize);
            int hash = ((cell.x * 73856093) ^ (cell.y * 19349663)) & HashMask;
            keysPtr[i] = ((ulong)(uint)hash << 32) | (uint)i;
        }
    }
}

[BurstCompile(FloatMode = FloatMode.Fast, FloatPrecision = FloatPrecision.Standard)]
public unsafe struct LocalHistogramJob : IJobParallelForBatch
{
    [ReadOnly] public NativeArray<ulong> Keys;
    [NativeDisableParallelForRestriction] public NativeArray<int> Histograms;
    public int BatchSize;
    public int Shift;
    public int ChunkCount;

    public void Execute(int startIndex, int count)
    {
        int chunkIndex = startIndex / BatchSize;
        int* localHist = stackalloc int[256];
        UnsafeUtility.MemClear(localHist, 256 * sizeof(int));

        ulong* keysPtr = (ulong*)Keys.GetUnsafeReadOnlyPtr();
        int end = startIndex + count;
        for (int i = startIndex; i < end; i++)
        {
            localHist[(int)((keysPtr[i] >> Shift) & 0xFF)]++;
        }

        int* globalHistPtr = (int*)Histograms.GetUnsafePtr();
        for (int i = 0; i < 256; i++)
        {
            globalHistPtr[i * ChunkCount + chunkIndex] = localHist[i];
        }
    }
}

[BurstCompile(FloatMode = FloatMode.Fast, FloatPrecision = FloatPrecision.Standard)]
public unsafe struct BinLocalPrefixSumBatchJob : IJobParallelForBatch
{
    [NativeDisableParallelForRestriction] public NativeArray<int> Histograms;
    [NativeDisableParallelForRestriction] public NativeArray<int> BinTotals;
    public int ChunkCount;

    public void Execute(int startIndex, int count)
    {
        int* histPtr = (int*)Histograms.GetUnsafePtr();
        int endBin = startIndex + count;
        for (int bin = startIndex; bin < endBin; bin++)
        {
            int start = bin * ChunkCount;
            int sum = 0;
            for (int chunk = 0; chunk < ChunkCount; chunk++)
            {
                int value = histPtr[start + chunk];
                histPtr[start + chunk] = sum;
                sum += value;
            }

            BinTotals[bin] = sum;
        }
    }
}

[BurstCompile(FloatMode = FloatMode.Fast, FloatPrecision = FloatPrecision.Standard)]
public unsafe struct GlobalBinSumJob : IJob
{
    public NativeArray<int> BinTotals;

    public void Execute()
    {
        int* totals = (int*)BinTotals.GetUnsafePtr();
        int sum = 0;
        for (int i = 0; i < 256; i++)
        {
            int value = totals[i];
            totals[i] = sum;
            sum += value;
        }
    }
}

[BurstCompile(FloatMode = FloatMode.Fast, FloatPrecision = FloatPrecision.Standard)]
public unsafe struct ApplyGlobalOffsetBatchJob : IJobParallelForBatch
{
    [NativeDisableParallelForRestriction] public NativeArray<int> Histograms;
    [ReadOnly] public NativeArray<int> BinTotals;
    public int ChunkCount;

    public void Execute(int startIndex, int count)
    {
        int* histPtr = (int*)Histograms.GetUnsafePtr();
        int end = startIndex + count;
        for (int bin = startIndex; bin < end; bin++)
        {
            int globalOffset = BinTotals[bin];
            int histogramIndex = bin * ChunkCount;
            for (int chunk = 0; chunk < ChunkCount; chunk++)
            {
                histPtr[histogramIndex + chunk] += globalOffset;
            }
        }
    }
}

[BurstCompile(FloatMode = FloatMode.Fast, FloatPrecision = FloatPrecision.Standard)]
public unsafe struct ScatterJob : IJobParallelForBatch
{
    [ReadOnly] public NativeArray<ulong> SrcKeys;
    [NativeDisableParallelForRestriction] public NativeArray<ulong> DstKeys;
    [ReadOnly] public NativeArray<int> Histograms;
    public int BatchSize;
    public int Shift;
    public int ChunkCount;

    public void Execute(int startIndex, int count)
    {
        int chunkIndex = startIndex / BatchSize;
        int* localOffsets = stackalloc int[256];
        int* histPtr = (int*)Histograms.GetUnsafeReadOnlyPtr();
        for (int i = 0; i < 256; i++)
        {
            localOffsets[i] = histPtr[i * ChunkCount + chunkIndex];
        }

        ulong* srcPtr = (ulong*)SrcKeys.GetUnsafeReadOnlyPtr();
        ulong* dstPtr = (ulong*)DstKeys.GetUnsafePtr();
        int end = startIndex + count;
        for (int i = startIndex; i < end; i++)
        {
            ulong key = srcPtr[i];
            dstPtr[localOffsets[(int)((key >> Shift) & 0xFF)]++] = key;
        }
    }
}

[BurstCompile(FloatMode = FloatMode.Fast, FloatPrecision = FloatPrecision.Standard)]
public unsafe struct CopyArrayJob : IJobParallelForBatch
{
    [ReadOnly] public NativeArray<ulong> Src;
    [NativeDisableParallelForRestriction] public NativeArray<ulong> Dst;

    public void Execute(int startIndex, int count)
    {
        UnsafeUtility.MemCpy(
            (ulong*)Dst.GetUnsafePtr() + startIndex,
            (ulong*)Src.GetUnsafeReadOnlyPtr() + startIndex,
            count * sizeof(ulong));
    }
}

[BurstCompile(FloatMode = FloatMode.Fast, FloatPrecision = FloatPrecision.Standard)]
public unsafe struct ClearGridJob : IJobParallelForBatch
{
    [NativeDisableParallelForRestriction] public NativeArray<int> CellCounts;
    [NativeDisableParallelForRestriction] public NativeArray<int> CellOffsets;

    public void Execute(int startIndex, int count)
    {
        int* countPtr = (int*)CellCounts.GetUnsafePtr();
        int* offsetPtr = (int*)CellOffsets.GetUnsafePtr();
        UnsafeUtility.MemClear(countPtr + startIndex, count * sizeof(int));
        UnsafeUtility.MemClear(offsetPtr + startIndex, count * sizeof(int));
    }
}

[BurstCompile(FloatMode = FloatMode.Fast, FloatPrecision = FloatPrecision.Standard)]
public unsafe struct BuildCellOffsetsJob : IJobParallelForBatch
{
    [ReadOnly] public NativeArray<ulong> SortedKeys;
    [NativeDisableParallelForRestriction] public NativeArray<int> CellOffsets;
    [NativeDisableParallelForRestriction] public NativeArray<int> CellCounts;

    public void Execute(int startIndex, int count)
    {
        ulong* keysPtr = (ulong*)SortedKeys.GetUnsafeReadOnlyPtr();
        int* offsetsPtr = (int*)CellOffsets.GetUnsafePtr();
        int* countsPtr = (int*)CellCounts.GetUnsafePtr();
        int end = startIndex + count;

        for (int i = startIndex; i < end; i++)
        {
            int hash = (int)(keysPtr[i] >> 32);
            if (i == 0 || hash != (int)(keysPtr[i - 1] >> 32))
            {
                offsetsPtr[hash] = i;
            }

            System.Threading.Interlocked.Increment(ref *(countsPtr + hash));
        }
    }
}

[BurstCompile(FloatMode = FloatMode.Fast, FloatPrecision = FloatPrecision.Standard)]
public unsafe struct ReorderEnemiesJob : IJobParallelForBatch
{
    [ReadOnly] public NativeArray<ulong> SortedKeys;
    [ReadOnly] public NativeArray<float4> PositionScaleIn;
    [ReadOnly] public NativeArray<float4> VelocityIn;
    [ReadOnly] public NativeArray<float4> StateIn;
    [NativeDisableParallelForRestriction] public NativeArray<float4> PositionScaleOut;
    [NativeDisableParallelForRestriction] public NativeArray<float4> VelocityOut;
    [NativeDisableParallelForRestriction] public NativeArray<float4> StateOut;

    public void Execute(int startIndex, int count)
    {
        ulong* keyPtr = (ulong*)SortedKeys.GetUnsafeReadOnlyPtr();
        float4* posInPtr = (float4*)PositionScaleIn.GetUnsafeReadOnlyPtr();
        float4* velInPtr = (float4*)VelocityIn.GetUnsafeReadOnlyPtr();
        float4* stateInPtr = (float4*)StateIn.GetUnsafeReadOnlyPtr();
        float4* posOutPtr = (float4*)PositionScaleOut.GetUnsafePtr();
        float4* velOutPtr = (float4*)VelocityOut.GetUnsafePtr();
        float4* stateOutPtr = (float4*)StateOut.GetUnsafePtr();
        int end = startIndex + count;

        for (int i = startIndex; i < end; i++)
        {
            int sourceIndex = (int)keyPtr[i];
            posOutPtr[i] = posInPtr[sourceIndex];
            velOutPtr[i] = velInPtr[sourceIndex];
            stateOutPtr[i] = stateInPtr[sourceIndex];
        }
    }
}

[BurstCompile(FloatMode = FloatMode.Fast, FloatPrecision = FloatPrecision.Standard)]
public unsafe struct SimulateEnemiesJob : IJobParallelForBatch
{
    [ReadOnly] public NativeArray<ulong> SortedKeys;
    [ReadOnly] public NativeArray<float4> PositionScaleIn;
    [ReadOnly] public NativeArray<float4> VelocityIn;
    [ReadOnly] public NativeArray<float4> StateIn;
    [ReadOnly] public NativeArray<int> CellOffsets;
    [ReadOnly] public NativeArray<int> CellCounts;
    [ReadOnly] public NativeArray<int2> NeighborOffsets;
    [ReadOnly] public NativeArray<RougeBullet> Bullets;
    [ReadOnly] public NativeArray<RougeObstacle> Obstacles;
    [NativeDisableParallelForRestriction] public NativeArray<int> PlayerDamageCount;
    [NativeDisableParallelForRestriction] public NativeArray<int> EnemyKillCount;
    [NativeDisableParallelForRestriction] public NativeArray<float4> PositionScaleOut;
    [NativeDisableParallelForRestriction] public NativeArray<float4> VelocityOut;
    [NativeDisableParallelForRestriction] public NativeArray<float4> StateOut;

    public int BulletCount;
    public int ObstacleCount;
    public float2 PlayerPos;
    public float EnemyMaxHealth;
    public float EnemyRadius;
    public float EnemyMaxSpeed;
    public float ArenaHalfExtent;
    public float SpawnRadiusMin;
    public float SpawnRadiusMax;
    public float DespawnDistanceSq;
    public float ChaseAcceleration;
    public float VelocityDamping;
    public float SeparationRadius;
    public float SeparationStrength;
    public float ObstacleLookAhead;
    public float ObstacleRepulsion;
    public float ObstacleOrbitStrength;
    public float KnockbackResist;
    [WriteOnly] public NativeQueue<float2>.ParallelWriter ExplosionQueue;
    public int CurrentMaxEnemies;
    [ReadOnly] public NativeArray<RougeSkillArea> SkillAreas;
    public int SkillAreaCount;
    public float RenderHeight;
    public float DeltaTime;
    public float InvCellSize;
    public int HashMask;
    public uint FrameSeed;
    public float2 BulletMin;
    public float2 BulletMax;
    // Skill kill tracking & damage multipliers
    [NativeDisableParallelForRestriction] public NativeArray<int> SkillKillCounts;
    public float BombDmgMult;
    public float LaserDmgMult;
    public float MeleeDmgMult;
    public float OrbitDmgMult;
    public float BulletDmgMult;

    public void Execute(int startIndex, int count)
    {
        ulong* keyPtr = (ulong*)SortedKeys.GetUnsafeReadOnlyPtr();
        float4* posInPtr = (float4*)PositionScaleIn.GetUnsafeReadOnlyPtr();
        float4* velInPtr = (float4*)VelocityIn.GetUnsafeReadOnlyPtr();
        float4* stateInPtr = (float4*)StateIn.GetUnsafeReadOnlyPtr();
        float4* posOutPtr = (float4*)PositionScaleOut.GetUnsafePtr();
        float4* velOutPtr = (float4*)VelocityOut.GetUnsafePtr();
        float4* stateOutPtr = (float4*)StateOut.GetUnsafePtr();
        int* offsetsPtr = (int*)CellOffsets.GetUnsafeReadOnlyPtr();
        int* countsPtr = (int*)CellCounts.GetUnsafeReadOnlyPtr();
        int2* neighborOffsetsPtr = (int2*)NeighborOffsets.GetUnsafeReadOnlyPtr();
        RougeBullet* bulletPtr = (RougeBullet*)Bullets.GetUnsafeReadOnlyPtr();
        RougeObstacle* obstaclePtr = (RougeObstacle*)Obstacles.GetUnsafeReadOnlyPtr();

        int endIndex = startIndex + count;
        int lastHashX = int.MinValue;
        int lastHashY = int.MinValue;
        int* neighborStart = stackalloc int[9];
        int* neighborEnd = stackalloc int[9];
            float separationRadiusSq = SeparationRadius * SeparationRadius;

            for (int i = startIndex; i < endIndex; i++)
            {
                int sourceIndex = (int)keyPtr[i];
                if (sourceIndex >= CurrentMaxEnemies)
                {
                    posOutPtr[sourceIndex] = new float4(99999f, -9999f, 99999f, 0);
                    velOutPtr[sourceIndex] = float4.zero;
                    stateOutPtr[sourceIndex] = new float4(-1f, 0f, 0f, 0f);
                    continue;
                }

                float4 pos4 = posInPtr[i];
                float4 vel4 = velInPtr[i];
                float4 state4 = stateInPtr[i];

                float3 pos = pos4.xyz;
                float3 vel = vel4.xyz;
                float tornadoMark = vel4.w; // 0=普�? 1=被龙卷风卷起(落地强制爆炸/不伤玩家)
                float health = state4.x;
                float radius = state4.y;
                float maxSpeed = state4.z;
                float flashTimer = state4.w;

                if (health <= 0f || math.lengthsq(pos.xz - PlayerPos) > DespawnDistanceSq)
                {
                    Respawn(sourceIndex, ref pos4, ref vel4, ref state4);
                    posOutPtr[sourceIndex] = pos4;
                    velOutPtr[sourceIndex] = vel4;
                    stateOutPtr[sourceIndex] = state4;
                    continue;
                }

            int2 cell = (int2)math.floor(pos.xz * InvCellSize);
            int hashX = cell.x * 73856093;
            int hashY = cell.y * 19349663;
            if (hashX != lastHashX || hashY != lastHashY)
            {
                lastHashX = hashX;
                lastHashY = hashY;
                for (int n = 0; n < 9; n++)
                {
                    int2 offset = neighborOffsetsPtr[n];
                    int hash = ((hashX + offset.x) ^ (hashY + offset.y)) & HashMask;
                    neighborStart[n] = offsetsPtr[hash];
                    neighborEnd[n] = neighborStart[n] + countsPtr[hash];
                }
            }

            float2 desired = math.normalizesafe(PlayerPos - pos.xz);
            bool isAirborne = pos.y > RenderHeight + 0.5f;
            
            float3 acceleration = new float3(0f, -30f, 0f);  // 高重力让敌人快速落�?
            if (!isAirborne) 
            {
                acceleration.xz += desired * ChaseAcceleration;
            }
            
            float2 separation = float2.zero;

            for (int n = 0; n < 9; n++)
            {
                for (int k = neighborStart[n]; k < neighborEnd[n]; k++)
                {
                    if (k == i) continue;
                    float2 other = posInPtr[k].xz;
                    float2 diff = pos.xz - other;
                    float distSq = math.lengthsq(diff);
                    if (distSq < separationRadiusSq && distSq > 0.0001f)
                    {
                        float dist = math.sqrt(distSq);
                        float weight = 1f - math.saturate(dist / SeparationRadius);
                        separation += (diff / dist) * (weight * SeparationStrength);
                    }
                }
            }

            float sepLen = math.length(separation);
            if (sepLen > SeparationStrength * 3f) separation = (separation / sepLen) * (SeparationStrength * 3f);
            
            if (!isAirborne) {
                acceleration.xz += separation;
            }

            for (int obstacleIndex = 0; obstacleIndex < ObstacleCount; obstacleIndex++)
            {
                RougeObstacle obstacle = obstaclePtr[obstacleIndex];
                
                if (obstacle.Type == 1) // Circle
                {
                    float2 diff = pos.xz - obstacle.Center;
                    float distSq = math.lengthsq(diff);
                    float totalRadius = obstacle.CircleRadius + radius + obstacle.Padding;
                    
                    if (distSq < totalRadius * totalRadius && distSq > 0.0001f)
                    {
                        float dist = math.sqrt(distSq);
                        float2 normal = diff / dist;
                        float overlap = totalRadius - dist;
                        acceleration.xz += normal * (ObstacleRepulsion + overlap * 50f);
                    }
                    else if (!isAirborne)
                    {
                        float dist = math.sqrt(math.max(distSq, 0.0001f));
                        float edgeDist = dist - totalRadius;
                        if (edgeDist >= 0f && edgeDist < ObstacleLookAhead)
                        {
                            float2 normal = diff / dist;
                            float weight = 1f - math.saturate(edgeDist / math.max(ObstacleLookAhead, 0.001f));
                            acceleration.xz += normal * (ObstacleRepulsion * weight);
                            float2 tangent = new float2(-normal.y, normal.x);
                            if (math.dot(tangent, desired) < 0f) tangent = -tangent;
                            acceleration.xz += tangent * (ObstacleOrbitStrength * weight);
                        }
                    }
                }
                else // AABB (Type 0)
                {
                    float2 minPadded = obstacle.Min - new float2(radius + obstacle.Padding);
                    float2 maxPadded = obstacle.Max + new float2(radius + obstacle.Padding);
                    bool isInside = pos.x >= minPadded.x && pos.x <= maxPadded.x && pos.z >= minPadded.y && pos.z <= maxPadded.y;

                    if (isInside)
                    {
                        float dx1 = pos.x - minPadded.x, dx2 = maxPadded.x - pos.x;
                        float dy1 = pos.z - minPadded.y, dy2 = maxPadded.y - pos.z;
                        float minD = math.min(math.min(dx1, dx2), math.min(dy1, dy2));
                        
                        float2 normal = minD == dx1 ? new float2(-1, 0) : 
                                        minD == dx2 ? new float2(1, 0) : 
                                        minD == dy1 ? new float2(0, -1) : 
                                                      new float2(0, 1);
                        acceleration.xz += normal * (ObstacleRepulsion + minD * 50f);
                    }
                    else
                    {
                        float2 closest = math.clamp(pos.xz, minPadded, maxPadded);
                        float2 diff = pos.xz - closest;
                        float distSq = math.lengthsq(diff);
                        if (distSq >= ObstacleLookAhead * ObstacleLookAhead) continue;

                        if (!isAirborne) {
                            float dist = math.sqrt(math.max(distSq, 0.0001f));
                            float2 normal = diff / dist;
                            float weight = 1f - math.saturate(dist / math.max(ObstacleLookAhead, 0.001f));
                            acceleration.xz += normal * (ObstacleRepulsion * weight);
                            float2 tangent = new float2(-normal.y, normal.x);
                            if (math.dot(tangent, desired) < 0f) tangent = -tangent;
                            acceleration.xz += tangent * (ObstacleOrbitStrength * weight);
                        }
                    }
                }
            }

            for (int s = 0; s < SkillAreaCount; s++)
            {
                RougeSkillArea skill = SkillAreas[s];
                switch (skill.Type)
                {
                    case 1: ProcessTornado(ref acceleration, ref health, ref flashTimer, ref tornadoMark, pos, skill); break;
                    case 2: ProcessBomb(ref vel, ref health, ref flashTimer, pos, skill); break;
                    case 3: ProcessLaser(ref acceleration, ref health, ref flashTimer, pos, skill); break;
                    case 4: ProcessMelee(ref acceleration, ref health, ref flashTimer, pos, skill); break;
                    case 5: ProcessOrbit(ref acceleration, ref health, ref flashTimer, pos, skill); break;
                    case 6: ProcessSpike(ref vel, ref flashTimer, ref tornadoMark, pos, skill); break;
                    case 7: ProcessShockwave(ref acceleration, ref health, ref flashTimer, ref tornadoMark, pos, vel, skill); break;
                    case 8: ProcessIceZone(ref acceleration, ref health, ref flashTimer, ref vel, pos, skill); break;
                }
            }

            if (BulletCount > 0 && pos.x >= BulletMin.x && pos.x <= BulletMax.x && pos.z >= BulletMin.y && pos.z <= BulletMax.y)
            {
                if (!isAirborne) {
                    for (int bulletIndex = 0; bulletIndex < BulletCount; bulletIndex++)
                    {
                        RougeBullet bullet = bulletPtr[bulletIndex];
                        if (bullet.Life <= 0f) continue;
                        float r = radius + bullet.Radius;

                        float minX = math.min(bullet.Previous.x, bullet.Current.x) - r;
                        float maxX = math.max(bullet.Previous.x, bullet.Current.x) + r;
                        if (pos.x < minX || pos.x > maxX) continue;

                        float minY = math.min(bullet.Previous.y, bullet.Current.y) - r;
                        float maxY = math.max(bullet.Previous.y, bullet.Current.y) + r;
                        if (pos.z < minY || pos.z > maxY) continue;

                        float distSq = DistanceSqPointSegment(pos.xz, bullet.Previous, bullet.Current);
                        if (distSq <= r * r)
                        {
                            float prevH = health;
                            health -= bullet.Damage * BulletDmgMult;
                            flashTimer = 1f;
                            if (prevH > 0f && health <= 0f)
                                System.Threading.Interlocked.Increment(ref ((int*)SkillKillCounts.GetUnsafePtr())[5]);
                        }
                    }
                }
            }

            bool hitPlayer = false;
            float distToPlayerSq = math.lengthsq(pos.xz - PlayerPos);
            // 被龙卷风标记的敌�?tornadoMark=1)落地前不伤玩家，只能用爆炸伤其他敌人
            if (!isAirborne && tornadoMark < 0.5f && distToPlayerSq < (radius + 0.5f) * (radius + 0.5f))
            {
                System.Threading.Interlocked.Increment(ref ((int*)PlayerDamageCount.GetUnsafePtr())[0]);
                health = -1f;
                hitPlayer = true;
            }

            vel += acceleration * DeltaTime;
            
            if (!isAirborne) {
                float speedSq = math.lengthsq(vel.xz);
                if (speedSq > maxSpeed * maxSpeed) vel.xz *= maxSpeed * math.rsqrt(speedSq);
                vel.xz *= VelocityDamping;
            } else {
                vel.xz *= 0.99f;
            }
            
            pos += vel * DeltaTime;

            if (pos.y <= RenderHeight) 
            { 
                // tornadoMark 1 = 龙卷�?冲击�?落地爆炸)；tornadoMark 2 = 地刺(落地死亡但不爆炸)
                if (vel.y < -3.5f || tornadoMark > 0.5f)
                {
                    bool isSkillKill = tornadoMark > 0.5f;
                    bool isSpikeKill = tornadoMark > 1.5f;
                    health = 0f;
                    tornadoMark = 0f;
                    if (isSkillKill) {
                        ExplosionQueue.Enqueue(pos.xz);
                        if (isSpikeKill) {
                            System.Threading.Interlocked.Increment(ref ((int*)SkillKillCounts.GetUnsafePtr())[3]); // melee/spike
                        } else {
                            System.Threading.Interlocked.Increment(ref ((int*)SkillKillCounts.GetUnsafePtr())[0]); // tornado
                        }
                    } else {
                        // Fixed full-screen AOE bug: do not enqueue an explosion for every falling enemy!
                    }
                }
                else if (vel.y < -1f)
                {
                    health -= math.abs(vel.y) * 15f; // Fall damage
                    flashTimer = 1f;
                }
                pos.y = RenderHeight; 
                vel.y = 0f; 
            }

            if (health <= 0f && !hitPlayer)
            {
                System.Threading.Interlocked.Increment(ref ((int*)EnemyKillCount.GetUnsafePtr())[0]);
            }

                pos.x = math.clamp(pos.x, -ArenaHalfExtent, ArenaHalfExtent);
                pos.z = math.clamp(pos.z, -ArenaHalfExtent, ArenaHalfExtent);

                flashTimer = math.max(0f, flashTimer - DeltaTime * 5f);
                posOutPtr[sourceIndex] = new float4(pos, radius);
                velOutPtr[sourceIndex] = new float4(vel, tornadoMark); // w保存tornado标记
                stateOutPtr[sourceIndex] = new float4(health, radius, maxSpeed, flashTimer);
            }
        }

        private void ProcessTornado(ref float3 acceleration, ref float health, ref float flashTimer, ref float tornadoMark, float3 pos, RougeSkillArea skill)
        {
            float2 diff = pos.xz - skill.Position;
            float distSq = math.lengthsq(diff);
            float outerR = skill.Radius;
            float innerR = math.max(0f, outerR - 6f);
            
            if (distSq < outerR * outerR && distSq > innerR * innerR && distSq > 0.0001f)
            {
                float dist = math.sqrt(distSq);
                float2 dir = diff / dist;
                float weight = 1f - math.saturate((dist - innerR) / 6f);
                
                acceleration.xz += dir * (skill.PullForce * weight * KnockbackResist);
                acceleration.y += skill.VerticalForce * weight * KnockbackResist;
                tornadoMark = 1f;

                health -= skill.Damage * 0.05f * DeltaTime;
                flashTimer = 1f;
            }
        }

        private void ProcessBomb(ref float3 vel, ref float health, ref float flashTimer, float3 pos, RougeSkillArea skill)
        {
            if (math.abs(pos.y - RenderHeight) > 5f) return;
            float2 diff = skill.Position - pos.xz;
            float distSq = math.lengthsq(diff);
            if (distSq < skill.Radius * skill.Radius && distSq > 0.0001f)
            {
                float dist = math.sqrt(distSq);
                float2 dir = -(diff / dist);
                float weight = 1f - math.saturate(dist / skill.Radius);
                vel.xz += dir * (skill.PullForce * weight * KnockbackResist * 0.1f);
                vel.y += skill.VerticalForce * weight * KnockbackResist * 0.5f;
                float prevHB = health;
                health -= skill.Damage * BombDmgMult;
                flashTimer = 1f;
                if (prevHB > 0f && health <= 0f)
                    System.Threading.Interlocked.Increment(ref ((int*)SkillKillCounts.GetUnsafePtr())[1]);
            }
        }

        private void ProcessLaser(ref float3 acceleration, ref float health, ref float flashTimer, float3 pos, RougeSkillArea skill)
        {
            if (math.abs(pos.y - RenderHeight) > 6f) return;
            float2 pToS = pos.xz - skill.Position;
            float dot = math.dot(pToS, skill.Direction);
            if (dot > 0f && dot < skill.Length)
            {
                float2 proj = skill.Position + skill.Direction * dot;
                float distSq = math.lengthsq(pos.xz - proj);
                if (distSq < skill.Radius * skill.Radius)
                {
                    float weight = 1f - math.saturate(math.sqrt(distSq) / skill.Radius);
                    float prevHL = health;
                    health -= skill.Damage * DeltaTime * LaserDmgMult;
                    flashTimer = 1f;
                    if (prevHL > 0f && health <= 0f)
                        System.Threading.Interlocked.Increment(ref ((int*)SkillKillCounts.GetUnsafePtr())[2]);
                    acceleration.xz += skill.Direction * (skill.PullForce * weight * KnockbackResist);
                    acceleration.y += skill.VerticalForce * weight * KnockbackResist;
                }
            }
        }

        private void ProcessMelee(ref float3 acceleration, ref float health, ref float flashTimer, float3 pos, RougeSkillArea skill)
        {
            if (math.abs(pos.y - RenderHeight) > 6f) return;
            float2 pToS = pos.xz - skill.Position;
            float distSq = math.lengthsq(pToS);
            if (distSq < skill.Radius * skill.Radius)
            {
                float2 dir = math.normalizesafe(pToS, new float2(0f, 1f));
                float dot = math.dot(dir, skill.Direction);
                if (dot > 0.3f)  // ~72°正面扇区，确保击退生效
                {
                    float prevHM = health;
                    health -= skill.Damage * DeltaTime * 200f * MeleeDmgMult;
                    flashTimer = 1f;
                    if (prevHM > 0f && health <= 0f)
                        System.Threading.Interlocked.Increment(ref ((int*)SkillKillCounts.GetUnsafePtr())[3]);
                    acceleration.xz += dir * skill.PullForce * KnockbackResist;
                    acceleration.y += skill.VerticalForce * KnockbackResist;
                }
            }
        }

        private void ProcessOrbit(ref float3 acceleration, ref float health, ref float flashTimer, float3 pos, RougeSkillArea skill)
        {
            if (math.abs(pos.y - RenderHeight) > 6f) return;
            float2 diff = skill.Position - pos.xz;
            float distSq = math.lengthsq(diff);
            if (distSq < skill.Radius * skill.Radius && distSq > 0.0001f)
            {
                float dist = math.sqrt(distSq);
                float2 dir = -(diff / dist); 
                float weight = 1f - math.saturate(dist / skill.Radius);
                acceleration.xz += dir * (skill.PullForce * weight * KnockbackResist);
                acceleration.y += skill.VerticalForce * weight * KnockbackResist;
                float prevHO = health;
                health -= skill.Damage * DeltaTime * OrbitDmgMult;
                flashTimer = 1f;
                if (prevHO > 0f && health <= 0f)
                    System.Threading.Interlocked.Increment(ref ((int*)SkillKillCounts.GetUnsafePtr())[4]);
            }
        }

        private void ProcessSpike(ref float3 acceleration, ref float flashTimer, ref float tornadoMark, float3 pos, RougeSkillArea skill)
        {
            // 只对地面附近的敌人生效（已在空中的不重复标记�?
            if (pos.y > RenderHeight + 3f) return;
            float2 diff = pos.xz - skill.Position;
            float distSq = math.lengthsq(diff);
            if (distSq < skill.Radius * skill.Radius)
            {
                float2 dir = math.normalizesafe(diff, new float2(0f, 1f));
                float weight = 1f - math.saturate(math.sqrt(distSq) / skill.Radius);
                // 强力上抛 + 向外散射冲击�?
                acceleration.y    += skill.VerticalForce * (1f + weight * 3f);
                acceleration.xz   += dir * (skill.PullForce + 25f);  // 向外散射
                // 标记为被地刺命中（落地时死亡，但不爆炸）
                tornadoMark = 2f;
                flashTimer = 1f;
            }
        }

        private void ProcessShockwave(ref float3 acceleration, ref float health, ref float flashTimer, ref float tornadoMark, float3 pos, float3 vel, RougeSkillArea skill)
        {
            if (pos.y > RenderHeight + 3f) return; // only affect grounded enemies
            float2 diff = pos.xz - skill.Position;
            float distSq = math.lengthsq(diff);
            float outerR = skill.Radius;
            float innerR = math.max(0f, outerR - skill.Length);
            if (distSq < outerR * outerR && distSq > innerR * innerR && distSq > 0.0001f)
            {
                float dist = math.sqrt(distSq);
                float2 dir = diff / dist;
                float weight = 1f - math.saturate((dist - innerR) / math.max(skill.Length, 0.001f));
                // Strong upward lift — enemies launched high into the air by ring
                acceleration.xz += dir * (-skill.PullForce * weight * KnockbackResist);
                acceleration.y += skill.VerticalForce * weight * KnockbackResist * 1.5f; // extra lift
                // Mark as spike kill: fall = instant death, no explosion chain
                tornadoMark = 2f;
                flashTimer = 1f;
            }
        }

        private void ProcessIceZone(ref float3 acceleration, ref float health, ref float flashTimer, ref float3 vel, float3 pos, RougeSkillArea skill)
        {
            if (math.abs(pos.y - RenderHeight) > 4f) return;
            float2 diff = pos.xz - skill.Position;
            float distSq = math.lengthsq(diff);
            if (distSq < skill.Radius * skill.Radius)
            {
                float weight = 1f - math.saturate(math.sqrt(distSq) / skill.Radius);
                // Slow enemies: dampen velocity heavily
                vel.xz *= math.lerp(1f, 0.1f, weight);
                // Pull toward center
                float2 pullDir = -math.normalizesafe(diff, float2.zero);
                acceleration.xz += pullDir * (skill.PullForce * weight);
                // Damage over time
                float prevH = health;
                health -= skill.Damage * DeltaTime * LaserDmgMult;
                flashTimer = math.max(flashTimer, 0.3f);
                if (prevH > 0f && health <= 0f)
                    System.Threading.Interlocked.Increment(ref ((int*)SkillKillCounts.GetUnsafePtr())[2]);
            }
        }

    private void Respawn(int index, ref float4 pos4, ref float4 vel4, ref float4 state4)
    {
        uint hash = math.hash(new uint2((uint)index + FrameSeed, FrameSeed ^ 0xA511E9B3u));
        float angle = ((hash & 0xFFFFu) / 65535f) * math.PI * 2f;
        float distance = math.lerp(SpawnRadiusMin, SpawnRadiusMax, ((hash >> 16) & 0xFFFFu) / 65535f);
        float speedScale = math.lerp(0.9f, 1.15f, ((hash >> 8) & 0xFFu) / 255f);
        float2 spawn = PlayerPos + new float2(math.cos(angle), math.sin(angle)) * distance;
        spawn.x = math.clamp(spawn.x, -ArenaHalfExtent + 2f, ArenaHalfExtent - 2f);
        spawn.y = math.clamp(spawn.y, -ArenaHalfExtent + 2f, ArenaHalfExtent - 2f);
        pos4 = new float4(spawn.x, RenderHeight, spawn.y, EnemyRadius);
        vel4 = float4.zero;
        state4 = new float4(EnemyMaxHealth, EnemyRadius, EnemyMaxSpeed * speedScale, 0f);
    }

    private static float DistanceSqPointSegment(float2 point, float2 a, float2 b)
    {
        float2 ab = b - a;
        float abLenSq = math.lengthsq(ab);
        if (abLenSq <= 0.0001f) return math.lengthsq(point - a);
        float t = math.saturate(math.dot(point - a, ab) / abLenSq);
        float2 closest = a + ab * t;
        return math.lengthsq(point - closest);
    }
}


