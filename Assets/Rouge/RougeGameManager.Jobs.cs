using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;

public struct RougeObstacle
{
    public int Type;
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
    [ReadOnly] public NativeArray<RougeEnemyEffectState> EffectStateIn;
    [NativeDisableParallelForRestriction] public NativeArray<float4> PositionScaleOut;
    [NativeDisableParallelForRestriction] public NativeArray<float4> VelocityOut;
    [NativeDisableParallelForRestriction] public NativeArray<float4> StateOut;
    [NativeDisableParallelForRestriction] public NativeArray<RougeEnemyEffectState> EffectStateOut;

    public void Execute(int startIndex, int count)
    {
        ulong* keyPtr = (ulong*)SortedKeys.GetUnsafeReadOnlyPtr();
        float4* posInPtr = (float4*)PositionScaleIn.GetUnsafeReadOnlyPtr();
        float4* velInPtr = (float4*)VelocityIn.GetUnsafeReadOnlyPtr();
        float4* stateInPtr = (float4*)StateIn.GetUnsafeReadOnlyPtr();
        RougeEnemyEffectState* effectInPtr = (RougeEnemyEffectState*)EffectStateIn.GetUnsafeReadOnlyPtr();
        float4* posOutPtr = (float4*)PositionScaleOut.GetUnsafePtr();
        float4* velOutPtr = (float4*)VelocityOut.GetUnsafePtr();
        float4* stateOutPtr = (float4*)StateOut.GetUnsafePtr();
        RougeEnemyEffectState* effectOutPtr = (RougeEnemyEffectState*)EffectStateOut.GetUnsafePtr();
        int end = startIndex + count;

        for (int i = startIndex; i < end; i++)
        {
            int sourceIndex = (int)keyPtr[i];
            posOutPtr[i] = posInPtr[sourceIndex];
            velOutPtr[i] = velInPtr[sourceIndex];
            stateOutPtr[i] = stateInPtr[sourceIndex];
            effectOutPtr[i] = effectInPtr[sourceIndex];
        }
    }
}

[BurstCompile(FloatMode = FloatMode.Fast, FloatPrecision = FloatPrecision.Standard)]
public unsafe struct SimulateEnemiesJob : IJobParallelForBatch
{
    private const float VisualStateFlagStep = 10f;
    private const int CurseVisualFlag = 1;
    private const int DeadVisualFlag = 2;
    private const float PoisonDurationSeconds = 2f;
    private const float PoisonTickInterval = 0.5f;
    private const float PoisonTickMaxHealthRatio = 0.1f;
    private const float BurnTickInterval = 0.5f;
    private const float BurnGroundRadius = 5f;
    private const float DeadFlashDecayRate = 2f;
    private const float BurnPatchReapplyCooldown = 0.85f;
    private const float BurnPatchDurationMultiplier = 0.45f;
    private const float BurnPatchDamageMultiplier = 0.55f;

