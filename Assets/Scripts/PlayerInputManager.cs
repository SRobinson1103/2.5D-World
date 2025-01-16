using UnityEngine;
using Unity.Entities;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Transforms;

public class PlayerInputManager : MonoBehaviour
{
    private EntityManager entityManager;
    private EntityQueryDesc movablePlayerCharacterQueryDesc;

    void Start()
    {
        entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;

        movablePlayerCharacterQueryDesc = new EntityQueryDesc
        {
            // The components the entity must have:
            All = new[]
            {
                ComponentType.ReadOnly<PlayableCharacter>(),
                ComponentType.ReadOnly<PathfindingRequest>(),
                ComponentType.ReadOnly<PathfindingResult>(),
                ComponentType.ReadOnly<LocalTransform>(),
                ComponentType.ReadOnly<VelocityComponent>()
            },

            // The components the entity must NOT have:
            None = new[]
            {
                ComponentType.ReadOnly<PlayerSpawnTag>()
            }
        };
    }

    void Update()
    {
        if (Input.GetMouseButtonDown(2)) // middle click
        {
            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            if (Physics.Raycast(ray, out RaycastHit hit))
            {
                float3 pos = hit.point;
                Entity instance = entityManager.CreateEntity();
                entityManager.AddComponent<PlayerSpawnTag>(instance);
                entityManager.SetComponentData(instance, new PlayerSpawnTag { Position = pos });
            }

            return;
        }
        else if (Input.GetMouseButtonDown(1)) // right-click for movement
        {
            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            if (Physics.Raycast(ray, out RaycastHit hit))
            {
                Vector3 targetPosition = hit.point;                
                IssuePathfindingRequest(new float2(targetPosition.x, targetPosition.z));
            }

            return;
        }
    }

    private void IssuePathfindingRequest(float2 targetPosition)
    {
        //Debug.Log($"PlayerInputManager IssuePathfindingRequest to {targetPosition}");

        EntityQuery movablePlayerCharacterQuery = entityManager.CreateEntityQuery(movablePlayerCharacterQueryDesc);
        NativeArray<Entity> entities = movablePlayerCharacterQuery.ToEntityArray(Allocator.Temp);

        foreach (Entity entity in entities)
        {
            Debug.Log("Adding pathfinding request for entity.");
            LocalTransform position = entityManager.GetComponentData<LocalTransform>(entity);
            PathfindingRequest request = entityManager.GetComponentData<PathfindingRequest>(entity);
            
            request.GlobalStartPosition = new float2(position.Position.x, position.Position.z);
            request.GlobalTargetPosition = targetPosition;
            entityManager.SetComponentData(entity, request);

            // Add the PathfindingActiveTag to enable processing
            if (!entityManager.HasComponent<PathfindingRequestActiveTag>(entity))
            {
                entityManager.AddComponent<PathfindingRequestActiveTag>(entity);
            }
        }

        entities.Dispose();
    }
}
