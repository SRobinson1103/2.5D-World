using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

[BurstCompile]
public struct MeshJob : IJob
{
    public NativeArray<Vector3> vertices; // output
    public NativeArray<int> triangles; // output
    public NativeArray<Vector2> uvs; // output

    [ReadOnly] public NativeArray<Vector2> uvOffsets; // input
    [ReadOnly] public NativeArray<Tile> tiles; // input

    public int chunkSize;
    public float tileSize;
    public float tileOffset;

    public void Execute()
    {
        for (int index = 0; index < chunkSize * chunkSize; index++)
        {
            int x = index % chunkSize;
            int y = index / chunkSize;

            int vertexIndex = index * 4;
            int triangleIndex = index * 6;

            // Define vertices
            vertices[vertexIndex + 0] = new Vector3(x * tileSize, 0.0f, y * tileSize);
            vertices[vertexIndex + 1] = new Vector3((x + 1) * tileSize, 0.0f, y * tileSize);
            vertices[vertexIndex + 2] = new Vector3((x + 1) * tileSize, 0.0f, (y + 1) * tileSize);
            vertices[vertexIndex + 3] = new Vector3(x * tileSize, 0.0f, (y + 1) * tileSize);

            // Define triangles
            triangles[triangleIndex + 0] = vertexIndex + 0;
            triangles[triangleIndex + 1] = vertexIndex + 2;
            triangles[triangleIndex + 2] = vertexIndex + 1;
            triangles[triangleIndex + 3] = vertexIndex + 0;
            triangles[triangleIndex + 4] = vertexIndex + 3;
            triangles[triangleIndex + 5] = vertexIndex + 2;

            // Define UVs
            int atlasIndex = (int)tiles[index].type;
            uvs[vertexIndex + 0] = uvOffsets[atlasIndex] + new Vector2(0, 0);
            uvs[vertexIndex + 1] = uvOffsets[atlasIndex] + new Vector2(tileOffset, 0);
            uvs[vertexIndex + 2] = uvOffsets[atlasIndex] + new Vector2(tileOffset, tileOffset);
            uvs[vertexIndex + 3] = uvOffsets[atlasIndex] + new Vector2(0, tileOffset);
        }
    }
}