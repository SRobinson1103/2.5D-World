using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

public enum ChunkState
{
    Unloaded,          // The chunk is not loaded
    WaitingToLoad,     // The chunk is scheduled to be loaded
    Loaded,            // The chunk is fully loaded
    WaitingToUnload    // The chunk is scheduled to be unloaded
}

public class Chunk
{
    public Vector2Int position;       // Chunk's grid position
    public NativeArray<Tile> tiles;   // Flattened array
    public int chunkSize;
    public Mesh mesh;                 // Combined mesh for the chunk
    public bool isDirty;      // Indicates if the chunk needs updating
    public GameObject chunkObjectRef; // stores a reference
    public ChunkState state;          // loading state

    //temp arrays for job processing
    public NativeArray<Vector3> vertices;
    public NativeArray<int> triangles;
    public NativeArray<Vector2> uvs;

    public Chunk(int chunkSize, Vector2Int position)
    {
        isDirty = false;
        this.position = position;
        this.chunkSize = chunkSize;
        state = ChunkState.Unloaded;
        tiles = new NativeArray<Tile>(chunkSize * chunkSize, Allocator.Persistent);
    }

    public int GetTileIndex(int x, int y)
    {
        return x + y * chunkSize;
    }

    public void InitTempArrays()
    {
        vertices = new NativeArray<Vector3>(chunkSize * chunkSize * 4, Allocator.Persistent);
        triangles = new NativeArray<int>(chunkSize * chunkSize * 6, Allocator.Persistent);
        uvs = new NativeArray<Vector2>(chunkSize * chunkSize * 4, Allocator.Persistent);
    }

    public void DisposeTempArrays()
    {
        if (vertices.IsCreated) vertices.Dispose();
        if (triangles.IsCreated) triangles.Dispose();
        if (uvs.IsCreated) uvs.Dispose();
    }

    public void DisposePermArrays()
    {
        if (tiles.IsCreated) tiles.Dispose();
    }
}

//value corresponds to an index in tile atlas; left to right and bottom to top
public enum TileType
{
    DIRT = 56,
    GRASS = 48,
    STONE = 32,
    WATER = 40
}

public struct Tile : IBufferElementData
{
    public TileType type;
    public bool isWalkable;        // Whether the tile is walkable
}

//TODO: add biomes
//TODO: add water - rivers and lakes
//TODO: make water look pretty
//TODO: Add tile edge blending
//TODO: fix edges (can see adjacent tile edge)
//TODO: use the worldseed
public class ChunkingSystem : MonoBehaviour
{
    public int chunkSize = 16;                // Size of each chunk (16x16 tiles)
    public int loadRadius = 2;                // Number of chunks to load around the camera
    public float colliderHeight = 0.1f;       // height of the chunk box collider
    public float noiseScale = 0.1f;           // scale for noise generation
    public UnityEngine.Material tileMaterial; // Material for rendering the tiles
    public int tileAtlasDimension = 8;        // Number of tiles on a row of the atlas
    public float tileSize = 1.0f;             // the size of the generated tiles
    public string layer = "Terrain";          // The layer of the chunk GameObjects

    private int worldSeed = -1;               // the global seed

    private float tileOffset = 0.125f;        // offset of each tile on the atlas = 1 / tileAtlasDimension
    private NativeArray<Vector2> uvOffsets;   // The pre-calculated UV offsets for texture selection

    private Transform cameraTransform;        // Reference to the active camera
    private Vector2Int lastCameraChunk = Vector2Int.zero; //tracks the previous chunk that the camera was positioned above

    //map of chunk-relative coordinates to active chunk objects
    [HideInInspector] public Dictionary<Vector2Int, Chunk> activeChunks = new Dictionary<Vector2Int, Chunk>();

    //ECS
    private EntityManager entityManager;
    //map of chunk-relative coordinates to active chunk entities
    private Dictionary<Vector2Int, Entity> chunkToEntityMap = new Dictionary<Vector2Int, Entity>();    

    void Start()
    {
        // Get the default world and its EntityManager
        entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;
  
        worldSeed = WorldManager.Instance.GetWorldSeed();
        cameraTransform = Camera.main.transform;
        tileMaterial.enableInstancing = true;
        tileOffset = 1.0f / tileAtlasDimension;

        PreComputeUVOffsets();
        UpdateChunks(); // Initial load
    }

    void Update()
    {
        Vector2Int currentCameraChunk = WorldToChunk(cameraTransform.position);
        if (currentCameraChunk != lastCameraChunk)
        {
            lastCameraChunk = currentCameraChunk;
            UpdateChunks();
        }
        //update buffers for altered chunks (isDirty)
        UpdateChunkBuffers();
    }

