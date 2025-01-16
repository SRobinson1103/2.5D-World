using Unity.Entities;
using Unity.Mathematics;

public struct ChunkData : IComponentData
{
    public int2 ChunkCoord; // chunk-relative coordinates
    public int NumTiles;
}

[InternalBufferCapacity(64)] // TODO: Adjust based on average chunk size
public struct TileBufferElement : IBufferElementData
{
    public Tile TileData;
}
