using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;

public struct RougeObstacle
{
    public const int BoxType = 0;
    public const int CircleType = 1;

    public int Type;
    public float2 Min;
    public float2 Max;
    public float2 Center;
    public float2 BoxAxisX;
    public float2 BoxAxisY;
    public float2 BoxHalfExtents;
    public float CircleRadius;
    public float Padding;
}

public static class RougeObstacleMath
{
    public static RougeObstacle CreateCircle(float2 center, float radius, float padding)
    {
        float safeRadius = math.max(radius, 0.05f);
        return new RougeObstacle
        {
            Type = RougeObstacle.CircleType,
            Center = center,
            CircleRadius = safeRadius,
            Min = center - new float2(safeRadius),
            Max = center + new float2(safeRadius),
            Padding = math.max(padding, 0f)
        };
    }

    public static RougeObstacle CreateBox(float2 center, float2 axisX, float2 axisY, float2 halfExtents, float padding)
    {
        float2 safeAxisX = math.normalizesafe(axisX, new float2(1f, 0f));
        float2 safeAxisY = math.normalizesafe(axisY, new float2(0f, 1f));
        float2 safeHalfExtents = math.max(halfExtents, new float2(0.05f));
        float2 aabbHalf = math.abs(safeAxisX) * safeHalfExtents.x + math.abs(safeAxisY) * safeHalfExtents.y;

        return new RougeObstacle
        {
            Type = RougeObstacle.BoxType,
            Center = center,
            BoxAxisX = safeAxisX,
            BoxAxisY = safeAxisY,
            BoxHalfExtents = safeHalfExtents,
            Min = center - aabbHalf,
            Max = center + aabbHalf,
            Padding = math.max(padding, 0f)
        };
    }

    public static bool ContainsPoint(RougeObstacle obstacle, float2 point, float extraPadding)
    {
        if (obstacle.Type == RougeObstacle.CircleType)
        {
            float paddedRadius = obstacle.CircleRadius + math.max(extraPadding, 0f);
            return math.lengthsq(point - obstacle.Center) <= paddedRadius * paddedRadius;
        }

        float2 minPadded = obstacle.Min - new float2(math.max(extraPadding, 0f));
        float2 maxPadded = obstacle.Max + new float2(math.max(extraPadding, 0f));
        if (point.x < minPadded.x || point.x > maxPadded.x || point.y < minPadded.y || point.y > maxPadded.y)
        {
            return false;
        }

        float2 delta = point - obstacle.Center;
        float localX = math.dot(delta, obstacle.BoxAxisX);
        float localY = math.dot(delta, obstacle.BoxAxisY);
        float2 paddedHalf = obstacle.BoxHalfExtents + new float2(math.max(extraPadding, 0f));
        return math.abs(localX) <= paddedHalf.x && math.abs(localY) <= paddedHalf.y;
    }

    public static float2 ClosestPoint(RougeObstacle obstacle, float2 point, float extraPadding)
    {
        if (obstacle.Type == RougeObstacle.CircleType)
        {
            float paddedRadius = obstacle.CircleRadius + math.max(extraPadding, 0f);
            float2 delta = point - obstacle.Center;
            float distSq = math.lengthsq(delta);
            if (distSq <= 0.000001f)
            {
                return obstacle.Center + new float2(paddedRadius, 0f);
            }

            float invDist = math.rsqrt(distSq);
            return obstacle.Center + delta * (paddedRadius * invDist);
        }

        float2 deltaBox = point - obstacle.Center;
        float2 paddedHalf = obstacle.BoxHalfExtents + new float2(math.max(extraPadding, 0f));
        float localX = math.dot(deltaBox, obstacle.BoxAxisX);
        float localY = math.dot(deltaBox, obstacle.BoxAxisY);
        float clampedX = math.clamp(localX, -paddedHalf.x, paddedHalf.x);
        float clampedY = math.clamp(localY, -paddedHalf.y, paddedHalf.y);
        return obstacle.Center + obstacle.BoxAxisX * clampedX + obstacle.BoxAxisY * clampedY;
    }

    public static float2 ResolvePointOutside(RougeObstacle obstacle, float2 point, float extraPadding)
    {
        if (obstacle.Type == RougeObstacle.CircleType)
        {
            float paddedRadius = obstacle.CircleRadius + math.max(extraPadding, 0f);
            float2 delta = point - obstacle.Center;
            float distSq = math.lengthsq(delta);
            if (distSq >= paddedRadius * paddedRadius)
            {
                return point;
            }

            float dist = math.sqrt(math.max(distSq, 0f));
            float2 direction = dist > 0.001f ? delta / dist : new float2(1f, 0f);
            return obstacle.Center + direction * paddedRadius;
        }

        if (!ContainsPoint(obstacle, point, extraPadding))
        {
            return point;
        }

        float2 deltaBox = point - obstacle.Center;
        float localX = math.dot(deltaBox, obstacle.BoxAxisX);
        float localY = math.dot(deltaBox, obstacle.BoxAxisY);
        float2 paddedHalf = obstacle.BoxHalfExtents + new float2(math.max(extraPadding, 0f));
        float remainingX = paddedHalf.x - math.abs(localX);
        float remainingY = paddedHalf.y - math.abs(localY);

        if (remainingX <= remainingY)
        {
            localX = (localX >= 0f ? 1f : -1f) * paddedHalf.x;
        }
        else
        {
            localY = (localY >= 0f ? 1f : -1f) * paddedHalf.y;
        }

        return obstacle.Center + obstacle.BoxAxisX * localX + obstacle.BoxAxisY * localY;
    }
}

public static class RougeMortonGridUtility
{
    public const int DensityFixedScale = 256;

    public static int ClampCoord(int value, int gridDim)
    {
        return math.clamp(value, 0, gridDim - 1);
    }

    public static int2 WorldToGrid(float2 worldPos, float2 origin, float invCellSize, int gridDim)
    {
        int2 cell = (int2)math.floor((worldPos - origin) * invCellSize);
        cell.x = ClampCoord(cell.x, gridDim);
        cell.y = ClampCoord(cell.y, gridDim);
        return cell;
    }

    public static int EncodeMortonFromWorld(float2 worldPos, float2 origin, float invCellSize, int gridDim)
    {
        int2 cell = WorldToGrid(worldPos, origin, invCellSize, gridDim);
        return EncodeMorton(cell.x, cell.y);
    }

    public static int EncodeMorton(int x, int y)
    {
        return Part1By1(x) | (Part1By1(y) << 1);
    }

    public static int2 DecodeMorton(int morton)
    {
        return new int2(Compact1By1(morton), Compact1By1(morton >> 1));
    }

    private static int Part1By1(int value)
    {
        uint x = (uint)value & 0x0000FFFFu;
        x = (x | (x << 8)) & 0x00FF00FFu;
        x = (x | (x << 4)) & 0x0F0F0F0Fu;
        x = (x | (x << 2)) & 0x33333333u;
        x = (x | (x << 1)) & 0x55555555u;
        return (int)x;
    }

    private static int Compact1By1(int value)
    {
        uint x = (uint)value & 0x55555555u;
        x = (x | (x >> 1)) & 0x33333333u;
        x = (x | (x >> 2)) & 0x0F0F0F0Fu;
        x = (x | (x >> 4)) & 0x00FF00FFu;
        x = (x | (x >> 8)) & 0x0000FFFFu;
        return (int)x;
    }
}

public struct RougeTowerTargetRequest
{
    public float2 Position;
    public float Range;
    public float BossRangePadding;
    public int TargetCount;
    public int PriorityMode;
}

[BurstCompile(FloatMode = FloatMode.Fast, FloatPrecision = FloatPrecision.Standard)]
public unsafe struct BuildEnemyTargetGridJob : IJobParallelForBatch
{
    private const float VisualStateFlagStep = 10f;
    private const int BufferedLaunchVisualFlag = 4;

    [ReadOnly] public NativeArray<float4> Positions;
    [ReadOnly] public NativeArray<float4> States;
    [NativeDisableParallelForRestriction] public NativeArray<int> CellHeads;
    [NativeDisableParallelForRestriction] public NativeArray<int> CellNext;
    public float2 GridOrigin;
    public float InvCellSize;
    public int GridDim;
    public float RenderHeight;
    public bool ExcludeAirborne;

    public void Execute(int startIndex, int count)
    {
        float4* positionPtr = (float4*)Positions.GetUnsafeReadOnlyPtr();
        float4* statePtr = (float4*)States.GetUnsafeReadOnlyPtr();
        int* headPtr = (int*)CellHeads.GetUnsafePtr();
        int* nextPtr = (int*)CellNext.GetUnsafePtr();
        int end = startIndex + count;
        for (int i = startIndex; i < end; i++)
        {
            nextPtr[i] = -1;
            if (statePtr[i].x <= 0f) continue;
            if (ExcludeAirborne)
            {
                int visualFlags = (int)math.floor(math.max(statePtr[i].w, 0f) /
                    VisualStateFlagStep + 0.0001f);
                if (positionPtr[i].y > RenderHeight + 0.05f ||
                    (visualFlags & BufferedLaunchVisualFlag) != 0)
                {
                    continue;
                }
            }
            int cell = RougeMortonGridUtility.EncodeMortonFromWorld(
                positionPtr[i].xz, GridOrigin, InvCellSize, GridDim);
            nextPtr[i] = System.Threading.Interlocked.Exchange(ref headPtr[cell], i);
        }
    }
}

[BurstCompile(FloatMode = FloatMode.Fast, FloatPrecision = FloatPrecision.Standard)]
public unsafe struct CrowdPbdProjectionJob : IJobParallelForBatch
{
    private const int MaxStoredNeighbors = 32;
    private const int NeighborCellDiameter = 5;
    private const int NeighborCellCount = NeighborCellDiameter * NeighborCellDiameter;

    [ReadOnly] public NativeArray<float4> Positions;
    [ReadOnly] public NativeArray<float4> Velocities;
    [ReadOnly] public NativeArray<float4> States;
    [ReadOnly] public NativeArray<RougeEnemyEffectState> Effects;
    [ReadOnly] public NativeArray<int> CellHeads;
    [ReadOnly] public NativeArray<int> CellNext;
    [ReadOnly] public NativeArray<byte> BlockedCells;
    [NativeDisableParallelForRestriction] public NativeArray<float4> ProjectedPositions;
    public float2 GridOrigin;
    public float CrowdInvCellSize;
    public float BlockedInvCellSize;
    public int GridDim;
    public int CurrentMaxEnemies;
    public int BossEnemyIndex;
    public float RenderHeight;
    public float2 ArenaHalfExtents;
    public int MaxCandidates;
    public int MaxNeighbors;
    public float Stiffness;
    public float RadiusScale;
    public float MaxCorrectionSpeed;
    public float DeltaTime;
    public uint FrameSeed;

    public void Execute(int startIndex, int count)
    {
        float4* positionPtr = (float4*)Positions.GetUnsafeReadOnlyPtr();
        float4* velocityPtr = (float4*)Velocities.GetUnsafeReadOnlyPtr();
        float4* statePtr = (float4*)States.GetUnsafeReadOnlyPtr();
        RougeEnemyEffectState* effectPtr = (RougeEnemyEffectState*)Effects.GetUnsafeReadOnlyPtr();
        int* headPtr = (int*)CellHeads.GetUnsafeReadOnlyPtr();
        int* nextPtr = (int*)CellNext.GetUnsafeReadOnlyPtr();
        byte* blockedPtr = (byte*)BlockedCells.GetUnsafeReadOnlyPtr();
        float4* outputPtr = (float4*)ProjectedPositions.GetUnsafePtr();
        int end = math.min(startIndex + count, CurrentMaxEnemies);
        int candidateLimit = math.max(1, MaxCandidates);
        int neighborLimit = math.clamp(MaxNeighbors, 1, MaxStoredNeighbors);
        float stiffness = math.saturate(Stiffness);
        float radiusScale = math.max(0.1f, RadiusScale);

        for (int sourceIndex = startIndex; sourceIndex < end; sourceIndex++)
        {
            float4 sourcePosition = positionPtr[sourceIndex];
            float4 sourceState = statePtr[sourceIndex];
            float4 sourceVelocity = velocityPtr[sourceIndex];
            RougeEnemyEffectState sourceEffects = effectPtr[sourceIndex];
            outputPtr[sourceIndex] = sourcePosition;
            if (sourceIndex == BossEnemyIndex || sourceState.x <= 0f ||
                IsAirborne(sourcePosition, sourceVelocity, sourceEffects))
                continue;

            int* selectedNeighbors = stackalloc int[MaxStoredNeighbors];
            int selectedCount = 0;
            int overlappingSeen = 0;
            int candidatesScanned = 0;
            int2 centerCell = RougeMortonGridUtility.WorldToGrid(
                sourcePosition.xz, GridOrigin, CrowdInvCellSize, GridDim);
            int firstNeighborCell = (int)(Hash((uint)sourceIndex, FrameSeed, 0x85EBCA6Bu) %
                NeighborCellCount);

            for (int cellStep = 0;
                 cellStep < NeighborCellCount && candidatesScanned < candidateLimit;
                 cellStep++)
            {
                int packedOffset = (firstNeighborCell + cellStep) % NeighborCellCount;
                int offsetX = packedOffset % NeighborCellDiameter - NeighborCellDiameter / 2;
                int offsetY = packedOffset / NeighborCellDiameter - NeighborCellDiameter / 2;
                int cellY = centerCell.y + offsetY;
                if (cellY < 0 || cellY >= GridDim) continue;
                int cellX = centerCell.x + offsetX;
                if (cellX < 0 || cellX >= GridDim) continue;
                int cell = RougeMortonGridUtility.EncodeMorton(cellX, cellY);
                for (int candidate = headPtr[cell];
                     candidate >= 0 && candidatesScanned < candidateLimit;
                     candidate = nextPtr[candidate])
                {
                    if (candidate == sourceIndex || (uint)candidate >= (uint)CurrentMaxEnemies)
                        continue;
                    candidatesScanned++;
                    float4 otherPosition = positionPtr[candidate];
                    float4 otherState = statePtr[candidate];
                    float4 otherVelocity = velocityPtr[candidate];
                    RougeEnemyEffectState otherEffects = effectPtr[candidate];
                    if (otherState.x <= 0f || IsAirborne(otherPosition, otherVelocity, otherEffects))
                        continue;

                    float minimumDistance = math.max(0.02f,
                        (sourcePosition.w + otherPosition.w) * radiusScale);
                    if (math.lengthsq(sourcePosition.xz - otherPosition.xz) >=
                        minimumDistance * minimumDistance)
                        continue;

                    overlappingSeen++;
                    if (selectedCount < neighborLimit)
                    {
                        selectedNeighbors[selectedCount++] = candidate;
                        continue;
                    }

                    // Reservoir replacement rotates the constrained subset over time. Dense
                    // cells therefore keep their aggregate density pressure without permanently
                    // starving units that happen to be late in the linked list.
                    uint random = Hash((uint)sourceIndex, (uint)candidate, FrameSeed);
                    int replacement = (int)(random % (uint)overlappingSeen);
                    if (replacement < neighborLimit) selectedNeighbors[replacement] = candidate;
                }
            }

            if (selectedCount <= 0) continue;
            float sourceMobility = GetMobility(sourceVelocity.xyz, sourceState.z, sourceEffects);
            float2 correction = float2.zero;
            for (int selected = 0; selected < selectedCount; selected++)
            {
                int neighborIndex = selectedNeighbors[selected];
                float4 otherPosition = positionPtr[neighborIndex];
                float4 otherVelocity = velocityPtr[neighborIndex];
                float4 otherState = statePtr[neighborIndex];
                RougeEnemyEffectState otherEffects = effectPtr[neighborIndex];
                float2 difference = sourcePosition.xz - otherPosition.xz;
                float distanceSq = math.lengthsq(difference);
                float minimumDistance = math.max(0.02f,
                    (sourcePosition.w + otherPosition.w) * radiusScale);
                float distance = math.sqrt(math.max(distanceSq, 0.000001f));
                float overlap = minimumDistance - distance;
                if (overlap <= 0f) continue;

                float2 direction;
                if (distanceSq > 0.000001f) direction = difference / distance;
                else
                {
                    float angle = Hash01(Hash((uint)sourceIndex, (uint)neighborIndex, 0x9E3779B9u)) *
                        (math.PI * 2f);
                    direction = new float2(math.cos(angle), math.sin(angle));
                }

                float otherMobility = GetMobility(otherVelocity.xyz, otherState.z, otherEffects);
                float mobilitySum = math.max(sourceMobility + otherMobility, 0.0001f);
                correction += direction * (overlap * (sourceMobility / mobilitySum));
            }

            correction *= stiffness;
            // Resolve deep piles over several frames. A radius-only cap allowed a unit to move
            // more than its full diameter in one projection, which looked like teleportation.
            float radiusLimit = math.max(sourcePosition.w * radiusScale * 0.25f, 0.015f);
            float speedLimit = math.max(MaxCorrectionSpeed, 0.5f) *
                math.clamp(DeltaTime, 0.001f, 0.05f);
            float maxCorrection = math.min(radiusLimit, speedLimit);
            float correctionLengthSq = math.lengthsq(correction);
            if (correctionLengthSq > maxCorrection * maxCorrection)
                correction *= maxCorrection * math.rsqrt(correctionLengthSq);

            float2 planarVelocity = sourceVelocity.xz;
            float speedSq = math.lengthsq(planarVelocity);
            float authoritySpeed = math.max(sourceState.z * 1.25f, 0.5f);
            if (speedSq > authoritySpeed * authoritySpeed)
            {
                float2 motionDirection = planarVelocity * math.rsqrt(speedSq);
                float opposing = math.dot(correction, motionDirection);
                if (opposing < 0f) correction -= motionDirection * opposing;
            }

            float2 arenaLimit = math.max(float2.zero,
                ArenaHalfExtents - math.max(sourcePosition.w * radiusScale, 0.05f));
            float2 projected = math.clamp(sourcePosition.xz + correction,
                -arenaLimit, arenaLimit);
            if (IsBlocked(projected, blockedPtr))
            {
                float2 xOnly = math.clamp(
                    sourcePosition.xz + new float2(correction.x, 0f),
                    -arenaLimit, arenaLimit);
                float2 yOnly = math.clamp(
                    sourcePosition.xz + new float2(0f, correction.y),
                    -arenaLimit, arenaLimit);
                projected = !IsBlocked(xOnly, blockedPtr) ? xOnly :
                    !IsBlocked(yOnly, blockedPtr) ? yOnly : sourcePosition.xz;
            }
            outputPtr[sourceIndex] = new float4(projected.x, sourcePosition.y,
                projected.y, sourcePosition.w);
        }
    }

    private bool IsBlocked(float2 worldPosition, byte* blockedPtr)
    {
        int2 cell = RougeMortonGridUtility.WorldToGrid(
            worldPosition, GridOrigin, BlockedInvCellSize, GridDim);
        return blockedPtr[RougeMortonGridUtility.EncodeMorton(cell.x, cell.y)] != 0;
    }

    private bool IsAirborne(float4 position, float4 velocity, RougeEnemyEffectState effects)
    {
        return velocity.w > 2.5f || velocity.y > 0.05f ||
            position.y > RenderHeight + 0.05f || effects.LaunchMotionTimer > 0f ||
            effects.LaunchStackTimer > 0f || effects.LaunchLandingRadius > 0f ||
            effects.LaunchLandingDamage > 0f;
    }

    private static float GetMobility(float3 velocity, float maxSpeed, RougeEnemyEffectState effects)
    {
        float speed = math.length(velocity.xz);
        float authority = math.saturate((speed - math.max(maxSpeed, 0.1f)) /
            math.max(maxSpeed * 2f, 0.5f));
        float mobility = math.lerp(1f, 0.15f, authority);
        if (effects.FreezeTimer > 0f) mobility *= 0.15f;
        return math.max(mobility, 0.02f);
    }