    private void OnDestroy()
    {
        foreach (var chunk in activeChunks.Values)
        {
            chunk.DisposeTempArrays();
            chunk.DisposePermArrays();
        }
        activeChunks.Clear();

        if (uvOffsets.IsCreated) uvOffsets.Dispose();
    }

    void UpdateChunks()
    {
        Vector3 cameraPosition = cameraTransform.position;
        Vector2Int cameraChunk = WorldToChunk(cameraPosition);

        HashSet<Vector2Int> chunksToKeep = new HashSet<Vector2Int>();

        // Load chunks around the camera
        for (int x = -loadRadius; x <= loadRadius; x++)
        {
            for (int y = -loadRadius; y <= loadRadius; y++)
            {
                Vector2Int chunkCoord = new Vector2Int(cameraChunk.x + x, cameraChunk.y + y);
                chunksToKeep.Add(chunkCoord);
            }
        }

        LoadChunksAsync(chunksToKeep);

        // Unload distant chunks
        List<Vector2Int> chunksToRemove = new List<Vector2Int>();
        foreach (var chunkCoord in activeChunks.Keys)
        {
            if (!chunksToKeep.Contains(chunkCoord))
            {
                chunksToRemove.Add(chunkCoord);
            }
        }

        foreach (var chunkCoord in chunksToRemove)
        {
            UnloadChunk(chunkCoord);
        }
    }

    void LoadChunksAsync(HashSet<Vector2Int> chunksToLoad)
    {
        NativeArray<JobHandle> handles = new NativeArray<JobHandle>(chunksToLoad.Count, Allocator.Temp);
        int index = 0;
        foreach (var chunkCoord in chunksToLoad)
        {
            // Skip chunks that are already loaded or waiting to be unloaded
            if (activeChunks.TryGetValue(chunkCoord, out Chunk existingChunk))
            {
                if (existingChunk.state == ChunkState.Loaded || existingChunk.state == ChunkState.WaitingToUnload)
                {
                    continue;
                }
            }

            // Schedule the chunk for loading
            if (!activeChunks.ContainsKey(chunkCoord))
            {
                Chunk newChunk = new Chunk(chunkSize, chunkCoord)
                {
                    state = ChunkState.WaitingToLoad
                };
                activeChunks[chunkCoord] = newChunk;

            }
            handles[index++] = ScheduleChunkJob(chunkCoord, activeChunks[chunkCoord]);
        }

        JobHandle combinedHandle = JobHandle.CombineDependencies(handles);
        combinedHandle.Complete(); // Wait for all jobs to finish
        handles.Dispose();

        // Finalize all chunks
        foreach (var chunkCoord in chunksToLoad)
        {
            if (activeChunks.TryGetValue(chunkCoord, out Chunk chunk) && chunk.state == ChunkState.WaitingToLoad)
            {
                FinalizeChunkMesh(chunkCoord, chunk);
                chunk.state = ChunkState.Loaded;
                OnChunkLoaded(chunkCoord);
            }
        }
    }

    private JobHandle ScheduleChunkJob(Vector2Int chunkCoord, Chunk chunk)
    {
        // Allocate NativeArrays for mesh data
        chunk.InitTempArrays();

        // Schedule Noise Job
        var noiseJob = new GenerateChunkTilesJob
        {
            tiles = chunk.tiles,
            chunkSize = chunkSize,
            noiseScale = noiseScale,
            worldOffsetX = chunkCoord.x * chunkSize,
            worldOffsetY = chunkCoord.y * chunkSize
        };
        JobHandle noiseHandle = noiseJob.Schedule(chunkSize * chunkSize, 64);

        // Schedule Mesh Job
        var meshJob = new MeshJob
        {
            vertices = chunk.vertices,
            triangles = chunk.triangles,
            uvs = chunk.uvs,
            uvOffsets = uvOffsets,
            tiles = chunk.tiles,
            chunkSize = chunkSize,
            tileSize = tileSize,
            tileOffset = tileOffset
        };
        JobHandle meshHandle = meshJob.Schedule(noiseHandle);

        return meshHandle;
    }

