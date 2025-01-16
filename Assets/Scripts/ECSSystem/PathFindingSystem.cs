using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

/*
 * [UpdateInGroup(typeof(InitializationSystemGroup))] - first (setup)
 * [UpdateInGroup(typeof(SimulationSystemGroup))] - middle (simulation)
 * [UpdateInGroup(typeof(PresentationSystemGroup))] - last (rendering)
 */
//[BurstCompile]
[UpdateInGroup(typeof(SimulationSystemGroup))]
public partial class PathfindingSystem : SystemBase
{
    protected override void OnCreate()
    {
        base.OnCreate();

        // For simplicity, we spawn entities on system creation
        // In a real game, you can trigger this with an event
        RequireForUpdate(GetEntityQuery(typeof(PathfindingActiveTag)));
    }

    protected override void OnUpdate()
    {
        EntityQuery chunkQuery = GetEntityQuery(typeof(ChunkData));
        NativeArray<Entity> chunkEntities = chunkQuery.ToEntityArray(Allocator.Persistent);

        //access tile buffers - readonly
        BufferLookup<TileBufferElement> tileBufferLookup = GetBufferLookup<TileBufferElement>(true);
        //access results buffer - writable
        BufferLookup<PathPointBufferElement> pathResultBufferLookup = GetBufferLookup<PathPointBufferElement>(false);

        //create a map of chunk coordinates to active chunks - readonly
        NativeHashMap<int2, DynamicBuffer<TileBufferElement>> combinedTileBuffers = GetCombinedTileBuffers(chunkEntities, tileBufferLookup);

        // Iterate through pathfinding requests
        Entities
        .WithBurst()
        .WithAll<PathfindingActiveTag>() // only process active requests, Redundant with RequireForUpdate in OnCreate?
        .WithNativeDisableContainerSafetyRestriction(combinedTileBuffers) //disable safety checks - this intended to be readonly
        .WithNativeDisableParallelForRestriction(pathResultBufferLookup) //allow writing
        .ForEach((Entity entity, ref PathfindingConfig config, ref PathfindingRequest request, ref PathfindingResult result) =>
        {
            int2 startChunkCoord = (int2) (request.GlobalStartPosition / config.ChunkSize);
            int2 targetChunkCoord = (int2) (request.GlobalTargetPosition / config.ChunkSize);

            if(!combinedTileBuffers.ContainsKey(startChunkCoord) || !combinedTileBuffers.ContainsKey(targetChunkCoord))
                Debug.Log($"Combined chunk dynamicbuffers hashmap doesnt contain chunk with one of these coordinates: {startChunkCoord} or {targetChunkCoord}.");

            if(!pathResultBufferLookup.HasBuffer(entity))
                Debug.Log($"Entity does not contain pathfiniding results buffer");

            DynamicBuffer<PathPointBufferElement> pathResultBuffer = pathResultBufferLookup[entity];
            PerformAStarPathfinding(startChunkCoord, config, request, combinedTileBuffers, ref result, pathResultBuffer);                
         }).ScheduleParallel();

        Dependency.Complete();
    }

    // create a map of chunk coords to dynamic buffers
    private NativeHashMap<int2, DynamicBuffer<TileBufferElement>> GetCombinedTileBuffers(NativeArray<Entity> chunkEntities, BufferLookup<TileBufferElement> tileBufferLookup)
    {
        NativeHashMap<int2, DynamicBuffer<TileBufferElement>> combinedTileBuffers = new NativeHashMap<int2, DynamicBuffer<TileBufferElement>>(chunkEntities.Length, Allocator.Persistent);

        foreach (Entity chunkEntity in chunkEntities)
        {
            ChunkData chunkData = SystemAPI.GetComponent<ChunkData>(chunkEntity);
            if (tileBufferLookup.HasBuffer(chunkEntity))
            {
                combinedTileBuffers[chunkData.ChunkCoord] = tileBufferLookup[chunkEntity];
            }
        }

        return combinedTileBuffers;
    }

