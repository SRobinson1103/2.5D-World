using Unity.Collections;
using UnityEngine;

public class PathfindingDebugger : MonoBehaviour
{
    public ChunkingSystem chunkSystem;
    public bool displayWalkability = false;

    void Update()
    {        
        // 0 - left
        // 1 - right
        // 2 - middle
        if (Input.GetMouseButtonDown(2))
        {
            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            if (Physics.Raycast(ray, out RaycastHit hit))
            {
                Vector3 clickedPoint = hit.point;
                Tile? tile = chunkSystem.GetTileDataFromWorldPosition(clickedPoint);

                if (tile.HasValue)
                {
                    Tile theTile = tile.Value;
                    Debug.Log($"Clicked Point: {clickedPoint}, Tile Type: {theTile.type}, Walkability: {theTile.isWalkable}");
                }
            }
        }
    }

    private void OnDrawGizmos()
    {
        if (chunkSystem == null || chunkSystem.activeChunks == null)
            return;

        //Debug.Log($"Num active chunks {chunkSystem.activeChunks.Count}. ");
        if(!displayWalkability ) { return; }
        foreach (var kvp in chunkSystem.activeChunks)
        {
            Chunk chunk = kvp.Value; // Access the chunk
            //Debug.Log($"Chunk at {chunk.position} has {chunk.tiles.Length} tiles.");
            DisplayWalkability(chunk);
        }
    }

    void DisplayWalkability(Chunk chunk)
    {
        int chunkSize = chunk.chunkSize;
        if (chunk.tiles.IsCreated)
        {
            for (int x = 0; x < chunkSize; x++)
            {
                for (int y = 0; y < chunkSize; y++)
                {
                    int index = x + y * chunkSize;
                    Tile tile = chunk.tiles[index];

                    if (tile.isWalkable)
                    {
                        Vector3 worldPosition = new Vector3(
                            chunk.position.x * chunkSize + x + chunkSystem.tileSize * 0.5f,
                            0.5f, // height above the ground
                            chunk.position.y * chunkSize + y + chunkSystem.tileSize * 0.5f
                        );

                        // Draw a cube at the walkable tile position
                        Debug.DrawLine(worldPosition, worldPosition + Vector3.up, Color.green, 1.0f);
                    }
                }
            }
        }
    }
}