    private void FinalizeChunkMesh(Vector2Int chunkCoord, Chunk chunk)
    {
        // Create the mesh
        Mesh mesh = new Mesh
        {
            vertices = chunk.vertices.ToArray(),
            triangles = chunk.triangles.ToArray(),
            uv = chunk.uvs.ToArray()
        };
        chunk.DisposeTempArrays();
        mesh.RecalculateNormals();

        // Create the chunk GameObject
        GameObject chunkObject = new GameObject($"Chunk {chunkCoord}");
        chunkObject.layer = LayerMask.NameToLayer(layer);
        chunk.chunkObjectRef = chunkObject;
        chunkObject.transform.position = ChunkToWorld(chunkCoord);

        // Set the chunk as a child of the parent GameObject
        chunkObject.transform.SetParent(this.transform);

        // Assign mesh to GameObject
        MeshFilter filter = chunkObject.AddComponent<MeshFilter>();
        MeshRenderer renderer = chunkObject.AddComponent<MeshRenderer>();
        renderer.material = tileMaterial;
        filter.mesh = mesh;

        // Add BoxCollider
        BoxCollider collider = chunkObject.AddComponent<BoxCollider>();
        collider.center = new Vector3(chunkSize / 2, 0, chunkSize / 2);
        collider.size = new Vector3(chunkSize, colliderHeight, chunkSize);
    }

    private void UnloadChunk(Vector2Int chunkCoord)
    {
        if (activeChunks.TryGetValue(chunkCoord, out Chunk chunk))
        {
            if (chunk.state == ChunkState.WaitingToLoad)
            {
                // Cancel loading and mark as unloaded
                chunk.state = ChunkState.Unloaded;
                chunk.DisposeTempArrays();
                chunk.DisposePermArrays();
                activeChunks.Remove(chunkCoord);
                OnChunkUnloaded(chunkCoord);
            }
            else if (chunk.state == ChunkState.Loaded)
            {
                // Mark chunk as waiting to unload
                chunk.state = ChunkState.WaitingToUnload;
                chunk.DisposeTempArrays();
                chunk.DisposePermArrays();

                if (chunk.chunkObjectRef != null)
                {
                    Destroy(chunk.chunkObjectRef);
                }

                activeChunks.Remove(chunkCoord);
                OnChunkUnloaded(chunkCoord);
            }
        }
        else
        {
            Debug.LogWarning($"Attempted to unload a chunk that does not exist at {chunkCoord}");
        }
    }

    private void PreComputeUVOffsets()
    {
        uvOffsets = new NativeArray<Vector2>(tileAtlasDimension * tileAtlasDimension, Allocator.Persistent);
        for (int i = 0; i < uvOffsets.Length; i++)
        {
            int x = i % tileAtlasDimension;
            int y = i / tileAtlasDimension;
            uvOffsets[i] = new Vector2(x * tileOffset, y * tileOffset);
        }
    }

    // Retrieves the chunk at the specified chunk coordinate
    public Chunk GetLoadedChunk(Vector2Int chunkCoord)
    {
        if (activeChunks.TryGetValue(chunkCoord, out Chunk chunk))
        {
            if (chunk.state == ChunkState.Loaded)
                return chunk;
        }

        Debug.LogWarning($"Chunk at {chunkCoord} not found!");
        return null; // Return null if the chunk is not loaded
    }

    public Vector2Int WorldToChunk(Vector3 worldPosition)
    {
        int chunkX = Mathf.FloorToInt(worldPosition.x / chunkSize);
        int chunkY = Mathf.FloorToInt(worldPosition.z / chunkSize); // Assuming XZ plane
        return new Vector2Int(chunkX, chunkY);
    }

    public Vector2Int WorldToChunk(Vector2Int worldPosition)
    {
        int chunkX = Mathf.FloorToInt((float)worldPosition.x / chunkSize);
        int chunkY = Mathf.FloorToInt((float)worldPosition.y / chunkSize);
        return new Vector2Int(chunkX, chunkY);
    }

    public Vector3 ChunkToWorld(Vector2Int chunkCoord)
    {
        return new Vector3(chunkCoord.x * chunkSize, 0, chunkCoord.y * chunkSize);
    }

    public static Vector2Int WorldToChunk(Vector3 worldPosition, int chunkSize)
    {
        int chunkX = Mathf.FloorToInt(worldPosition.x / chunkSize);
        int chunkY = Mathf.FloorToInt(worldPosition.z / chunkSize); // Assuming XZ plane
        return new Vector2Int(chunkX, chunkY);
        }

    public static Vector2Int WorldToChunk(Vector2Int worldPosition, int chunkSize)
    {
        int chunkX = Mathf.FloorToInt((float)worldPosition.x / chunkSize);
        int chunkY = Mathf.FloorToInt((float)worldPosition.y / chunkSize);
        return new Vector2Int(chunkX, chunkY);
    }

    public static Vector3 ChunkToWorld(Vector2Int chunkCoord, int chunkSize)
    {
        return new Vector3(chunkCoord.x * chunkSize, 0, chunkCoord.y * chunkSize);
    }