    #region AStar
    private static void PerformAStarPathfinding(
    int2 startChunkCoord,
    PathfindingConfig config,
    PathfindingRequest request,
    NativeHashMap<int2, DynamicBuffer<TileBufferElement>> combinedTileBuffers,
    ref PathfindingResult result,
    DynamicBuffer<PathPointBufferElement> resultBuffer)
    {
        Debug.Log($"PerformAStarPathfinding entered.");

        int chunkSize = config.ChunkSize;
        float2 globalStartPosition = request.GlobalStartPosition;
        float2 globalTargetPosition = request.GlobalTargetPosition;
        int2 globalStartTile = (int2)math.floor(globalStartPosition);
        int2 globalTargetTile = (int2)math.floor(globalTargetPosition);

        //ensure both tiles are walkable
        if (!IsTileWalkable(globalStartTile, combinedTileBuffers, chunkSize) || !IsTileWalkable(globalTargetTile, combinedTileBuffers, chunkSize))
            return;

        // Break out early if the path is within the same tile
        if (math.all(globalStartTile == globalTargetTile))
        {
            resultBuffer.ResizeUninitialized(2);
            resultBuffer[0] = new PathPointBufferElement { Position = globalStartPosition };
            resultBuffer[1] = new PathPointBufferElement { Position = globalTargetPosition };
            result.PathLength = 2;
            result.Success = true;
            return;
        }

        // Initialize A* structures
        int totalTileCount = combinedTileBuffers.Count * (chunkSize * chunkSize);
        NativeHashMap<int2, float> gCost = new NativeHashMap<int2, float>(totalTileCount, Allocator.Temp);
        NativeHashMap<int2, float> fCost = new NativeHashMap<int2, float>(totalTileCount, Allocator.Temp);
        NativeHashMap<int2, int2> parent = new NativeHashMap<int2, int2>(totalTileCount, Allocator.Temp);
        NativePriorityQueue openList = new NativePriorityQueue(Allocator.Temp);
        NativeHashSet<int2> closedList = new NativeHashSet<int2>(totalTileCount, Allocator.Temp);

        // Add start node to A* structures
        gCost[globalStartTile] = 0;
        fCost[globalStartTile] = CalculateHeuristicOctile(globalStartPosition, globalTargetPosition);
        openList.Enqueue(new NodeWithPriority(globalStartTile, fCost[globalStartTile]));

        bool pathFound = false;

        while (!openList.IsEmpty)
        {
            NodeWithPriority currentNode = openList.Dequeue();
            int2 globalCurrentTile = currentNode.Position;

            if (globalCurrentTile.Equals(globalTargetTile))
            {
                pathFound = true;
                break;
            }

            closedList.Add(globalCurrentTile);

            // Get neighbors of the current tile
            NativeList<int2> globalNeighbors = GetWalkableNeighborsAcrossChunks(globalCurrentTile, combinedTileBuffers, chunkSize);
            foreach (int2 globalNeighborTile in globalNeighbors)
            {
                //Debug.Log($"Current Tile: {globalCurrentTile} neighbor {globalNeighborTile}");
                if (closedList.Contains(globalNeighborTile))
                    continue;

                float tentativeGCost = gCost[globalCurrentTile] + math.distance(globalCurrentTile, globalNeighborTile);

                if (!gCost.TryGetValue(globalNeighborTile, out float existingGCost) || tentativeGCost < existingGCost)
                {
                    gCost[globalNeighborTile] = tentativeGCost;
                    float fCostValue = tentativeGCost + CalculateHeuristicOctile(globalNeighborTile, globalTargetPosition);
                    fCost[globalNeighborTile] = fCostValue;
                    parent[globalNeighborTile] = globalCurrentTile;

                    openList.Enqueue(new NodeWithPriority(globalNeighborTile, fCostValue));
                }
            }
            globalNeighbors.Dispose();
        }

        // If a path was found, retrace and store the result
        if (pathFound)
        {
            NativeList<float2> path = new NativeList<float2>(Allocator.Temp);
            RetracePathSmooth(globalTargetTile, parent, chunkSize, globalStartPosition, globalTargetPosition, ref path);
            NativeList<float2> smoothPath = OptimizePathLineOfSight(path, combinedTileBuffers, chunkSize);

            resultBuffer.ResizeUninitialized(smoothPath.Length);
            result.PathLength = smoothPath.Length;

            for (int i = 0; i < smoothPath.Length; i++)
            {
                resultBuffer[i] = new PathPointBufferElement { Position = smoothPath[i] };
            }

            path.Dispose();
            smoothPath.Dispose();
            result.Success = true;
        }
        else
        {
            result.Success = false;
            Debug.LogWarning("A* pathfinding failed.");
        }

        // Cleanup
        gCost.Dispose();
        fCost.Dispose();
        parent.Dispose();
        openList.Dispose();
        closedList.Dispose();
    }   
    #endregion

