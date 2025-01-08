using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;

public class Chunk
{
    public Vector2Int position;    // Chunk's grid position
    public NativeArray<Tile> tiles; // Flattened array
    //public Tile[,] tiles;          // 2D array of tiles in the chunk
    public Mesh mesh;              // Combined mesh for the chunk
    public bool isDirty = false;   // Indicates if the chunk needs updating

    public GameObject chunkObjectRef; //store reference

    public Chunk(int chunkSize)
    {
        //tiles = new Tile[chunkSize, chunkSize];
        tiles = new NativeArray<Tile>(chunkSize * chunkSize, Allocator.Persistent);
    }
}

//value is index in tile atlas; left to right and bottom to top
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
public class ChunkingSystem : MonoBehaviour
{
    public int chunkSize = 16;     // Size of each chunk (16x16 tiles)
    public int loadRadius = 2;     // Number of chunks to load around the camera
    public float colliderHeight = 0.1f;
    public float noiseScale = 0.1f;
    public UnityEngine.Material tileMaterial; // Material for rendering the tiles
    
    public int TileAtlasDimension = 8;
    private float TileOffset = 0.125f;
    private Vector2[] uvOffsets;

    private Transform cameraTransform; // Reference to the camera
    private Dictionary<Vector2Int, Chunk> activeChunks = new Dictionary<Vector2Int, Chunk>();

    private Vector2Int lastCameraChunk = Vector2Int.zero;
    //TODO: use the worldseed
    private int worldSeed = -1;

    void Start()
    {
        worldSeed = WorldManager.Instance.GetWorldSeed();
        cameraTransform = Camera.main.transform;
        tileMaterial.enableInstancing = true;
        TileOffset = 1.0f / TileAtlasDimension;

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

                if (!activeChunks.ContainsKey(chunkCoord))
                {
                    LoadChunk(chunkCoord);
                }
            }
        }

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

    private void LoadChunk(Vector2Int chunkCoord)
    {
        Chunk chunk = new Chunk(chunkSize);
        InitChunkData(chunk, chunkCoord);

        // Create the chunk GameObject
        GameObject chunkObject = new GameObject($"Chunk {chunkCoord}");
        chunkObject.transform.position = ChunkToWorld(chunkCoord);

        //save reference
        chunk.chunkObjectRef = chunkObject;

        // Assign the Terrain layer
        chunkObject.layer = LayerMask.NameToLayer("Terrain");

        // Add MeshRenderer and MeshFilter
        MeshRenderer renderer = chunkObject.AddComponent<MeshRenderer>();
        MeshFilter filter = chunkObject.AddComponent<MeshFilter>();

        // Generate the chunk mesh
        filter.mesh = CreateChunkMesh(chunk, 1f); // Tile size = 1 unit
        renderer.material = tileMaterial;

        // Add a BoxCollider to the chunk
        UnityEngine.BoxCollider collider = chunkObject.AddComponent<UnityEngine.BoxCollider>();
        collider.center = new Vector3(chunkSize / 2, 0, chunkSize / 2); // Center at the chunk
        collider.size = new Vector3(chunkSize, colliderHeight, chunkSize); // Match chunk dimensions

        activeChunks[chunkCoord] = chunk;
    }

    private void UnloadChunk(Vector2Int chunkCoord)
    {
        if (activeChunks.TryGetValue(chunkCoord, out Chunk chunk))
        {
            if (chunk.chunkObjectRef != null)
            {
                if (chunk.tiles.IsCreated)
                {
                    chunk.tiles.Dispose();
                }
                Destroy(chunk.chunkObjectRef);
            }

            activeChunks.Remove(chunkCoord);
        }
        else
        {
            Debug.LogWarning($"Failed to unload chunk at {chunkCoord}");
        }
    }

    private void InitChunkData(Chunk chunk, Vector2Int chunkCoord)
    {
        // Create chunk data
        chunk.position = chunkCoord;

        // Generate random tile data for this example
        int index = 0;
        for (int x = 0; x < chunkSize; x++)
        {
            for (int y = 0; y < chunkSize; y++)
            {
                float noiseValue = Mathf.PerlinNoise(x * noiseScale, y * noiseScale) +
                   0.5f * Mathf.PerlinNoise(x * noiseScale * 2, y * noiseScale * 2);

                TileType tileType;
                bool walkable;
                if (noiseValue < 0.3f)
                {
                    tileType = TileType.WATER;
                    walkable = false;
                }
                else if (noiseValue < 0.5f)
                {
                    tileType = TileType.DIRT;
                    walkable = true;
                }
                else if (noiseValue < 0.8f)
                {
                    tileType = TileType.GRASS;
                    walkable = true;
                }
                else
                {
                    tileType = TileType.STONE;
                    walkable = true;
                }

                chunk.tiles[index++] = new Tile()
                {
                    type = tileType,
                    isWalkable = walkable
                };
            }
        }
    }

    private Mesh CreateChunkMesh(Chunk chunk, float tileSize)
    {
        Mesh mesh = new Mesh();

        List<Vector3> vertices = new List<Vector3>();
        List<int> triangles = new List<int>();
        List<Vector2> uv = new List<Vector2>();

        int vertexIndex = 0;

        int index = 0;
        for (int x = 0; x < chunkSize; x++)
        {
            for (int y = 0; y < chunkSize; y++)
            {
                // Tile world position
                float worldX = x * tileSize;
                float worldY = y * tileSize;

                // Add quad vertices
                vertices.Add(new Vector3(worldX, 0, worldY));               // Bottom-left
                vertices.Add(new Vector3(worldX + tileSize, 0, worldY));    // Bottom-right
                vertices.Add(new Vector3(worldX + tileSize, 0, worldY + tileSize)); // Top-right
                vertices.Add(new Vector3(worldX, 0, worldY + tileSize));    // Top-left

                // Add triangles
                triangles.Add(vertexIndex + 0);
                triangles.Add(vertexIndex + 2);
                triangles.Add(vertexIndex + 1);
                triangles.Add(vertexIndex + 0);
                triangles.Add(vertexIndex + 3);
                triangles.Add(vertexIndex + 2);

                // Calculate UV coordinates for the tile
                int textureIndex = (int)chunk.tiles[index].type;
                Vector2 uvOffset = GetUVOffset(textureIndex);
                uv.Add(uvOffset + new Vector2(0, 0)); // Bottom-left
                uv.Add(uvOffset + new Vector2(TileOffset, 0)); // Bottom-right
                uv.Add(uvOffset + new Vector2(TileOffset, TileOffset)); // Top-right
                uv.Add(uvOffset + new Vector2(0, TileOffset)); // Top-left

                vertexIndex += 4;
                ++index;
            }
        }

        mesh.vertices = vertices.ToArray();
        mesh.triangles = triangles.ToArray();
        mesh.uv = uv.ToArray();
        mesh.RecalculateNormals();

        return mesh;
    }

    private Vector2 GetUVOffset(int textureIndex)
    {
        return uvOffsets[textureIndex];
    }

    private void PreComputeUVOffsets()
    {
        uvOffsets = new Vector2[TileAtlasDimension * TileAtlasDimension];
        for (int i = 0; i < uvOffsets.Length; i++)
        {
            int x = i % TileAtlasDimension;
            int y = i / TileAtlasDimension;
            uvOffsets[i] = new Vector2(x * TileOffset, y * TileOffset);
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
