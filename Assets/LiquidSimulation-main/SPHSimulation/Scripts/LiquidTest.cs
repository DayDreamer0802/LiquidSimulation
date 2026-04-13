using System;
using System.Runtime.CompilerServices;
using HighPerform.SPHSimulation.Scripts;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Serialization;

namespace HighPerform.test.Scripts
{
    public class LiquidTest : MonoBehaviour
    {
        private static readonly int SpinForce = Shader.PropertyToID("_SpinForce");
        private static readonly int LiftForce = Shader.PropertyToID("_LiftForce");
        private static readonly int BottomRadius = Shader.PropertyToID("_BottomRadius");
        private static readonly int TopRadius = Shader.PropertyToID("_TopRadius");
        private static readonly int PosRadBuffer = Shader.PropertyToID("_PosRadBuffer");
        private static readonly int FluidDepthTex = Shader.PropertyToID("_FluidDepthTex");

        private float _invCellSize;
        private float _maxSizeSq;
        private int _hashSize;
        private int _hashMask;
        private bool _colorDirty = true;


        private NativeArray<float4> _posRadA;
        private NativeArray<float4> _posRadB;
        private NativeArray<float4> _velMassA;
        private NativeArray<float4> _velMassB;

     
        private NativeArray<float4> _fluidPropsB;

        private NativeArray<float4> _colorA;

        private NativeArray<ulong> _particleKeys;
        private NativeArray<ulong> _tempParticleKeys;

        private NativeArray<int> _cellCounts;
        private NativeArray<int> _cellOffsets;
        private NativeArray<int3> _neighborOffsets;


        private GraphicsBuffer[] _graphicsBuffers = new GraphicsBuffer[2];
        private GraphicsBuffer _colorBuffer, _argsBuffer;
        private uint[] _args = new uint[5] { 0, 0, 0, 0, 0 };


        public float playerRadius = 0.5f;
        public float playerMoveSpeed = 25f;

        [Header("ReSetRef")] public Mesh quadMesh;
        public Mesh sphereMesh;
        [FormerlySerializedAs("tonadoMesh")] public Mesh tornadoMesh;
        public Material quadMat;
        public Transform plane;
        public Material blackHoleMat;
        public Material tornadoMat;
        public Material waterMat;


        [Range(1, 10000000)] public int gridCount = 1000000;
        private int _currentParticleCount;

        public int maxSize = 50;
        private int _maxSize;

        [Header("Physics & General")] [Range(-50, 50)]
        public float gravity = 19.6f;

        [Range(-2000, 2000)] public float playerGravity = 200f;
        [Range(-1000, 1000)] public float playerJumpForce = 80f;

        public bool reservePlayerGravity;
        private float _verticalVelocity;

        [Header("Player Interaction")] public Transform playerTransform;
        public PlayerController player;

        [Header("SPH Fluid Dynamics")] public float particleMass = 4.0f; // 粒子质量
        public float smoothingRadius = 0.6f; // 平滑核半径
        public float restDensity = 16f; // 静态密度
        public float gasConstant = 55f; // 压力刚度系数
        public float viscosity = 75f; // 粘滞阻力系数
        [Range(0.2f, 1.0f)] public float collisionScale = 0.6f;

        [Header("Tornado Skill (Space)")] public float tornadoRadius = 25f;
        public float tornadoPullForce = 500f;
        public float tornadoSpinForce = 1500f;
        public float tornadoLiftForce = 1500f;
        public float tornadoMoveSpeed = 5f;
        public float tornadoDuration = 5f;
        private bool _isTornadoActive;
        private float _tornadoTimer;
        private float3 _currentTornadoPos;
        private float3 _currentTornadoDir;
        private float _runTimeTornadoRadius;
        private float _runTimeTornadoPull;
        private float _runTimeTornadoSpin;
        private float _runTimeTornadoLift;

        [Header("Black Hole Skill (Key: B)")] public float blackHoleRadius = 30f;
        public float blackHolePullForce = 2000f;
        public float blackHoleDuration = 8f;
        private bool _isBlackHoleActive;
        private float _blackHoleTimer;
        private float3 _currentBlackHolePos;
        private float _blackHoleStartY;
        private float _runTimeBlackHoleRadius;
        private float _runTimeBlackHolePull;

        private int _currentGraphicIndex;

        float3 _currentPlrPos;
        float3 _currentPlrVel;

        JobHandle _physic;

        void Start()
        {
            Create();
        }