    #region PostProcessing
    private static void RetracePathSmooth(int2 targetIndex, NativeHashMap<int2, int2> parent, int chunkSize,
        float2 preciseStart, float2 preciseTarget, ref NativeList<float2> path)
    {
        int2 currentIndex = targetIndex;

        while (parent.TryGetValue(currentIndex, out int2 parentIndex))
        {
            float2 globalPosition = new float2(currentIndex.x + 0.5f, currentIndex.y + 0.5f); // Center of tile
            path.Add(globalPosition);
            currentIndex = parentIndex;
        }
        float2 startGlobalPosition = new float2(currentIndex.x + 0.5f, currentIndex.y + 0.5f); // Center of tile
        path.Add(startGlobalPosition);

        // Reverse the path as we traced it backward
        for (int i = 0; i < path.Length / 2; i++)
        {
            float2 temp = path[i];
            path[i] = path[path.Length - 1 - i];
            path[path.Length - 1 - i] = temp;
        }

        // Set precise start and end points
        path[0] = preciseStart;
        path[path.Length - 1] = preciseTarget;
    }

    private static NativeList<float2> OptimizePathLineOfSight(NativeList<float2> path, NativeHashMap<int2, DynamicBuffer<TileBufferElement>> combinedTileBuffers, int chunkSize)
    {
        NativeList<float2> optimizedPath = new NativeList<float2>(Allocator.Temp);
        if (path.Length == 0) return optimizedPath;

        optimizedPath.Add(path[0]); // Add the start point

        int currentIndex = 0;
        while (currentIndex < path.Length - 1)
        {
            int nextIndex = currentIndex + 1;

            // Test line of sight between currentIndex and nextIndex
            while (nextIndex < path.Length && HasLineOfSight(path[currentIndex], path[nextIndex], combinedTileBuffers, chunkSize))
            {
                nextIndex++;
            }

            // Add the last valid point with line of sight
            optimizedPath.Add(path[nextIndex - 1]);
            currentIndex = nextIndex - 1;
        }

        return optimizedPath;
    }

    private static bool HasLineOfSight(float2 start, float2 end, NativeHashMap<int2, DynamicBuffer<TileBufferElement>> combinedTileBuffers, int chunkSize)
    {
        int2 startTile = (int2)math.floor(start);
        int2 endTile = (int2)math.floor(end);

        NativeList<int2> line = SupercoverLine(startTile, endTile);

        int2 currentChunk = new int2(int.MaxValue, int.MaxValue); // Placeholder for initial chunk.
        DynamicBuffer<TileBufferElement> currentTileBuffer = default;

        foreach (int2 point in line)
        {
            int2 chunkCoord = new int2(
                (int)math.floor((float)point.x / chunkSize),
                (int)math.floor((float)point.y / chunkSize)
            );

            if (!chunkCoord.Equals(currentChunk))
            {
                if (!combinedTileBuffers.TryGetValue(chunkCoord, out currentTileBuffer))
                {
                    line.Dispose();
                    return false;
                }
                currentChunk = chunkCoord;
            }

            int2 localTile = point - (chunkCoord * chunkSize);
            if (localTile.x < 0 || localTile.y < 0 || localTile.x >= chunkSize || localTile.y >= chunkSize)
            {
                Debug.LogError($"Invalid local tile calculation: {localTile}, ChunkCoord: {chunkCoord}, GlobalPoint: {point}");
                line.Dispose();
                return false; // Invalid local tile calculation
            }
            int index = localTile.x + localTile.y * chunkSize;

            if (!currentTileBuffer[index].TileData.isWalkable)
            {
                line.Dispose();
                return false;
            }
        }

        line.Dispose();
        return true;
    }