    private static uint Hash(uint a, uint b, uint seed)
    {
        uint value = a * 747796405u + b * 2891336453u + seed;
        value = (value ^ (value >> 16)) * 2246822519u;
        value = (value ^ (value >> 13)) * 3266489917u;
        return value ^ (value >> 16);
    }

    private static float Hash01(uint value)
    {
        return (value & 0x00FFFFFFu) * (1f / 16777215f);
    }
}

[BurstCompile]
public unsafe struct CopyProjectedPositionsJob : IJobParallelForBatch
{
    [ReadOnly] public NativeArray<float4> Source;
    [NativeDisableParallelForRestriction] public NativeArray<float4> Destination;

    public void Execute(int startIndex, int count)
    {
        UnsafeUtility.MemCpy(
            (float4*)Destination.GetUnsafePtr() + startIndex,
            (float4*)Source.GetUnsafeReadOnlyPtr() + startIndex,
            count * sizeof(float4));
    }
}

[BurstCompile(FloatMode = FloatMode.Fast, FloatPrecision = FloatPrecision.Standard)]
public unsafe struct FindTowerTargetsJob : IJobParallelFor
{
    // The Lv.5 laser reaches 30 beams; an echo tile raises that by 1.5x to 45.
    public const int MaxTargetsPerTower = 45;

    [ReadOnly] public NativeArray<RougeTowerTargetRequest> Requests;
    [ReadOnly] public NativeArray<float4> EnemyPositions;
    [ReadOnly] public NativeArray<float4> EnemyStates;
    [ReadOnly] public NativeArray<byte> EnemyKinds;
    [ReadOnly] public NativeArray<float> FlowDistances;
    [ReadOnly] public NativeArray<int> CellHeads;
    [ReadOnly] public NativeArray<int> CellNext;
    [NativeDisableParallelForRestriction] public NativeArray<int> ResultIndices;
    [NativeDisableParallelForRestriction] public NativeArray<float> ResultDistances;
    public float2 GridOrigin;
    public float InvCellSize;
    public int GridDim;

    public void Execute(int towerIndex)
    {
        RougeTowerTargetRequest request = Requests[towerIndex];
        int resultStart = towerIndex * MaxTargetsPerTower;
        int resultCapacity = math.clamp(request.TargetCount, 0, MaxTargetsPerTower);
        int* resultPtr = (int*)ResultIndices.GetUnsafePtr() + resultStart;
        float* distancePtr = (float*)ResultDistances.GetUnsafePtr() + resultStart;
        for (int i = 0; i < MaxTargetsPerTower; i++)
        {
            resultPtr[i] = -1;
            distancePtr[i] = float.MaxValue;
        }

        if (resultCapacity <= 0 || request.Range <= 0f) return;

        float4* enemyPositionPtr = (float4*)EnemyPositions.GetUnsafeReadOnlyPtr();
        float4* enemyStatePtr = (float4*)EnemyStates.GetUnsafeReadOnlyPtr();
        float* flowDistancePtr = (float*)FlowDistances.GetUnsafeReadOnlyPtr();
        int* headPtr = (int*)CellHeads.GetUnsafeReadOnlyPtr();
        int* nextPtr = (int*)CellNext.GetUnsafeReadOnlyPtr();
        bool bossFirst = request.PriorityMode == (int)RougeTowerTargetPriority.BossFirst;
        float queryRange = request.Range + (bossFirst
            ? math.max(0f, request.BossRangePadding)
            : 0f);
        float2 extent = new float2(queryRange);
        int2 minCell = RougeMortonGridUtility.WorldToGrid(
            request.Position - extent, GridOrigin, InvCellSize, GridDim);
        int2 maxCell = RougeMortonGridUtility.WorldToGrid(
            request.Position + extent, GridOrigin, InvCellSize, GridDim);
        int found = 0;

        for (int y = minCell.y; y <= maxCell.y; y++)
        {
            for (int x = minCell.x; x <= maxCell.x; x++)
            {
                int cell = RougeMortonGridUtility.EncodeMorton(x, y);
                for (int enemyIndex = headPtr[cell]; enemyIndex >= 0; enemyIndex = nextPtr[enemyIndex])
                {
                    if (enemyStatePtr[enemyIndex].x <= 0f) continue;
                    float4 enemyPosition = enemyPositionPtr[enemyIndex];
                    float distanceSq = math.lengthsq(enemyPosition.xz - request.Position);
                    byte enemyKind = EnemyKinds[enemyIndex];
                    bool bossCandidate = bossFirst && (enemyKind & 0x80) != 0;
                    float candidateRange = request.Range + (bossCandidate
                        ? math.max(0f, enemyPosition.w)
                        : 0f);
                    if (distanceSq > candidateRange * candidateRange) continue;
                    int candidatePriority = bossFirst ? GetTargetPriority(enemyKind) : 0;
                    float candidateSortDistance = distanceSq;
                    if (!bossFirst)
                    {
                        // EncodeMortonFromWorld floors X/Z to one integer cell, so this is
                        // just one lookup in the shared global flow field (no tower pathfinding).
                        int flowCell = RougeMortonGridUtility.EncodeMortonFromWorld(
                            enemyPositionPtr[enemyIndex].xz, GridOrigin, InvCellSize, GridDim);
                        candidateSortDistance = flowDistancePtr[flowCell];
                    }
                    int insertion = math.min(found, resultCapacity);
                    int sortedCount = math.min(found, resultCapacity);
                    for (int sorted = 0; sorted < sortedCount; sorted++)
                    {
                        int existingIndex = resultPtr[sorted];
                        int existingPriority = bossFirst && existingIndex >= 0
                            ? GetTargetPriority(EnemyKinds[existingIndex])
                            : 0;
                        bool equalFlowCloserToTower = !bossFirst &&
                            candidateSortDistance == distancePtr[sorted] && existingIndex >= 0 &&
                            distanceSq < math.lengthsq(enemyPositionPtr[existingIndex].xz - request.Position);
                        if (candidatePriority > existingPriority ||
                            (candidatePriority == existingPriority && candidateSortDistance < distancePtr[sorted]) ||
                            equalFlowCloserToTower)
                        {
                            insertion = sorted;
                            break;
                        }
                    }
                    if (insertion >= resultCapacity) continue;

                    int moveEnd = math.min(found, resultCapacity - 1);
                    for (int move = moveEnd; move > insertion; move--)
                    {
                        resultPtr[move] = resultPtr[move - 1];
                        distancePtr[move] = distancePtr[move - 1];
                    }
                    resultPtr[insertion] = enemyIndex;
                    distancePtr[insertion] = candidateSortDistance;
                    if (found < resultCapacity) found++;
                }
            }
        }
    }

    private static int GetTargetPriority(byte kind)
    {
        if ((kind & 0x80) != 0) return 2;
        return (kind & 0x40) != 0 ? 1 : 0;
    }
}

[BurstCompile(FloatMode = FloatMode.Fast, FloatPrecision = FloatPrecision.Standard)]
public unsafe struct ClearFlowFieldGridJob : IJobParallelForBatch
{
    [NativeDisableParallelForRestriction] public NativeArray<int> DensityFieldFixed;
    [NativeDisableParallelForRestriction] public NativeArray<byte> BlockedCells;

    public void Execute(int startIndex, int count)
    {
        UnsafeUtility.MemClear((int*)DensityFieldFixed.GetUnsafePtr() + startIndex, count * sizeof(int));
        UnsafeUtility.MemClear((byte*)BlockedCells.GetUnsafePtr() + startIndex, count * sizeof(byte));
    }
}

// Per-frame: 把启动期一次性烘焙的静态阻挡 mask memcpy 到工作 buffer，省去每帧重新栅格化整个静态场景
[BurstCompile(FloatMode = FloatMode.Fast, FloatPrecision = FloatPrecision.Standard)]
public unsafe struct CopyStaticBlockedMaskJob : IJobParallelForBatch
{
    [ReadOnly] public NativeArray<byte> StaticBlockedCells;
    [NativeDisableParallelForRestriction] public NativeArray<byte> BlockedCells;
    [NativeDisableParallelForRestriction] public NativeArray<int> DensityFieldFixed;

    public void Execute(int startIndex, int count)
    {
        UnsafeUtility.MemClear((int*)DensityFieldFixed.GetUnsafePtr() + startIndex, count * sizeof(int));
        UnsafeUtility.MemCpy(
            (byte*)BlockedCells.GetUnsafePtr() + startIndex,
            (byte*)StaticBlockedCells.GetUnsafeReadOnlyPtr() + startIndex,
            count * sizeof(byte));
    }
}

[BurstCompile(FloatMode = FloatMode.Fast, FloatPrecision = FloatPrecision.Standard)]
public unsafe struct ClearBulletGridHeadsJob : IJobParallelForBatch
{
    [NativeDisableParallelForRestriction] public NativeArray<int> CellHeads;

    public void Execute(int startIndex, int count)
    {
        UnsafeUtility.MemSet((int*)CellHeads.GetUnsafePtr() + startIndex, 0xFF, count * sizeof(int));
    }
}

[BurstCompile(FloatMode = FloatMode.Fast, FloatPrecision = FloatPrecision.Standard)]
public unsafe struct RasterizeObstacleGridJob : IJob
{
    [ReadOnly] public NativeArray<RougeObstacle> Obstacles;
    [NativeDisableParallelForRestriction] public NativeArray<byte> BlockedCells;
    public int StartIndex;
    public int ObstacleCount;
    public float2 GridOrigin;
    public float InvCellSize;
    public int GridDim;
    public float ExtraPadding;

    public void Execute()
    {
        RougeObstacle* obstaclePtr = (RougeObstacle*)Obstacles.GetUnsafeReadOnlyPtr();
        byte* blockedPtr = (byte*)BlockedCells.GetUnsafePtr();
        float cellSize = 1f / math.max(InvCellSize, 0.0001f);

        int endIndex = StartIndex + ObstacleCount;
        for (int obstacleIndex = StartIndex; obstacleIndex < endIndex; obstacleIndex++)
        {
            RougeObstacle obstacle = obstaclePtr[obstacleIndex];
            float navPadding = math.max(ExtraPadding, 0f);
            if (obstacle.Type == RougeObstacle.CircleType)
            {
                float paddedRadius = obstacle.CircleRadius + navPadding;
                float2 min = obstacle.Center - paddedRadius;
                float2 max = obstacle.Center + paddedRadius;
                int2 minCell = RougeMortonGridUtility.WorldToGrid(min, GridOrigin, InvCellSize, GridDim);
                int2 maxCell = RougeMortonGridUtility.WorldToGrid(max, GridOrigin, InvCellSize, GridDim);
                float radiusSq = paddedRadius * paddedRadius;

                for (int y = minCell.y; y <= maxCell.y; y++)
                {
                    for (int x = minCell.x; x <= maxCell.x; x++)
                    {
                        float2 cellCenter = GridOrigin + (new float2(x + 0.5f, y + 0.5f) * cellSize);
                        if (math.lengthsq(cellCenter - obstacle.Center) <= radiusSq)
                        {
                            blockedPtr[RougeMortonGridUtility.EncodeMorton(x, y)] = 1;
                        }
                    }
                }

                continue;
            }

            float2 minPadded = obstacle.Min - new float2(navPadding);
            float2 maxPadded = obstacle.Max + new float2(navPadding);
            int2 minCellAabb = RougeMortonGridUtility.WorldToGrid(minPadded, GridOrigin, InvCellSize, GridDim);
            int2 maxCellAabb = RougeMortonGridUtility.WorldToGrid(maxPadded, GridOrigin, InvCellSize, GridDim);

            for (int y = minCellAabb.y; y <= maxCellAabb.y; y++)
            {
                for (int x = minCellAabb.x; x <= maxCellAabb.x; x++)
                {
                    float2 cellCenter = GridOrigin + (new float2(x + 0.5f, y + 0.5f) * cellSize);
                    if (RougeObstacleMath.ContainsPoint(obstacle, cellCenter, navPadding))
                    {
                        blockedPtr[RougeMortonGridUtility.EncodeMorton(x, y)] = 1;
                    }
                }
            }
        }
    }
}

[BurstCompile(FloatMode = FloatMode.Fast, FloatPrecision = FloatPrecision.Standard)]
public unsafe struct BuildEnemyDensityFieldJob : IJobParallelForBatch
{
    [ReadOnly] public NativeArray<float4> PositionScaleIn;
    [ReadOnly] public NativeArray<float4> StateIn;
    [NativeDisableParallelForRestriction] public NativeArray<int> DensityFieldFixed;
    public float2 GridOrigin;
    public float InvCellSize;
    public int GridDim;
    public float RenderHeight;

    public void Execute(int startIndex, int count)
    {
        float4* posPtr = (float4*)PositionScaleIn.GetUnsafeReadOnlyPtr();
        float4* statePtr = (float4*)StateIn.GetUnsafeReadOnlyPtr();
        int* densityPtr = (int*)DensityFieldFixed.GetUnsafePtr();
        int end = startIndex + count;

        for (int i = startIndex; i < end; i++)
        {
            float4 state = statePtr[i];
            if (state.x <= 0f)
            {
                continue;
            }

            float4 pos4 = posPtr[i];
            if (pos4.y > RenderHeight + 0.5f)
            {
                continue;
            }

            float2 gridPos = (pos4.xz - GridOrigin) * InvCellSize - 0.5f;
            int2 baseCell = (int2)math.floor(gridPos);
            float2 frac = math.saturate(gridPos - baseCell);
            int x0 = RougeMortonGridUtility.ClampCoord(baseCell.x, GridDim);
            int x1 = RougeMortonGridUtility.ClampCoord(baseCell.x + 1, GridDim);
            int y0 = RougeMortonGridUtility.ClampCoord(baseCell.y, GridDim);
            int y1 = RougeMortonGridUtility.ClampCoord(baseCell.y + 1, GridDim);

            int w00 = math.max(1, (int)math.round((1f - frac.x) * (1f - frac.y) * RougeMortonGridUtility.DensityFixedScale));
            int w10 = math.max(1, (int)math.round(frac.x * (1f - frac.y) * RougeMortonGridUtility.DensityFixedScale));
            int w01 = math.max(1, (int)math.round((1f - frac.x) * frac.y * RougeMortonGridUtility.DensityFixedScale));
            int w11 = math.max(1, RougeMortonGridUtility.DensityFixedScale - w00 - w10 - w01);

            System.Threading.Interlocked.Add(ref densityPtr[RougeMortonGridUtility.EncodeMorton(x0, y0)], w00);
            System.Threading.Interlocked.Add(ref densityPtr[RougeMortonGridUtility.EncodeMorton(x1, y0)], w10);
            System.Threading.Interlocked.Add(ref densityPtr[RougeMortonGridUtility.EncodeMorton(x0, y1)], w01);
            System.Threading.Interlocked.Add(ref densityPtr[RougeMortonGridUtility.EncodeMorton(x1, y1)], w11);
        }
    }
}

[BurstCompile(FloatMode = FloatMode.Fast, FloatPrecision = FloatPrecision.Standard)]
public unsafe struct SmoothDensityFieldJob : IJobParallelForBatch
{
    [ReadOnly] public NativeArray<int> DensityIn;
    [ReadOnly] public NativeArray<byte> BlockedCells;
    [NativeDisableParallelForRestriction] public NativeArray<int> DensityOut;
    public int GridDim;

    public void Execute(int startIndex, int count)
    {
        int* sourcePtr = (int*)DensityIn.GetUnsafeReadOnlyPtr();
        byte* blockedPtr = (byte*)BlockedCells.GetUnsafeReadOnlyPtr();
        int* outputPtr = (int*)DensityOut.GetUnsafePtr();
        int end = math.min(startIndex + count, DensityOut.Length);

        for (int index = startIndex; index < end; index++)
        {
            if (blockedPtr[index] != 0)
            {
                outputPtr[index] = 0;
                continue;
            }

            int2 cell = RougeMortonGridUtility.DecodeMorton(index);
            long weightedDensity = 0;
            int totalWeight = 0;
            for (int offsetY = -1; offsetY <= 1; offsetY++)
            {
                int sampleY = cell.y + offsetY;
                if (sampleY < 0 || sampleY >= GridDim) continue;
                int weightY = offsetY == 0 ? 2 : 1;
                for (int offsetX = -1; offsetX <= 1; offsetX++)
                {
                    int sampleX = cell.x + offsetX;
                    if (sampleX < 0 || sampleX >= GridDim) continue;
                    int sampleIndex = RougeMortonGridUtility.EncodeMorton(sampleX, sampleY);
                    if (blockedPtr[sampleIndex] != 0) continue;
                    int weight = weightY * (offsetX == 0 ? 2 : 1);
                    weightedDensity += (long)sourcePtr[sampleIndex] * weight;
                    totalWeight += weight;
                }
            }

            long smoothedDensity = totalWeight > 0 ? weightedDensity / totalWeight : 0;
            outputPtr[index] = (int)math.min(smoothedDensity, (long)int.MaxValue);
        }
    }
}

[BurstCompile(FloatMode = FloatMode.Fast, FloatPrecision = FloatPrecision.Standard)]
public unsafe struct InitializeFlowFieldJob : IJobParallelForBatch
{
    [ReadOnly] public NativeArray<byte> BlockedCells;
    [NativeDisableParallelForRestriction] public NativeArray<float> FlowDistances;
    public int GridDim;

    public void Execute(int startIndex, int count)
    {
        byte* blockedPtr = (byte*)BlockedCells.GetUnsafeReadOnlyPtr();
        float* distancePtr = (float*)FlowDistances.GetUnsafePtr();
        int end = startIndex + count;
        int cellCount = GridDim * GridDim;

        for (int i = startIndex; i < end; i++)
        {
            if (i >= cellCount)
            {
                break;
            }

            distancePtr[i] = blockedPtr[i] != 0 ? 1e20f : 1e18f;
        }
    }
}

// 多目标支持：把所有目标 cell 一次性置 0；后续 RelaxFlowFieldJob 因为 min 操作天然不会再升回去，无需在 relax 中再单独判断
[BurstCompile(FloatMode = FloatMode.Fast, FloatPrecision = FloatPrecision.Standard)]
public unsafe struct SeedGoalCellsJob : IJob
{
    [ReadOnly] public NativeArray<int> GoalIndices;
    public int GoalCount;
    [NativeDisableParallelForRestriction] public NativeArray<float> FlowDistances;
    [NativeDisableParallelForRestriction] public NativeArray<byte> BlockedCells;

    public void Execute()
    {
        int* goalPtr = (int*)GoalIndices.GetUnsafeReadOnlyPtr();
        float* distancePtr = (float*)FlowDistances.GetUnsafePtr();
        byte* blockedPtr = (byte*)BlockedCells.GetUnsafePtr();
        int cellCount = FlowDistances.Length;

        for (int i = 0; i < GoalCount; i++)
        {
            int idx = goalPtr[i];
            if ((uint)idx >= (uint)cellCount) continue;
            // 目标 cell 必然可通行（ResolveFlowGoalIndex 已找最近未阻挡 cell）
            blockedPtr[idx] = 0;
            distancePtr[idx] = 0f;
        }
    }
}

[BurstCompile(FloatMode = FloatMode.Fast, FloatPrecision = FloatPrecision.Standard)]
public unsafe struct RelaxFlowFieldJob : IJobParallelForBatch
{
    [ReadOnly] public NativeArray<byte> BlockedCells;
    [ReadOnly] public NativeArray<float> FlowDistancesIn;
    [NativeDisableParallelForRestriction] public NativeArray<float> FlowDistancesOut;
    public int GridDim;
    public float CellSize;

