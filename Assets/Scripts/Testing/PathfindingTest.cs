using UnityEngine;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using static UnityEngine.EventSystems.EventTrigger;
using Unity.Entities.UniversalDelegates;
using System.Collections.Generic;

public class PathfindingTest : MonoBehaviour
{
    private EntityManager entityManager;
    private Entity characterEntity;
    private PathfindingSystem pathfindingSystem;
    public ChunkingSystem chunkingSystem;
    private bool pathRequestedThisFrame;

    public GameObject pathPointPrefab; // Assign a prefab (e.g., a small sphere) in the Inspector
    private List<GameObject> pathMarkers = new List<GameObject>();

    void Start()
    {
        // Get the EntityManager and PathfindingSystem
        entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;
        pathfindingSystem = World.DefaultGameObjectInjectionWorld.GetOrCreateSystemManaged<PathfindingSystem>();
        pathRequestedThisFrame = false;

        if (pathfindingSystem == null )
        {
            Debug.Log("PathfindingTest: pathfindingSystem null");
            return;
        }
        if (entityManager == null )
        {
            Debug.Log("PathfindingTest: entityManager null");
            return;
        }
        if(chunkingSystem == null)
        {
            Debug.Log("PathfindingTest: chunkingSystem null");
            return;
        }

        // Manually create an entity
        characterEntity = entityManager.CreateEntity(
            typeof(LocalTransform),
            typeof(PathfindingConfig),
            typeof(PathfindingRequest),
            typeof(PathfindingResult)
        );

        // Set initial position
        entityManager.SetComponentData(characterEntity, new LocalTransform
        {
            Position = new float3(0, 0, 0),
            Rotation = quaternion.identity,
            Scale = 1
        });
        //set initial request
        entityManager.SetComponentData(characterEntity, new PathfindingRequest
        {
            GlobalStartPosition = float2.zero,
            GlobalTargetPosition = new float2 { x = 11, y = 12}
        });
        //set chunk size
        entityManager.SetComponentData(characterEntity, new PathfindingConfig
        {
            ChunkSize = chunkingSystem.chunkSize
        });
        entityManager.AddBuffer<PathPointBufferElement>(characterEntity); // Add a DynamicBuffer to the entity

    }

    void Update()
    {
        // Check for mouse click
        if (Input.GetMouseButtonDown(0))
        {
            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            if (Physics.Raycast(ray, out RaycastHit hit))
            {
                Vector3 targetPosition = hit.point;
                //Debug.Log($"Clicked on position: {targetPosition}");
                //Debug.Log($"Clicked on Chunk: {chunkingSystem.WorldToChunk(targetPosition)}");
                // Send a pathfinding request
                SendPathfindingRequest(characterEntity, targetPosition);
                pathRequestedThisFrame = true;
            }
        }
    }

    private void LateUpdate()
    {
        if (!pathRequestedThisFrame)
            return;

        PathfindingResult result = entityManager.GetComponentData<PathfindingResult>(characterEntity);
        if(result.Success)
        {
            DynamicBuffer<PathPointBufferElement> pathBuffer = entityManager.GetBuffer<PathPointBufferElement>(characterEntity);
            int finalPositionIndex = result.PathLength - 1;

            // Update fields and write back to the entity
            //Debug.Log($"PathfindingTest: Moving entity to: {pathBuffer[finalPositionIndex].Position}");
            LocalTransform transform = entityManager.GetComponentData<LocalTransform>(characterEntity);
            transform.Position = new float3(pathBuffer[finalPositionIndex].Position.x, 0f, pathBuffer[finalPositionIndex].Position.y);
            entityManager.SetComponentData(characterEntity, transform);

            DisplayPath(pathBuffer);
            //printPath(pathBuffer);
        }
        else
        {
            Debug.Log("PathfindingTest: Pathfinding request failed.");
        }

        //remove active request tag to prevent duplicate requests
        entityManager.RemoveComponent<PathfindingActiveTag>(characterEntity);
        pathRequestedThisFrame = false;
    }

    void SendPathfindingRequest(Entity entity, Vector3 targetPosition)
    {
        if (!entityManager.Exists(entity))
        {
            Debug.LogError("PathfindingTest: Entity does not exist!");
            return;
        }

        if (!entityManager.HasComponent<PathfindingRequest>(entity))
        {
            Debug.LogError("PathfindingTest: Entity does not have the PathfindingRequest component!");
            return;
        }

        float3 startPosition = entityManager.GetComponentData<LocalTransform>(entity).Position;

        // Update existing PathfindingRequest
        entityManager.SetComponentData(entity, new PathfindingRequest
        {
            GlobalStartPosition = new float2(startPosition.x, startPosition.z),
            GlobalTargetPosition = new float2(targetPosition.x, targetPosition.z)
        });

        // Add the PathfindingActiveTag to enable processing
        if (!entityManager.HasComponent<PathfindingActiveTag>(characterEntity))
        {
            entityManager.AddComponent<PathfindingActiveTag>(characterEntity);
        }
    }

    void DisplayPath(DynamicBuffer<PathPointBufferElement> pathBuffer)
    {
        // Clear existing markers
        foreach (GameObject marker in pathMarkers)
        {
            Destroy(marker);
        }
        pathMarkers.Clear();

        // Create markers for each path point
        for (int i = 0; i < pathBuffer.Length; i++)
        {
            float3 position = new float3(pathBuffer[i].Position.x, 0f, pathBuffer[i].Position.y);
            GameObject marker = Instantiate(pathPointPrefab, position, Quaternion.identity);
            pathMarkers.Add(marker);


        }

        for (int i = 0; i < pathBuffer.Length - 1; i++)
        {
            Vector3 start = new Vector3(pathBuffer[i].Position.x, 0.5f, pathBuffer[i].Position.y); // Slightly above ground
            Vector3 end = new Vector3(pathBuffer[i + 1].Position.x, 0.5f, pathBuffer[i + 1].Position.y);
            Debug.DrawLine(start, end, Color.yellow, 10f, false); // Draw the path with green lines
        }
    }

    private void printPath(DynamicBuffer<PathPointBufferElement> pathBuffer)
    {
        string path = "";
        for (int i = 0; i < pathBuffer.Length; i++)
        {
            path += pathBuffer[i].Position.ToString();
        }
        Debug.Log($"PathfindingTest: Path = {path}.");
    }
}