    public void OnChunkLoaded(Vector2Int chunkCoord)
    {
        if (activeChunks.TryGetValue(chunkCoord, out Chunk chunk) && chunk.state == ChunkState.Loaded)
        {
            Entity entity = entityManager.CreateEntity(typeof(ChunkData));            
            entityManager.SetComponentData(entity, new ChunkData
            {
                ChunkCoord = new int2(chunkCoord.x, chunkCoord.y),
                NumTiles = chunk.tiles.Length
            });
            entityManager.AddBuffer<TileBufferElement>(entity); // Add a DynamicBuffer to the entity
            chunkToEntityMap[chunkCoord] = entity; // Update the map

            // Immediately copy the tile data
            CopyTileDataToBuffer(chunkCoord);
            chunk.isDirty = false;
        }
    }

    public void OnChunkUnloaded(Vector2Int chunkCoord)
    {
        if (chunkToEntityMap.TryGetValue(chunkCoord, out Entity entity))
        {
            entityManager.DestroyEntity(entity);
            chunkToEntityMap.Remove(chunkCoord);
        }
    }

    public void UpdateChunkBuffers()
    {
        foreach (KeyValuePair<Vector2Int, Chunk> kvp in activeChunks)
        {
            Vector2Int chunkCoord = kvp.Key;
            Chunk chunk = kvp.Value;

            if (chunk.state == ChunkState.Loaded && chunk.isDirty)
            {
                CopyTileDataToBuffer(chunkCoord);
                chunk.isDirty = false; // Reset the dirty flag after updating
            }
        }
    }

    // updates buffer for ALL tiles in a chunk
    public void CopyTileDataToBuffer(Vector2Int chunkCoord)
    {
        // Ensure the chunk exists and is loaded
        if (!activeChunks.TryGetValue(chunkCoord, out Chunk chunk) || chunk.state != ChunkState.Loaded)
        {
            Debug.LogWarning($"Chunk at {chunkCoord} is not loaded. Skipping buffer update.");
            return;
        }

        // Get the corresponding entity for this chunk
        if (!chunkToEntityMap.TryGetValue(chunkCoord, out Entity entity))
        {
            Debug.LogWarning($"No entity found for chunk at {chunkCoord}. Skipping buffer update.");
            return;
        }

        // Get the DynamicBuffer for the chunk's tile data
        DynamicBuffer<TileBufferElement> buffer = entityManager.GetBuffer<TileBufferElement>(entity);

        // Resize the buffer to match the tile data size
        buffer.ResizeUninitialized(chunk.tiles.Length);

        //buffer.Clear(); // Clear the old data

        // Copy the chunk's tile data into the buffer
        NativeArray<Tile> tiles = chunk.tiles;
        for (int i = 0; i < tiles.Length; i++)
        {
            buffer[i] = new TileBufferElement { TileData = tiles[i] };
            //buffer.Add(new TileBufferElement { TileData = tiles[i] });
        }
    }

    public Tile? GetTileDataFromWorldPosition(Vector3 worldPosition)
    {
        // Step 1: Convert world position to global tile coordinates
        Vector2Int globalTilePosition = new Vector2Int((int)math.floor(worldPosition.x), (int)math.floor(worldPosition.z));

        // Step 2: Find the chunk coordinate
        Vector2Int chunkCoord = new Vector2Int(
            globalTilePosition.x / chunkSize,
            globalTilePosition.y / chunkSize
        );

        // Step 3: Check if the chunk is loaded
        if (!activeChunks.TryGetValue(chunkCoord, out Chunk chunk))
        {
            Debug.LogError($"Chunk at {chunkCoord} is not loaded.");
            return null;
        }

        // Step 4: Calculate the local position within the chunk
        int2 localTilePosition = new int2(
            globalTilePosition.x - chunkCoord.x * chunkSize,
            globalTilePosition.y - chunkCoord.y * chunkSize
        );

        // Step 5: Validate the local position is within bounds
        if (localTilePosition.x < 0 || localTilePosition.x >= chunkSize ||
            localTilePosition.y < 0 || localTilePosition.y >= chunkSize)
        {
            Debug.LogError($"Position {globalTilePosition} is out of bounds for chunk at {chunkCoord}.");
            return null;
        }

        // Step 6: Calculate the tile index
        int tileIndex = localTilePosition.x + localTilePosition.y * chunkSize;

        // Step 7: Retrieve the tile data from the chunk's tile buffer
        if (chunk.tiles.Length > tileIndex)
        {
            return chunk.tiles[tileIndex];
        }

        Debug.LogError($"Tile index {tileIndex} is out of bounds for chunk at {chunkCoord}.");
        return null;
    }

}