    public void Execute(int startIndex, int count)
    {
        byte* blockedPtr = (byte*)BlockedCells.GetUnsafeReadOnlyPtr();
        float* srcPtr = (float*)FlowDistancesIn.GetUnsafeReadOnlyPtr();
        float* dstPtr = (float*)FlowDistancesOut.GetUnsafePtr();
        int end = startIndex + count;
        int cellCount = GridDim * GridDim;
        float diagonalStep = CellSize * 1.41421356f;

        for (int index = startIndex; index < end; index++)
        {
            if (index >= cellCount)
            {
                break;
            }

            if (blockedPtr[index] != 0)
            {
                dstPtr[index] = 1e20f;
                continue;
            }

            // 目标 cell 已被 SeedGoalCellsJob 置 0；min 操作保证它不会被抬升
            float currentDist = srcPtr[index];
            if (currentDist <= 0f)
            {
                dstPtr[index] = 0f;
                continue;
            }

            int2 cell = RougeMortonGridUtility.DecodeMorton(index);
            if (cell.x >= GridDim || cell.y >= GridDim)
            {
                dstPtr[index] = srcPtr[index];
                continue;
            }

            float best = srcPtr[index];
            for (int offsetY = -1; offsetY <= 1; offsetY++)
            {
                int neighborY = cell.y + offsetY;
                if (neighborY < 0 || neighborY >= GridDim)
                {
                    continue;
                }

                for (int offsetX = -1; offsetX <= 1; offsetX++)
                {
                    if (offsetX == 0 && offsetY == 0)
                    {
                        continue;
                    }

                    int neighborX = cell.x + offsetX;
                    if (neighborX < 0 || neighborX >= GridDim)
                    {
                        continue;
                    }

                    int neighborIndex = RougeMortonGridUtility.EncodeMorton(neighborX, neighborY);
                    if (blockedPtr[neighborIndex] != 0)
                    {
                        continue;
                    }

                    float stepCost = (offsetX == 0 || offsetY == 0) ? CellSize : diagonalStep;
                    best = math.min(best, srcPtr[neighborIndex] + stepCost);
                }
            }

            dstPtr[index] = best;
        }
    }
}

[BurstCompile(FloatMode = FloatMode.Fast, FloatPrecision = FloatPrecision.Standard)]
public unsafe struct SolveFlowFieldJob : IJob
{
    [NativeDisableParallelForRestriction] public NativeArray<byte> BlockedCells;
    [NativeDisableParallelForRestriction] public NativeArray<float> FlowDistances;
    [ReadOnly] public NativeArray<int> GoalIndices;
    public int GoalCount;
    public int GridDim;
    public float CellSize;
    public int IterationCount;

    public void Execute()
    {
        byte* blockedPtr = (byte*)BlockedCells.GetUnsafePtr();
        float* distancePtr = (float*)FlowDistances.GetUnsafePtr();
        int cellCount = GridDim * GridDim;
        for (int i = 0; i < cellCount; i++)
        {
            distancePtr[i] = blockedPtr[i] != 0 ? 1e20f : 1e18f;
        }

        int* goalPtr = (int*)GoalIndices.GetUnsafeReadOnlyPtr();
        int validGoalCount = math.min(math.max(GoalCount, 0), GoalIndices.Length);
        for (int i = 0; i < validGoalCount; i++)
        {
            int goalIndex = goalPtr[i];
            if ((uint)goalIndex >= (uint)cellCount) continue;
            blockedPtr[goalIndex] = 0;
            distancePtr[goalIndex] = 0f;
        }

        float diagonalStep = CellSize * 1.41421356f;
        int iterationBudget = math.max(IterationCount, 1);
        for (int iteration = 0; iteration < iterationBudget; iteration++)
        {
            RelaxSweep(0, GridDim, 1, 0, GridDim, 1, diagonalStep, blockedPtr, distancePtr);
            RelaxSweep(GridDim - 1, -1, -1, 0, GridDim, 1, diagonalStep, blockedPtr, distancePtr);
            RelaxSweep(0, GridDim, 1, GridDim - 1, -1, -1, diagonalStep, blockedPtr, distancePtr);
            RelaxSweep(GridDim - 1, -1, -1, GridDim - 1, -1, -1, diagonalStep, blockedPtr, distancePtr);
        }
    }

    private void RelaxSweep(int startX, int endX, int stepX, int startY, int endY, int stepY, float diagonalStep, byte* blockedPtr, float* distancePtr)
    {
        for (int y = startY; y != endY; y += stepY)
        {
            for (int x = startX; x != endX; x += stepX)
            {
                int index = RougeMortonGridUtility.EncodeMorton(x, y);
                if (blockedPtr[index] != 0 || distancePtr[index] == 0f)
                {
                    continue;
                }

                float best = distancePtr[index];
                for (int offsetY = -1; offsetY <= 1; offsetY++)
                {
                    int neighborY = y + offsetY;
                    if (neighborY < 0 || neighborY >= GridDim)
                    {
                        continue;
                    }

                    for (int offsetX = -1; offsetX <= 1; offsetX++)
                    {
                        if ((offsetX == 0 && offsetY == 0))
                        {
                            continue;
                        }

                        int neighborX = x + offsetX;
                        if (neighborX < 0 || neighborX >= GridDim)
                        {
                            continue;
                        }

                        int neighborIndex = RougeMortonGridUtility.EncodeMorton(neighborX, neighborY);
                        if (blockedPtr[neighborIndex] != 0)
                        {
                            continue;
                        }

                        if (offsetX != 0 && offsetY != 0)
                        {
                            int sideX = RougeMortonGridUtility.EncodeMorton(x + offsetX, y);
                            int sideY = RougeMortonGridUtility.EncodeMorton(x, y + offsetY);
                            if (blockedPtr[sideX] != 0 || blockedPtr[sideY] != 0) continue;
                        }

                        float stepCost = (offsetX == 0 || offsetY == 0) ? CellSize : diagonalStep;
                        best = math.min(best, distancePtr[neighborIndex] + stepCost);
                    }
                }

                distancePtr[index] = best;
            }
        }
    }
}

[BurstCompile(FloatMode = FloatMode.Fast, FloatPrecision = FloatPrecision.Standard)]
public unsafe struct BuildFlowFieldDirectionsJob : IJobParallelForBatch
{
    [ReadOnly] public NativeArray<byte> BlockedCells;
    [ReadOnly] public NativeArray<float> FlowDistances;
    [NativeDisableParallelForRestriction] public NativeArray<float2> FlowDirections;
    public int GridDim;

    public void Execute(int startIndex, int count)
    {
        byte* blockedPtr = (byte*)BlockedCells.GetUnsafeReadOnlyPtr();
        float* distancePtr = (float*)FlowDistances.GetUnsafeReadOnlyPtr();
        float2* directionPtr = (float2*)FlowDirections.GetUnsafePtr();
        int end = startIndex + count;

        for (int i = startIndex; i < end; i++)
        {
            int2 cell = RougeMortonGridUtility.DecodeMorton(i);
            if (cell.x >= GridDim || cell.y >= GridDim || blockedPtr[i] != 0)
            {
                directionPtr[i] = float2.zero;
                continue;
            }

            float bestDistance = distancePtr[i];
            int2 bestOffset = int2.zero;
            for (int offsetY = -1; offsetY <= 1; offsetY++)
            {
                int neighborY = cell.y + offsetY;
                if (neighborY < 0 || neighborY >= GridDim) continue;
                for (int offsetX = -1; offsetX <= 1; offsetX++)
                {
                    if (offsetX == 0 && offsetY == 0) continue;
                    int neighborX = cell.x + offsetX;
                    if (neighborX < 0 || neighborX >= GridDim) continue;
                    int neighborIndex = RougeMortonGridUtility.EncodeMorton(neighborX, neighborY);
                    if (blockedPtr[neighborIndex] != 0) continue;

                    // Match the solver's no-corner-cutting rule. The previous central
                    // gradient could point diagonally through a blocked corner even though
                    // the distance field itself correctly routed around it.
                    if (offsetX != 0 && offsetY != 0)
                    {
                        int sideX = RougeMortonGridUtility.EncodeMorton(cell.x + offsetX, cell.y);
                        int sideY = RougeMortonGridUtility.EncodeMorton(cell.x, cell.y + offsetY);
                        if (blockedPtr[sideX] != 0 || blockedPtr[sideY] != 0) continue;
                    }

                    float neighborDistance = distancePtr[neighborIndex];
                    if (neighborDistance + 0.0001f >= bestDistance) continue;
                    bestDistance = neighborDistance;
                    bestOffset = new int2(offsetX, offsetY);
                }
            }
            directionPtr[i] = math.normalizesafe(new float2(bestOffset.x, bestOffset.y), float2.zero);
        }
    }
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
public unsafe struct BuildBulletGridJob : IJob
{
    [ReadOnly] public NativeArray<RougeBullet> Bullets;
    [NativeDisableParallelForRestriction] public NativeArray<int> CellHeads;
    [NativeDisableParallelForRestriction] public NativeArray<int> CellEntries;
    [NativeDisableParallelForRestriction] public NativeArray<int> CellNext;
    public int BulletCount;
    public int EntryCapacity;
    public float2 GridOrigin;
    public float InvCellSize;
    public int GridDim;
    public float TargetRadiusPadding;

    public void Execute()
    {
        RougeBullet* bulletPtr = (RougeBullet*)Bullets.GetUnsafeReadOnlyPtr();
        int* headPtr = (int*)CellHeads.GetUnsafePtr();
        int* entryPtr = (int*)CellEntries.GetUnsafePtr();
        int* nextPtr = (int*)CellNext.GetUnsafePtr();
        float cellSize = 1f / math.max(InvCellSize, 0.0001f);
        float2 gridMax = GridOrigin + GridDim * cellSize;
        int entryIndex = 0;

        for (int bulletIndex = 0; bulletIndex < BulletCount; bulletIndex++)
        {
            RougeBullet bullet = bulletPtr[bulletIndex];
            float expandedRadius = bullet.Radius + TargetRadiusPadding;
            float2 min = math.min(bullet.Previous, bullet.Current) - expandedRadius;
            float2 max = math.max(bullet.Previous, bullet.Current) + expandedRadius;
            if (max.x < GridOrigin.x || max.y < GridOrigin.y || min.x > gridMax.x || min.y > gridMax.y)
            {
                continue;
            }

            int2 minCell = RougeMortonGridUtility.WorldToGrid(min, GridOrigin, InvCellSize, GridDim);
            int2 maxCell = RougeMortonGridUtility.WorldToGrid(max, GridOrigin, InvCellSize, GridDim);

            for (int cellY = minCell.y; cellY <= maxCell.y; cellY++)
            {
                for (int cellX = minCell.x; cellX <= maxCell.x; cellX++)
                {
                    if (entryIndex >= EntryCapacity)
                    {
                        return;
                    }

                    int hash = RougeMortonGridUtility.EncodeMorton(cellX, cellY);
                    entryPtr[entryIndex] = bulletIndex;
                    nextPtr[entryIndex] = headPtr[hash];
                    headPtr[hash] = entryIndex;
                    entryIndex++;
                }
            }
        }
    }
}

[BurstCompile(FloatMode = FloatMode.Fast, FloatPrecision = FloatPrecision.Standard)]
public unsafe struct BuildSkillAreaGridJob : IJob
{
    [ReadOnly] public NativeArray<RougeSkillArea> SkillAreas;
    [NativeDisableParallelForRestriction] public NativeArray<int> CellHeads;
    [NativeDisableParallelForRestriction] public NativeArray<int> CellEntries;
    [NativeDisableParallelForRestriction] public NativeArray<int> CellNext;
    public int SkillAreaCount;
    public int EntryCapacity;
    public float2 GridOrigin;
    public float InvCellSize;
    public int GridDim;

    public void Execute()
    {
        RougeSkillArea* skillPtr = (RougeSkillArea*)SkillAreas.GetUnsafeReadOnlyPtr();
        int* headPtr = (int*)CellHeads.GetUnsafePtr();
        int* entryPtr = (int*)CellEntries.GetUnsafePtr();
        int* nextPtr = (int*)CellNext.GetUnsafePtr();
        float cellSize = 1f / math.max(InvCellSize, 0.0001f);
        float2 gridMax = GridOrigin + GridDim * cellSize;
        int entryIndex = 0;

        for (int skillIndex = 0; skillIndex < SkillAreaCount; skillIndex++)
        {
            RougeSkillArea skill = skillPtr[skillIndex];
            ComputeSkillBounds(skill, out float2 min, out float2 max);
            if (max.x < GridOrigin.x || max.y < GridOrigin.y || min.x > gridMax.x || min.y > gridMax.y)
            {
                continue;
            }

            int2 minCell = RougeMortonGridUtility.WorldToGrid(min, GridOrigin, InvCellSize, GridDim);
            int2 maxCell = RougeMortonGridUtility.WorldToGrid(max, GridOrigin, InvCellSize, GridDim);
            for (int cellY = minCell.y; cellY <= maxCell.y; cellY++)
            {
                for (int cellX = minCell.x; cellX <= maxCell.x; cellX++)
                {
                    if (entryIndex >= EntryCapacity)
                    {
                        return;
                    }

                    int hash = RougeMortonGridUtility.EncodeMorton(cellX, cellY);
                    entryPtr[entryIndex] = skillIndex;
                    nextPtr[entryIndex] = headPtr[hash];
                    headPtr[hash] = entryIndex;
                    entryIndex++;
                }
            }
        }
    }

    private static void ComputeSkillBounds(RougeSkillArea skill, out float2 min, out float2 max)
    {
        float radius = math.max(skill.Radius, 0f);
        if (skill.Type == 3 || skill.Type == 15 || skill.Type == 16 || skill.Type == 19)
        {
            float2 end = skill.Position + skill.Direction * math.max(skill.Length, 0f);
            float2 extent = new float2(radius, radius);
            min = math.min(skill.Position, end) - extent;
            max = math.max(skill.Position, end) + extent;
            return;
        }

        // A focused flamethrower can touch a boss before its centre enters the
        // authored cone length. Length carries that boss-only broadphase padding;
        // the narrow phase still rejects ordinary enemies outside Radius.
        if (skill.Type == 22)
            radius += math.max(skill.Length, 0f);

        float2 circleExtent = new float2(radius, radius);
        min = skill.Position - circleExtent;
        max = skill.Position + circleExtent;
    }
}

[BurstCompile(FloatMode = FloatMode.Fast, FloatPrecision = FloatPrecision.Standard)]
public unsafe struct SimulateEnemiesFlowFieldJob : IJobParallelForBatch
{
    private const float VisualStateFlagStep = 10f;
    private const int CurseVisualFlag = 1;
    private const int DeadVisualFlag = 2;
    private const int BufferedLaunchVisualFlag = 4;
    private const int SlowVisualFlag = 8;
    private const int FacingLeftVisualFlag = 16;
    private const int FacingValidVisualFlag = 32;
    private const int FrozenVisualFlag = 64;
    private const int BurnVisualFlag = 128;
    private const float LaunchMotionDuration = 0.22f;
    private const float LaunchStackDuration = 0.12f;
    private const float LaunchPlanarImpulseFactor = 1.05f;
    private const float LaunchPlanarImpulseWithKnockbackFactor = 0.35f;
    private const float LaunchMaxVerticalSpeedMultiplier = 1.85f;
    private const float PoisonDurationSeconds = 2f;
    private const float PoisonTickInterval = 0.5f;
    private const float PoisonTickMaxHealthRatio = 0.1f;
    private const float BurnTickInterval = 0.5f;
    private const float BurnGroundRadius = 5f;
    private const float DeadFlashDecayRate = 2f;
    private const float BurnPatchReapplyCooldown = 0.85f;
    private const float BurnPatchDurationMultiplier = 0.45f;
    private const float BurnPatchDamageMultiplier = 0.55f;
    private const float TowerKillLaunchGoalExclusionRadius = 15f;
    private const float TowerKillLaunchVerticalImpulse = 12f;
    private const float TowerKillLaunchPlanarImpulseFactor = 1.05f;
    private const float TowerTileExplosionChance = 0.03f;
    private const float TowerTileExplosionMaxHealthRatio = 0.30f;
    private const float TowerTileExplosionRadius = 8f;

    [ReadOnly] public NativeArray<float4> PositionScaleIn;
    [ReadOnly] public NativeArray<float4> VelocityIn;
    [ReadOnly] public NativeArray<float4> StateIn;
    [ReadOnly] public NativeArray<RougeEnemyEffectState> EffectStateIn;
    [ReadOnly] public NativeArray<int> DensityFieldFixed;
    [ReadOnly] public NativeArray<float2> FlowDirections;
    [ReadOnly] public NativeArray<float> FlowDistances;
    [ReadOnly] public NativeArray<RougeBullet> Bullets;
    [ReadOnly] public NativeArray<int> BulletCellHeads;
    [ReadOnly] public NativeArray<int> BulletCellEntries;
    [ReadOnly] public NativeArray<int> BulletCellNext;
    [ReadOnly] public NativeArray<int> SkillCellHeads;
    [ReadOnly] public NativeArray<int> SkillCellEntries;
    [ReadOnly] public NativeArray<int> SkillCellNext;
    [ReadOnly] public NativeArray<RougeObstacle> Obstacles;
    [NativeDisableParallelForRestriction] public NativeArray<int> PlayerDamageCount;
    [NativeDisableParallelForRestriction] public NativeArray<int> MainTowerDamageCount;
    [NativeDisableParallelForRestriction] public NativeArray<int> BossReachedGoalCount;
    [NativeDisableParallelForRestriction] public NativeArray<int> EnemyKillCount;
    [ReadOnly] public NativeArray<byte> EnemyKinds;
    [NativeDisableParallelForRestriction] public NativeArray<int> TowerDefenseGoldEarned;
    [NativeDisableParallelForRestriction] public NativeArray<int> TowerDefenseWealthGoldEarned;
    [ReadOnly] public NativeArray<float> TowerLaserDamage;
    [ReadOnly] public NativeArray<int> TowerLaserDamageFrames;
    [ReadOnly] public NativeArray<int> TowerKillGoldBonus;
    [ReadOnly] public NativeArray<int> TowerWealthCellIndexPlusOne;
    [ReadOnly] public NativeArray<int> TowerKillTileEffects;
    [ReadOnly] public NativeArray<float> TowerDamageByType;
    [ReadOnly] public NativeArray<int> TowerDamageByTypeFrames;
    [NativeDisableParallelForRestriction] public NativeArray<long> TowerDamageTotalsFixed;
    [NativeDisableParallelForRestriction] public NativeArray<float4> PositionScaleOut;
    [NativeDisableParallelForRestriction] public NativeArray<float4> VelocityOut;
    [NativeDisableParallelForRestriction] public NativeArray<float4> StateOut;
    [NativeDisableParallelForRestriction] public NativeArray<RougeEnemyEffectState> EffectStateOut;