    [ReadOnly] public NativeArray<ulong> SortedKeys;
    [ReadOnly] public NativeArray<float4> PositionScaleIn;
    [ReadOnly] public NativeArray<float4> VelocityIn;
    [ReadOnly] public NativeArray<float4> StateIn;
    [ReadOnly] public NativeArray<RougeEnemyEffectState> EffectStateIn;
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
    [NativeDisableParallelForRestriction] public NativeArray<RougeEnemyEffectState> EffectStateOut;

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
    public float PlayerContactPadding;
    [WriteOnly] public NativeQueue<float2>.ParallelWriter ExplosionQueue;
    [WriteOnly] public NativeQueue<RougeSkillEvent>.ParallelWriter SkillEventQueue;
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
        RougeEnemyEffectState* effectInPtr = (RougeEnemyEffectState*)EffectStateIn.GetUnsafeReadOnlyPtr();
        float4* posOutPtr = (float4*)PositionScaleOut.GetUnsafePtr();
        float4* velOutPtr = (float4*)VelocityOut.GetUnsafePtr();
        float4* stateOutPtr = (float4*)StateOut.GetUnsafePtr();
        RougeEnemyEffectState* effectOutPtr = (RougeEnemyEffectState*)EffectStateOut.GetUnsafePtr();
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
                posOutPtr[sourceIndex] = new float4(99999f, -9999f, 99999f, 0f);
                velOutPtr[sourceIndex] = float4.zero;
                stateOutPtr[sourceIndex] = new float4(-1f, 0f, 0f, 0f);
                effectOutPtr[sourceIndex] = default;
                continue;
            }

            float4 pos4 = posInPtr[i];
            float4 vel4 = velInPtr[i];
            float4 state4 = stateInPtr[i];
            RougeEnemyEffectState effects = effectInPtr[i];

            float3 pos = pos4.xyz;
            float3 vel = vel4.xyz;
            float tornadoMark = vel4.w;
            float health = state4.x;
            float radius = state4.y;
            float maxSpeed = state4.z;
            float flashTimer = math.frac(math.max(state4.w, 0f));
            int visualFlags = DecodeVisualFlags(state4.w);
            bool isDeadVisual = (visualFlags & DeadVisualFlag) != 0;

            if (math.lengthsq(pos.xz - PlayerPos) > DespawnDistanceSq)
            {
                Respawn(sourceIndex, ref pos4, ref vel4, ref state4, ref effects);
                posOutPtr[sourceIndex] = pos4;
                velOutPtr[sourceIndex] = vel4;
                stateOutPtr[sourceIndex] = state4;
                effectOutPtr[sourceIndex] = effects;
                continue;
            }

            if (health <= 0f)
            {
                if (!isDeadVisual)
                {
                    flashTimer = math.max(flashTimer, 0.99f);
                }
                else
                {
                    flashTimer = math.max(0f, flashTimer - DeltaTime * DeadFlashDecayRate);
                }

                if (isDeadVisual && flashTimer <= 0f)
                {
                    Respawn(sourceIndex, ref pos4, ref vel4, ref state4, ref effects);
                    posOutPtr[sourceIndex] = pos4;
                    velOutPtr[sourceIndex] = vel4;
                    stateOutPtr[sourceIndex] = state4;
                    effectOutPtr[sourceIndex] = effects;
                    continue;
                }

                pos.y = math.max(pos.y, RenderHeight);
                vel = float3.zero;
                posOutPtr[sourceIndex] = new float4(pos, radius);
                velOutPtr[sourceIndex] = new float4(vel, 0f);
                stateOutPtr[sourceIndex] = new float4(
                    health,
                    radius,
                    maxSpeed,
                    EncodeVisualState(flashTimer, false, true));
                effectOutPtr[sourceIndex] = effects;
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

            if (effects.SlowTimer > 0f)
            {
                effects.SlowTimer = math.max(0f, effects.SlowTimer - DeltaTime);
                if (effects.SlowTimer <= 0f)
                {
                    effects.SlowPercent = 0f;
                }
            }
            else
            {
                effects.SlowPercent = 0f;
            }

            float slowMoveFactor = 1f - effects.SlowPercent * 0.01f;
            float2 desired = math.normalizesafe(PlayerPos - pos.xz);
            bool isAirborne = pos.y > RenderHeight + 0.5f;
            float3 acceleration = new float3(0f, -30f, 0f);
            if (!isAirborne)
            {
                acceleration.xz += desired * (ChaseAcceleration * slowMoveFactor);
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
            if (sepLen > SeparationStrength * 3f)
            {
                separation = (separation / sepLen) * (SeparationStrength * 3f);
            }

            if (!isAirborne)
            {
                acceleration.xz += separation;
            }

            for (int obstacleIndex = 0; obstacleIndex < ObstacleCount; obstacleIndex++)
            {
                RougeObstacle obstacle = obstaclePtr[obstacleIndex];
                if (obstacle.Type == 1)
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
                            acceleration.xz += normal * (ObstacleRepulsion * weight * math.max(math.abs(slowMoveFactor), 0.25f));
                            float2 tangent = new float2(-normal.y, normal.x);
                            if (math.dot(tangent, desired) < 0f) tangent = -tangent;
                            acceleration.xz += tangent * (ObstacleOrbitStrength * weight * math.max(math.abs(slowMoveFactor), 0.25f));
                        }
                    }
                }
                else
                {
                    float2 minPadded = obstacle.Min - new float2(radius + obstacle.Padding);
                    float2 maxPadded = obstacle.Max + new float2(radius + obstacle.Padding);
                    bool isInside = pos.x >= minPadded.x && pos.x <= maxPadded.x && pos.z >= minPadded.y && pos.z <= maxPadded.y;

                    if (isInside)
                    {
                        float dx1 = pos.x - minPadded.x;
                        float dx2 = maxPadded.x - pos.x;
                        float dy1 = pos.z - minPadded.y;
                        float dy2 = maxPadded.y - pos.z;
                        float minD = math.min(math.min(dx1, dx2), math.min(dy1, dy2));
                        float2 normal = minD == dx1 ? new float2(-1f, 0f)
                            : minD == dx2 ? new float2(1f, 0f)
                            : minD == dy1 ? new float2(0f, -1f)
                            : new float2(0f, 1f);
                        acceleration.xz += normal * (ObstacleRepulsion + minD * 50f);
                    }
                    else
                    {
                        float2 closest = math.clamp(pos.xz, minPadded, maxPadded);
                        float2 diff = pos.xz - closest;
                        float distSq = math.lengthsq(diff);
                        if (distSq >= ObstacleLookAhead * ObstacleLookAhead) continue;

                        if (!isAirborne)
                        {
                            float dist = math.sqrt(math.max(distSq, 0.0001f));
                            float2 normal = diff / dist;
                            float weight = 1f - math.saturate(dist / math.max(ObstacleLookAhead, 0.001f));
                            acceleration.xz += normal * (ObstacleRepulsion * weight * math.max(math.abs(slowMoveFactor), 0.25f));
                            float2 tangent = new float2(-normal.y, normal.x);
                            if (math.dot(tangent, desired) < 0f) tangent = -tangent;
                            acceleration.xz += tangent * (ObstacleOrbitStrength * weight * math.max(math.abs(slowMoveFactor), 0.25f));
                        }
                    }
                }
            }

            for (int s = 0; s < SkillAreaCount; s++)
            {
                RougeSkillArea skill = SkillAreas[s];
                switch (skill.Type)
                {
                    case 1: ProcessTornado(ref acceleration, ref vel, ref health, ref flashTimer, ref tornadoMark, ref effects, pos, skill); break;
                    case 2: ProcessBomb(ref vel, ref health, ref flashTimer, ref tornadoMark, ref effects, pos, skill); break;
                    case 3: ProcessLaser(ref acceleration, ref vel, ref health, ref flashTimer, ref tornadoMark, ref effects, pos, skill); break;
                    case 4: ProcessMelee(ref acceleration, ref vel, ref health, ref flashTimer, ref tornadoMark, ref effects, pos, skill); break;
                    case 5: ProcessOrbit(ref acceleration, ref vel, ref health, ref flashTimer, ref tornadoMark, ref effects, pos, skill); break;
                    case 6: ProcessSpike(ref acceleration, ref vel, ref flashTimer, ref tornadoMark, ref effects, pos, skill); break;
                    case 7: ProcessShockwave(ref acceleration, ref vel, ref health, ref flashTimer, ref tornadoMark, ref effects, pos, skill); break;
                    case 8: ProcessIceZone(ref acceleration, ref health, ref flashTimer, ref vel, ref tornadoMark, ref effects, pos, skill); break;
                    case 9:
                    case 10:
                    case 11:
                        ProcessTaggedArea(ref health, ref flashTimer, ref vel, ref tornadoMark, ref effects, pos, skill);
                        break;
                }
            }

            bool diedFromPoison = false;
            if (effects.PoisonTimer > 0f)
            {
                effects.PoisonTimer = math.max(0f, effects.PoisonTimer - DeltaTime);
                effects.PoisonTickTimer -= DeltaTime;
                while (effects.PoisonTimer > 0f && effects.PoisonTickTimer <= 0f)
                {
                    float previousHealth = health;
                    health -= EnemyMaxHealth * PoisonTickMaxHealthRatio;
                    flashTimer = math.max(flashTimer, 0.45f);
                    effects.PoisonTickTimer += PoisonTickInterval;
                    if (previousHealth > 0f && health <= 0f)
                    {
                        diedFromPoison = true;
                        break;
                    }
                }

                if (effects.PoisonTimer <= 0f)
                {
                    effects.PoisonTickTimer = 0f;
                    effects.PoisonSpreadRadius = 0f;
                }
            }
            else
            {
                effects.PoisonTickTimer = 0f;
                effects.PoisonSpreadRadius = 0f;
            }

            if (effects.BurnReapplyCooldown > 0f)
            {
                effects.BurnReapplyCooldown = math.max(0f, effects.BurnReapplyCooldown - DeltaTime);
            }

            if (effects.BurnTimer > 0f)
            {
                effects.BurnTimer = math.max(0f, effects.BurnTimer - DeltaTime);
                effects.BurnTickTimer -= DeltaTime;
                while (effects.BurnTimer > 0f && effects.BurnTickTimer <= 0f)
                {
                    health -= effects.BurnDamage;
                    flashTimer = math.max(flashTimer, 0.3f);
                    effects.BurnTickTimer += BurnTickInterval;
                }

                if (effects.BurnTimer <= 0f)
                {
                    effects.BurnTickTimer = 0f;
                    effects.BurnDamage = 0f;
                    effects.BurnDuration = 0f;
                    effects.BurnReapplyCooldown = 0f;
                }
            }
            else
            {
                effects.BurnTickTimer = 0f;
                effects.BurnDamage = 0f;
                effects.BurnDuration = 0f;
                effects.BurnReapplyCooldown = 0f;
            }

            if (BulletCount > 0 && pos.x >= BulletMin.x && pos.x <= BulletMax.x && pos.z >= BulletMin.y && pos.z <= BulletMax.y && !isAirborne)
            {
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
                        {
                            System.Threading.Interlocked.Increment(ref ((int*)SkillKillCounts.GetUnsafePtr())[5]);
                        }
                    }
                }
            }

            bool hitPlayer = false;
            float distToPlayerSq = math.lengthsq(pos.xz - PlayerPos);
            if (health > 0f && !isAirborne && tornadoMark < 0.5f && distToPlayerSq < (radius + PlayerContactPadding) * (radius + PlayerContactPadding))
            {
                System.Threading.Interlocked.Increment(ref ((int*)PlayerDamageCount.GetUnsafePtr())[0]);
                health = -1f;
                hitPlayer = true;
            }

            if (health <= 0f && !isAirborne)
            {
                acceleration = float3.zero;
                vel = float3.zero;
                tornadoMark = 0f;
            }

            vel += acceleration * DeltaTime;
            if (!isAirborne)
            {
                if (effects.SlowTimer > 0f)
                {
                    if (slowMoveFactor >= 0f)
                    {
                        vel.xz *= math.saturate(slowMoveFactor);
                    }
                    else
                    {
                        vel.xz = -vel.xz * math.min(math.abs(slowMoveFactor), 2f);
                    }
                }

                float speedSq = math.lengthsq(vel.xz);
                if (speedSq > maxSpeed * maxSpeed)
                {
                    vel.xz *= maxSpeed * math.rsqrt(speedSq);
                }

                vel.xz *= VelocityDamping;
            }
            else
            {
                vel.xz *= 0.99f;
            }

            pos += vel * DeltaTime;
            if (pos.y <= RenderHeight)
            {
                if (vel.y < -3.5f || tornadoMark > 0.5f)
                {
                    bool isSkillKill = tornadoMark > 0.5f;
                    bool isSpikeKill = tornadoMark > 1.5f && tornadoMark < 2.5f;
                    bool isLaunchKill = tornadoMark > 2.5f;
                    health = 0f;
                    tornadoMark = 0f;
                    if (isSkillKill)
                    {
                        if (isLaunchKill)
                        {
                            if (effects.LaunchLandingRadius > 0f)
                            {
                                SkillEventQueue.Enqueue(new RougeSkillEvent
                                {
                                    Type = (int)RougeSkillEventType.LaunchLandingExplosion,
                                    Position = pos.xz,
                                    Radius = effects.LaunchLandingRadius,
                                    Damage = effects.LaunchLandingDamage
                                });
                            }
                        }
                        else
                        {
                            ExplosionQueue.Enqueue(pos.xz);
                            if (isSpikeKill)
                            {
                                System.Threading.Interlocked.Increment(ref ((int*)SkillKillCounts.GetUnsafePtr())[3]);
                            }
                            else
                            {
                                System.Threading.Interlocked.Increment(ref ((int*)SkillKillCounts.GetUnsafePtr())[0]);
                            }
                        }
                    }

                    effects.LaunchLandingDamage = 0f;
                    effects.LaunchLandingRadius = 0f;
                }
                else if (vel.y < -1f)
                {
                    health -= math.abs(vel.y) * 15f;
                    flashTimer = 1f;
                }

                pos.y = RenderHeight;
                vel.y = 0f;
            }

            bool justDied = health <= 0f && !hitPlayer && !isDeadVisual;
            if (justDied)
            {
                if (diedFromPoison && effects.PoisonSpreadRadius > 0f)
                {
                    SkillEventQueue.Enqueue(new RougeSkillEvent
                    {
                        Type = (int)RougeSkillEventType.PoisonSpread,
                        Position = pos.xz,
                        Radius = effects.PoisonSpreadRadius
                    });
                }

                if (effects.CurseExplosionDamage > 0f && effects.CurseExplosionRadius > 0f)
                {
                    SkillEventQueue.Enqueue(new RougeSkillEvent
                    {
                        Type = (int)RougeSkillEventType.CurseExplosion,
                        Position = pos.xz,
                        Radius = effects.CurseExplosionRadius,
                        Damage = effects.CurseExplosionDamage
                    });
                }

                if (effects.BurnTimer > 0f && effects.BurnDamage > 0f)
                {
                    SkillEventQueue.Enqueue(new RougeSkillEvent
                    {
                        Type = (int)RougeSkillEventType.BurnGround,
                        Position = pos.xz,
                        Radius = BurnGroundRadius,
                        Damage = effects.BurnDamage,
                        Duration = math.max(effects.BurnDuration, 0.1f)
                    });
                }

                SkillEventQueue.Enqueue(new RougeSkillEvent
                {
                    Type = (int)RougeSkillEventType.EnemyDeathBurst,
                    Position = pos.xz,
                    Radius = radius
                });

                System.Threading.Interlocked.Increment(ref ((int*)EnemyKillCount.GetUnsafePtr())[0]);
                flashTimer = math.max(flashTimer, 0.99f);
                effects = default;
            }

            pos.x = math.clamp(pos.x, -ArenaHalfExtent, ArenaHalfExtent);
            pos.z = math.clamp(pos.z, -ArenaHalfExtent, ArenaHalfExtent);

            flashTimer = math.max(0f, flashTimer - DeltaTime * 5f);
            posOutPtr[sourceIndex] = new float4(pos, radius);
            velOutPtr[sourceIndex] = new float4(vel, tornadoMark);
            stateOutPtr[sourceIndex] = new float4(
                health,
                radius,
                maxSpeed,
                EncodeVisualState(
                    flashTimer,
                    effects.CurseExplosionDamage > 0f && effects.CurseExplosionRadius > 0f,
                    health <= 0f));
            effectOutPtr[sourceIndex] = effects;
        }
    }

    private static int DecodeVisualFlags(float encodedValue)
    {
        return (int)math.floor(math.max(encodedValue, 0f) / VisualStateFlagStep + 0.0001f);
    }

    private static float EncodeVisualState(float flashTimer, bool hasCurseVisual, bool isDeadVisual)
    {
        int flags = 0;
        if (hasCurseVisual)
        {
            flags |= CurseVisualFlag;
        }

        if (isDeadVisual)
        {
            flags |= DeadVisualFlag;
        }

        return math.min(math.max(flashTimer, 0f), 0.99f) + flags * VisualStateFlagStep;
    }

    private void ProcessTornado(ref float3 acceleration, ref float3 vel, ref float health, ref float flashTimer, ref float tornadoMark, ref RougeEnemyEffectState effects, float3 pos, RougeSkillArea skill)
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
            vel.y = math.max(vel.y, skill.VerticalForce * weight * KnockbackResist);
            tornadoMark = 1f;
            health -= skill.Damage * 0.05f * DeltaTime;
            flashTimer = 1f;
            ApplySkillEffects(ref vel, ref flashTimer, ref tornadoMark, ref effects, pos, skill);
        }
    }

    private void ProcessBomb(ref float3 vel, ref float health, ref float flashTimer, ref float tornadoMark, ref RougeEnemyEffectState effects, float3 pos, RougeSkillArea skill)
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
            vel.y = math.max(vel.y, skill.VerticalForce * weight * KnockbackResist);
            float prevHB = health;
            health -= skill.Damage * BombDmgMult;
            flashTimer = 1f;
            if (prevHB > 0f && health <= 0f)
            {
                System.Threading.Interlocked.Increment(ref ((int*)SkillKillCounts.GetUnsafePtr())[1]);
            }

            ApplySkillEffects(ref vel, ref flashTimer, ref tornadoMark, ref effects, pos, skill);
        }
    }

    private void ProcessLaser(ref float3 acceleration, ref float3 vel, ref float health, ref float flashTimer, ref float tornadoMark, ref RougeEnemyEffectState effects, float3 pos, RougeSkillArea skill)
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
                {
                    System.Threading.Interlocked.Increment(ref ((int*)SkillKillCounts.GetUnsafePtr())[2]);
                }

                acceleration.xz += skill.Direction * (skill.PullForce * weight * KnockbackResist);
                vel.y = math.max(vel.y, skill.VerticalForce * weight * KnockbackResist);
                ApplySkillEffects(ref vel, ref flashTimer, ref tornadoMark, ref effects, pos, skill);
            }
        }
    }

    private void ProcessMelee(ref float3 acceleration, ref float3 vel, ref float health, ref float flashTimer, ref float tornadoMark, ref RougeEnemyEffectState effects, float3 pos, RougeSkillArea skill)
    {
        if (math.abs(pos.y - RenderHeight) > 6f) return;
        float2 pToS = pos.xz - skill.Position;
        float distSq = math.lengthsq(pToS);
        if (distSq < skill.Radius * skill.Radius)
        {
            float2 dir = math.normalizesafe(pToS, new float2(0f, 1f));
            float dot = math.dot(dir, skill.Direction);
            if (dot > 0.3f)
            {
                float prevHM = health;
                health -= skill.Damage * DeltaTime * 200f * MeleeDmgMult;
                flashTimer = 1f;
                if (prevHM > 0f && health <= 0f)
                {
                    System.Threading.Interlocked.Increment(ref ((int*)SkillKillCounts.GetUnsafePtr())[3]);
                }

                acceleration.xz += dir * skill.PullForce * KnockbackResist;
                vel.y = math.max(vel.y, skill.VerticalForce * KnockbackResist);
                ApplySkillEffects(ref vel, ref flashTimer, ref tornadoMark, ref effects, pos, skill);
            }
        }
    }

    private void ProcessOrbit(ref float3 acceleration, ref float3 vel, ref float health, ref float flashTimer, ref float tornadoMark, ref RougeEnemyEffectState effects, float3 pos, RougeSkillArea skill)
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
            vel.y = math.max(vel.y, skill.VerticalForce * weight * KnockbackResist);
            float prevHO = health;
            health -= skill.Damage * DeltaTime * OrbitDmgMult;
            flashTimer = 1f;
            if (prevHO > 0f && health <= 0f)
            {
                System.Threading.Interlocked.Increment(ref ((int*)SkillKillCounts.GetUnsafePtr())[4]);
            }

            ApplySkillEffects(ref vel, ref flashTimer, ref tornadoMark, ref effects, pos, skill);
        }
    }

    private void ProcessSpike(ref float3 acceleration, ref float3 vel, ref float flashTimer, ref float tornadoMark, ref RougeEnemyEffectState effects, float3 pos, RougeSkillArea skill)
    {
        if (pos.y > RenderHeight + 3f) return;
        float2 diff = pos.xz - skill.Position;
        float distSq = math.lengthsq(diff);
        if (distSq < skill.Radius * skill.Radius)
        {
            float2 dir = math.normalizesafe(diff, new float2(0f, 1f));
            float weight = 1f - math.saturate(math.sqrt(distSq) / skill.Radius);
            vel.y = math.max(vel.y, skill.VerticalForce * (1f + weight * 2f));
            acceleration.xz += dir * (skill.PullForce + 25f);
            tornadoMark = 2f;
            flashTimer = 1f;
            ApplySkillEffects(ref vel, ref flashTimer, ref tornadoMark, ref effects, pos, skill);
        }
    }

    private void ProcessShockwave(ref float3 acceleration, ref float3 vel, ref float health, ref float flashTimer, ref float tornadoMark, ref RougeEnemyEffectState effects, float3 pos, RougeSkillArea skill)
    {
        if (pos.y > RenderHeight + 3f) return;
        float2 diff = pos.xz - skill.Position;
        float distSq = math.lengthsq(diff);
        float outerR = skill.Radius;
        float innerR = math.max(0f, outerR - skill.Length);
        if (distSq < outerR * outerR && distSq > innerR * innerR && distSq > 0.0001f)
        {
            float dist = math.sqrt(distSq);
            float2 dir = diff / dist;
            float weight = 1f - math.saturate((dist - innerR) / math.max(skill.Length, 0.001f));
            float previousHealth = health;
            health -= skill.Damage * weight;
            acceleration.xz += dir * (-skill.PullForce * weight * KnockbackResist);
            vel.y = math.max(vel.y, skill.VerticalForce * weight * KnockbackResist * 1.25f);
            tornadoMark = 2f;
            flashTimer = 1f;
            if (previousHealth > 0f && health <= 0f)
            {
                System.Threading.Interlocked.Increment(ref ((int*)SkillKillCounts.GetUnsafePtr())[3]);
            }

            ApplySkillEffects(ref vel, ref flashTimer, ref tornadoMark, ref effects, pos, skill);
        }
    }

    private void ProcessIceZone(ref float3 acceleration, ref float health, ref float flashTimer, ref float3 vel, ref float tornadoMark, ref RougeEnemyEffectState effects, float3 pos, RougeSkillArea skill)
    {
        if (math.abs(pos.y - RenderHeight) > 4f) return;
        float2 diff = pos.xz - skill.Position;
        float distSq = math.lengthsq(diff);
        if (distSq < skill.Radius * skill.Radius)
        {
            float weight = 1f - math.saturate(math.sqrt(distSq) / skill.Radius);
            vel.xz *= math.lerp(1f, 0.1f, weight);
            float2 pullDir = -math.normalizesafe(diff, float2.zero);
            acceleration.xz += pullDir * (skill.PullForce * weight);
            float prevH = health;
            health -= skill.Damage * DeltaTime * LaserDmgMult;
            flashTimer = math.max(flashTimer, 0.3f);
            if (prevH > 0f && health <= 0f)
            {
                System.Threading.Interlocked.Increment(ref ((int*)SkillKillCounts.GetUnsafePtr())[2]);
            }

            ApplySkillEffects(ref vel, ref flashTimer, ref tornadoMark, ref effects, pos, skill);
        }
    }

    private void ProcessTaggedArea(ref float health, ref float flashTimer, ref float3 vel, ref float tornadoMark, ref RougeEnemyEffectState effects, float3 pos, RougeSkillArea skill)
    {
        if (math.abs(pos.y - RenderHeight) > 5f) return;
        float2 diff = pos.xz - skill.Position;
        float distSq = math.lengthsq(diff);
        if (distSq > skill.Radius * skill.Radius) return;

        if (skill.AuxA > 0f)
        {
            float dist = math.sqrt(math.max(distSq, 0.0001f));
            float edgeNoise = SamplePoisonNoise(skill.Position, diff, skill.AuxC, skill.AuxD);
            float outerRadius = skill.Radius * (1f + edgeNoise * skill.AuxA);
            if (dist > outerRadius)
            {
                return;
            }
        }

        if (skill.Damage > 0f)
        {
            health -= skill.Damage * DeltaTime;
        }

        flashTimer = math.max(flashTimer, 0.2f);
        ApplySkillEffects(ref vel, ref flashTimer, ref tornadoMark, ref effects, pos, skill);
    }

    private static float SamplePoisonNoise(float2 center, float2 offset, float noiseScale, float seed)
    {
        float angle = math.atan2(offset.y, offset.x);
        float a = math.sin(angle * (5.3f + noiseScale * 11f) + seed * 0.73f + center.x * 0.11f);
        float b = math.cos(angle * (8.1f + noiseScale * 17f) - seed * 0.41f + center.y * 0.09f);
        return (a + b) * 0.5f;
    }

    private void ApplySkillEffects(ref float3 vel, ref float flashTimer, ref float tornadoMark, ref RougeEnemyEffectState effects, float3 pos, RougeSkillArea skill)
    {
        SkillHitEffectTag tags = (SkillHitEffectTag)skill.EffectFlags;
        if (tags == SkillHitEffectTag.None)
        {
            return;
        }

        float2 pushDir = math.normalizesafe(pos.xz - skill.Position, new float2(0f, 1f));
        if ((tags & SkillHitEffectTag.Knockback) != 0)
        {
            float knockbackForce = skill.EffectKnockbackForce == 0f ? 35f : skill.EffectKnockbackForce;
            vel.xz += pushDir * (knockbackForce * KnockbackResist);
        }

        if ((tags & SkillHitEffectTag.Launch) != 0)
        {
            float launchHeight = skill.EffectLaunchHeight == 0f ? 12f : skill.EffectLaunchHeight;
            vel.y = math.max(vel.y, launchHeight * KnockbackResist);
            tornadoMark = 3f;
            effects.LaunchLandingDamage = math.max(effects.LaunchLandingDamage, skill.Damage * 0.5f);
            effects.LaunchLandingRadius = math.max(effects.LaunchLandingRadius, skill.EffectLaunchLandingRadius);
        }

        if ((tags & SkillHitEffectTag.Poison) != 0)
        {
            if (effects.PoisonTimer <= 0f)
            {
                effects.PoisonTickTimer = PoisonTickInterval;
            }

            effects.PoisonTimer = PoisonDurationSeconds;
            effects.PoisonSpreadRadius = math.max(effects.PoisonSpreadRadius, skill.EffectPoisonSpreadRadius);
        }

        if ((tags & SkillHitEffectTag.Slow) != 0)
        {
            effects.SlowPercent = skill.EffectSlowPercent;
            effects.SlowTimer = math.max(effects.SlowTimer, skill.EffectSlowDuration > 0f ? skill.EffectSlowDuration : 2f);
        }

        if ((tags & SkillHitEffectTag.Curse) != 0)
        {
            effects.CurseExplosionDamage = math.max(effects.CurseExplosionDamage, skill.EffectCurseExplosionDamage);
            effects.CurseExplosionRadius = math.max(effects.CurseExplosionRadius, skill.EffectCurseExplosionRadius);
        }

        if ((tags & SkillHitEffectTag.Burn) != 0)
        {
            bool isBurnPatch = skill.Type == 11;
            float burnDuration = skill.EffectBurnDuration > 0f ? skill.EffectBurnDuration : 2f;
            float burnDamage = math.max(skill.EffectBurnDamage, 0f);
            if (isBurnPatch)
            {
                if (effects.BurnReapplyCooldown > 0f)
                {
                    flashTimer = math.max(flashTimer, 0.1f);
                    return;
                }

                burnDuration *= BurnPatchDurationMultiplier;
                burnDamage *= BurnPatchDamageMultiplier;
            }

            if (burnDamage <= 0f || burnDuration <= 0f)
            {
                flashTimer = math.max(flashTimer, 0.18f);
                return;
            }

            if (effects.BurnTimer <= 0f)
            {
                effects.BurnTickTimer = BurnTickInterval;
            }

            if (isBurnPatch)
            {
                if (effects.BurnTimer <= 0f)
                {
                    effects.BurnTimer = burnDuration;
                }
                else
                {
                    effects.BurnTimer = math.max(effects.BurnTimer, math.min(burnDuration, 0.35f));
                }

                effects.BurnReapplyCooldown = BurnPatchReapplyCooldown;
            }
            else
            {
                effects.BurnTimer = math.max(effects.BurnTimer, burnDuration);
                effects.BurnReapplyCooldown = 0.15f;
            }

            effects.BurnDamage = math.max(effects.BurnDamage, burnDamage);
            effects.BurnDuration = math.max(effects.BurnDuration, burnDuration);
        }

        flashTimer = math.max(flashTimer, 0.25f);
    }

    private void Respawn(int index, ref float4 pos4, ref float4 vel4, ref float4 state4, ref RougeEnemyEffectState effects)
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
        effects = default;
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