    /// <summary>
    /// Returns all grid cells that a line from `start` to `end` touches,
    /// including diagonal tiles if the line exactly passes through corners.
    /// </summary>
    private static NativeList<int2> SupercoverLine(int2 start, int2 end)
    {
        // We'll store the cells in a NativeList<int2>.
        // If you want a non-allocating approach or a simple C# List<int2>,
        // you can adapt as needed.
        NativeList<int2> result = new NativeList<int2>(Allocator.Temp);

        int x0 = start.x;
        int y0 = start.y;
        int x1 = end.x;
        int y1 = end.y;

        // Bresenham setup
        int dx = math.abs(x1 - x0);
        int dy = math.abs(y1 - y0);
        int sx = (x0 < x1) ? 1 : -1;
        int sy = (y0 < y1) ? 1 : -1;

        // Bresenham error term:
        //   Classic formula: err = dx - dy
        //   We will manipulate this to decide steps in X/Y
        int err = dx - dy;

        while (true)
        {
            // Include current tile
            result.Add(new int2(x0, y0));

            // If we've reached the endpoint, we're done
            if (x0 == x1 && y0 == y1)
                break;

            // e2 is "2 * err" in typical Bresenham
            int e2 = 2 * err;

            // ---- CORNER CASE CHECK (supercover extension) ----
            //
            // If e2 == 0, it means the line is *exactly* passing through
            // the boundary corner of the current tile. In that situation,
            // standard Bresenham will do both an X and a Y step, but
            // we also want to include the diagonal cell(s) that get
            // touched when crossing that corner.
            //
            // Specifically, we add:
            //   - the cell we would touch if we only stepped in X
            //   - the cell we would touch if we only stepped in Y
            //
            // That ensures we get the "supercover" property.
            if (e2 == 0)
            {
                // If you want to ensure no duplicates in the list,
                // you can check via result.Contains(...) before adding.

                // Cell in X direction
                result.Add(new int2(x0 + sx, y0));
                // Cell in Y direction
                result.Add(new int2(x0, y0 + sy));
            }

            // ---- BRESENHAM STEPS ----
            // Decide whether to move in X and/or Y
            if (e2 > -dy)
            {
                err -= dy;
                x0 += sx;
            }
            if (e2 < dx)
            {
                err += dx;
                y0 += sy;
            }
        }

        return result;
    }
    #endregion

    #region BufferQuery
    private static bool IsTileWalkable(int2 globalPosition, NativeHashMap<int2, DynamicBuffer<TileBufferElement>> combinedTileBuffers, int chunkSize)
    {
        int2 chunkCoord = new int2(
            (int)math.floor(globalPosition.x / (float)chunkSize),
            (int)math.floor(globalPosition.y / (float)chunkSize)
        );

        //check if chunk exists
        if (!combinedTileBuffers.TryGetValue(chunkCoord, out DynamicBuffer<TileBufferElement> tileBuffer))
        {
            Debug.Log($"IsTileWalkable called for non-existant chunk.");
            return false; // No data for this chunk
        }

        // Calculate local position within the chunk
        int2 localPosition = globalPosition - chunkCoord * chunkSize;
        int index = localPosition.x + localPosition.y * chunkSize;

        // Validate index bounds
        if (index < 0 || index >= chunkSize * chunkSize)
        {
            Debug.Log($"Global: {globalPosition}, Chunk: {chunkCoord}, ChunkSize: {chunkSize}, Local: {localPosition}, Index: {index}");
            return false;
        }

        return tileBuffer[index].TileData.isWalkable;
    }