    public int BulletCount;
    public int ObstacleCount;
    public float2 PlayerPos;
    public float2 GoalPos;
    public float2 SpawnCenter;
    public float EnemyMaxHealth;
    public float EnemyArmor;
    public float EnemyRadius;
    public float EnemyMaxSpeed;
    public float2 ArenaHalfExtents;
    public float SpawnRadiusMin;
    public float SpawnRadiusMax;
    public float DespawnDistanceSq;
    public float ChaseAcceleration;
    public float VelocityDamping;
    public float SeparationRadius;
    public float SeparationStrength;
    public float CrowdReliefRadius;
    public float CrowdReliefStrength;
    public float CrowdOrbitStrength;
    public float DenseSeparationBoost;
    public int DenseNeighborThreshold;
    public float ObstacleLookAhead;
    public float ObstacleRepulsion;
    public float ObstacleOrbitStrength;
    public float KnockbackResist;
    public bool PlayerContactEnabled;
    public bool DefeatEnemyOnPlayerContact;
    public float PlayerContactPadding;
    public bool MainTowerContactEnabled;
    public float MainTowerContactRadius;
    public bool ExternalSpawning;
    [WriteOnly] public NativeQueue<float2>.ParallelWriter ExplosionQueue;
    [WriteOnly] public NativeQueue<RougeSkillEvent>.ParallelWriter SkillEventQueue;
    public int CurrentMaxEnemies;
    public int TowerLaserDamageFrame;
    public bool BossShieldActive;
    public float2 BossShieldPosition;
    public float BossShieldRadius;
    public float BossShieldDamageMultiplier;
    public float BossShieldMinimumDamage;
    public int BossEnemyIndex;
    public float BossNavigationRadius;
    public float BossMaximumSlowPercent;
    public bool TowerDefenseRewardsEnabled;
    [ReadOnly] public NativeArray<RougeSkillArea> SkillAreas;
    public int SkillAreaCount;
    public float RenderHeight;
    public float DeltaTime;
    public float2 GridOrigin;
    public float GridCellSize;
    public float GridInvCellSize;
    public int GridDim;
    public float DensitySoftThreshold;
    public float DensityRepulsionStrength;
    public float DensityGradientClamp;
    public float DensityResponseJitter;
    public float CrowdReliefMaxDensityPressure;
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
        float4* posInPtr = (float4*)PositionScaleIn.GetUnsafeReadOnlyPtr();
        float4* velInPtr = (float4*)VelocityIn.GetUnsafeReadOnlyPtr();
        float4* stateInPtr = (float4*)StateIn.GetUnsafeReadOnlyPtr();
        RougeEnemyEffectState* effectInPtr = (RougeEnemyEffectState*)EffectStateIn.GetUnsafeReadOnlyPtr();
        int* densityPtr = (int*)DensityFieldFixed.GetUnsafeReadOnlyPtr();
        float2* flowPtr = (float2*)FlowDirections.GetUnsafeReadOnlyPtr();
        float* flowDistancePtr = (float*)FlowDistances.GetUnsafeReadOnlyPtr();
        RougeBullet* bulletPtr = (RougeBullet*)Bullets.GetUnsafeReadOnlyPtr();
        int* bulletHeadPtr = (int*)BulletCellHeads.GetUnsafeReadOnlyPtr();
        int* bulletEntryPtr = (int*)BulletCellEntries.GetUnsafeReadOnlyPtr();
        int* bulletNextPtr = (int*)BulletCellNext.GetUnsafeReadOnlyPtr();
        int* skillHeadPtr = (int*)SkillCellHeads.GetUnsafeReadOnlyPtr();
        int* skillEntryPtr = (int*)SkillCellEntries.GetUnsafeReadOnlyPtr();
        int* skillNextPtr = (int*)SkillCellNext.GetUnsafeReadOnlyPtr();
        RougeObstacle* obstaclePtr = (RougeObstacle*)Obstacles.GetUnsafeReadOnlyPtr();
        float4* posOutPtr = (float4*)PositionScaleOut.GetUnsafePtr();
        float4* velOutPtr = (float4*)VelocityOut.GetUnsafePtr();
        float4* stateOutPtr = (float4*)StateOut.GetUnsafePtr();
        RougeEnemyEffectState* effectOutPtr = (RougeEnemyEffectState*)EffectStateOut.GetUnsafePtr();

        int endIndex = startIndex + count;
        bool hasBulletBounds = BulletCount > 0 && BulletMin.x <= BulletMax.x && BulletMin.y <= BulletMax.y;

        for (int i = startIndex; i < endIndex; i++)
        {
            int sourceIndex = i;
            if (sourceIndex >= CurrentMaxEnemies)
            {
                posOutPtr[sourceIndex] = new float4(99999f, -9999f, 99999f, 0f);
                velOutPtr[sourceIndex] = float4.zero;
                stateOutPtr[sourceIndex] = new float4(-1f, 0f, 0f, 0f);
                effectOutPtr[sourceIndex] = default;
                continue;
            }

            float4 pos4 = posInPtr[sourceIndex];
            float4 vel4 = velInPtr[sourceIndex];
            float4 state4 = stateInPtr[sourceIndex];
            RougeEnemyEffectState effects = effectInPtr[sourceIndex];
            int towerKillGoldBonus = math.max(0, effects.TowerKillGoldBonus);
            int towerWealthCellIndexPlusOne = math.max(0, effects.TowerWealthCellIndexPlusOne);
            int towerSourceTileEffect = effects.TowerSourceTileEffect;

            float3 pos = pos4.xyz;
            float3 vel = vel4.xyz;
            float tornadoMark = vel4.w;
            float health = state4.x;
            effects.MaximumHealth = math.max(effects.MaximumHealth, math.max(health, 1f));
            if (!ExternalSpawning)
            {
                effects.Armor = math.max(effects.Armor, EnemyArmor);
            }
            float radius = state4.y;
            float crowdRadius = math.max(radius, pos4.w);
            float maxSpeed = state4.z;
            byte enemyKind = EnemyKinds[sourceIndex];
            bool isBoss = sourceIndex == BossEnemyIndex;
            if (isBoss)
            {
                // A boss never carries displacement/airborne state across frames.
                pos.y = RenderHeight;
                vel.y = 0f;
                tornadoMark = 0f;
                effects.LaunchMotionTimer = 0f;
                effects.LaunchStackTimer = 0f;
                effects.LaunchLandingRadius = 0f;
                effects.LaunchLandingDamage = 0f;
                effects.VulnerabilityLandingBlastPending = 0;
                effects.SlowPercent = math.clamp(effects.SlowPercent, 0f, BossMaximumSlowPercent);
            }
            float flashTimer = math.frac(math.max(state4.w, 0f));
            int visualFlags = DecodeVisualFlags(state4.w);
            bool isDeadVisual = (visualFlags & DeadVisualFlag) != 0;
            bool isBufferedLaunchVisual = (visualFlags & BufferedLaunchVisualFlag) != 0;
            bool bufferedLaunchDeath = isBufferedLaunchVisual;
            bool launchKillPending = tornadoMark > 2.5f || pos.y > RenderHeight + 0.45f || effects.LaunchLandingRadius > 0f || effects.LaunchLandingDamage > 0f;

            if (launchKillPending && health <= 0f)
            {
                bufferedLaunchDeath = true;
                health = 1f;
            }

            // Tower-defense spawn points are allowed anywhere inside the arena. The old
            // survival-mode despawn rule used the goal as its centre, which immediately
            // teleported enemies spawned by distant scene spawn points.
            if (!ExternalSpawning && math.lengthsq(pos.xz - GoalPos) > DespawnDistanceSq)
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

                if (isDeadVisual && flashTimer <= 0f && !ExternalSpawning)
                {
                    Respawn(sourceIndex, ref pos4, ref vel4, ref state4, ref effects);
                    posOutPtr[sourceIndex] = pos4;
                    velOutPtr[sourceIndex] = vel4;
                    stateOutPtr[sourceIndex] = state4;
                    effectOutPtr[sourceIndex] = effects;
                    continue;
                }

                if (isDeadVisual && flashTimer <= 0f && ExternalSpawning)
                {
                    posOutPtr[sourceIndex] = new float4(99999f, -9999f, 99999f, 0f);
                    velOutPtr[sourceIndex] = float4.zero;
                    stateOutPtr[sourceIndex] = new float4(-1f, 0f, 0f, DeadVisualFlag * VisualStateFlagStep);
                    effectOutPtr[sourceIndex] = default;
                    continue;
                }

                pos.y = math.max(pos.y, RenderHeight);
                vel = float3.zero;
                posOutPtr[sourceIndex] = new float4(pos, crowdRadius);
                velOutPtr[sourceIndex] = new float4(vel, 0f);
                stateOutPtr[sourceIndex] = new float4(
                    health,
                    radius,
                    maxSpeed,
                    EncodeVisualState(flashTimer, false, true, false, false, false, false,
                        effects.FacingDirection < 0f));
                effectOutPtr[sourceIndex] = effects;
                continue;
            }

            float healthBeforeShieldedDamage = health;
            float* towerDamageByType = stackalloc float[TowerDefenseVisuals.TowerTypeCount];
            for (int towerType = 0; towerType < TowerDefenseVisuals.TowerTypeCount; towerType++)
            {
                int damageEntry = sourceIndex * TowerDefenseVisuals.TowerTypeCount + towerType;
                towerDamageByType[towerType] = TowerDamageByTypeFrames[damageEntry] == TowerLaserDamageFrame
                    ? TowerDamageByType[damageEntry]
                    : 0f;
            }
            if (TowerLaserDamageFrames[sourceIndex] == TowerLaserDamageFrame)
            {
                float healthBeforeTowerDamage = health;
                // Direct tower hits are already armor-resolved per individual hit on
                // the managed side. Keeping them separated is essential for rapid-fire
                // lasers because the minimum-one rule belongs to each attack tick.
                health -= math.max(0f, TowerLaserDamage[sourceIndex]);
                flashTimer = math.max(flashTimer, 0.2f);
                if (healthBeforeTowerDamage > 0f && health <= 0f)
                {
                    towerKillGoldBonus = math.max(towerKillGoldBonus, TowerKillGoldBonus[sourceIndex]);
                    towerWealthCellIndexPlusOne = math.max(0,
                        TowerWealthCellIndexPlusOne[sourceIndex]);
                    towerSourceTileEffect = TowerKillTileEffects[sourceIndex];
                }
            }

            if (effects.LaunchMotionTimer > 0f)
            {
                effects.LaunchMotionTimer = math.max(0f, effects.LaunchMotionTimer - DeltaTime);
            }

            if (effects.LaunchStackTimer > 0f)
            {
                effects.LaunchStackTimer = math.max(0f, effects.LaunchStackTimer - DeltaTime);
            }

            if (effects.NavigationReverseCooldown > 0f)
            {
                effects.NavigationReverseCooldown = math.max(0f,
                    effects.NavigationReverseCooldown - DeltaTime);
            }

            if (effects.FreezeTimer > 0f)
            {
                effects.FreezeTimer = math.max(0f, effects.FreezeTimer - DeltaTime);
            }

            if (effects.BossFreezeImmunityTimer > 0f)
                effects.BossFreezeImmunityTimer = math.max(0f,
                    effects.BossFreezeImmunityTimer - DeltaTime);

            if (effects.VulnerabilityTimer > 0f)
                effects.VulnerabilityTimer = math.max(0f,
                    effects.VulnerabilityTimer - DeltaTime);
            if (effects.VulnerabilityDamageBonusTimer > 0f)
            {
                effects.VulnerabilityDamageBonusTimer = math.max(0f,
                    effects.VulnerabilityDamageBonusTimer - DeltaTime);
                if (effects.VulnerabilityDamageBonusTimer <= 0f)
                    effects.VulnerabilityDamageBonus = 0f;
            }
            else effects.VulnerabilityDamageBonus = 0f;
            if (effects.VulnerabilityArmorPenetrationTimer > 0f)
            {
                effects.VulnerabilityArmorPenetrationTimer = math.max(0f,
                    effects.VulnerabilityArmorPenetrationTimer - DeltaTime);
                if (effects.VulnerabilityArmorPenetrationTimer <= 0f)
                    effects.VulnerabilityArmorPenetration = 0f;
            }
            else effects.VulnerabilityArmorPenetration = 0f;
            if (effects.VulnerabilityLandingBlastTimer > 0f)
            {
                effects.VulnerabilityLandingBlastTimer = math.max(0f,
                    effects.VulnerabilityLandingBlastTimer - DeltaTime);
                if (effects.VulnerabilityLandingBlastTimer <= 0f)
                    effects.VulnerabilityLandingBlast = 0;
            }
            else effects.VulnerabilityLandingBlast = 0;

            if (effects.SlowTimer > 0f)
            {
                effects.SlowTimer = math.max(0f, effects.SlowTimer - DeltaTime);
                if (effects.SlowTimer <= 0f)
                {
                    effects.SlowPercent = 0f;
                    effects.SlowStacks = 0f;
                }
            }
            else
            {
                effects.SlowPercent = 0f;
                effects.SlowStacks = 0f;
            }
            float2 toPlayer = PlayerPos - pos.xz;
            float distToPlayerSq = math.lengthsq(toPlayer);
            float2 toGoal = GoalPos - pos.xz;
            float distToGoalSq = math.lengthsq(toGoal);
            float2 directToPlayer = distToGoalSq > 0.0001f ? toGoal * math.rsqrt(distToGoalSq) : new float2(0f, 1f);
            bool isAirborne = tornadoMark > 2.5f
                || vel.y > 0.05f
                || pos.y > RenderHeight + 0.05f
                || effects.LaunchMotionTimer > 0f
                || effects.LaunchStackTimer > 0f
                || effects.LaunchLandingRadius > 0f
                || effects.LaunchLandingDamage > 0f
                || bufferedLaunchDeath;
            if (isAirborne && effects.VulnerabilityTimer > 0f &&
                effects.VulnerabilityLandingBlast != 0)
                effects.VulnerabilityLandingBlastPending = 1;
            float3 acceleration = isAirborne ? new float3(0f, -30f, 0f) : float3.zero;
            if (!isAirborne)
            {
                float2 desired = SampleFlowDirection(pos.xz, flowPtr, flowDistancePtr);
                if (math.lengthsq(desired) < 0.0001f)
                {
                    desired = directToPlayer;
                }

                float snapRadius = math.max(GridCellSize * 1.35f, radius * 4f + 0.35f);
                float snapWeight = distToGoalSq > 0.0001f
                    ? 1f - math.saturate(math.sqrt(distToGoalSq) / math.max(snapRadius, 0.001f))
                    : 1f;
                if (snapWeight > 0f)
                {
                    desired = math.normalizesafe(math.lerp(desired, directToPlayer, snapWeight), directToPlayer);
                }

                desired = StabilizeNavigationDirection(ref effects, desired, sourceIndex);
                SteerVelocityTowards(ref vel, desired);

                acceleration.xz += desired * ChaseAcceleration;

                float unitVariation = Hash01((uint)sourceIndex + 1u);
                float signedVariation = unitVariation * 2f - 1f;
                float densityThreshold = math.max(0f, DensitySoftThreshold + signedVariation * DensityResponseJitter);
                float densityResponseScale = math.max(0.35f, 1f + signedVariation * (DensityResponseJitter * 0.75f));
                float density = SampleDensity(pos.xz, densityPtr);
                float densityExcess = math.max(0f, density - densityThreshold);
                // Preserve differences between moderately and extremely crowded cells instead
                // of hard-clamping every excess above one to the same pressure.
                float densityPressure = densityExcess / (densityExcess + 1f);
                densityPressure *= math.lerp(1f, 0.15f, snapWeight);
                if (!isBoss && densityPressure > 0f)
                {
                    float2 densityAvoidance = float2.zero;
                    float2 densityGradient = SampleDensityGradient(pos.xz, densityPtr);
                    float gradientLengthSq = math.lengthsq(densityGradient);
                    if (gradientLengthSq > 0.000001f)
                    {
                        float gradientLength = math.sqrt(gradientLengthSq);
                        float2 gradientDir = densityGradient / gradientLength;
                        densityAvoidance += -gradientDir * (math.min(gradientLength, DensityGradientClamp) *
                            DensityRepulsionStrength * densityPressure * densityResponseScale);
                    }

                    // A density-only gradient is nearly zero inside a uniformly packed plateau,
                    // which lets enemies remain stacked until they reach its edge. Give each
                    // enemy a stable, balanced micro direction there to break the overlap without
                    // doing any per-neighbor search. The force fades out as a real gradient appears.
                    float plateauGradientLength = math.sqrt(gradientLengthSq);
                    float plateauWeight = 1f - math.saturate(plateauGradientLength /
                        math.max(DensityGradientClamp * 0.35f, 0.001f));
                    if (plateauWeight > 0f && DensityResponseJitter > 0f)
                    {
                        float microAngle = Hash01(((uint)sourceIndex + 1u) * 747796405u + 2891336453u) *
                            (math.PI * 2f);
                        float2 microDirection = new float2(math.cos(microAngle), math.sin(microAngle));
                        densityAvoidance += microDirection * (DensityRepulsionStrength *
                            DensityResponseJitter * densityPressure * plateauWeight);
                    }

                    // Crowd relief may fan units around the route, but it must not overpower
                    // the route itself and create a second stream along the arena boundary.
                    float maxDensityAcceleration = math.max(0f,
                        ChaseAcceleration * CrowdReliefMaxDensityPressure);
                    float densityAvoidanceLengthSq = math.lengthsq(densityAvoidance);
                    if (densityAvoidanceLengthSq > maxDensityAcceleration * maxDensityAcceleration &&
                        densityAvoidanceLengthSq > 0.000001f)
                    {
                        densityAvoidance *= maxDensityAcceleration *
                            math.rsqrt(densityAvoidanceLengthSq);
                    }
                    acceleration.xz += densityAvoidance;
                }
            }

            float3 bossUnaffectedAcceleration = acceleration;
            float3 bossUnaffectedVelocity = vel;
            bool towerAreaRepelled = false;

            if (SkillAreaCount > 0)
            {
                int skillCell = RougeMortonGridUtility.EncodeMortonFromWorld(pos.xz, GridOrigin, GridInvCellSize, GridDim);
                for (int entryIndex = skillHeadPtr[skillCell]; entryIndex >= 0; entryIndex = skillNextPtr[entryIndex])
                {
                    int skillIndex = skillEntryPtr[entryIndex];
                    RougeSkillArea skill = SkillAreas[skillIndex];
                    ResolveEnemySpecificStatus(ref skill, enemyKind);
                    float2 skillDelta = pos.xz - skill.Position;
                    float skillPreR = skill.Radius + math.max(0f, skill.Length) + radius;
                    if (math.abs(skillDelta.x) > skillPreR || math.abs(skillDelta.y) > skillPreR) continue;

                    float healthBeforeTowerSkill = health;
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
                        case 12:
                            ProcessTaggedArea(ref health, ref flashTimer, ref vel, ref tornadoMark, ref effects, pos, skill);
                            break;
                        case 13:
                        case 14:
                            towerAreaRepelled |= ProcessTowerArea(ref acceleration, ref health,
                                ref flashTimer, ref vel, ref tornadoMark, ref effects, pos, skill,
                                !isBoss);
                            break;
                        case 15:
                        case 16:
                            ProcessTowerLaser(ref health, ref flashTimer, ref effects, pos, skill);
                            break;
                        case 17:
                            ProcessTacticalDamageArea(ref health, ref flashTimer, ref vel, ref tornadoMark, ref effects, pos, skill);
                            break;
                        case 18:
                            ProcessTacticalBlackHole(ref vel, pos, skill);
                            break;
                        case 19:
                            ProcessTowerLaser(ref health, ref flashTimer, ref effects, pos, skill);
                            break;
                        case 20:
                            ProcessVulnerabilityLandingBlast(ref health, ref flashTimer,
                                ref effects, pos, skill);
                            break;
                        case 21:
                            ProcessIceSpikeCell(ref health, ref flashTimer, ref vel,
                                ref tornadoMark, ref effects, pos, skill);
                            break;
                        case 22:
                            ProcessTowerCone(ref health, ref flashTimer, ref vel,
                                ref tornadoMark, ref effects, pos, skill,
                                isBoss ? math.max(0f, radius) : 0f);
                            break;
                    }
                    int sourceTowerType = skill.SourceTowerTypePlusOne - 1;
                    if ((uint)sourceTowerType < (uint)TowerDefenseVisuals.TowerTypeCount &&
                        health < healthBeforeTowerSkill)
                    {
                        towerDamageByType[sourceTowerType] += healthBeforeTowerSkill - health;
                    }
                    if (healthBeforeTowerSkill > 0f && health <= 0f &&
                        (uint)sourceTowerType < (uint)TowerDefenseVisuals.TowerTypeCount)
                    {
                        towerKillGoldBonus = math.max(towerKillGoldBonus,
                            skill.SourceTowerKillGoldBonus);
                        towerWealthCellIndexPlusOne = math.max(0,
                            skill.SourceTowerWealthCellIndexPlusOne);
                        towerSourceTileEffect = skill.SourceTowerTileEffect;
                        if (!isBoss && !bufferedLaunchDeath)
                            ApplyTowerKillLaunch(ref vel, ref flashTimer, ref tornadoMark,
                                ref effects, pos, skill, sourceTowerType, sourceIndex);
                    }
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
                    health -= ApplyArmor(effects.MaximumHealth * PoisonTickMaxHealthRatio,
                        effects);
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
                    float burnDamage = effects.BurnDamage *
                        (1f + math.max(0, effects.BurnStacks) *
                         math.max(0f, effects.BurnDamageBonusPerStack));
                    health -= ApplyArmor(burnDamage, effects);
                    flashTimer = math.max(flashTimer, 0.3f);
                    effects.BurnTickTimer += math.max(0.01f,
                        effects.BurnTickInterval > 0f
                            ? effects.BurnTickInterval
                            : BurnTickInterval);
                }

