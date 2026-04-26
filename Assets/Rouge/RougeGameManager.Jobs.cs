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

        for (int obstacleIndex = 0; obstacleIndex < ObstacleCount; obstacleIndex++)
        {
            RougeObstacle obstacle = obstaclePtr[obstacleIndex];
            float navPadding = math.max(ExtraPadding, 0f);
            if (obstacle.Type == 1)
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
                    if (cellCenter.x >= minPadded.x && cellCenter.x <= maxPadded.x && cellCenter.y >= minPadded.y && cellCenter.y <= maxPadded.y)
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
public unsafe struct InitializeFlowFieldJob : IJobParallelForBatch
{
    [ReadOnly] public NativeArray<byte> BlockedCells;
    [NativeDisableParallelForRestriction] public NativeArray<float> FlowDistances;
    public int GridDim;
    public int GoalIndex;

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

            distancePtr[i] = i == GoalIndex ? 0f : (blockedPtr[i] != 0 ? 1e20f : 1e18f);
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
    public int GoalIndex;

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

            if (index == GoalIndex)
            {
                dstPtr[index] = 0f;
                continue;
            }

            if (blockedPtr[index] != 0)
            {
                dstPtr[index] = 1e20f;
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
    [ReadOnly] public NativeArray<byte> BlockedCells;
    [NativeDisableParallelForRestriction] public NativeArray<float> FlowDistances;
    public float2 PlayerPos;
    public float2 GridOrigin;
    public float InvCellSize;
    public int GridDim;
    public float CellSize;
    public int IterationCount;

    public void Execute()
    {
        byte* blockedPtr = (byte*)BlockedCells.GetUnsafeReadOnlyPtr();
        float* distancePtr = (float*)FlowDistances.GetUnsafePtr();
        int cellCount = GridDim * GridDim;
        for (int i = 0; i < cellCount; i++)
        {
            distancePtr[i] = blockedPtr[i] != 0 ? 1e20f : 1e18f;
        }

        int2 goalCell = RougeMortonGridUtility.WorldToGrid(PlayerPos, GridOrigin, InvCellSize, GridDim);
        int goalIndex = RougeMortonGridUtility.EncodeMorton(goalCell.x, goalCell.y);
        distancePtr[goalIndex] = 0f;

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

            float left = SampleDistance(cell.x - 1, cell.y, cell.x, cell.y, distancePtr);
            float right = SampleDistance(cell.x + 1, cell.y, cell.x, cell.y, distancePtr);
            float down = SampleDistance(cell.x, cell.y - 1, cell.x, cell.y, distancePtr);
            float up = SampleDistance(cell.x, cell.y + 1, cell.x, cell.y, distancePtr);
            float2 gradient = new float2(right - left, up - down);
            directionPtr[i] = math.normalizesafe(-gradient, float2.zero);
        }
    }

    private float SampleDistance(int x, int y, int fallbackX, int fallbackY, float* distancePtr)
    {
        int sampleX = x < 0 || x >= GridDim ? fallbackX : x;
        int sampleY = y < 0 || y >= GridDim ? fallbackY : y;
        return distancePtr[RougeMortonGridUtility.EncodeMorton(sampleX, sampleY)];
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
public unsafe struct SimulateEnemiesFlowFieldJob : IJobParallelForBatch
{
    private const float VisualStateFlagStep = 10f;
    private const int CurseVisualFlag = 1;
    private const int DeadVisualFlag = 2;
    private const int BufferedLaunchVisualFlag = 4;
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

    [ReadOnly] public NativeArray<float4> PositionScaleIn;
    [ReadOnly] public NativeArray<float4> VelocityIn;
    [ReadOnly] public NativeArray<float4> StateIn;
    [ReadOnly] public NativeArray<RougeEnemyEffectState> EffectStateIn;
    [ReadOnly] public NativeArray<int> DensityFieldFixed;
    [ReadOnly] public NativeArray<float2> FlowDirections;
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
        RougeBullet* bulletPtr = (RougeBullet*)Bullets.GetUnsafeReadOnlyPtr();
        int* bulletHeadPtr = (int*)BulletCellHeads.GetUnsafeReadOnlyPtr();
        int* bulletEntryPtr = (int*)BulletCellEntries.GetUnsafeReadOnlyPtr();
        int* bulletNextPtr = (int*)BulletCellNext.GetUnsafeReadOnlyPtr();
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
                    EncodeVisualState(flashTimer, false, true, false));
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
            float2 toPlayer = PlayerPos - pos.xz;
            float distToPlayerSq = math.lengthsq(toPlayer);
            float2 directToPlayer = distToPlayerSq > 0.0001f ? toPlayer * math.rsqrt(distToPlayerSq) : new float2(0f, 1f);
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
                float2 desired = SampleFlowDirection(pos.xz, flowPtr);
                if (math.lengthsq(desired) < 0.0001f)
                {
                    desired = directToPlayer;
                }

                float snapRadius = math.max(GridCellSize * 1.35f, radius * 4f + 0.35f);
                float snapWeight = distToPlayerSq > 0.0001f
                    ? 1f - math.saturate(math.sqrt(distToPlayerSq) / math.max(snapRadius, 0.001f))
                    : 1f;
                if (snapWeight > 0f)
                {
                    desired = math.normalizesafe(math.lerp(desired, directToPlayer, snapWeight), directToPlayer);
                }

                acceleration.xz += desired * (ChaseAcceleration * slowMoveFactor);

                float unitVariation = Hash01((uint)sourceIndex + 1u);
                float signedVariation = unitVariation * 2f - 1f;
                float densityThreshold = math.max(0f, DensitySoftThreshold + signedVariation * DensityResponseJitter);
                float densityResponseScale = math.max(0.35f, 1f + signedVariation * (DensityResponseJitter * 0.75f));
                float density = SampleDensity(pos.xz, densityPtr);
                float densityPressure = math.saturate(density - densityThreshold);
                densityPressure *= math.lerp(1f, 0.15f, snapWeight);
                if (densityPressure > 0f)
                {
                    float2 densityGradient =  SampleDensityGradient(pos.xz, densityPtr);
                    float gradientLengthSq = math.lengthsq(densityGradient);
                    if (gradientLengthSq > 0.000001f)
                    {
                        float gradientLength = math.sqrt(gradientLengthSq);
                        float2 gradientDir = densityGradient / gradientLength;
                        acceleration.xz += -gradientDir * (math.min(gradientLength, DensityGradientClamp) * DensityRepulsionStrength * densityPressure * densityResponseScale);
                    }
                }
            }

            for (int s = 0; s < SkillAreaCount; s++)
            {
                RougeSkillArea skill = SkillAreas[s];
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
                else if (isAirborne && vel.y < -1f)
                {
                    health -= math.abs(vel.y) * 15f;
                    flashTimer = 1f;
                }

                pos.y = RenderHeight;
                vel.y = 0f;
                effects.LaunchMotionTimer = 0f;
                effects.LaunchStackTimer = 0f;
                ResolveObstaclePenetration(ref pos, ref vel, radius, obstaclePtr, ObstacleCount);
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
                    health <= 0f,
                    bufferedLaunchDeath && launchKillPending && health > 0f));
            effectOutPtr[sourceIndex] = effects;
        }
    }

    private static int DecodeVisualFlags(float encodedValue)
    {
        return (int)math.floor(math.max(encodedValue, 0f) / VisualStateFlagStep + 0.0001f);
    }

    private static float EncodeVisualState(float flashTimer, bool hasCurseVisual, bool isDeadVisual, bool isBufferedLaunchVisual)
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

    private float2 SampleFlowDirection(float2 worldPos, float2* flowPtr)
    {
        float2 gridPos = (worldPos - GridOrigin) * GridInvCellSize - 0.5f;
        int2 baseCell = (int2)math.floor(gridPos);
        float2 frac = math.saturate(gridPos - baseCell);
        float2 d00 = SampleFlowCell(baseCell.x, baseCell.y, flowPtr);
        float2 d10 = SampleFlowCell(baseCell.x + 1, baseCell.y, flowPtr);
        float2 d01 = SampleFlowCell(baseCell.x, baseCell.y + 1, flowPtr);
        float2 d11 = SampleFlowCell(baseCell.x + 1, baseCell.y + 1, flowPtr);
        float2 dirX0 = math.lerp(d00, d10, frac.x);
        float2 dirX1 = math.lerp(d01, d11, frac.x);
        return math.normalizesafe(math.lerp(dirX0, dirX1, frac.y), float2.zero);
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
            if (obstacle.Type == 1)
            {
                float totalRadius = obstacle.CircleRadius + radius + obstacle.Padding;
                float2 diff = pos.xz - obstacle.Center;
                float distSq = math.lengthsq(diff);
                if (distSq >= totalRadius * totalRadius)
                {
                    continue;
                }

                float2 normal = distSq > 0.0001f
                    ? diff * math.rsqrt(distSq)
                    : math.normalizesafe(pos.xz - PlayerPos, new float2(1f, 0f));
                pos.x = obstacle.Center.x + normal.x * totalRadius;
                pos.z = obstacle.Center.y + normal.y * totalRadius;
                float2 planarVelocity = vel.xz;
                RemoveInwardVelocity(ref planarVelocity, normal);
                vel.xz = planarVelocity;
                continue;
            }

            float2 minPadded = obstacle.Min - new float2(radius + obstacle.Padding);
            float2 maxPadded = obstacle.Max + new float2(radius + obstacle.Padding);
            bool isInside = pos.x >= minPadded.x && pos.x <= maxPadded.x && pos.z >= minPadded.y && pos.z <= maxPadded.y;
            if (!isInside)
            {
                continue;
            }

            float dx1 = pos.x - minPadded.x;
            float dx2 = maxPadded.x - pos.x;
            float dy1 = pos.z - minPadded.y;
            float dy2 = maxPadded.y - pos.z;
            float minD = math.min(math.min(dx1, dx2), math.min(dy1, dy2));
            if (minD == dx1)
            {
                pos.x = minPadded.x;
                if (vel.x < 0f) vel.x = 0f;
            }
            else if (minD == dx2)
            {
                pos.x = maxPadded.x;
                if (vel.x > 0f) vel.x = 0f;
            }
            else if (minD == dy1)
            {
                pos.z = minPadded.y;
                if (vel.z < 0f) vel.z = 0f;
            }
            else
            {
                pos.z = maxPadded.y;
                if (vel.z > 0f) vel.z = 0f;
            }
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

[BurstCompile(FloatMode = FloatMode.Fast, FloatPrecision = FloatPrecision.Standard)]
public unsafe struct SimulateEnemiesJob : IJobParallelForBatch
{
    private const float VisualStateFlagStep = 10f;
    private const int CurseVisualFlag = 1;
    private const int DeadVisualFlag = 2;
    private const int BufferedLaunchVisualFlag = 4;
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
                    EncodeVisualState(flashTimer, false, true, false));
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

                acceleration.xz += desired * (ChaseAcceleration * slowMoveFactor * chaseWeight);
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
                if (obstacle.Type == 1)
                {
                    float2 diff = pos.xz - obstacle.Center;
                    float distSq = math.lengthsq(diff);
                    float totalRadius = obstacle.CircleRadius + radius + obstacle.Padding;

                    if (distSq < totalRadius * totalRadius && distSq > 0.0001f)
                    {
                        float invDist = math.rsqrt(distSq);
                        float dist = distSq * invDist;
                        float2 normal = diff * invDist;
                        float overlap = totalRadius - dist;
                        acceleration.xz += normal * (ObstacleRepulsion + overlap * 50f);
                    }
                    else if (!isAirborne)
                    {
                        float invDist = math.rsqrt(math.max(distSq, 0.0001f));
                        float dist = math.max(distSq, 0.0001f) * invDist;
                        float edgeDist = dist - totalRadius;
                        if (edgeDist >= 0f && edgeDist < ObstacleLookAhead)
                        {
                            float2 normal = diff * invDist;
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
                    health <= 0f,
                    bufferedLaunchDeath && launchKillPending && health > 0f));
            effectOutPtr[sourceIndex] = effects;
        }
    }

    private static int DecodeVisualFlags(float encodedValue)
    {
        return (int)math.floor(math.max(encodedValue, 0f) / VisualStateFlagStep + 0.0001f);
    }

    private static float EncodeVisualState(float flashTimer, bool hasCurseVisual, bool isDeadVisual, bool isBufferedLaunchVisual)
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