        private void Create()
        {
            Dispose();

            if (playerTransform) ClampPlayerPos();

            _verticalVelocity = 0;
            _isBlackHoleActive = false;
            _blackHoleTimer = 0;
            _isTornadoActive = false;
            _tornadoTimer = 0;

            _currentParticleCount = gridCount;
            _maxSize = maxSize;
            plane.transform.position = new Vector3(0, -_maxSize - 0.1f, 0);


            _invCellSize = 1f / smoothingRadius;
            _maxSizeSq = smoothingRadius * smoothingRadius;
            _hashSize = Mathf.NextPowerOfTwo(Mathf.Max(_currentParticleCount * 4, 65536));
            _hashMask = _hashSize - 1;
            _neighborOffsets = new NativeArray<int3>(27, Allocator.Persistent);
            int idx = 0;
            for (int z = -1; z <= 1; z++)
            {
                for (int y = -1; y <= 1; y++)
                {
                    for (int x = -1; x <= 1; x++)
                    {
                        _neighborOffsets[idx++] = new int3(x * 73856093, y * 19349663, z * 83492791);
                    }
                }
            }

            _posRadA = new NativeArray<float4>(_currentParticleCount, Allocator.Persistent);
            _posRadB = new NativeArray<float4>(_currentParticleCount, Allocator.Persistent);
            _velMassA = new NativeArray<float4>(_currentParticleCount, Allocator.Persistent);
            _velMassB = new NativeArray<float4>(_currentParticleCount, Allocator.Persistent);
            _fluidPropsB = new NativeArray<float4>(_currentParticleCount, Allocator.Persistent); 

            _colorA = new NativeArray<float4>(_currentParticleCount, Allocator.Persistent);
            _particleKeys = new NativeArray<ulong>(_currentParticleCount, Allocator.Persistent);
            _tempParticleKeys = new NativeArray<ulong>(_currentParticleCount, Allocator.Persistent);

            _cellCounts = new NativeArray<int>(_hashSize, Allocator.Persistent);
            _cellOffsets = new NativeArray<int>(_hashSize, Allocator.Persistent);

            var rng = new Unity.Mathematics.Random((uint)(DateTime.Now.Ticks % uint.MaxValue));
            for (int i = 0; i < _currentParticleCount; i++)
            {
                float startSpeed = rng.NextFloat(5f, 15f);
                float3 startDir = math.normalize(new float3(rng.NextFloat(-1f, 1f), rng.NextFloat(-1f, 1f),
                    rng.NextFloat(-1f, 1f)));
                float3 finalSpeed = startDir * startSpeed;

                _colorA[i] = new float4(0.5f, 0.5f, 0.5f, 1f);

                _posRadA[i] = new float4(
                    rng.NextFloat(-_maxSize * 0.8f, _maxSize * 0.8f),
                    rng.NextFloat(_maxSize * 0.15f, _maxSize * 0.2f),
                    rng.NextFloat(-_maxSize * 0.8f, _maxSize * 0.8f),
                    smoothingRadius
                );

                // w 存储粒子质量 (SPH需要用到)
                _velMassA[i] = new float4(finalSpeed.x, finalSpeed.y, finalSpeed.z,
                    rng.NextFloat(particleMass * 0.8f, particleMass * 1.2f));

                _fluidPropsB[i] = float4.zero;
            }

            _colorBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, _currentParticleCount, 16);
            _argsBuffer = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments, 1, 20);
            _graphicsBuffers[0] = new GraphicsBuffer(GraphicsBuffer.Target.Structured, _currentParticleCount, 16);
            _graphicsBuffers[1] = new GraphicsBuffer(GraphicsBuffer.Target.Structured, _currentParticleCount, 16);
            _colorDirty = true;
        }

        void ClampPlayerPos()
        {
            player.transform.position = _currentPlrPos = ClampToBox(_currentPlrPos);
        }

        float3 ClampToBox(float3 clampPos)
        {
            float limitXZ = _maxSize - playerRadius;
            var limitY = _maxSize - playerRadius * 2f;
            clampPos.x = math.clamp(clampPos.x, -limitXZ, limitXZ);
            clampPos.z = math.clamp(clampPos.z, -limitXZ, limitXZ);
            clampPos.y = math.clamp(clampPos.y, -limitY, limitY);
            return clampPos;
        }

        float3 GetSkillDir()
        {
            var dir = new float3();
            if (Camera.main)
            {
                Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
                if (Physics.Raycast(ray, out RaycastHit hit))
                {
                    float3 mouseWorldPos = hit.point;
                    dir = mouseWorldPos - _currentPlrPos;
                    dir.y = 0;
                }
            }

            if (math.lengthsq(dir) > 0.01f)
                dir = _currentTornadoDir = math.normalizesafe(dir);
            else if (math.lengthsq(dir) > 0.01f)
                dir = _currentPlrVel;
            else
                dir = new float3(0, 0, -1);
            return dir;
        }

        void Tornado(float dt)
        {
            if (Input.GetKeyDown(KeyCode.Q) && !_isTornadoActive)
            {
                _isTornadoActive = true;
                _tornadoTimer = tornadoDuration;
                _currentTornadoDir = GetSkillDir();
                _currentTornadoPos = _currentPlrPos + _currentTornadoDir;
                _currentTornadoPos.y = -maxSize;
                player.playerForce += -_currentTornadoDir * 100f;
            }

            if (_isTornadoActive)
            {
                _tornadoTimer -= dt;
                if (_tornadoTimer <= 0) _isTornadoActive = false;
                else _currentTornadoPos += _currentTornadoDir * tornadoMoveSpeed * dt;

                float timePercent = 1f - (_tornadoTimer / tornadoDuration);
                float tornadoScale;
                float currentTornadoHeight;

                if (timePercent <= 0.15f)
                {
                    float t = timePercent / 0.15f;
                    tornadoScale = math.lerp(0.2f, 1f, t);
                    currentTornadoHeight = math.lerp(10f, 50f, t);
                }
                else if (timePercent <= 0.85f)
                {
                    tornadoScale = 1f;
                    currentTornadoHeight = 50f;
                }
                else
                {
                    float t = (timePercent - 0.85f) / 0.15f;
                    tornadoScale = math.lerp(1f, 0.2f, t);
                    currentTornadoHeight = math.lerp(50f, 10f, t);
                }

                _runTimeTornadoRadius = tornadoRadius * tornadoScale;
                _runTimeTornadoPull = tornadoPullForce * tornadoScale;
                _runTimeTornadoSpin = tornadoSpinForce * tornadoScale;
                _runTimeTornadoLift = tornadoLiftForce * tornadoScale;

                Vector3 renderPos = _currentTornadoPos;
                renderPos.y += currentTornadoHeight * 0.5f;
                Matrix4x4 tornadoMatrix = Matrix4x4.TRS(
                    renderPos,
                    Quaternion.identity,
                    new Vector3(1f, currentTornadoHeight, 1f)
                );

                tornadoMat.SetFloat(SpinForce, _runTimeTornadoSpin / 50f);
                tornadoMat.SetFloat(LiftForce, _runTimeTornadoLift / 50f);
                tornadoMat.SetFloat(BottomRadius, _runTimeTornadoRadius * 0.1f);
                tornadoMat.SetFloat(TopRadius, _runTimeTornadoRadius * 2.5f);

                Graphics.DrawMesh(tornadoMesh, tornadoMatrix, tornadoMat, 0);
                float3 diff = _currentTornadoPos - (float3)playerTransform.position;
                float distSqXZ = diff.x * diff.x + diff.z * diff.z;

                var physicPercent = math.clamp((_currentPlrPos.y - _currentTornadoPos.y) / 50f, 0, 1f);
                float physicalRadius = _runTimeTornadoRadius * (0.3f + 0.6f * physicPercent);
                float rangeSq = physicalRadius * physicalRadius;

                if (distSqXZ < rangeSq && _currentPlrPos.y < -maxSize + 55f && Mathf.Approximately(tornadoScale, 1f))
                {
                    float distXZ = math.sqrt(distSqXZ);
                    float3 dirToCenter = math.normalizesafe(new float3(diff.x, 0, diff.z));
                    float t = math.saturate(distXZ / physicalRadius);

                    float spinWeight, pullWeight, liftEffect;
                    float innerProgress = 1f - t;
                    pullWeight = math.lerp(0.15f, 1.0f, innerProgress);
                    spinWeight = math.lerp(0.20f, 1.0f, math.pow(innerProgress, 1.5f));
                    liftEffect = math.pow(innerProgress, 1.5f);

                    float3 tangentDir = new float3(-dirToCenter.z, 0, dirToCenter.x);
                    float3 spinForce = tangentDir * _runTimeTornadoSpin * 0.2f * spinWeight;
                    float3 pullForce = dirToCenter * (_runTimeTornadoPull * 0.25f * pullWeight);

                    float liftDir = reservePlayerGravity ? -1 : 1;
                    float3 horizontalMove = pullForce + spinForce;

                    _verticalVelocity += (_runTimeTornadoLift * 0.5f * liftDir) * liftEffect * Time.deltaTime;
                    float maxLift = _runTimeTornadoLift * 0.5f;
                    _verticalVelocity = math.clamp(_verticalVelocity, -maxLift, maxLift);

                    player.playerForceOnce += horizontalMove;
                }
            }
        }

        void BlackHole(float dt)
        {
            if (Input.GetKeyDown(KeyCode.E) && !_isBlackHoleActive)
            {
                _isBlackHoleActive = true;
                _blackHoleTimer = blackHoleDuration;
                _currentBlackHolePos = _currentPlrPos + new float3(0, blackHoleRadius / 4f, 0);
                _blackHoleStartY = _currentPlrPos.y;
            }

            if (_isBlackHoleActive)
            {
                _blackHoleTimer -= dt;
                if (_blackHoleTimer <= 0)
                {
                    float3 diff = _currentBlackHolePos - _currentPlrPos;
                    float distSq = diff.x * diff.x + diff.z * diff.z + diff.y * diff.y;
                    float rangeSq = _runTimeBlackHoleRadius * _runTimeBlackHoleRadius;

                    if (distSq <= rangeSq)
                    {
                        float pullpercent = (1f - distSq / rangeSq);
                        var dir = _currentPlrPos - _currentBlackHolePos;
                        dir.y = 0;
                        dir = math.normalizesafe(dir);
                        player.playerForce += dir * _runTimeBlackHolePull / 20f * (pullpercent + 0.25f);
                    }

                    _isBlackHoleActive = false;
                }

                float timePercent = 1f - (_blackHoleTimer / blackHoleDuration);
                float bhVisualScale;
                float bhEffectScale;
                bool playerSuction = false;

                if (timePercent <= 0.2f)
                {
                    float t = timePercent / 0.2f;
                    bhVisualScale = math.lerp(0.1f, 1f, t);
                    bhEffectScale = math.lerp(0.1f, 1f, t);
                    _currentBlackHolePos.y =
                        _blackHoleStartY + math.lerp(blackHoleRadius / 4f, blackHoleRadius / 2.5f, t);
                }
                else if (timePercent <= 0.93f)
                {
                    bhVisualScale = 1f;
                    bhEffectScale = 1f;
                    playerSuction = true;
                }
                else if (timePercent <= 0.97f)
                {
                    float t = (timePercent - 0.93f) / 0.04f;
                    bhVisualScale = math.lerp(1f, 1.25f, t);
                    bhEffectScale = math.lerp(1f, 1.25f, t);
                    playerSuction = true;
                }
                else
                {
                    float t = (timePercent - 0.97f) / 0.03f;
                    bhVisualScale = math.lerp(1f, 0f, t);
                    bhEffectScale = 1f;
                    playerSuction = true;
                }

                _runTimeBlackHoleRadius = blackHoleRadius * bhEffectScale;
                _runTimeBlackHolePull = blackHolePullForce * bhEffectScale;

                Matrix4x4 bhMatrix = Matrix4x4.TRS(_currentBlackHolePos, Quaternion.identity,
                    Vector3.one * (blackHoleRadius * bhVisualScale));
                Graphics.DrawMesh(sphereMesh, bhMatrix, blackHoleMat, 0);

                if (playerSuction)
                {
                    float3 diff = _currentBlackHolePos - (float3)_currentPlrPos;
                    float distSq = diff.x * diff.x + diff.z * diff.z + diff.y * diff.y;
                    float rangeSq = _runTimeBlackHoleRadius * _runTimeBlackHoleRadius;

                    if (distSq <= rangeSq && distSq >= 1.0f)
                    {
                        float pullpercent = distSq / rangeSq;
                        if (pullpercent >= 0.025f)
                        {
                            float3 dirToCenter = math.normalizesafe(diff);
                            float3 pullForce = dirToCenter * (_runTimeBlackHolePull * pullpercent / 50f);
                            float3 tangentDir = new float3(-dirToCenter.z, 0, dirToCenter.x);
                            float3 spinForce = tangentDir * (_runTimeBlackHolePull * pullpercent / 100f);
                            player.playerForceOnce += (pullForce + spinForce);
                        }
                    }
                }
            }
        }

        void Jump(float dt)
        {
            var limitY = _maxSize - playerRadius * 2f;
            bool isTouchingFloor = (!reservePlayerGravity && _currentPlrPos.y <= -limitY + 0.01f);
            bool isTouchingCeiling = (reservePlayerGravity && _currentPlrPos.y >= limitY - 0.01f);
            if (isTouchingFloor || isTouchingCeiling)
            {
                _verticalVelocity = 0;
            }

            if (Input.GetKeyDown(KeyCode.Space) && (isTouchingFloor || isTouchingCeiling))
            {
                _verticalVelocity = playerJumpForce * (reservePlayerGravity ? -1 : 1);
            }
        }

        void Move(float dt)
        {
            player.moveSpeed = playerMoveSpeed;
            player.transform.localScale = playerRadius * 2f * Vector3.one;
            _currentPlrPos = playerTransform.position;
            _currentPlrVel = player.GetPlayerMoveVelocity();
        }

        void ApplyForce(float dt)
        {
            _verticalVelocity -= playerGravity * (reservePlayerGravity ? -1 : 1) * dt;
            float terminalVel = playerGravity * 2f;
            _verticalVelocity = math.clamp(_verticalVelocity, -terminalVel, terminalVel);
            _currentPlrPos += (new float3(0, 1, 0) * _verticalVelocity + player.playerForce + player.playerForceOnce +
                               _currentPlrVel * player.moveSpeed) * dt;
            player.transform.position = _currentPlrPos;
        }

        void SheduleSortJob(ref JobHandle handle)
        {
            int chunkSize = 16384;
            int chunkCount = (int)math.ceil((float)_currentParticleCount / chunkSize);

            NativeArray<int> histograms = new NativeArray<int>(256 * chunkCount, Allocator.TempJob);
            NativeArray<int> binTotals = new NativeArray<int>(256, Allocator.TempJob);

            int[] shifts = { 32, 40, 48 };
            NativeArray<ulong> currentSrc = _particleKeys;
            NativeArray<ulong> currentDst = _tempParticleKeys;

            for (int i = 0; i < shifts.Length; i++)
            {
                int shift = shifts[i];

                handle = new LocalHistogramJob
                    {
                        Keys = currentSrc, Histograms = histograms, BatchSize = chunkSize, Shift = shift,
                        ChunkCount = chunkCount
                    }
                    .ScheduleBatch(_currentParticleCount, chunkSize, handle);

                handle = new BinLocalPrefixSumBatchJob
                        { Histograms = histograms, BinTotals = binTotals, ChunkCount = chunkCount }
                    .ScheduleBatch(256, 16, handle);

                handle = new GlobalBinSumJob { BinTotals = binTotals }.Schedule(handle);

                handle = new ApplyGlobalOffsetBatchJob
                        { Histograms = histograms, BinTotals = binTotals, ChunkCount = chunkCount }
                    .ScheduleBatch(256, 16, handle);

                handle = new ScatterJob
                    {
                        SrcKeys = currentSrc, DstKeys = currentDst, Histograms = histograms, BatchSize = chunkSize,
                        Shift = shift, ChunkCount = chunkCount
                    }
                    .ScheduleBatch(_currentParticleCount, chunkSize, handle);

                (currentSrc, currentDst) = (currentDst, currentSrc);
            }

            if (currentSrc == _tempParticleKeys)
            {
                handle = new CopyArrayJob { Src = _tempParticleKeys, Dst = _particleKeys }.ScheduleBatch(
                    _currentParticleCount, 4096, handle);
            }

            handle = histograms.Dispose(handle);
            handle = binTotals.Dispose(handle);
        }

        void PhysicsJob(float dt)
        {
            JobHandle handle = default;


            handle = new BuildKeysJob
                {
                    PosRadIn = _posRadA, ParticleKeys = _particleKeys, InvCellSize = _invCellSize, HashMask = _hashMask
                }
                .ScheduleBatch(_currentParticleCount, 4096, handle);

            // ClearGrid can run in parallel with Sort (no data dependency)
            var clearHandle = new ClearGridJob { CellCounts = _cellCounts }.ScheduleBatch(_hashSize, 16384, handle);

            SheduleSortJob(ref handle);

            handle = JobHandle.CombineDependencies(handle, clearHandle);
            handle = new ParallelBuildOffsetsJob
                    { Keys = _particleKeys, CellOffsets = _cellOffsets, CellCounts = _cellCounts }
                .ScheduleBatch(_currentParticleCount, 8192, handle);

            // 2. 数据重排发散 (读 A，写 B)
            handle = new ReorderDataJob
            {
                SortedKeys = _particleKeys,
                PosRadIn = _posRadA,
                VelIn = _velMassA,
                PosRadOut = _posRadB,
                VelOut = _velMassB
            }.ScheduleBatch(_currentParticleCount, 4096, handle);

            // 3. --- SPH 第一阶段：计算密度和压力 --- 
            handle = new CalculateDensityPressureJob
            {
                PosRadB = _posRadB,
                VelMassB = _velMassB,
                FluidPropsB = _fluidPropsB,
                CellOffsets = _cellOffsets,
                CellCounts = _cellCounts,
                SmoothingRadius = smoothingRadius,
                RestDensity = restDensity,
                GasConstant = gasConstant,
                NeighborOffsets = _neighborOffsets,
                InvCellSize = _invCellSize,
                HashMask = _hashMask
            }.ScheduleBatch(_currentParticleCount, 2048, handle);

            handle = new UltimatePhysicsJob
            {
                PlayerPos = _currentPlrPos,
                PlayerVelocity = _currentPlrVel,
                PlayerDiameter = playerRadius * 2f,
                MaxRSumSq = _maxSizeSq,
                NeighborOffsets = _neighborOffsets,
                TornadoPos = _currentTornadoPos,
                TornadoRadius = _runTimeTornadoRadius,
                TornadoPullForce = _isTornadoActive ? _runTimeTornadoPull : 0f,
                TornadoSpinForce = _isTornadoActive ? _runTimeTornadoSpin : 0f,
                TornadoLiftForce = _isTornadoActive ? _runTimeTornadoLift : 0f,

                BlackHolePos = _currentBlackHolePos,
                BlackHoleRadius = _runTimeBlackHoleRadius,
                BlackHolePullForce = _isBlackHoleActive ? _runTimeBlackHolePull : 0f,

                PosRadIn = _posRadB,
                VelIn = _velMassB,
                FluidPropsIn = _fluidPropsB, 

                PosRadOut = _posRadA,
                VelOut = _velMassA,
                ColorsOut = _colorA,

                CellOffsets = _cellOffsets,
                CellCounts = _cellCounts,


                Gravity = gravity,

                MaxSize = _maxSize,
                InvCellSize = _invCellSize,
                HashMask = _hashMask,
                DeltaTime = 0.02f,

                SmoothingRadius = smoothingRadius,
                Viscosity = viscosity
            }.ScheduleBatch(_currentParticleCount, 2048, handle);

            _physic = handle;
        }

        void Render()
        {
            _currentGraphicIndex = (_currentGraphicIndex + 1) % 2;
            _graphicsBuffers[_currentGraphicIndex].SetData(_posRadA);
            if (_colorDirty)
            {
                _colorBuffer.SetData(_colorA);
                _colorDirty = false;
            }

            _args[0] = quadMesh.GetIndexCount(0);
            _args[1] = (uint)_currentParticleCount;
            _argsBuffer.SetData(_args);

            quadMat.SetBuffer(PosRadBuffer, _graphicsBuffers[_currentGraphicIndex]);
            int targetLayer = 6;

            var currentRT = (RenderTexture)waterMat.GetTexture(FluidDepthTex);
            if (currentRT)
            {
                var oldRT = RenderTexture.active;
                RenderTexture.active = currentRT;
                GL.Clear(true, true, new Color(0, 0, 0, 0));
                RenderTexture.active = oldRT;
            }

            Graphics.DrawMeshInstancedIndirect(
                quadMesh, 0, quadMat, new Bounds(Vector3.zero, Vector3.one * 1000),
                _argsBuffer, 0, null, UnityEngine.Rendering.ShadowCastingMode.On, true, targetLayer, null);
        }

        void Update()
        {
            float dt = Time.deltaTime;
            _physic.Complete();
            Render();

            if (playerTransform && player)
            {
                float3 startPosThisFrame = playerTransform.position;
                _currentPlrVel = float3.zero;
                _currentPlrPos = startPosThisFrame;
                player.playerForceOnce = float3.zero;

                if (!(player.playerForce.x == 0 && player.playerForce.y == 0 && player.playerForce.z == 0))
                    player.playerForce = math.lerp(player.playerForce, float3.zero, dt * 10f);

                Move(dt);
                Jump(dt);
                Tornado(dt);
                BlackHole(dt);
                ApplyForce(dt);
                ClampPlayerPos();

                _currentPlrVel = (_currentPlrPos - startPosThisFrame) / dt;
            }

            PhysicsJob(dt);
        }

        void Dispose()
        {
            _physic.Complete();
            _physic = default;
            if (_neighborOffsets.IsCreated) _neighborOffsets.Dispose();
            if (_posRadA.IsCreated) _posRadA.Dispose();
            if (_posRadB.IsCreated) _posRadB.Dispose();
            if (_velMassA.IsCreated) _velMassA.Dispose();
            if (_velMassB.IsCreated) _velMassB.Dispose();
            if (_fluidPropsB.IsCreated) _fluidPropsB.Dispose();

            if (_colorA.IsCreated) _colorA.Dispose();
            if (_particleKeys.IsCreated) _particleKeys.Dispose();
            if (_tempParticleKeys.IsCreated) _tempParticleKeys.Dispose();
            if (_cellCounts.IsCreated) _cellCounts.Dispose();
            if (_cellOffsets.IsCreated) _cellOffsets.Dispose();

            _graphicsBuffers[0]?.Release();
            _graphicsBuffers[1]?.Release();
            _graphicsBuffers[0] = null;
            _graphicsBuffers[1] = null;
            _colorBuffer?.Release();
            _argsBuffer?.Release();
        }

        void OnDestroy()
        {
            Dispose();
        }
    }


    [BurstCompile(FloatMode = FloatMode.Fast, FloatPrecision = FloatPrecision.Standard)]
    public unsafe struct BuildKeysJob : IJobParallelForBatch
    {
        [ReadOnly] public NativeArray<float4> PosRadIn;
        [NativeDisableParallelForRestriction] public NativeArray<ulong> ParticleKeys;
        public float InvCellSize;
        public int HashMask;

        public void Execute(int startIndex, int count)
        {
            float4* posPtr = (float4*)PosRadIn.GetUnsafeReadOnlyPtr();
            ulong* keysPtr = (ulong*)ParticleKeys.GetUnsafePtr();
            int end = startIndex + count;

            for (int i = startIndex; i < end; i++)
            {
                int3 cell = (int3)math.floor(posPtr[i].xyz * InvCellSize);
                int h = (cell.x * 73856093) ^ (cell.y * 19349663) ^ (cell.z * 83492791);
                h = h & HashMask;
                keysPtr[i] = ((ulong)(uint)h << 32) | (uint)i;
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
            int chunkIdx = startIndex / BatchSize;
            int* localHist = stackalloc int[256];
            UnsafeUtility.MemClear(localHist, 256 * sizeof(int));

            ulong* keysPtr = (ulong*)Keys.GetUnsafeReadOnlyPtr();
            int end = startIndex + count;
            int i = startIndex;

            for (; i <= end - 4; i += 4)
            {
                localHist[(int)((keysPtr[i] >> Shift) & 0xFF)]++;
                localHist[(int)((keysPtr[i + 1] >> Shift) & 0xFF)]++;
                localHist[(int)((keysPtr[i + 2] >> Shift) & 0xFF)]++;
                localHist[(int)((keysPtr[i + 3] >> Shift) & 0xFF)]++;
            }

            for (; i < end; i++) localHist[(int)((keysPtr[i] >> Shift) & 0xFF)]++;

            int* globalHistPtr = (int*)Histograms.GetUnsafePtr();
            for (int j = 0; j < 256; j++) globalHistPtr[j * ChunkCount + chunkIdx] = localHist[j];
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
                int startIdx = bin * ChunkCount;
                int sum = 0;
                for (int c = 0; c < ChunkCount; c++)
                {
                    int val = histPtr[startIdx + c];
                    histPtr[startIdx + c] = sum;
                    sum += val;
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
                int val = totals[i];
                totals[i] = sum;
                sum += val;
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
            int endBin = startIndex + count;
            for (int bin = startIndex; bin < endBin; bin++)
            {
                int globalOffset = BinTotals[bin];
                int startIdx = bin * ChunkCount;
                for (int c = 0; c < ChunkCount; c++) histPtr[startIdx + c] += globalOffset;
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
            int chunkIdx = startIndex / BatchSize;
            int* localOffsets = stackalloc int[256];
            int* histPtr = (int*)Histograms.GetUnsafeReadOnlyPtr();
            for (int i = 0; i < 256; i++) localOffsets[i] = histPtr[i * ChunkCount + chunkIdx];

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

        public void Execute(int startIndex, int count)
        {
            int* ptr = (int*)CellCounts.GetUnsafePtr();
            UnsafeUtility.MemClear(ptr + startIndex, count * sizeof(int));
        }
    }

    [BurstCompile(FloatMode = FloatMode.Fast, FloatPrecision = FloatPrecision.Standard)]
    public unsafe struct ParallelBuildOffsetsJob : IJobParallelForBatch
    {
        [ReadOnly] public NativeArray<ulong> Keys;
        [NativeDisableParallelForRestriction] public NativeArray<int> CellOffsets;
        [NativeDisableParallelForRestriction] public NativeArray<int> CellCounts;

        public void Execute(int startIndex, int count)
        {
            ulong* keysPtr = (ulong*)Keys.GetUnsafeReadOnlyPtr();
            int* offsetsPtr = (int*)CellOffsets.GetUnsafePtr();
            int* countsPtr = (int*)CellCounts.GetUnsafePtr();

            int end = startIndex + count;
            for (int i = startIndex; i < end; i++)
            {
                int h = (int)(keysPtr[i] >> 32);
                if (i == 0 || h != (int)(keysPtr[i - 1] >> 32))
                    offsetsPtr[h] = i;
                System.Threading.Interlocked.Increment(ref *(countsPtr + h));
            }
        }
    }

    [BurstCompile(FloatMode = FloatMode.Fast, FloatPrecision = FloatPrecision.Standard)]
    public unsafe struct ReorderDataJob : IJobParallelForBatch
    {
        [ReadOnly] public NativeArray<ulong> SortedKeys;
        [ReadOnly] public NativeArray<float4> PosRadIn;
        [ReadOnly] public NativeArray<float4> VelIn;

        [NativeDisableParallelForRestriction] public NativeArray<float4> PosRadOut;
        [NativeDisableParallelForRestriction] public NativeArray<float4> VelOut;

        public void Execute(int startIndex, int count)
        {
            ulong* keysPtr = (ulong*)SortedKeys.GetUnsafeReadOnlyPtr();
            float4* posInPtr = (float4*)PosRadIn.GetUnsafeReadOnlyPtr();
            float4* velInPtr = (float4*)VelIn.GetUnsafeReadOnlyPtr();
            float4* posOutPtr = (float4*)PosRadOut.GetUnsafePtr();
            float4* velOutPtr = (float4*)VelOut.GetUnsafePtr();

            int endIndex = startIndex + count;
            int i = startIndex;

            for (; i <= endIndex - 4; i += 4)
            {
                int idx0 = (int)keysPtr[i];
                int idx1 = (int)keysPtr[i + 1];
                int idx2 = (int)keysPtr[i + 2];
                int idx3 = (int)keysPtr[i + 3];

                posOutPtr[i] = posInPtr[idx0];
                velOutPtr[i] = velInPtr[idx0];

                posOutPtr[i + 1] = posInPtr[idx1];
                velOutPtr[i + 1] = velInPtr[idx1];

                posOutPtr[i + 2] = posInPtr[idx2];
                velOutPtr[i + 2] = velInPtr[idx2];

                posOutPtr[i + 3] = posInPtr[idx3];
                velOutPtr[i + 3] = velInPtr[idx3];
            }

            for (; i < endIndex; i++)
            {
                int idx = (int)keysPtr[i];
                posOutPtr[i] = posInPtr[idx];
                velOutPtr[i] = velInPtr[idx];
            }
        }
    }


    [BurstCompile(FloatMode = FloatMode.Fast, FloatPrecision = FloatPrecision.Standard)]
    public unsafe struct CalculateDensityPressureJob : IJobParallelForBatch
    {
        [ReadOnly] public NativeArray<float4> PosRadB;
        [ReadOnly] public NativeArray<float4> VelMassB;
        [ReadOnly] public NativeArray<int> CellOffsets;
        [ReadOnly] public NativeArray<int> CellCounts;
        [ReadOnly] public NativeArray<int3> NeighborOffsets;
        [NativeDisableParallelForRestriction] public NativeArray<float4> FluidPropsB;

        public float SmoothingRadius;
        public float RestDensity;
        public float GasConstant;
        public float InvCellSize;
        public int HashMask;

        public void Execute(int startIndex, int count)
        {
            float4* posPtr = (float4*)PosRadB.GetUnsafeReadOnlyPtr();
            float4* velPtr = (float4*)VelMassB.GetUnsafeReadOnlyPtr();
            int* offsetsPtr = (int*)CellOffsets.GetUnsafeReadOnlyPtr();
            int* countsPtr = (int*)CellCounts.GetUnsafeReadOnlyPtr();
            float4* propsPtr = (float4*)FluidPropsB.GetUnsafePtr();
            int3* offsetPtr = (int3*)NeighborOffsets.GetUnsafeReadOnlyPtr();

            float h = SmoothingRadius;
            float h2 = h * h;
            float h9 = h2 * h2 * h2 * h2 * h;
            float poly6Const = 315.0f / (64.0f * math.PI * h9);

            int endIndex = startIndex + count;

            int lastCx = int.MinValue;
            int lastCy = int.MinValue;
            int lastCz = int.MinValue;

            int* neighborStart = stackalloc int[27];
            int* neighborEnd = stackalloc int[27];

            for (int i = startIndex; i < endIndex; i++)
            {
                float3 myPos = posPtr[i].xyz;
                float densityAcc = 0f;

                float3 cellCoord = myPos * InvCellSize;
                int3 baseCell = (int3)math.floor(cellCoord);

                int cx = baseCell.x * 73856093;
                int cy = baseCell.y * 19349663;
                int cz = baseCell.z * 83492791;

                if (cx != lastCx || cy != lastCy || cz != lastCz)
                {
                    lastCx = cx;
                    lastCy = cy;
                    lastCz = cz;
                    for (int n = 0; n < 27; n++)
                    {
                        int3 offset = offsetPtr[n];
                        int hash = ((cx + offset.x) ^ (cy + offset.y) ^ (cz + offset.z)) & HashMask;
                        int cellCount = countsPtr[hash];
                        neighborStart[n] = offsetsPtr[hash];
                        neighborEnd[n] = neighborStart[n] + cellCount;
                    }
                }

                for (int n = 0; n < 27; n++)
                {
                    int startIdx = neighborStart[n];
                    int endIdx = neighborEnd[n];

                    for (int k = startIdx; k < endIdx; k++)
                    {
                        float3 diff = myPos - posPtr[k].xyz;
                        float r2 = math.lengthsq(diff);

                        if (r2 < h2)
                        {
                            float h2R2 = h2 - r2;
                            densityAcc += velPtr[k].w * (h2R2 * h2R2 * h2R2);
                        }
                    }
                }

                float density = math.max(densityAcc * poly6Const, 0.0001f);
                float pressure = GasConstant * (density - RestDensity);

                propsPtr[i] = new float4(density, pressure, 0, 0);
            }
        }
    }
}



[BurstCompile(FloatMode = FloatMode.Fast, FloatPrecision = FloatPrecision.Standard)]
public unsafe struct UltimatePhysicsJob : IJobParallelForBatch
{
    [ReadOnly] public NativeArray<float4> PosRadIn;
    [ReadOnly] public NativeArray<float4> VelIn;
    [ReadOnly] public NativeArray<float4> FluidPropsIn; 
    [ReadOnly] public NativeArray<int3> NeighborOffsets;
    [ReadOnly] public NativeArray<int> CellOffsets;
    [ReadOnly] public NativeArray<int> CellCounts;

    [NativeDisableParallelForRestriction] public NativeArray<float4> PosRadOut;
    [NativeDisableParallelForRestriction] public NativeArray<float4> VelOut;
    [NativeDisableParallelForRestriction] public NativeArray<float4> ColorsOut;

    public float3 PlayerPos;
    public float3 PlayerVelocity;
    public float PlayerDiameter;

    public float3 TornadoPos;
    public float TornadoRadius;
    public float TornadoPullForce;
    public float TornadoSpinForce;
    public float TornadoLiftForce;

    public float3 BlackHolePos;
    public float BlackHoleRadius;
    public float BlackHolePullForce;

    public float Gravity;
    public float MaxSize;
    public float InvCellSize;
    public float DeltaTime;
    public int HashMask;
    public float MaxRSumSq;


    public float SmoothingRadius;
    public float Viscosity;

    public void Execute(int startIndex, int count)
    {
        float4* posRadInPtr = (float4*)PosRadIn.GetUnsafeReadOnlyPtr();
        float4* velInPtr = (float4*)VelIn.GetUnsafeReadOnlyPtr();
        float4* fluidPropsPtr = (float4*)FluidPropsIn.GetUnsafeReadOnlyPtr();
        int* offsetsPtr = (int*)CellOffsets.GetUnsafeReadOnlyPtr();
        int* countsPtr = (int*)CellCounts.GetUnsafeReadOnlyPtr();
        int3* neighborOffsetsPtr = (int3*)NeighborOffsets.GetUnsafeReadOnlyPtr();
        float4* posRadOutPtr = (float4*)PosRadOut.GetUnsafePtr();
        float4* velOutPtr = (float4*)VelOut.GetUnsafePtr();
      //  float4* colOutPtr = (float4*)ColorsOut.GetUnsafePtr();

        int endIndex = startIndex + count;

        int lastCx = int.MinValue;
        int lastCy = int.MinValue;
        int lastCz = int.MinValue;

        int* neighborStart = stackalloc int[27];
        int* neighborEnd = stackalloc int[27];

        for (int i = startIndex; i < endIndex; i++)
        {
            float4 myPosRad = posRadInPtr[i];
            float3 myPos = myPosRad.xyz;
            float rI = myPosRad.w;

            float4 myVelMass = velInPtr[i];
            float3 myVel = myVelMass.xyz;
            float mass = myVelMass.w;

            float densityI = fluidPropsPtr[i].x;
            float pressureI = fluidPropsPtr[i].y;

            float pRadius = PlayerDiameter * 0.5f;
            float yBottom = PlayerPos.y - 2f * pRadius;
            float yTop = PlayerPos.y + pRadius;

            float3 closestPlayerPt;
            if (myPos.y > yTop) closestPlayerPt = new float3(PlayerPos.x, yTop, PlayerPos.z);
            else if (myPos.y < yBottom)
            {
                closestPlayerPt = new float3(myPos.x, yBottom, myPos.z);
                float2 offsetXZ = myPos.xz - PlayerPos.xz;
                float distXZ = math.length(offsetXZ);
                if (distXZ > pRadius)
                {
                    offsetXZ = (offsetXZ / distXZ) * pRadius;
                    closestPlayerPt.x = PlayerPos.x + offsetXZ.x;
                    closestPlayerPt.z = PlayerPos.z + offsetXZ.y;
                }
            }
            else closestPlayerPt = new float3(PlayerPos.x, myPos.y, PlayerPos.z);

            float distSqToPlayer = math.lengthsq(myPos - closestPlayerPt);
            
            float3 cellCoord = myPos * InvCellSize;
            int3 baseCell = (int3)math.floor(cellCoord);

            int cx = baseCell.x * 73856093;
            int cy = baseCell.y * 19349663;
            int cz = baseCell.z * 83492791;

            if (cx != lastCx || cy != lastCy || cz != lastCz)
            {
                lastCx = cx;
                lastCy = cy;
                lastCz = cz;
                for (int n = 0; n < 27; n++)
                {
                    int3 offset = neighborOffsetsPtr[n];
                    int hash = ((cx + offset.x) ^ (cy + offset.y) ^ (cz + offset.z)) & HashMask;
                    int cellCount = countsPtr[hash];
                    neighborStart[n] = offsetsPtr[hash];
                    neighborEnd[n] = neighborStart[n] + cellCount;
                }
            }

            float3 sphAcceleration = ApplySphForces(
                ref myPos, ref myVel, mass, densityI, pressureI,
                posRadInPtr, velInPtr, fluidPropsPtr, neighborStart, neighborEnd);

            myVel += sphAcceleration * DeltaTime;

        //    float tornadoInfluence;
         //   float blackHoleInfluence;
            if (TornadoPullForce > 0f && myPos.y < -MaxSize + 60)
            {
                ApplyTornado(i, ref myPos, ref myVel, out _);
            }


            if (BlackHolePullForce > 0f)
            {
                ApplyBlackHole(i, ref myPos, ref myVel, out _);
            }

        
            ApplyPlayerInteraction(ref closestPlayerPt, distSqToPlayer, pRadius, rI, ref myPos, ref myVel);

         
             ApplyBoundaryAndGravity(rI, ref myPos, ref myVel);

         
            posRadOutPtr[i] = new float4(myPos, rI);
            velOutPtr[i] = new float4(myVel, mass);


          //  colOutPtr[i] = CalculateColor(myPos.y, currentSpeedSq, tornadoInfluence, blackHoleInfluence);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private float3 ApplySphForces(ref float3 myPos, ref float3 myVel, float massI, float densityI, float pressureI,
        float4* posRadInPtr, float4* velInPtr, float4* fluidPropsPtr, int* neighborStart, int* neighborEnd)
    {
        float h = SmoothingRadius;
        float h2 = h * h;
        float h6 = h2 * h2 * h2;
        float spikyGradConst = -45.0f / (math.PI * h6);
        float viscLapConst = 45.0f / (math.PI * h6);

        float3 forcePressure = float3.zero;
        float3 forceViscosity = float3.zero;

        for (int n = 0; n < 27; n++)
        {
            int startIdx = neighborStart[n];
            int endIdx = neighborEnd[n];

            for (int k = startIdx; k < endIdx; k++)
            {
                   
                    float3 diff = myPos - posRadInPtr[k].xyz;
                    float r2 = math.lengthsq(diff);

                    if (r2 < h2 && r2 > 0.00001f)
                    {
                 
                        float invR = math.rsqrt(r2);
                        float r = r2 * invR;
                        float hR = h - r;
                        float massJ = velInPtr[k].w;
                        float densityJ = fluidPropsPtr[k].x;
                        float pressureJ = fluidPropsPtr[k].y;
                        float massJOverDensityJ = massJ / densityJ;
                        float pTerm = massJOverDensityJ * (pressureI + pressureJ) * 0.5f;
                        float3 gradW = (diff * invR) * (spikyGradConst * hR * hR);
                        forcePressure -= pTerm * gradW;
                        float3 velDiff = velInPtr[k].xyz - myVel;
                        forceViscosity += Viscosity * massJOverDensityJ * velDiff * (viscLapConst * hR);
                    }
                }
        }

        return (forcePressure + forceViscosity) / densityI;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ApplyTornado(int i, ref float3 myPos, ref float3 myVel, out float tornadoInfluence)
    {
        tornadoInfluence = 0f;
        float heightRatio = myPos.y / MaxSize + 1;
        float funnelRadius = TornadoRadius * (0.1f + 2.0f * heightRatio * heightRatio);

        float2 diffXZ = TornadoPos.xz - myPos.xz;
        float distSqXZ = math.lengthsq(diffXZ);
        float influenceRadius = funnelRadius * 2.0f;

        if (distSqXZ < influenceRadius * influenceRadius && distSqXZ > 0.001f)
        {
            float distXZ = math.sqrt(distSqXZ);
            float2 dirToCenterXZ = diffXZ / distXZ;
            float2 tangentDir = math.normalize(new float2(-dirToCenterXZ.y, dirToCenterXZ.x));

            float normalizedDist = 1.0f - math.saturate(distXZ / influenceRadius);
            tornadoInfluence = normalizedDist;

            float distToWall = distXZ - funnelRadius;
            float wallPull = (distToWall > 0f) ? (TornadoPullForce * normalizedDist) : (-TornadoPullForce * 0.8f);

            myVel.xz += dirToCenterXZ * (wallPull * DeltaTime);

            float wallFactor = 1.0f - math.saturate(math.abs(distToWall) / funnelRadius);
            float spinForce = TornadoSpinForce * (0.3f + 1.5f * wallFactor);
            myVel.xz += tangentDir * (spinForce * DeltaTime);

            myVel.y += Gravity * 0.9f * DeltaTime;

            float liftForce = TornadoLiftForce * (1.0f + wallFactor * 2.0f) * (1.0f - heightRatio * 0.5f);
            myVel.y += liftForce * DeltaTime;

            if (heightRatio > 0.6f)
            {
                float ejectionForce = math.pow(heightRatio, 3.0f) * TornadoSpinForce * 2.0f;
                myVel.xz -= dirToCenterXZ * (ejectionForce * DeltaTime);
            }

            float chaosY = math.sin(i * 13.7f + myPos.y * 0.5f);
            float chaosX = math.cos(i * 7.3f - myPos.x * 0.5f);
            myVel.y += chaosY * (TornadoLiftForce * 0.2f) * DeltaTime;
            myVel.x += chaosX * (TornadoSpinForce * 0.1f) * DeltaTime;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ApplyBlackHole(int i, ref float3 myPos, ref float3 myVel, out float blackHoleInfluence)
    {
        blackHoleInfluence = 0f;
        float eventHorizonSq = BlackHoleRadius * BlackHoleRadius;
        float distSqToBlackHole = math.lengthsq(myPos - BlackHolePos);

        if (distSqToBlackHole < eventHorizonSq && distSqToBlackHole > 0.001f)
        {
            float distBh = math.sqrt(distSqToBlackHole);
            float normalizedDist = 1.0f - math.saturate(distBh / BlackHoleRadius);
            blackHoleInfluence = normalizedDist;

            float3 dirToCenter = (BlackHolePos - myPos) / distBh;
            myVel.y += Gravity * DeltaTime;

            float pullStrength = BlackHolePullForce * (1.0f + normalizedDist * normalizedDist * 5.0f);
            myVel.xyz += dirToCenter * (pullStrength * DeltaTime);

            float3 uniqueOrbitAxis =
                math.normalize(new float3(math.sin(i * 12.3f), math.cos(i * 7.8f), math.sin(i * 3.4f)));
            float3 tangentDir = math.normalize(math.cross(dirToCenter, uniqueOrbitAxis));

            float spinForce = BlackHolePullForce * 4.0f * normalizedDist;
            myVel.xyz += tangentDir * (spinForce * DeltaTime);

            float targetRadius = BlackHoleRadius * 0.25f;

            if (distBh <= targetRadius)
            {
                myPos = BlackHolePos - dirToCenter * targetRadius;
                float inwardVel = math.dot(myVel.xyz, dirToCenter);
                if (inwardVel > 0) myVel.xyz -= dirToCenter * inwardVel;
                myVel *= 0.85f;
            }
            else
            {
                myVel *= 0.98f;
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ApplyPlayerInteraction(ref float3 closestPlayerPt, float distSqToPlayer, float pRadius, float rI,
        ref float3 myPos, ref float3 myVel)
    {
        float rSumPlayer = rI + pRadius;

        if (distSqToPlayer < rSumPlayer * rSumPlayer && distSqToPlayer > 0.0001f)
        {
            float invDistPlayer = math.rsqrt(distSqToPlayer);
            float3 normalToPlayer = (myPos - closestPlayerPt) * invDistPlayer;
            float overlapPlayer = rSumPlayer - math.sqrt(distSqToPlayer);

            myPos += normalToPlayer * overlapPlayer;
            float vDotNPlayer = math.dot(myVel.xyz - PlayerVelocity, normalToPlayer);
            if (vDotNPlayer < 0) myVel.xyz += -(1f + 1.5f) * vDotNPlayer * normalToPlayer;
            myVel.xyz += normalToPlayer * (overlapPlayer * 60f);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private float ApplyBoundaryAndGravity(float rI, ref float3 myPos, ref float3 myVel)
    {
        myVel.y -= Gravity * DeltaTime;
        myPos.xyz += myVel.xyz * DeltaTime;

        if (myPos.y - rI < -MaxSize)
        {
            myPos.y = -MaxSize + rI;
            myVel.y = math.abs(myVel.y) * 0.1f;
            myVel.xz *= 0.99f;
        }

        if (math.abs(myPos.x) + rI > MaxSize)
        {
            myPos.x = math.sign(myPos.x) * (MaxSize - rI);
            myVel.x *= -0.1f;
        }

        if (math.abs(myPos.z) + rI > MaxSize)
        {
            myPos.z = math.sign(myPos.z) * (MaxSize - rI);
            myVel.z *= -0.1f;
        }

        float currentSpeedSq = math.lengthsq(myVel);
        float speedLimit = (BlackHolePullForce > 0f) ? 25000f : 10000f;

        if (currentSpeedSq > speedLimit)
        {
            myVel *= (math.rsqrt(currentSpeedSq) * math.sqrt(speedLimit));
            currentSpeedSq = speedLimit;
        }

        return currentSpeedSq;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private float4 CalculateColor(float posY, float currentSpeedSq, float tornadoInfluence, float blackHoleInfluence)
    {
        float heightLerp = math.saturate((posY + MaxSize) / (MaxSize * 1.5f));
        float4 deepColor = new float4(0.0f, 0.3f, 0.6f, 1f);
        float4 waterColor = new float4(0.4f, 0.9f, 1.0f, 1f);

        float speedLerp = math.saturate(currentSpeedSq / 2500f);
        float4 baseCol = math.lerp(deepColor, waterColor, heightLerp);

        float4 finalCol = baseCol + new float4(0.2f, 0.2f, 0.2f, 0) * speedLerp;

        float4 tornadoColor = new float4(0.0f, 0.8f, 0.6f, 1f);
        finalCol = math.lerp(finalCol, tornadoColor, math.saturate(tornadoInfluence * 1.5f));

        float4 pureBlack = new float4(0.0f, 0.0f, 0.0f, 1f);
        float darkness = math.saturate(blackHoleInfluence * 2.0f);
        finalCol = math.lerp(finalCol, pureBlack, darkness);

        return finalCol;
    }

}