                if (effects.BurnTimer <= 0f)
                {
                    effects.BurnTickTimer = 0f;
                    effects.BurnDamage = 0f;
                    effects.BurnTickInterval = 0f;
                    effects.BurnStacks = 0;
                    effects.BurnMaximumStacks = 0;
                    effects.BurnDamageBonusPerStack = 0f;
                    effects.BurnCreatesGround = 0;
                    effects.BurnDuration = 0f;
                    effects.BurnReapplyCooldown = 0f;
                }
            }
            else
            {
                effects.BurnTickTimer = 0f;
                effects.BurnDamage = 0f;
                effects.BurnTickInterval = 0f;
                effects.BurnStacks = 0;
                effects.BurnMaximumStacks = 0;
                effects.BurnDamageBonusPerStack = 0f;
                effects.BurnCreatesGround = 0;
                effects.BurnDuration = 0f;
                effects.BurnReapplyCooldown = 0f;
            }

            if (hasBulletBounds && !isAirborne && pos.x >= BulletMin.x && pos.x <= BulletMax.x && pos.z >= BulletMin.y && pos.z <= BulletMax.y)
            {
                int bulletCell = RougeMortonGridUtility.EncodeMortonFromWorld(pos.xz, GridOrigin, GridInvCellSize, GridDim);
                for (int entryIndex = bulletHeadPtr[bulletCell]; entryIndex >= 0; entryIndex = bulletNextPtr[entryIndex])
                {
                    int bulletIndex = bulletEntryPtr[entryIndex];
                    RougeBullet bullet = bulletPtr[bulletIndex];
                    float r = radius + bullet.Radius;
                    float2 bulletMin = math.min(bullet.Previous, bullet.Current) - r;
                    float2 bulletMax = math.max(bullet.Previous, bullet.Current) + r;
                    if (pos.x < bulletMin.x || pos.x > bulletMax.x || pos.z < bulletMin.y || pos.z > bulletMax.y)
                    {
                        continue;
                    }

                    float distSq = DistanceSqPointSegment(pos.xz, bullet.Previous, bullet.Current);
                    if (distSq > r * r)
                    {
                        continue;
                    }

                    float prevH = health;
                    health -= ApplyArmor(bullet.Damage * BulletDmgMult, effects);
                    flashTimer = 1f;
                    if (prevH > 0f && health <= 0f)
                    {
                        System.Threading.Interlocked.Increment(ref ((int*)SkillKillCounts.GetUnsafePtr())[5]);
                    }

                    RougeSkillArea mockSkill = new RougeSkillArea
                    {
                        Position = bullet.Current,
                        Type = 0,
                        Damage = bullet.Damage,
                        EffectFlags = bullet.EffectFlags,
                        EffectKnockbackCenter = bullet.EffectKnockbackCenter,
                        EffectKnockbackForce = bullet.EffectKnockbackForce,
                        EffectLaunchHeight = bullet.EffectLaunchHeight,
                        EffectLaunchLandingRadius = bullet.EffectLaunchLandingRadius,
                        EffectPoisonSpreadRadius = bullet.EffectPoisonSpreadRadius,
                        EffectSlowPercent = bullet.EffectSlowPercent,
                        EffectSlowDuration = bullet.EffectSlowDuration,
                        EffectCurseExplosionDamage = bullet.EffectCurseExplosionDamage,
                        EffectCurseExplosionRadius = bullet.EffectCurseExplosionRadius,
                        EffectBurnDamage = bullet.EffectBurnDamage,
                        EffectBurnDuration = bullet.EffectBurnDuration
                    };
                    ApplySkillEffects(ref vel, ref flashTimer, ref tornadoMark, ref effects, pos, mockSkill);

                    if (health <= 0f)
                    {
                        break;
                    }
                }
            }

            if (isBoss)
            {
                // Damage and non-motion status effects still apply, but all direct pulls,
                // knockback and launch impulses are discarded. Branch-A ice can still
                // freeze the Boss for its explicitly reduced duration.
                acceleration = bossUnaffectedAcceleration;
                vel = bossUnaffectedVelocity;
                vel.y = 0f;
                pos.y = RenderHeight;
                tornadoMark = 0f;
                effects.LaunchMotionTimer = 0f;
                effects.LaunchStackTimer = 0f;
                effects.LaunchLandingRadius = 0f;
                effects.LaunchLandingDamage = 0f;
                effects.VulnerabilityLandingBlastPending = 0;
                effects.SlowPercent = math.clamp(effects.SlowPercent, 0f, BossMaximumSlowPercent);
            }

            float incomingDamage = healthBeforeShieldedDamage - health;
            if (BossShieldActive && incomingDamage > 0f &&
                math.lengthsq(pos.xz - BossShieldPosition) <= BossShieldRadius * BossShieldRadius)
            {
                float reducedDamage = math.max(BossShieldMinimumDamage,
                    incomingDamage * BossShieldDamageMultiplier);
                health = healthBeforeShieldedDamage - reducedDamage;
            }

            if (incomingDamage > 0f)
            {
                float appliedDamage = math.min(math.max(healthBeforeShieldedDamage, 0f),
                    math.max(healthBeforeShieldedDamage - health, 0f));
                float attributionScale = appliedDamage / incomingDamage;
                long* totalDamagePtr = (long*)TowerDamageTotalsFixed.GetUnsafePtr();
                for (int towerType = 0; towerType < TowerDefenseVisuals.TowerTypeCount; towerType++)
                {
                    long fixedDamage = (long)math.round(math.max(0f, towerDamageByType[towerType]) *
                        attributionScale * 1000f);
                    if (fixedDamage > 0)
                    {
                        System.Threading.Interlocked.Add(ref totalDamagePtr[towerType], fixedDamage);
                    }
                }
            }

            bool hitPlayer = false;
            float towerContactDistance = radius + MainTowerContactRadius;
            if (MainTowerContactEnabled && !towerAreaRepelled && health > 0f && !isAirborne &&
                tornadoMark < 0.5f && distToGoalSq < towerContactDistance * towerContactDistance)
            {
                if (isBoss)
                    System.Threading.Interlocked.Increment(ref ((int*)BossReachedGoalCount.GetUnsafePtr())[0]);
                else
                    System.Threading.Interlocked.Increment(ref ((int*)MainTowerDamageCount.GetUnsafePtr())[0]);
                health = -1f;
                hitPlayer = true;
            }

            if (!hitPlayer && PlayerContactEnabled && health > 0f && !isAirborne && tornadoMark < 0.5f && distToPlayerSq < (radius + PlayerContactPadding) * (radius + PlayerContactPadding))
            {
                System.Threading.Interlocked.Increment(ref ((int*)PlayerDamageCount.GetUnsafePtr())[0]);
                if (DefeatEnemyOnPlayerContact)
                {
                    health = -1f;
                    hitPlayer = true;
                }
            }

            launchKillPending = tornadoMark > 2.5f || vel.y > 0.05f || pos.y > RenderHeight + 0.45f || effects.LaunchLandingRadius > 0f || effects.LaunchLandingDamage > 0f;
            if (launchKillPending && health <= 0f)
            {
                bufferedLaunchDeath = true;
                health = 1f;
            }

            if (health <= 0f && !launchKillPending)
            {
                acceleration = float3.zero;
                vel = float3.zero;
                tornadoMark = 0f;
            }

            vel += acceleration * DeltaTime;
            if (!isAirborne)
            {
                // Status areas are processed later in this iteration, so derive the cap
                // here from the updated effect state. Ice therefore slows on the hit frame
                // instead of waiting for the following simulation frame.
                float slowMoveFactor = effects.FreezeTimer > 0f
                    ? 0f
                    : math.clamp(1f - effects.SlowPercent * 0.01f, 0.05f, 1f);
                float effectiveMaxSpeed = maxSpeed * slowMoveFactor;
                float speedSq = math.lengthsq(vel.xz);
                // A main-tower blast is an impulse, not ordinary locomotion. Let the first
                // outward frame exceed the walking-speed cap or the knockback is reduced to
                // a tiny hesitation before pathfinding turns the enemy back toward the goal.
                if (!towerAreaRepelled && speedSq > effectiveMaxSpeed * effectiveMaxSpeed)
                {
                    vel.xz *= effectiveMaxSpeed * math.rsqrt(speedSq);
                }

                // VelocityDamping is a retained fraction per second. Applying it directly
                // every frame made configured move speed irrelevant and changed movement
                // with frame rate (0.9/frame capped ordinary motion near 2.4 units/second).
                float locomotionDamping = math.pow(
                    math.clamp(VelocityDamping, 0.0001f, 1f),
                    math.max(DeltaTime, 0f));
                float impulseDamping = math.pow(0.99f,
                    math.max(DeltaTime, 0f) * 60f);
                vel.xz *= towerAreaRepelled ? impulseDamping : locomotionDamping;
            }
            else
            {
                vel.xz *= math.pow(0.99f, math.max(DeltaTime, 0f) * 60f);
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
                    if (effects.VulnerabilityLandingBlastPending != 0)
                    {
                        SkillEventQueue.Enqueue(new RougeSkillEvent
                        {
                            Type = (int)RougeSkillEventType.VulnerabilityLandingBlast,
                            Position = pos.xz,
                            Radius = math.max(0.1f, radius * math.max(0f,
                                effects.VulnerabilityLandingRadiusMultiplier)),
                            Damage = math.max(0f,
                                effects.VulnerabilityLandingNormalDamageRatio),
                            Duration = math.max(0f,
                                effects.VulnerabilityLandingEliteBossDamageRatio)
                        });
                    }
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
                    effects.VulnerabilityLandingBlastPending = 0;
                }
                else if (!ExternalSpawning && isAirborne && vel.y < -1f)
                {
                    health -= ApplyArmor(math.abs(vel.y) * 15f, effects);
                    flashTimer = 1f;
                }

