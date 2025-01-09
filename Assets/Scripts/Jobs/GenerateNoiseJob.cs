using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

[BurstCompile]
public struct GenerateChunkTilesJob : IJobParallelFor
{
    public NativeArray<Tile> tiles; //Output Tile Values
    public int chunkSize;
    public float noiseScale;
    public int worldOffsetX;
    public int worldOffsetY;

    public void Execute(int index)
    {
        int x = index % chunkSize;
        int y = index / chunkSize;
        float noiseValue  = Mathf.PerlinNoise(
            (x + worldOffsetX) * noiseScale,
            (y + worldOffsetY) * noiseScale
        ) + 0.5f * Mathf.PerlinNoise(
            (x + worldOffsetX) * noiseScale * 2,
            (y + worldOffsetY) * noiseScale * 2
        );

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

        tiles[index] = new Tile()
        {
            type = tileType,
            isWalkable = walkable
        };
    }
}