    private static NativeList<int2> GetWalkableNeighborsAcrossChunks(int2 globalCurrentTile, NativeHashMap<int2, DynamicBuffer<TileBufferElement>> combinedTileBuffers, int chunkSize)
    {
        NativeList<int2> neighbors = new NativeList<int2>(Allocator.Temp);

        NativeArray<int2> directions = new NativeArray<int2>(8, Allocator.Temp)
        {
            [0] = new int2(1, 0),  // Right
            [1] = new int2(-1, 0), // Left
            [2] = new int2(0, 1),  // Up
            [3] = new int2(0, -1), // Down
            [4] = new int2(1, 1),  // Up-Right
            [5] = new int2(1, -1), // Down-Right
            [6] = new int2(-1, 1), // Up-Left
            [7] = new int2(-1, -1) // Down-Left
        };

        //iterate through each adjacent tile
        foreach (int2 direction in directions)
        {
            int2 globalNeighborTile = globalCurrentTile + direction;

            // Determine the chunk this neighbor belongs to
            int2 neighborChunkCoord = (int2)math.floor(globalNeighborTile / chunkSize);

            //check if the chunk exists and the tile is walkable
            if (combinedTileBuffers.TryGetValue(neighborChunkCoord, out var tileBuffer) &&
                IsTileWalkable(globalNeighborTile, combinedTileBuffers, chunkSize))
            {
                //check diagonals
                if (direction.x != 0 && direction.y != 0)
                {
                    int2 globalHorizontalTile = new int2(globalCurrentTile.x + direction.x, globalCurrentTile.y); // Horizontal movement
                    int2 globalVerticalTile = new int2(globalCurrentTile.x, globalCurrentTile.y + direction.y);   // Vertical movement

                    if (!IsTileWalkable(globalHorizontalTile, combinedTileBuffers, chunkSize) ||
                        !IsTileWalkable(globalVerticalTile, combinedTileBuffers, chunkSize))
                    {
                        continue; // Skip diagonal if either perpendicular tile is unwalkable
                    }
                }

                neighbors.Add(globalNeighborTile);
            }
        }

        directions.Dispose();
        return neighbors;
    }
    #endregion

    #region heuristicCalculations
    /*
    private static float CalculateHeuristicManhattan(float2 a, float2 b)
    {
        return math.abs(a.x - b.x) + math.abs(a.y - b.y);
    }

    private static float CalculateHeuristicEuclidean(float2 a, float2 b)
    {
        return math.distance(a, b); // Equivalent to math.sqrt((a.x - b.x)^2 + (a.y - b.y)^2)
    }
    */

private static float CalculateHeuristicOctile(float2 a, float2 b)
    {
        float dx = math.abs(a.x - b.x);
        float dy = math.abs(a.y - b.y);
        float D = 1f;       // Cost for cardinal movement
        float D2 = math.sqrt(2f); // Cost for diagonal movement

        return D * (dx + dy) + (D2 - 2 * D) * math.min(dx, dy);
    }
    #endregion