                pos.y = RenderHeight;
                vel.y = 0f;
                effects.LaunchMotionTimer = 0f;
                effects.LaunchStackTimer = 0f;
                float navigationRadius = sourceIndex == BossEnemyIndex
                    ? math.min(radius, math.max(0.1f, BossNavigationRadius))
                    : radius;
                ResolveObstaclePenetration(ref pos, ref vel, navigationRadius, obstaclePtr, ObstacleCount);
            }

            launchKillPending = tornadoMark > 2.5f || vel.y > 0.05f || pos.y > RenderHeight + 0.45f || effects.LaunchLandingRadius > 0f || effects.LaunchLandingDamage > 0f;
            bool justDied = health <= 0f && !hitPlayer && !isDeadVisual && !launchKillPending;
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

                if (effects.BurnTimer > 0f && effects.BurnDamage > 0f &&
                    effects.BurnCreatesGround != 0)
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

                if (effects.EmbeddedMachineGunFragmentCount > 0 &&
                    effects.EmbeddedMachineGunFragmentDamage > 0f)
                {
                    SkillEventQueue.Enqueue(new RougeSkillEvent
                    {
                        Type = (int)RougeSkillEventType.MachineGunEmbeddedFragments,
                        Position = pos.xz,
                        Damage = effects.EmbeddedMachineGunFragmentDamage,
                        Duration = effects.EmbeddedMachineGunFragmentRange,
                        Count = effects.EmbeddedMachineGunFragmentCount,
                        KillGoldBonus = effects.EmbeddedMachineGunKillGoldBonus,
                        WealthCellIndexPlusOne =
                            effects.EmbeddedMachineGunWealthCellIndexPlusOne,
                        TileEffect = effects.EmbeddedMachineGunTileEffect
                    });
                }

                SkillEventQueue.Enqueue(new RougeSkillEvent
                {
                    Type = (int)RougeSkillEventType.EnemyDeathBurst,
                    Position = pos.xz,
                    Radius = radius
                });

                if (towerSourceTileEffect == (int)RougeTowerPlaceEffect.Explosion &&
                    Hash01((uint)(sourceIndex + 1) * 2246822519u + FrameSeed) <
                    TowerTileExplosionChance)
                {
                    SkillEventQueue.Enqueue(new RougeSkillEvent
                    {
                        Type = (int)RougeSkillEventType.TowerTileExplosion,
                        Position = pos.xz,
                        Radius = TowerTileExplosionRadius,
                        Damage = math.max(1f, effects.MaximumHealth) *
                                 TowerTileExplosionMaxHealthRatio
                    });
                }

                System.Threading.Interlocked.Increment(ref ((int*)EnemyKillCount.GetUnsafePtr())[0]);
                if (TowerDefenseRewardsEnabled)
                {
                    int reward = (enemyKind & 0x80) != 0
                        ? 0
                        : math.max(0, effects.BaseKillGold);
                    int killGoldPercentBonus = math.max(0, towerKillGoldBonus);
                    reward = (int)math.ceil(reward * (1f + killGoldPercentBonus * 0.01f));
                    if (reward > 0)
                    {
                        int wealthCellIndex = towerWealthCellIndexPlusOne - 1;
                        if ((uint)wealthCellIndex < (uint)TowerDefenseWealthGoldEarned.Length)
                            System.Threading.Interlocked.Add(
                                ref ((int*)TowerDefenseWealthGoldEarned.GetUnsafePtr())[wealthCellIndex],
                                reward);
                        else
                            System.Threading.Interlocked.Add(
                                ref ((int*)TowerDefenseGoldEarned.GetUnsafePtr())[0], reward);
                    }
                }
                flashTimer = math.max(flashTimer, 0.99f);
                float facingDirectionAtDeath = effects.FacingDirection;
                effects = default;
                effects.FacingDirection = facingDirectionAtDeath;
            }

            float2 arenaLimit = math.max(float2.zero, ArenaHalfExtents - math.max(radius, 0.05f));
            if (pos.x <= -arenaLimit.x && vel.x < 0f) vel.x = 0f;
            else if (pos.x >= arenaLimit.x && vel.x > 0f) vel.x = 0f;
            if (pos.z <= -arenaLimit.y && vel.z < 0f) vel.z = 0f;
            else if (pos.z >= arenaLimit.y && vel.z > 0f) vel.z = 0f;
            pos.x = math.clamp(pos.x, -arenaLimit.x, arenaLimit.x);
            pos.z = math.clamp(pos.z, -arenaLimit.y, arenaLimit.y);

            flashTimer = math.max(0f, flashTimer - DeltaTime * 5f);
            posOutPtr[sourceIndex] = new float4(pos, crowdRadius);
            velOutPtr[sourceIndex] = new float4(vel, tornadoMark);
            stateOutPtr[sourceIndex] = new float4(
                health,
                radius,
                maxSpeed,
                EncodeVisualState(
                    flashTimer,
                    effects.CurseExplosionDamage > 0f && effects.CurseExplosionRadius > 0f,
                    health <= 0f,
                    bufferedLaunchDeath && launchKillPending && health > 0f,
                    effects.SlowTimer > 0f && effects.SlowPercent > 0f,
                    effects.FreezeTimer > 0f,
                    effects.BurnTimer > 0f,
                    effects.FacingDirection < 0f));
            effectOutPtr[sourceIndex] = effects;
        }
    }

    private static int DecodeVisualFlags(float encodedValue)
    {
        return (int)math.floor(math.max(encodedValue, 0f) / VisualStateFlagStep + 0.0001f);
    }

    private static float EncodeVisualState(float flashTimer, bool hasCurseVisual, bool isDeadVisual,
        bool isBufferedLaunchVisual, bool isSlowedVisual, bool isFrozenVisual,
        bool isBurningVisual, bool isFacingLeft)
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

        if (isBufferedLaunchVisual)
        {
            flags |= BufferedLaunchVisualFlag;
        }

        if (isSlowedVisual)
        {
            flags |= SlowVisualFlag;
        }

        if (isFrozenVisual)
        {
            flags |= FrozenVisualFlag;
        }

        if (isBurningVisual)
        {
            flags |= BurnVisualFlag;
        }

        flags |= FacingValidVisualFlag;
        if (isFacingLeft)
        {
            flags |= FacingLeftVisualFlag;
        }

        return math.min(math.max(flashTimer, 0f), 0.99f) + flags * VisualStateFlagStep;
    }

    private static RougeSkillArea BuildWeightedMotionArea(RougeSkillArea skill, float knockbackScale, float launchScale, bool ensureKnockback)
    {
        RougeSkillArea weightedSkill = skill;
        SkillHitEffectTag tags = (SkillHitEffectTag)weightedSkill.EffectFlags;
        float baseLaunch = skill.EffectLaunchHeight == 0f ? 12f : skill.EffectLaunchHeight;

        if (ensureKnockback && (tags & SkillHitEffectTag.Launch) != 0)
        {
            tags |= SkillHitEffectTag.Knockback;
            weightedSkill.EffectFlags = (int)tags;
        }

        if ((tags & SkillHitEffectTag.Knockback) != 0)
        {
            float baseKnockback = skill.EffectKnockbackForce == 0f ? 35f : skill.EffectKnockbackForce;
            if (ensureKnockback && (tags & SkillHitEffectTag.Launch) != 0)
            {
                baseKnockback = math.max(baseKnockback, baseLaunch * 2.2f);
            }

            weightedSkill.EffectKnockbackForce = baseKnockback * math.max(knockbackScale, 0f);
        }

        if ((tags & SkillHitEffectTag.Launch) != 0)
        {
            weightedSkill.EffectLaunchHeight = baseLaunch * math.max(launchScale, 0f);
        }

        return weightedSkill;
    }

    private static float Hash01(uint value)
    {
        value ^= 2747636419u;
        value *= 2654435769u;
        value ^= value >> 16;
        value *= 2654435769u;
        value ^= value >> 16;
        return (value & 0x00FFFFFFu) * (1f / 16777215f);
    }

    private float2 SampleFlowDirection(float2 worldPos, float2* flowPtr, float* flowDistancePtr)
    {
        int2 containingCell = RougeMortonGridUtility.WorldToGrid(
            worldPos, GridOrigin, GridInvCellSize, GridDim);
        int containingIndex = RougeMortonGridUtility.EncodeMorton(containingCell.x, containingCell.y);
        float2 direction = flowPtr[containingIndex];
        if (math.lengthsq(direction) > 0.0001f)
        {
            return AimFlowThroughNextCellCenter(worldPos, containingCell, direction);
        }

        // Inertia or crowd projection can briefly place a unit in a padded blocked cell.
        // Direct-to-goal fallback points through the obstacle and causes corner oscillation,
        // so escape toward the best nearby routed cell instead.
        float bestDistance = 1e20f;
        int2 bestCell = containingCell;
        float2 bestDirection = float2.zero;
        for (int offsetY = -2; offsetY <= 2; offsetY++)
        {
            int cellY = containingCell.y + offsetY;
            if (cellY < 0 || cellY >= GridDim) continue;
            for (int offsetX = -2; offsetX <= 2; offsetX++)
            {
                int cellX = containingCell.x + offsetX;
                if (cellX < 0 || cellX >= GridDim || (offsetX == 0 && offsetY == 0)) continue;
                int candidateIndex = RougeMortonGridUtility.EncodeMorton(cellX, cellY);
                float2 candidateDirection = flowPtr[candidateIndex];
                float candidateDistance = flowDistancePtr[candidateIndex];
                if (math.lengthsq(candidateDirection) <= 0.0001f ||
                    !math.isfinite(candidateDistance) || candidateDistance >= bestDistance)
                {
                    continue;
                }

                bestDistance = candidateDistance;
                bestCell = new int2(cellX, cellY);
                bestDirection = candidateDirection;
            }
        }

        if (math.lengthsq(bestDirection) <= 0.0001f) return float2.zero;
        float2 escapeCenter = GridOrigin +
            (new float2(bestCell.x + 0.5f, bestCell.y + 0.5f) * GridCellSize);
        return math.normalizesafe(escapeCenter - worldPos, bestDirection);
    }

    private float2 AimFlowThroughNextCellCenter(float2 worldPos, int2 containingCell, float2 direction)
    {
        float2 cellCenter = GridOrigin +
            (new float2(containingCell.x + 0.5f, containingCell.y + 0.5f) * GridCellSize);
        float2 waypoint = cellCenter + direction * (GridCellSize * 0.72f);
        return math.normalizesafe(waypoint - worldPos, direction);
    }

    private float2 StabilizeNavigationDirection(ref RougeEnemyEffectState effects,
        float2 desired, int sourceIndex)
    {
        desired = math.normalizesafe(desired, new float2(0f, 1f));
        float2 previous = new float2(effects.NavigationDirectionX, effects.NavigationDirectionY);
        if (math.lengthsq(previous) <= 0.0001f)
        {
            previous = desired;
        }
        else
        {
            previous = math.normalize(previous);
            bool reversing = math.dot(previous, desired) < -0.35f;
            if (reversing && effects.NavigationReverseCooldown > 0f)
            {
                desired = previous;
            }
            else if (reversing)
            {
                effects.NavigationReverseCooldown = math.lerp(0.2f, 0.3f,
                    Hash01((uint)(sourceIndex + 1) * 2246822519u + FrameSeed));
            }
        }

        effects.NavigationDirectionX = desired.x;
        effects.NavigationDirectionY = desired.y;
        if (math.abs(desired.x) > 0.12f)
        {
            effects.FacingDirection = desired.x < 0f ? -1f : 1f;
        }
        else if (effects.FacingDirection == 0f)
        {
            effects.FacingDirection = 1f;
        }
        return desired;
    }

    private static void SteerVelocityTowards(ref float3 velocity, float2 desired)
    {
        float speedSq = math.lengthsq(velocity.xz);
        if (speedSq <= 0.0001f) return;
        float speed = math.sqrt(speedSq);
        float2 currentDirection = velocity.xz / speed;
        float alignment = math.clamp(math.dot(currentDirection, desired), -1f, 1f);
        float turnWeight = math.saturate((1f - alignment) * 0.72f);
        velocity.xz = math.lerp(velocity.xz, desired * speed, turnWeight);
    }

    private float SampleDensity(float2 worldPos, int* densityPtr)
    {
        float2 gridPos = (worldPos - GridOrigin) * GridInvCellSize - 0.5f;
        int2 baseCell = (int2)math.floor(gridPos);
        float2 frac = math.saturate(gridPos - baseCell);
        float d00 = SampleDensityCell(baseCell.x, baseCell.y, densityPtr);
        float d10 = SampleDensityCell(baseCell.x + 1, baseCell.y, densityPtr);
        float d01 = SampleDensityCell(baseCell.x, baseCell.y + 1, densityPtr);
        float d11 = SampleDensityCell(baseCell.x + 1, baseCell.y + 1, densityPtr);
        float densityX0 = math.lerp(d00, d10, frac.x);
        float densityX1 = math.lerp(d01, d11, frac.x);
        return math.lerp(densityX0, densityX1, frac.y);
    }

    private float2 SampleDensityGradient(float2 worldPos, int* densityPtr)
    {
        float sampleOffset = math.max(GridCellSize * 0.75f, 0.1f);
        float dx = SampleDensity(worldPos + new float2(sampleOffset, 0f), densityPtr) - SampleDensity(worldPos - new float2(sampleOffset, 0f), densityPtr);
        float dy = SampleDensity(worldPos + new float2(0f, sampleOffset), densityPtr) - SampleDensity(worldPos - new float2(0f, sampleOffset), densityPtr);
        return new float2(dx, dy) * (0.5f / sampleOffset);
    }

    private float2 SampleFlowCell(int x, int y, float2* flowPtr)
    {
        int cellX = RougeMortonGridUtility.ClampCoord(x, GridDim);
        int cellY = RougeMortonGridUtility.ClampCoord(y, GridDim);
        return flowPtr[RougeMortonGridUtility.EncodeMorton(cellX, cellY)];
    }

    private float SampleDensityCell(int x, int y, int* densityPtr)
    {
        int cellX = RougeMortonGridUtility.ClampCoord(x, GridDim);
        int cellY = RougeMortonGridUtility.ClampCoord(y, GridDim);
        return densityPtr[RougeMortonGridUtility.EncodeMorton(cellX, cellY)] * (1f / RougeMortonGridUtility.DensityFixedScale);
    }

    private void ResolveObstaclePenetration(ref float3 pos, ref float3 vel, float radius, RougeObstacle* obstaclePtr, int obstacleCount)
    {
        for (int obstacleIndex = 0; obstacleIndex < obstacleCount; obstacleIndex++)
        {
            RougeObstacle obstacle = obstaclePtr[obstacleIndex];
            float2 currentPos = pos.xz;
            float2 resolvedPos = RougeObstacleMath.ResolvePointOutside(obstacle, currentPos, radius + obstacle.Padding);
            float2 pushDelta = resolvedPos - currentPos;
            if (math.lengthsq(pushDelta) <= 0.000001f)
            {
                continue;
            }

            float2 normal = math.normalizesafe(pushDelta, math.normalizesafe(currentPos - PlayerPos, new float2(1f, 0f)));
            pos.x = resolvedPos.x;
            pos.z = resolvedPos.y;
            float2 planarVelocity = vel.xz;
            RemoveInwardVelocity(ref planarVelocity, normal);
            vel.xz = planarVelocity;
        }
    }

    private static void RemoveInwardVelocity(ref float2 velocity, float2 normal)
    {
        float inward = math.dot(velocity, normal);
        if (inward < 0f)
        {
            velocity -= normal * inward;
        }
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
            health -= ApplyArmor(skill.Damage * 0.05f * DeltaTime, effects);
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
            health -= ApplyArmor(skill.Damage * BombDmgMult, effects);
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
                health -= ApplyArmor(skill.Damage * DeltaTime * LaserDmgMult, effects);
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
        SkillHitEffectTag tags = (SkillHitEffectTag)skill.EffectFlags;
        RougeSkillArea appliedSkill = skill;
        if ((tags & SkillHitEffectTag.Launch) != 0)
        {
            bool launchAlreadyActive = tornadoMark > 2.5f
                || vel.y > 0.05f
                || pos.y > RenderHeight + 0.05f
                || effects.LaunchMotionTimer > 0f;
            if (launchAlreadyActive && effects.LaunchStackTimer <= 0f)
            {
                appliedSkill.EffectFlags = (int)(tags & ~SkillHitEffectTag.Launch);
                appliedSkill.EffectLaunchHeight = 0f;
                appliedSkill.EffectLaunchLandingRadius = 0f;
            }
        }

        float2 pToS = pos.xz - skill.Position;
        float distSq = math.lengthsq(pToS);
        if (distSq < skill.Radius * skill.Radius)
        {
            float2 dir = math.normalizesafe(pToS, new float2(0f, 1f));
            float dot = math.dot(dir, skill.Direction);
            if (dot > 0.3f)
            {
                float prevHM = health;
                health -= ApplyArmor(skill.Damage * DeltaTime * 200f * MeleeDmgMult, effects);
                flashTimer = 1f;
                if (prevHM > 0f && health <= 0f)
                {
                    System.Threading.Interlocked.Increment(ref ((int*)SkillKillCounts.GetUnsafePtr())[3]);
                }

                acceleration.xz += dir * skill.PullForce * KnockbackResist;
                vel.y = math.max(vel.y, skill.VerticalForce * KnockbackResist);
                ApplySkillEffects(ref vel, ref flashTimer, ref tornadoMark, ref effects, pos, appliedSkill);
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
            health -= ApplyArmor(skill.Damage * DeltaTime * OrbitDmgMult, effects);
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
            float knockbackScale = math.lerp(0.3f, 1f, weight);
            float launchScale = math.lerp(0.35f, 1f, weight);
            vel.y = math.max(vel.y, skill.VerticalForce * (1f + weight * 2f));
            acceleration.xz += dir * (skill.PullForce + 25f) * knockbackScale;
            tornadoMark = 2f;
            flashTimer = 1f;
            RougeSkillArea weightedSkill = BuildWeightedMotionArea(skill, knockbackScale, launchScale, false);
            ApplySkillEffects(ref vel, ref flashTimer, ref tornadoMark, ref effects, pos, weightedSkill);
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
            health -= ApplyArmor(skill.Damage * weight, effects);
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
            health -= ApplyArmor(skill.Damage * DeltaTime * LaserDmgMult, effects);
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

        SkillHitEffectTag tags = (SkillHitEffectTag)skill.EffectFlags;
        bool isSkateboardLaunchArea = skill.Type == 12 && (tags & SkillHitEffectTag.Launch) != 0;
        bool launchAlreadyActive = tornadoMark > 2.5f || vel.y > 0.05f || pos.y > RenderHeight + 0.05f || effects.LaunchMotionTimer > 0f;
        if (isSkateboardLaunchArea && launchAlreadyActive && effects.LaunchStackTimer <= 0f)
        {
            return;
        }

        float radialWeight = skill.Radius > 0.001f
            ? 1f - math.saturate(math.sqrt(math.max(distSq, 0.0001f)) / skill.Radius)
            : 1f;
        RougeSkillArea weightedSkill = isSkateboardLaunchArea
            ? BuildWeightedMotionArea(skill, math.lerp(0.9f, 1.75f, radialWeight), math.lerp(1.05f, 1.75f, radialWeight), true)
            : skill;

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
            health -= ApplyArmor(skill.Damage * DeltaTime, effects);
            if (isSkateboardLaunchArea)
            {
                health = math.max(health, 1f);
            }
        }

        flashTimer = math.max(flashTimer, isSkateboardLaunchArea ? 0.9f : 0.2f);
        ApplySkillEffects(ref vel, ref flashTimer, ref tornadoMark, ref effects, pos, weightedSkill);
    }

    private bool ProcessTowerArea(ref float3 acceleration, ref float health,
        ref float flashTimer, ref float3 vel, ref float tornadoMark,
        ref RougeEnemyEffectState effects, float3 pos, RougeSkillArea skill,
        bool allowRepulse)
    {
        if (math.abs(pos.y - RenderHeight) > 5f) return false;
        float2 diff = pos.xz - skill.Position;
        if (math.lengthsq(diff) > skill.Radius * skill.Radius) return false;

        bool repelled = false;
        if (allowRepulse && skill.PullForce > 0f)
        {
            float2 outward = math.normalizesafe(diff, new float2(0f, 1f));
            float inwardSpeed = math.max(0f, -math.dot(vel.xz, outward));
            vel.xz += outward * (skill.PullForce + inwardSpeed);

            // Navigation acceleration has already been calculated by the time tower areas
            // are processed. Remove its inward component and remember the outward heading,
            // so the next few navigation frames continue the push instead of immediately
            // steering back into the main tower.
            float inwardAcceleration = math.min(0f, math.dot(acceleration.xz, outward));
            acceleration.xz -= outward * inwardAcceleration;
            effects.NavigationDirectionX = outward.x;
            effects.NavigationDirectionY = outward.y;
            effects.NavigationReverseCooldown = math.max(effects.NavigationReverseCooldown, 0.32f);
            if (math.abs(outward.x) > 0.12f)
            {
                effects.FacingDirection = outward.x < 0f ? -1f : 1f;
            }
            repelled = true;
        }

        if (skill.Damage > 0f)
        {
            float rawDamage = skill.Type == 14 ? skill.Damage * DeltaTime : skill.Damage;
            if (skill.Type == 13 && skill.AuxA > 0f && skill.AuxB > 1f)
            {
                float innerRadius = skill.Radius * skill.AuxA;
                if (math.lengthsq(diff) <= innerRadius * innerRadius)
                    rawDamage *= skill.AuxB;
            }
            health -= ApplyArmor(rawDamage, effects);
            flashTimer = math.max(flashTimer, skill.Type == 14 ? 0.2f : 0.8f);
        }

        if (skill.EffectConflagrationDamage > 0f && effects.FreezeTimer > 0f)
        {
            health -= ApplyArmor(skill.EffectConflagrationDamage, effects);
            effects.FreezeTimer = 0f;
            ClearBurnStatus(ref effects);
            skill.EffectFlags &= ~(int)SkillHitEffectTag.Burn;
            flashTimer = math.max(flashTimer, 0.99f);
            SkillEventQueue.Enqueue(new RougeSkillEvent
            {
                Type = (int)RougeSkillEventType.Conflagration,
                Position = pos.xz,
                Radius = math.max(1.5f, skill.Radius * 0.28f)
            });
        }

        ApplySkillEffects(ref vel, ref flashTimer, ref tornadoMark, ref effects, pos, skill);
        return repelled;
    }

    private void ProcessTowerCone(ref float health, ref float flashTimer,
        ref float3 vel, ref float tornadoMark, ref RougeEnemyEffectState effects,
        float3 pos, RougeSkillArea skill, float targetRadius)
    {
        if (math.abs(pos.y - RenderHeight) > 5f) return;
        float2 offset = pos.xz - skill.Position;
        float distanceSq = math.lengthsq(offset);
        float range = math.max(0f, skill.Radius) + math.max(0f, targetRadius);
        if (distanceSq > range * range) return;
        if (distanceSq > 0.0001f)
        {
            float cosine = math.cos(math.radians(math.clamp(skill.AuxA, 0f, 180f)));
            if (math.dot(offset * math.rsqrt(distanceSq),
                    math.normalizesafe(skill.Direction, new float2(0f, 1f))) < cosine)
                return;
        }
        health -= ApplyArmor(math.max(0f, skill.Damage), effects);
        flashTimer = math.max(flashTimer, 0.24f);
        ApplySkillEffects(ref vel, ref flashTimer, ref tornadoMark, ref effects,
            pos, skill);
    }

    private static void ClearBurnStatus(ref RougeEnemyEffectState effects)
    {
        effects.BurnTimer = 0f;
        effects.BurnTickTimer = 0f;
        effects.BurnDamage = 0f;
        effects.BurnTickInterval = 0f;
        effects.BurnStacks = 0;
        effects.BurnMaximumStacks = 0;
        effects.BurnDamageBonusPerStack = 0f;
        effects.BurnCreatesGround = 0;
        effects.BurnDuration = 0f;
        effects.BurnReapplyCooldown = 0f;
    }

    private void ProcessTowerLaser(ref float health, ref float flashTimer,
        ref RougeEnemyEffectState effects, float3 pos, RougeSkillArea skill)
    {
        if (math.abs(pos.y - RenderHeight) > 5f) return;
        float2 fromStart = pos.xz - skill.Position;
        float along = math.dot(fromStart, skill.Direction);
        if (along < 0f || along > skill.Length) return;
        float2 closest = skill.Position + skill.Direction * along;
        if (math.lengthsq(pos.xz - closest) > skill.Radius * skill.Radius) return;
        float rawDamage = skill.Type == 16 ? skill.Damage * DeltaTime : skill.Damage;
        health -= ApplyArmor(rawDamage, effects);
        if (((SkillHitEffectTag)skill.EffectFlags & SkillHitEffectTag.Slow) != 0)
        {
            effects.SlowStacks = 1f;
            effects.SlowPercent = math.max(effects.SlowPercent,
                math.max(0f, skill.EffectSlowPercent));
            effects.SlowTimer = math.max(effects.SlowTimer,
                skill.EffectSlowDuration > 0f ? skill.EffectSlowDuration : 2f);
        }
        if (((SkillHitEffectTag)skill.EffectFlags & SkillHitEffectTag.Freeze) != 0)
            ApplyFreezeStatus(ref effects, skill.EffectFreezeDuration,
                skill.EffectBossFreezeImmunityDuration);
        flashTimer = math.max(flashTimer, skill.Type == 16 ? 0.2f : 0.9f);
    }

    private static void ProcessVulnerabilityLandingBlast(ref float health,
        ref float flashTimer, ref RougeEnemyEffectState effects, float3 pos,
        RougeSkillArea skill)
    {
        if (math.lengthsq(pos.xz - skill.Position) > skill.Radius * skill.Radius) return;
        health -= math.max(0f, effects.MaximumHealth) * math.max(0f, skill.Damage);
        flashTimer = math.max(flashTimer, 0.9f);
    }

    private void ProcessIceSpikeCell(ref float health, ref float flashTimer,
        ref float3 vel, ref float tornadoMark, ref RougeEnemyEffectState effects,
        float3 pos, RougeSkillArea skill)
    {
        float2 delta = math.abs(pos.xz - skill.Position);
        if (math.max(delta.x, delta.y) > skill.Radius) return;
        health -= ApplyArmor(skill.Damage, effects);
        flashTimer = math.max(flashTimer, 0.9f);
        ApplySkillEffects(ref vel, ref flashTimer, ref tornadoMark, ref effects,
            pos, skill);
    }

    private static void ResolveEnemySpecificStatus(ref RougeSkillArea skill, byte enemyKind)
    {
        bool boss = (enemyKind & 0x80) != 0;
        bool elite = (enemyKind & 0x40) != 0;
        if (boss)
        {
            if (skill.EffectBossFreezeDuration > 0f)
                skill.EffectFreezeDuration = skill.EffectBossFreezeDuration;
            if (skill.EffectVulnerabilityDamageBonus > 0f)
                skill.EffectVulnerabilityDamageBonus *=
                    math.max(0f, skill.EffectVulnerabilityBossScale);
        }
        else
        {
            skill.EffectBossFreezeImmunityDuration = 0f;
            if (elite && skill.EffectEliteFreezeDuration > 0f)
                skill.EffectFreezeDuration = skill.EffectEliteFreezeDuration;
            if (elite && skill.EffectVulnerabilityDamageBonus > 0f)
                skill.EffectVulnerabilityDamageBonus *=
                    math.max(0f, skill.EffectVulnerabilityEliteScale);
        }

        if (skill.Type == 20 && (boss || elite))
            skill.Damage = math.max(0f, skill.AuxA);
    }

    private void ApplyTowerKillLaunch(ref float3 vel, ref float flashTimer, ref float tornadoMark,
        ref RougeEnemyEffectState effects, float3 pos, RougeSkillArea skill, int sourceTowerType,
        int sourceIndex)
    {
        if (math.lengthsq(pos.xz - GoalPos) <
            TowerKillLaunchGoalExclusionRadius * TowerKillLaunchGoalExclusionRadius)
        {
            return;
        }

        float2 launchDirection;
        switch ((RougeTowerType)sourceTowerType)
        {
            case RougeTowerType.Cannon:
                launchDirection = math.normalizesafe(pos.xz - skill.Position, new float2(0f, 1f));
                break;
            case RougeTowerType.PiercingLaser:
            {
                float along = math.clamp(math.dot(pos.xz - skill.Position, skill.Direction), 0f, skill.Length);
                float2 closest = skill.Position + skill.Direction * along;
                float2 beamNormal = new float2(-skill.Direction.y, skill.Direction.x);
                launchDirection = math.normalizesafe(pos.xz - closest, beamNormal);
                break;
            }
            case RougeTowerType.OrbitSphere:
                launchDirection = new float2(-skill.Direction.y, skill.Direction.x) *
                    (skill.AuxA < 0f ? -1f : 1f);
                break;
            default:
                return;
        }

        float heightVariation = math.lerp(0.80f, 1.20f,
            Hash01((uint)(sourceIndex + 1) * 3266489917u + FrameSeed));
        float launchImpulse = TowerKillLaunchVerticalImpulse * heightVariation * KnockbackResist;
        vel.y = math.max(vel.y, launchImpulse);
        vel.xz += launchDirection * (launchImpulse * TowerKillLaunchPlanarImpulseFactor);
        tornadoMark = 3f;
        effects.LaunchMotionTimer = math.max(effects.LaunchMotionTimer, LaunchMotionDuration);
        effects.LaunchStackTimer = math.max(effects.LaunchStackTimer, LaunchStackDuration);
        // This is a death presentation, not a combat launch. Landing must not deal a
        // second hit or create another area of effect.
        effects.LaunchLandingDamage = 0f;
        effects.LaunchLandingRadius = 0f;
        effects.TowerKillGoldBonus = math.max(effects.TowerKillGoldBonus,
            skill.SourceTowerKillGoldBonus);
        effects.TowerWealthCellIndexPlusOne = math.max(0,
            skill.SourceTowerWealthCellIndexPlusOne);
        effects.TowerSourceTileEffect = skill.SourceTowerTileEffect;
        flashTimer = math.max(flashTimer, 0.9f);
    }

    private void ProcessTacticalDamageArea(ref float health, ref float flashTimer, ref float3 vel,
        ref float tornadoMark, ref RougeEnemyEffectState effects, float3 pos, RougeSkillArea skill)
    {
        if (math.abs(pos.y - RenderHeight) > 5f) return;
        float2 difference = pos.xz - skill.Position;
        if (math.lengthsq(difference) > skill.Radius * skill.Radius) return;

        float previousHealth = health;
        health -= ApplyArmor(skill.Damage, effects);
        flashTimer = math.max(flashTimer, 0.9f);
        if (skill.AuxA <= 0f || previousHealth <= 0f || health > 0f) return;

        float launchHeight = math.max(1f, skill.EffectLaunchHeight);
        float2 launchDirection = math.normalizesafe(difference, new float2(0f, 1f));
        vel.y = math.max(vel.y, launchHeight * KnockbackResist);
        vel.xz += launchDirection * (launchHeight * LaunchPlanarImpulseFactor * KnockbackResist);
        tornadoMark = 3f;
        effects.LaunchMotionTimer = math.max(effects.LaunchMotionTimer, LaunchMotionDuration);
        effects.LaunchStackTimer = math.max(effects.LaunchStackTimer, LaunchStackDuration);
        // Tactical kills are already dead; the airborne state is presentation only.
        // Explicitly clear landing payload so touching down can never create another AOE.
        effects.LaunchLandingDamage = 0f;
        effects.LaunchLandingRadius = 0f;
    }

    private void ProcessTacticalBlackHole(ref float3 vel, float3 pos, RougeSkillArea skill)
    {
        if (math.abs(pos.y - RenderHeight) > 5f) return;
        float2 toCenter = skill.Position - pos.xz;
        float distanceSq = math.lengthsq(toCenter);
        if (distanceSq <= 0.0001f || distanceSq > skill.Radius * skill.Radius) return;
        vel.xz += toCenter * math.rsqrt(distanceSq) * skill.PullForce;
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

        float2 centerPos = skill.EffectKnockbackCenter == 1 ? PlayerPos : skill.Position;
        float2 pushDir = math.normalizesafe(pos.xz - centerPos, new float2(0f, 1f));
        if ((tags & SkillHitEffectTag.Knockback) != 0)
        {
            float knockbackForce = skill.EffectKnockbackForce == 0f ? 35f : skill.EffectKnockbackForce;
            vel.xz += pushDir * (knockbackForce * KnockbackResist);
        }

        if ((tags & SkillHitEffectTag.Launch) != 0)
        {
            float launchImpulse = (skill.EffectLaunchHeight == 0f ? 12f : skill.EffectLaunchHeight) * KnockbackResist;
            float launchMaxVerticalSpeed = math.max(launchImpulse * LaunchMaxVerticalSpeedMultiplier, launchImpulse + 6f);
            bool allowLaunchStack = effects.LaunchStackTimer > 0f;
            bool hasKnockbackTag = (tags & SkillHitEffectTag.Knockback) != 0;
            bool startingLaunchChain = tornadoMark <= 2.5f && effects.LaunchMotionTimer <= 0f && pos.y <= RenderHeight + 0.05f && vel.y <= 0.05f;

            vel.y = allowLaunchStack
                ? math.min(vel.y + launchImpulse, launchMaxVerticalSpeed)
                : math.max(vel.y, launchImpulse);

            float planarLaunchImpulse = launchImpulse * (hasKnockbackTag ? LaunchPlanarImpulseWithKnockbackFactor : LaunchPlanarImpulseFactor);
            vel.xz += pushDir * planarLaunchImpulse;
            tornadoMark = 3f;
            effects.LaunchLandingDamage = math.max(effects.LaunchLandingDamage, skill.Damage * 0.5f);
            effects.LaunchLandingRadius = math.max(effects.LaunchLandingRadius, skill.EffectLaunchLandingRadius);
            effects.LaunchMotionTimer = math.max(effects.LaunchMotionTimer, LaunchMotionDuration);
            if (effects.VulnerabilityTimer > 0f &&
                effects.VulnerabilityLandingBlast != 0)
                effects.VulnerabilityLandingBlastPending = 1;
            if (startingLaunchChain)
            {
                effects.LaunchStackTimer = math.max(effects.LaunchStackTimer, LaunchStackDuration);
            }
            flashTimer = math.max(flashTimer, 0.9f);
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
            if (skill.Type == 13)
            {
                effects.SlowStacks = 1f;
                effects.SlowPercent = math.max(effects.SlowPercent,
                    math.max(0f, skill.EffectSlowPercent));
            }
            else
            {
                effects.SlowStacks = 0f;
                effects.SlowPercent = skill.SourceTowerTileEffect ==
                                      (int)RougeTowerPlaceEffect.Frost
                    ? math.max(effects.SlowPercent, skill.EffectSlowPercent)
                    : skill.EffectSlowPercent;
            }
            effects.SlowTimer = math.max(effects.SlowTimer, skill.EffectSlowDuration > 0f ? skill.EffectSlowDuration : 2f);
        }

        if (skill.EffectVulnerabilityDuration > 0f)
        {
            // Vulnerability components form a strongest-value union. Any new
            // vulnerability application refreshes the whole merged set, so +damage
            // and armor penetration from different Ice towers can coexist.
            float mergedDuration = math.max(effects.VulnerabilityTimer,
                skill.EffectVulnerabilityDuration);
            effects.VulnerabilityTimer = mergedDuration;
            effects.VulnerabilityDamageBonus = math.max(
                effects.VulnerabilityDamageBonus,
                math.max(0f, skill.EffectVulnerabilityDamageBonus));
            effects.VulnerabilityDamageBonusTimer = mergedDuration;
            if (skill.EffectVulnerabilityArmorPenetration > 0f)
            {
                effects.VulnerabilityArmorPenetration = math.max(
                    effects.VulnerabilityArmorPenetration,
                    skill.EffectVulnerabilityArmorPenetration);
                effects.VulnerabilityArmorPenetrationTimer = mergedDuration;
            }
            if (skill.EffectVulnerabilityLandingBlast != 0)
            {
                effects.VulnerabilityLandingBlast = 1;
                effects.VulnerabilityLandingBlastTimer = math.max(
                    effects.VulnerabilityLandingBlastTimer,
                    skill.EffectVulnerabilityDuration);
                effects.VulnerabilityLandingRadiusMultiplier = math.max(0f,
                    skill.EffectVulnerabilityLandingRadiusMultiplier);
                effects.VulnerabilityLandingNormalDamageRatio = math.max(0f,
                    skill.EffectVulnerabilityLandingNormalDamageRatio);
                effects.VulnerabilityLandingEliteBossDamageRatio = math.max(0f,
                    skill.EffectVulnerabilityLandingEliteBossDamageRatio);
                if (tornadoMark > 0.5f || vel.y > 0.05f ||
                    pos.y > RenderHeight + 0.05f || effects.LaunchMotionTimer > 0f)
                    effects.VulnerabilityLandingBlastPending = 1;
            }
        }

        if ((tags & SkillHitEffectTag.Freeze) != 0)
        {
            float freezeDuration = skill.EffectFreezeDuration > 0f
                ? skill.EffectFreezeDuration
                : 2f;
            ApplyFreezeStatus(ref effects, freezeDuration,
                skill.EffectBossFreezeImmunityDuration);
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
                effects.BurnTickTimer = skill.EffectBurnTickInterval > 0f
                    ? skill.EffectBurnTickInterval
                    : BurnTickInterval;
                effects.BurnStacks = 0;
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
            int maximumStacks = math.max(1, skill.EffectBurnMaximumStacks);
            if (maximumStacks > 1)
                effects.BurnStacks = math.min(maximumStacks,
                    math.max(0, effects.BurnStacks) + 1);
            else
                effects.BurnStacks = 1;
            effects.BurnMaximumStacks = maximumStacks;
            effects.BurnDamageBonusPerStack = math.max(
                effects.BurnDamageBonusPerStack,
                math.max(0f, skill.EffectBurnDamageBonusPerStack));
            effects.BurnTickInterval = effects.BurnTickInterval > 0f
                ? math.min(effects.BurnTickInterval,
                    skill.EffectBurnTickInterval > 0f
                        ? skill.EffectBurnTickInterval
                        : BurnTickInterval)
                : skill.EffectBurnTickInterval > 0f
                    ? skill.EffectBurnTickInterval
                    : BurnTickInterval;
            effects.BurnDuration = math.max(effects.BurnDuration, burnDuration);
            if (skill.SourceTowerTypePlusOne != (int)RougeTowerType.Flame + 1)
                effects.BurnCreatesGround = 1;
        }

        flashTimer = math.max(flashTimer, 0.25f);
    }

    private void Respawn(int index, ref float4 pos4, ref float4 vel4, ref float4 state4, ref RougeEnemyEffectState effects)
    {
        uint hash = math.hash(new uint2((uint)index + FrameSeed, FrameSeed ^ 0xA511E9B3u));
        float angle = ((hash & 0xFFFFu) / 65535f) * math.PI * 2f;
        float safeSpawnRadius = math.max(SpawnRadiusMin, math.min(SpawnRadiusMax, math.max(8f,
            math.min(ArenaHalfExtents.x - math.abs(SpawnCenter.x) - 2f,
                ArenaHalfExtents.y - math.abs(SpawnCenter.y) - 2f))));
        float safeSpawnRadiusMin = math.min(SpawnRadiusMin, safeSpawnRadius * 0.78f);
        float distance = math.lerp(safeSpawnRadiusMin, safeSpawnRadius, ((hash >> 16) & 0xFFFFu) / 65535f);
        float speedScale = math.lerp(0.9f, 1.15f, ((hash >> 8) & 0xFFu) / 255f);
        float2 spawn = SpawnCenter + new float2(math.cos(angle), math.sin(angle)) * distance;
        spawn = math.clamp(spawn, -ArenaHalfExtents + 2f, ArenaHalfExtents - 2f);
        pos4 = new float4(spawn.x, RenderHeight, spawn.y, EnemyRadius);
        vel4 = float4.zero;
        state4 = new float4(EnemyMaxHealth, EnemyRadius, EnemyMaxSpeed * speedScale, 0f);
        effects = default;
        effects.MaximumHealth = EnemyMaxHealth;
        effects.Armor = math.clamp(EnemyArmor, RougeArmorRules.MinimumEnemyArmor,
            RougeArmorRules.MaximumEnemyArmor);
    }

    private static float ApplyArmor(float rawDamage, RougeEnemyEffectState effects)
    {
        rawDamage = math.max(0f, rawDamage);
        if (rawDamage <= 0f)
        {
            return 0f;
        }

        float armor = effects.Armor;
        bool vulnerable = effects.VulnerabilityTimer > 0f;
        if (vulnerable && armor > 0f) armor *= 0.5f;
        if (vulnerable && effects.VulnerabilityArmorPenetrationTimer > 0f)
            armor -= math.max(0f, effects.VulnerabilityArmorPenetration);
        float resolved = (rawDamage - armor) *
            (1f - armor * RougeArmorRules.DamageReductionPerArmorPoint);
        resolved = math.max(1f, resolved);
        if (vulnerable)
            resolved *= 1f + math.max(0f, effects.VulnerabilityDamageBonus);
        return math.max(1f, resolved);
    }

    private static void ApplyFreezeStatus(ref RougeEnemyEffectState effects,
        float freezeDuration, float bossImmunityDuration)
    {
        freezeDuration = math.max(0f, freezeDuration);
        bool blocked = bossImmunityDuration > 0f && effects.BossFreezeImmunityTimer > 0f;
        if (freezeDuration <= 0f || blocked) return;
        effects.FreezeTimer = math.max(effects.FreezeTimer, freezeDuration);
        if (bossImmunityDuration > 0f)
            effects.BossFreezeImmunityTimer = math.max(effects.BossFreezeImmunityTimer,
                freezeDuration + bossImmunityDuration);
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

[BurstCompile(FloatMode = FloatMode.Fast, FloatPrecision = FloatPrecision.Standard)]
public unsafe struct SimulateEnemiesJob : IJobParallelForBatch
{
    private const float VisualStateFlagStep = 10f;
    private const int CurseVisualFlag = 1;
    private const int DeadVisualFlag = 2;
    private const int BufferedLaunchVisualFlag = 4;
    private const int SlowVisualFlag = 8;
    private const float LaunchMotionDuration = 0.22f;
    private const float LaunchStackDuration = 0.12f;
    private const float LaunchPlanarImpulseFactor = 1.05f;
    private const float LaunchPlanarImpulseWithKnockbackFactor = 0.35f;
    private const float LaunchMaxVerticalSpeedMultiplier = 1.85f;
    private const float PoisonDurationSeconds = 2f;
    private const float PoisonTickInterval = 0.5f;
    private const float PoisonTickMaxHealthRatio = 0.1f;
    private const float BurnTickInterval = 0.5f;
    private const float BurnGroundRadius = 5f;
    private const float DeadFlashDecayRate = 2f;
    private const float BurnPatchReapplyCooldown = 0.85f;
    private const float BurnPatchDurationMultiplier = 0.45f;
    private const float BurnPatchDamageMultiplier = 0.55f;
    private const int BulletVisitedBitsetBytes = 256;

    [ReadOnly] public NativeArray<ulong> SortedKeys;
    [ReadOnly] public NativeArray<float4> PositionScaleIn;
    [ReadOnly] public NativeArray<float4> VelocityIn;
    [ReadOnly] public NativeArray<float4> StateIn;
    [ReadOnly] public NativeArray<RougeEnemyEffectState> EffectStateIn;
    [ReadOnly] public NativeArray<int> CellOffsets;
    [ReadOnly] public NativeArray<int> CellCounts;
    [ReadOnly] public NativeArray<int2> NeighborOffsets;
    [ReadOnly] public NativeArray<RougeBullet> Bullets;
    [ReadOnly] public NativeArray<int> BulletCellHeads;
    [ReadOnly] public NativeArray<int> BulletCellEntries;
    [ReadOnly] public NativeArray<int> BulletCellNext;
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
    public float CrowdReliefRadius;
    public float CrowdReliefStrength;
    public float CrowdOrbitStrength;
    public float DenseSeparationBoost;
    public int DenseNeighborThreshold;
    public float ObstacleLookAhead;
    public float ObstacleRepulsion;
    public float ObstacleOrbitStrength;
    public float KnockbackResist;
    public bool PlayerContactEnabled;
    public bool DefeatEnemyOnPlayerContact;
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
        int* bulletHeadPtr = (int*)BulletCellHeads.GetUnsafeReadOnlyPtr();
        int* bulletEntryPtr = (int*)BulletCellEntries.GetUnsafeReadOnlyPtr();
        int* bulletNextPtr = (int*)BulletCellNext.GetUnsafeReadOnlyPtr();
        RougeObstacle* obstaclePtr = (RougeObstacle*)Obstacles.GetUnsafeReadOnlyPtr();

        int endIndex = startIndex + count;
        int lastHashX = int.MinValue;
        int lastHashY = int.MinValue;
        int* neighborStart = stackalloc int[9];
        int* neighborEnd = stackalloc int[9];
        float separationRadiusSq = SeparationRadius * SeparationRadius;
        float crowdReliefRadiusSq = CrowdReliefRadius * CrowdReliefRadius;
        float invSepRadius = 1f / math.max(SeparationRadius, 0.0001f);

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
            bool isBufferedLaunchVisual = (visualFlags & BufferedLaunchVisualFlag) != 0;
            bool bufferedLaunchDeath = isBufferedLaunchVisual;
            bool launchKillPending = tornadoMark > 2.5f || pos.y > RenderHeight + 0.45f || effects.LaunchLandingRadius > 0f || effects.LaunchLandingDamage > 0f;

            if (launchKillPending && health <= 0f)
            {
                bufferedLaunchDeath = true;
                health = 1f;
            }

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
                    EncodeVisualState(flashTimer, false, true, false, false));
                effectOutPtr[sourceIndex] = effects;
                continue;
            }

            if (effects.LaunchMotionTimer > 0f)
            {
                effects.LaunchMotionTimer = math.max(0f, effects.LaunchMotionTimer - DeltaTime);
            }

            if (effects.LaunchStackTimer > 0f)
            {
                effects.LaunchStackTimer = math.max(0f, effects.LaunchStackTimer - DeltaTime);
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

            if (effects.FreezeTimer > 0f)
            {
                effects.FreezeTimer = math.max(0f, effects.FreezeTimer - DeltaTime);
            }

            if (effects.SlowTimer > 0f)
            {
                effects.SlowTimer = math.max(0f, effects.SlowTimer - DeltaTime);
                if (effects.SlowTimer <= 0f)
                {
                    effects.SlowPercent = 0f;
                    effects.SlowStacks = 0f;
                }
            }
            else
            {
                effects.SlowPercent = 0f;
                effects.SlowStacks = 0f;
            }

            float2 toPlayer = PlayerPos - pos.xz;
            float distToPlayerSq = math.lengthsq(toPlayer);
            float2 desired = math.normalizesafe(toPlayer);
            bool isAirborne = tornadoMark > 2.5f
                || vel.y > 0.05f
                || pos.y > RenderHeight + 0.05f
                || effects.LaunchMotionTimer > 0f
                || effects.LaunchStackTimer > 0f
                || effects.LaunchLandingRadius > 0f
                || effects.LaunchLandingDamage > 0f
                || bufferedLaunchDeath;
            float3 acceleration = isAirborne ? new float3(0f, -30f, 0f) : float3.zero;
            if (!isAirborne)
            {
                float chaseWeight = 1f;
                if (CrowdReliefRadius > 0f && distToPlayerSq > 0.0001f && distToPlayerSq < crowdReliefRadiusSq)
                {
                    float distToPlayer = math.sqrt(distToPlayerSq);
                    float centerWeight = 1f - math.saturate(distToPlayer / math.max(CrowdReliefRadius, 0.001f));
                    chaseWeight = math.lerp(1f, 0.35f, centerWeight);
                }

                acceleration.xz += desired * (ChaseAcceleration * chaseWeight);
            }

            float2 separation = float2.zero;
            int crowdedNeighbors = 0;
            // 空中敌人不参与分离计算，大量被击飞时节省内层循环开销
          if(!isAirborne)
                for (int n = 0; n < 9; n++)
                {
                    for (int k = neighborStart[n]; k < neighborEnd[n]; k++)
                    {
                        if (k == i) continue;
                        float2 other = posInPtr[k].xz;
                        float2 diff = pos.xz - other;
                        // AABB廉价剔除，跳过明显超出分离半径的邻居
                        if (math.abs(diff.x) >= SeparationRadius || math.abs(diff.y) >= SeparationRadius) continue;
                        float distSq = math.lengthsq(diff);
                        if (distSq < separationRadiusSq && distSq > 0.0001f)
                        {
                            crowdedNeighbors++;
                            float invDist = math.rsqrt(distSq);
                            float dist = distSq * invDist;
                            float weight = 1f - math.saturate(dist * invSepRadius);
                            separation += (diff * invDist) * (weight * SeparationStrength);
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
                float crowdPressure = 0f;
                if (DenseNeighborThreshold > 0)
                {
                    crowdPressure = math.saturate((crowdedNeighbors - DenseNeighborThreshold) / math.max(1f, DenseNeighborThreshold * 1.5f));
                }

                acceleration.xz += separation * (1f + crowdPressure * DenseSeparationBoost);

                if (crowdPressure > 0f && CrowdReliefRadius > 0f && distToPlayerSq > 0.0001f && distToPlayerSq < crowdReliefRadiusSq)
                {
                    float distToPlayer = math.sqrt(distToPlayerSq);
                    float playerWeight = 1f - math.saturate(distToPlayer / math.max(CrowdReliefRadius, 0.001f));
                    float reliefWeight = crowdPressure * playerWeight;
                    float2 awayFromPlayer = -desired;
                    float2 tangent = new float2(-awayFromPlayer.y, awayFromPlayer.x);
                    if ((sourceIndex & 1) == 0)
                    {
                        tangent = -tangent;
                    }

                    acceleration.xz += awayFromPlayer * (CrowdReliefStrength * reliefWeight);
                    acceleration.xz += tangent * (CrowdOrbitStrength * reliefWeight);
                }
            }

            for (int obstacleIndex = 0; obstacleIndex < ObstacleCount; obstacleIndex++)
            {
                RougeObstacle obstacle = obstaclePtr[obstacleIndex];
                float extraPadding = radius + obstacle.Padding;
                if (RougeObstacleMath.ContainsPoint(obstacle, pos.xz, extraPadding))
                {
                    float2 resolvedPos = RougeObstacleMath.ResolvePointOutside(obstacle, pos.xz, extraPadding);
                    float2 pushVector = resolvedPos - pos.xz;
                    float overlap = math.length(pushVector);
                    if (overlap > 0.0001f)
                    {
                        float2 normal = pushVector / overlap;
                        acceleration.xz += normal * (ObstacleRepulsion + overlap * 50f);
                    }
                }
                else if (!isAirborne)
                {
                    float2 closest = RougeObstacleMath.ClosestPoint(obstacle, pos.xz, extraPadding);
                    float2 diff = pos.xz - closest;
                    float distSq = math.lengthsq(diff);
                    if (distSq >= ObstacleLookAhead * ObstacleLookAhead)
                    {
                        continue;
                    }

                    float dist = math.sqrt(math.max(distSq, 0.0001f));
                    float2 normal = diff / dist;
                    float weight = 1f - math.saturate(dist / math.max(ObstacleLookAhead, 0.001f));
                    acceleration.xz += normal * (ObstacleRepulsion * weight);
                    float2 tangent = new float2(-normal.y, normal.x);
                    if (math.dot(tangent, desired) < 0f)
                    {
                        tangent = -tangent;
                    }

                    acceleration.xz += tangent * (ObstacleOrbitStrength * weight);
                }
            }

            for (int s = 0; s < SkillAreaCount; s++)
            {
                RougeSkillArea skill = SkillAreas[s];
                // AABB 初筛：保守包围盒 = Radius + Length（覆盖激光等线形技能）+ 敌人半径，避免无意义的函数调用
                float2 skillDelta = pos.xz - skill.Position;
                float skillPreR = skill.Radius + math.max(0f, skill.Length) + radius;
                if (math.abs(skillDelta.x) > skillPreR || math.abs(skillDelta.y) > skillPreR) continue;
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
                    case 12:
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
                    float burnDamage = effects.BurnDamage *
                        (1f + math.max(0, effects.BurnStacks) *
                         math.max(0f, effects.BurnDamageBonusPerStack));
                    health -= burnDamage;
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

            if (BulletCount > 0 && !isAirborne)
            {
                byte* visitedBulletBits = stackalloc byte[BulletVisitedBitsetBytes];
                UnsafeUtility.MemClear(visitedBulletBits, BulletVisitedBitsetBytes);
                bool stopBulletChecks = false;

                for (int n = 0; n < 9; n++)
                {
                    int2 offset = neighborOffsetsPtr[n];
                    int bulletHash = ((hashX + offset.x) ^ (hashY + offset.y)) & HashMask;
                    for (int entryIndex = bulletHeadPtr[bulletHash]; entryIndex >= 0; entryIndex = bulletNextPtr[entryIndex])
                    {
                        int bulletIndex = bulletEntryPtr[entryIndex];
                        int byteIndex = bulletIndex >> 3;
                        byte bitMask = (byte)(1 << (bulletIndex & 7));
                        if ((visitedBulletBits[byteIndex] & bitMask) != 0)
                        {
                            continue;
                        }

                        visitedBulletBits[byteIndex] |= bitMask;

                        RougeBullet bullet = bulletPtr[bulletIndex];
                        float r = radius + bullet.Radius;
                        float2 bulletMin = math.min(bullet.Previous, bullet.Current) - r;
                        float2 bulletMax = math.max(bullet.Previous, bullet.Current) + r;
                        if (pos.x < bulletMin.x || pos.x > bulletMax.x || pos.z < bulletMin.y || pos.z > bulletMax.y)
                        {
                            continue;
                        }

                        float distSq = DistanceSqPointSegment(pos.xz, bullet.Previous, bullet.Current);
                        if (distSq > r * r)
                        {
                            continue;
                        }

                        float prevH = health;
                        health -= bullet.Damage * BulletDmgMult;
                        flashTimer = 1f;
                        if (prevH > 0f && health <= 0f)
                        {
                            System.Threading.Interlocked.Increment(ref ((int*)SkillKillCounts.GetUnsafePtr())[5]);
                        }
                        RougeSkillArea mockSkill = new RougeSkillArea
                        {
                            Position = bullet.Current,
                            Type = 0,
                            Damage = bullet.Damage,
                            EffectFlags = bullet.EffectFlags,
                            EffectKnockbackCenter = bullet.EffectKnockbackCenter,
                            EffectKnockbackForce = bullet.EffectKnockbackForce,
                            EffectLaunchHeight = bullet.EffectLaunchHeight,
                            EffectLaunchLandingRadius = bullet.EffectLaunchLandingRadius,
                            EffectPoisonSpreadRadius = bullet.EffectPoisonSpreadRadius,
                            EffectSlowPercent = bullet.EffectSlowPercent,
                            EffectSlowDuration = bullet.EffectSlowDuration,
                            EffectCurseExplosionDamage = bullet.EffectCurseExplosionDamage,
                            EffectCurseExplosionRadius = bullet.EffectCurseExplosionRadius,
                            EffectBurnDamage = bullet.EffectBurnDamage,
                            EffectBurnDuration = bullet.EffectBurnDuration
                        };
                        ApplySkillEffects(ref vel, ref flashTimer, ref tornadoMark, ref effects, pos, mockSkill);

                        if (health <= 0f)
                        {
                            stopBulletChecks = true;
                            break;
                        }
                    }

                    if (stopBulletChecks)
                    {
                        break;
                    }
                }
            }

            bool hitPlayer = false;
            if (PlayerContactEnabled && health > 0f && !isAirborne && tornadoMark < 0.5f && distToPlayerSq < (radius + PlayerContactPadding) * (radius + PlayerContactPadding))
            {
                System.Threading.Interlocked.Increment(ref ((int*)PlayerDamageCount.GetUnsafePtr())[0]);
                if (DefeatEnemyOnPlayerContact)
                {
                    health = -1f;
                    hitPlayer = true;
                }
            }

            launchKillPending = tornadoMark > 2.5f || vel.y > 0.05f || pos.y > RenderHeight + 0.45f || effects.LaunchLandingRadius > 0f || effects.LaunchLandingDamage > 0f;
            if (launchKillPending && health <= 0f)
            {
                bufferedLaunchDeath = true;
                health = 1f;
            }

            if (health <= 0f && !launchKillPending)
            {
                acceleration = float3.zero;
                vel = float3.zero;
                tornadoMark = 0f;
            }

            vel += acceleration * DeltaTime;
            if (!isAirborne)
            {
                float slowMoveFactor = effects.FreezeTimer > 0f
                    ? 0f
                    : math.clamp(1f - effects.SlowPercent * 0.01f, 0.05f, 1f);
                float effectiveMaxSpeed = maxSpeed * slowMoveFactor;
                float speedSq = math.lengthsq(vel.xz);
                if (speedSq > effectiveMaxSpeed * effectiveMaxSpeed)
                {
                    vel.xz *= effectiveMaxSpeed * math.rsqrt(speedSq);
                }

                vel.xz *= math.pow(
                    math.clamp(VelocityDamping, 0.0001f, 1f),
                    math.max(DeltaTime, 0f));
            }
            else
            {
                vel.xz *= math.pow(0.99f, math.max(DeltaTime, 0f) * 60f);
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
                else if (isAirborne && vel.y < -1f)
                {
                    health -= math.abs(vel.y) * 15f;
                    flashTimer = 1f;
                }

                pos.y = RenderHeight;
                vel.y = 0f;
                effects.LaunchMotionTimer = 0f;
                effects.LaunchStackTimer = 0f;
            }

            launchKillPending = tornadoMark > 2.5f || vel.y > 0.05f || pos.y > RenderHeight + 0.45f || effects.LaunchLandingRadius > 0f || effects.LaunchLandingDamage > 0f;
            bool justDied = health <= 0f && !hitPlayer && !isDeadVisual && !launchKillPending;
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

                if (effects.BurnTimer > 0f && effects.BurnDamage > 0f &&
                    effects.BurnCreatesGround != 0)
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

                if (effects.EmbeddedMachineGunFragmentCount > 0 &&
                    effects.EmbeddedMachineGunFragmentDamage > 0f)
                {
                    SkillEventQueue.Enqueue(new RougeSkillEvent
                    {
                        Type = (int)RougeSkillEventType.MachineGunEmbeddedFragments,
                        Position = pos.xz,
                        Damage = effects.EmbeddedMachineGunFragmentDamage,
                        Duration = effects.EmbeddedMachineGunFragmentRange,
                        Count = effects.EmbeddedMachineGunFragmentCount,
                        KillGoldBonus = effects.EmbeddedMachineGunKillGoldBonus,
                        WealthCellIndexPlusOne =
                            effects.EmbeddedMachineGunWealthCellIndexPlusOne,
                        TileEffect = effects.EmbeddedMachineGunTileEffect
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
                    health <= 0f,
                    bufferedLaunchDeath && launchKillPending && health > 0f,
                    effects.SlowTimer > 0f && effects.SlowPercent > 0f));
            effectOutPtr[sourceIndex] = effects;
        }
    }

    private static int DecodeVisualFlags(float encodedValue)
    {
        return (int)math.floor(math.max(encodedValue, 0f) / VisualStateFlagStep + 0.0001f);
    }

    private static float EncodeVisualState(float flashTimer, bool hasCurseVisual, bool isDeadVisual,
        bool isBufferedLaunchVisual, bool isSlowedVisual)
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

        if (isBufferedLaunchVisual)
        {
            flags |= BufferedLaunchVisualFlag;
        }

        if (isSlowedVisual)
        {
            flags |= SlowVisualFlag;
        }

        return math.min(math.max(flashTimer, 0f), 0.99f) + flags * VisualStateFlagStep;
    }

    private static RougeSkillArea BuildWeightedMotionArea(RougeSkillArea skill, float knockbackScale, float launchScale, bool ensureKnockback)
    {
        RougeSkillArea weightedSkill = skill;
        SkillHitEffectTag tags = (SkillHitEffectTag)weightedSkill.EffectFlags;
        float baseLaunch = skill.EffectLaunchHeight == 0f ? 12f : skill.EffectLaunchHeight;

        if (ensureKnockback && (tags & SkillHitEffectTag.Launch) != 0)
        {
            tags |= SkillHitEffectTag.Knockback;
            weightedSkill.EffectFlags = (int)tags;
        }

        if ((tags & SkillHitEffectTag.Knockback) != 0)
        {
            float baseKnockback = skill.EffectKnockbackForce == 0f ? 35f : skill.EffectKnockbackForce;
            if (ensureKnockback && (tags & SkillHitEffectTag.Launch) != 0)
            {
                baseKnockback = math.max(baseKnockback, baseLaunch * 2.2f);
            }

            weightedSkill.EffectKnockbackForce = baseKnockback * math.max(knockbackScale, 0f);
        }

        if ((tags & SkillHitEffectTag.Launch) != 0)
        {
            weightedSkill.EffectLaunchHeight = baseLaunch * math.max(launchScale, 0f);
        }

        return weightedSkill;
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
        SkillHitEffectTag tags = (SkillHitEffectTag)skill.EffectFlags;
        RougeSkillArea appliedSkill = skill;
        if ((tags & SkillHitEffectTag.Launch) != 0)
        {
            bool launchAlreadyActive = tornadoMark > 2.5f
                || vel.y > 0.05f
                || pos.y > RenderHeight + 0.05f
                || effects.LaunchMotionTimer > 0f;
            if (launchAlreadyActive && effects.LaunchStackTimer <= 0f)
            {
                appliedSkill.EffectFlags = (int)(tags & ~SkillHitEffectTag.Launch);
                appliedSkill.EffectLaunchHeight = 0f;
                appliedSkill.EffectLaunchLandingRadius = 0f;
            }
        }

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
                ApplySkillEffects(ref vel, ref flashTimer, ref tornadoMark, ref effects, pos, appliedSkill);
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
            float knockbackScale = math.lerp(0.3f, 1f, weight);
            float launchScale = math.lerp(0.35f, 1f, weight);
            vel.y = math.max(vel.y, skill.VerticalForce * (1f + weight * 2f));
            acceleration.xz += dir * (skill.PullForce + 25f) * knockbackScale;
            tornadoMark = 2f;
            flashTimer = 1f;
            RougeSkillArea weightedSkill = BuildWeightedMotionArea(skill, knockbackScale, launchScale, false);
            ApplySkillEffects(ref vel, ref flashTimer, ref tornadoMark, ref effects, pos, weightedSkill);
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

        SkillHitEffectTag tags = (SkillHitEffectTag)skill.EffectFlags;
        bool isSkateboardLaunchArea = skill.Type == 12 && (tags & SkillHitEffectTag.Launch) != 0;
        bool launchAlreadyActive = tornadoMark > 2.5f || vel.y > 0.05f || pos.y > RenderHeight + 0.05f || effects.LaunchMotionTimer > 0f;
        if (isSkateboardLaunchArea && launchAlreadyActive && effects.LaunchStackTimer <= 0f)
        {
            return;
        }

        float radialWeight = skill.Radius > 0.001f
            ? 1f - math.saturate(math.sqrt(math.max(distSq, 0.0001f)) / skill.Radius)
            : 1f;
        RougeSkillArea weightedSkill = isSkateboardLaunchArea
            ? BuildWeightedMotionArea(skill, math.lerp(0.9f, 1.75f, radialWeight), math.lerp(1.05f, 1.75f, radialWeight), true)
            : skill;

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
            if (isSkateboardLaunchArea)
            {
                health = math.max(health, 1f);
            }
        }

        flashTimer = math.max(flashTimer, isSkateboardLaunchArea ? 0.9f : 0.2f);
        ApplySkillEffects(ref vel, ref flashTimer, ref tornadoMark, ref effects, pos, weightedSkill);
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

        float2 centerPos = skill.EffectKnockbackCenter == 1 ? PlayerPos : skill.Position;
        float2 pushDir = math.normalizesafe(pos.xz - centerPos, new float2(0f, 1f));
        if ((tags & SkillHitEffectTag.Knockback) != 0)
        {
            float knockbackForce = skill.EffectKnockbackForce == 0f ? 35f : skill.EffectKnockbackForce;
            vel.xz += pushDir * (knockbackForce * KnockbackResist);
        }

        if ((tags & SkillHitEffectTag.Launch) != 0)
        {
            float launchImpulse = (skill.EffectLaunchHeight == 0f ? 12f : skill.EffectLaunchHeight) * KnockbackResist;
            float launchMaxVerticalSpeed = math.max(launchImpulse * LaunchMaxVerticalSpeedMultiplier, launchImpulse + 6f);
            bool allowLaunchStack = effects.LaunchStackTimer > 0f;
            bool hasKnockbackTag = (tags & SkillHitEffectTag.Knockback) != 0;
            bool startingLaunchChain = tornadoMark <= 2.5f && effects.LaunchMotionTimer <= 0f && pos.y <= RenderHeight + 0.05f && vel.y <= 0.05f;

            vel.y = allowLaunchStack
                ? math.min(vel.y + launchImpulse, launchMaxVerticalSpeed)
                : math.max(vel.y, launchImpulse);

            float planarLaunchImpulse = launchImpulse * (hasKnockbackTag ? LaunchPlanarImpulseWithKnockbackFactor : LaunchPlanarImpulseFactor);
            vel.xz += pushDir * planarLaunchImpulse;
            tornadoMark = 3f;
            effects.LaunchLandingDamage = math.max(effects.LaunchLandingDamage, skill.Damage * 0.5f);
            effects.LaunchLandingRadius = math.max(effects.LaunchLandingRadius, skill.EffectLaunchLandingRadius);
            effects.LaunchMotionTimer = math.max(effects.LaunchMotionTimer, LaunchMotionDuration);
            if (startingLaunchChain)
            {
                effects.LaunchStackTimer = math.max(effects.LaunchStackTimer, LaunchStackDuration);
            }
            flashTimer = math.max(flashTimer, 0.9f);
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
            if (skill.Type == 13)
            {
                effects.SlowStacks = 1f;
                effects.SlowPercent = math.max(effects.SlowPercent,
                    math.max(0f, skill.EffectSlowPercent));
            }
            else
            {
                effects.SlowStacks = 0f;
                effects.SlowPercent = skill.SourceTowerTileEffect ==
                                      (int)RougeTowerPlaceEffect.Frost
                    ? math.max(effects.SlowPercent, skill.EffectSlowPercent)
                    : skill.EffectSlowPercent;
            }
            effects.SlowTimer = math.max(effects.SlowTimer, skill.EffectSlowDuration > 0f ? skill.EffectSlowDuration : 2f);
        }

        if ((tags & SkillHitEffectTag.Freeze) != 0)
        {
            effects.FreezeTimer = math.max(effects.FreezeTimer,
                skill.EffectFreezeDuration > 0f ? skill.EffectFreezeDuration : 2f);
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
        float safeSpawnRadius = math.max(SpawnRadiusMin, math.min(SpawnRadiusMax, math.max(8f, math.min(ArenaHalfExtent - math.abs(PlayerPos.x) - 2f, ArenaHalfExtent - math.abs(PlayerPos.y) - 2f))));
        float safeSpawnRadiusMin = math.min(SpawnRadiusMin, safeSpawnRadius * 0.78f);
        float distance = math.lerp(safeSpawnRadiusMin, safeSpawnRadius, ((hash >> 16) & 0xFFFFu) / 65535f);
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
