using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
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
    public Vector2Int position;    // Chunk's grid position
    public NativeArray<Tile> tiles; // Flattened array
    public Mesh mesh;              // Combined mesh for the chunk
    public bool isDirty = false;   // Indicates if the chunk needs updating
    public GameObject chunkObjectRef; // stores a reference
    public ChunkState state;

    //temp arrays for job processing
    public NativeArray<Vector3> Vertices;
    public NativeArray<int> Triangles;
    public NativeArray<Vector2> UVs;

    public Chunk(int chunkSize)
    {
        state = ChunkState.Unloaded;
        tiles = new NativeArray<Tile>(chunkSize * chunkSize, Allocator.Persistent);
    }

    public void InitTempArrays(int chunkSize)
    {
        Vertices = new NativeArray<Vector3>(chunkSize * chunkSize * 4, Allocator.Persistent);
        Triangles = new NativeArray<int>(chunkSize * chunkSize * 6, Allocator.Persistent);
        UVs = new NativeArray<Vector2>(chunkSize * chunkSize * 4, Allocator.Persistent);
    }

    public void DisposeTempArrays()
    {
        if (Vertices.IsCreated) Vertices.Dispose();
        if (Triangles.IsCreated) Triangles.Dispose();
        if (UVs.IsCreated) UVs.Dispose();
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

public struct Tile
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
    public int chunkSize = 16;     // Size of each chunk (16x16 tiles)
    public int loadRadius = 2;     // Number of chunks to load around the camera
    public float colliderHeight = 0.1f;
    public float noiseScale = 0.1f;
    public UnityEngine.Material tileMaterial; // Material for rendering the tiles
    public int tileAtlasDimension = 8;
    public float tileSize = 1.0f;

    private float tileOffset = 0.125f;
    private NativeArray<Vector2> uvOffsets;
    NativeArray<JobHandle> handles;

    private Transform cameraTransform; // Reference to the camera
    private Dictionary<Vector2Int, Chunk> activeChunks = new Dictionary<Vector2Int, Chunk>();

    private Vector2Int lastCameraChunk = Vector2Int.zero;
    private int worldSeed = -1;

    void Start()
    {
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
        if (handles.IsCreated) handles.Dispose();
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
        handles = new NativeArray<JobHandle>(chunksToLoad.Count, Allocator.Temp);
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
                Chunk newChunk = new Chunk(chunkSize)
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
            }
        }
    }

    private JobHandle ScheduleChunkJob(Vector2Int chunkCoord, Chunk chunk)
    {
        // Allocate NativeArrays for mesh data
        chunk.InitTempArrays(chunkSize);

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
            vertices = chunk.Vertices,
            triangles = chunk.Triangles,
            uvs = chunk.UVs,
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
            vertices = chunk.Vertices.ToArray(),
            triangles = chunk.Triangles.ToArray(),
            uv = chunk.UVs.ToArray()
        };
        chunk.DisposeTempArrays();
        mesh.RecalculateNormals();

        // Create the chunk GameObject
        GameObject chunkObject = new GameObject($"Chunk {chunkCoord}");
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
                chunk.DisposeTempArrays();
                chunk.DisposePermArrays();
                activeChunks.Remove(chunkCoord);
                return;
            }

            if (chunk.state == ChunkState.Loaded)
            {
                // Mark chunk as waiting to unload
                chunk.state = ChunkState.WaitingToUnload;

                if (chunk.chunkObjectRef != null)
                {
                    chunk.DisposeTempArrays();
                    chunk.DisposePermArrays();
                    Destroy(chunk.chunkObjectRef);
                }

                activeChunks.Remove(chunkCoord);
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

    public Vector2Int WorldToChunk(Vector3 worldPosition)
    {
        int chunkX = Mathf.FloorToInt(worldPosition.x / chunkSize);
        int chunkY = Mathf.FloorToInt(worldPosition.z / chunkSize); // Assuming XZ plane
        return new Vector2Int(chunkX, chunkY);
    }

    public Vector3 ChunkToWorld(Vector2Int chunkCoord)
    {
        return new Vector3(chunkCoord.x * chunkSize, 0, chunkCoord.y * chunkSize);
    }
}
    