    #region JumpPointSearch
    /*
    private static void PerformPathfindingJPS(int2 chunkCoord, PathfindingConfig config, PathfindingRequest request,
        DynamicBuffer<TileBufferElement> tileBuffer, ref PathfindingResult result, DynamicBuffer<PathPointBufferElement> resultBuffer)
    {
        //Debug.Log($"PathfindingSystem: Performing JPS pathfinding for {request.GlobalStartPosition} to {request.GlobalTargetPosition}");

        int chunkSize = config.ChunkSize;
        float2 globalStartPosition = request.GlobalStartPosition;
        float2 globalTargetPosition = request.GlobalTargetPosition;

        int startIndex = GlobalPositionToBufferIndex((int2)globalStartPosition, chunkSize, chunkCoord);
        int targetIndex = GlobalPositionToBufferIndex((int2)globalTargetPosition, chunkSize, chunkCoord);

        if (!tileBuffer[startIndex].TileData.isWalkable || !tileBuffer[targetIndex].TileData.isWalkable)
        {
            Debug.LogError($"Start or target position is not walkable: Start={globalStartPosition}, Target={globalTargetPosition}");
            result.Success = false;
            return;
        }

        NativeArray<float> gCost = new NativeArray<float>(tileBuffer.Length, Allocator.Temp);
        NativeArray<int> parent = new NativeArray<int>(tileBuffer.Length, Allocator.Temp);
        NativePriorityQueue openList = new NativePriorityQueue(Allocator.Temp, true);
        NativeHashSet<int> closedList = new NativeHashSet<int>(tileBuffer.Length, Allocator.Temp);

        for (int i = 0; i < tileBuffer.Length; i++)
        {
            gCost[i] = float.MaxValue;
            parent[i] = -1;
        }

        gCost[startIndex] = 0;
        openList.Enqueue(new NodeWithPriority(startIndex, CalculateHeuristicManhattan(globalStartPosition, globalTargetPosition)));

        bool pathFound = false;

        while (!openList.IsEmpty)
        {
            NodeWithPriority currentNode = openList.Dequeue();
            int currentIndex = currentNode.NodeIndex;

            if (currentIndex == targetIndex)
            {
                pathFound = true;
                break;
            }

            closedList.Add(currentIndex);

            NativeList<int> jumpPoints = FindJumpPoints(currentIndex, globalTargetPosition, tileBuffer, chunkSize, chunkCoord);
            foreach (int jumpPointIndex in jumpPoints)
            {
                if (closedList.Contains(jumpPointIndex))
                    continue;

                float tentativeGCost = gCost[currentIndex] + math.distance(
                    BufferIndexToGlobalPosition(currentIndex, chunkSize, chunkCoord),
                    BufferIndexToGlobalPosition(jumpPointIndex, chunkSize, chunkCoord)
                );

                if (tentativeGCost < gCost[jumpPointIndex])
                {
                    gCost[jumpPointIndex] = tentativeGCost;
                    parent[jumpPointIndex] = currentIndex;

                    float fCost = tentativeGCost + CalculateHeuristicManhattan(
                        BufferIndexToGlobalPosition(jumpPointIndex, chunkSize, chunkCoord),
                        globalTargetPosition
                    );

                    openList.Enqueue(new NodeWithPriority(jumpPointIndex, fCost));
                }
            }
            jumpPoints.Dispose();
        }

        //postprocess the path and copy it to the buffer
        if (pathFound)
        {
            NativeList<float2> precisePath = new NativeList<float2>(Allocator.Temp);
            RetracePathSmooth(targetIndex, parent, chunkSize, chunkCoord, globalStartPosition, globalTargetPosition, ref precisePath);

            resultBuffer.ResizeUninitialized(precisePath.Length);
            result.PathLength = precisePath.Length;
            for (int i = 0; i < precisePath.Length; i++)
            {
                resultBuffer[i] = new PathPointBufferElement { Position = precisePath[i] };
            }

            precisePath.Dispose();
            result.Success = true;
        }
        else
        {
            result.Success = false;
            Debug.LogWarning("JPS pathfinding failed.");
        }

        gCost.Dispose();
        parent.Dispose();
        openList.Dispose();
        closedList.Dispose();
    }

    private static NativeList<int> FindJumpPoints(int currentIndex, float2 targetPosition, DynamicBuffer<TileBufferElement> tileBuffer, int chunkSize, int2 chunkCoord)
    {
        NativeList<int> jumpPoints = new NativeList<int>(Allocator.Temp);

        int2 currentPos = BufferIndexToGlobalPosition(currentIndex, chunkSize, chunkCoord);

        NativeArray<int2> directions = new NativeArray<int2>(8, Allocator.Temp);
        directions[0] = new int2(1, 0); // cardinal directions
        directions[1] = new int2(-1, 0);
        directions[2] = new int2(0, 1);
        directions[3] = new int2(0, -1);
        directions[4] = new int2(1, 1); // diagonal directions
        directions[5] = new int2(1, -1);
        directions[6] = new int2(-1, 1);
        directions[7] = new int2(-1, -1);

        foreach (int2 dir in directions)
        {
            int2 nextPos = currentPos + dir;

            while (IsWalkable(nextPos, tileBuffer, chunkSize, chunkCoord))
            {
                if (math.all(nextPos == (int2)targetPosition))
                {
                    jumpPoints.Add(GlobalPositionToBufferIndex(nextPos, chunkSize, chunkCoord));
                    break;
                }

                if (HasForcedNeighbor(nextPos, dir, tileBuffer, chunkSize, chunkCoord))
                {
                    jumpPoints.Add(GlobalPositionToBufferIndex(nextPos, chunkSize, chunkCoord));
                    break;
                }

                // If moving diagonally, ensure adjacent cardinal tiles are walkable
                if (dir.x != 0 && dir.y != 0) // Diagonal direction
                {
                    int2 horizontal = new int2(dir.x, 0);
                    int2 vertical = new int2(0, dir.y);

                    if (!IsWalkable(nextPos + horizontal, tileBuffer, chunkSize, chunkCoord) ||
                        !IsWalkable(nextPos + vertical, tileBuffer, chunkSize, chunkCoord))
                    {
                        break;
                    }
                }

                nextPos += dir;
            }
        }

        directions.Dispose();
        return jumpPoints;
    }

    private static bool IsWalkable(int2 position, DynamicBuffer<TileBufferElement> tileBuffer, int chunkSize, int2 chunkCoord)
    {
        if (position.x < 0 || position.y < 0 || position.x >= chunkSize || position.y >= chunkSize)
            return false;

        int index = GlobalPositionToBufferIndex(position, chunkSize, chunkCoord);
        return tileBuffer[index].TileData.isWalkable;
    }

    private static bool HasForcedNeighbor(int2 position, int2 direction, DynamicBuffer<TileBufferElement> tileBuffer, int chunkSize, int2 chunkCoord)
    {
        if (direction.x != 0 && direction.y != 0) // Diagonal direction
        {
            // For diagonal movement, ensure both adjacent cardinal tiles are walkable
            int2 horizontal = new int2(direction.x, 0);
            int2 vertical = new int2(0, direction.y);

            if (!IsWalkable(position + horizontal, tileBuffer, chunkSize, chunkCoord) ||
                !IsWalkable(position + vertical, tileBuffer, chunkSize, chunkCoord))
            {
                return false;
            }
        }

        // Check perpendicular forced neighbors
        int2 left = new int2(-direction.y, direction.x);
        int2 right = new int2(direction.y, -direction.x);

        int2 leftNeighbor = position + left;
        int2 rightNeighbor = position + right;

        int2 leftDiagonal = position + direction + left;
        int2 rightDiagonal = position + direction + right;

        bool hasLeftForcedNeighbor = !IsWalkable(leftNeighbor, tileBuffer, chunkSize, chunkCoord) &&
                                      IsWalkable(leftDiagonal, tileBuffer, chunkSize, chunkCoord);

        bool hasRightForcedNeighbor = !IsWalkable(rightNeighbor, tileBuffer, chunkSize, chunkCoord) &&
                                       IsWalkable(rightDiagonal, tileBuffer, chunkSize, chunkCoord);

        return hasLeftForcedNeighbor || hasRightForcedNeighbor;
    }
    */
    #endregion
}
