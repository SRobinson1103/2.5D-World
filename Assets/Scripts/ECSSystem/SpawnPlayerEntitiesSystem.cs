using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using Unity.Burst;
using Unity.Collections;

[BurstCompile]
[UpdateInGroup(typeof(PresentationSystemGroup))]
public partial struct SpawnerSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate(state.GetEntityQuery(typeof(PlayerSpawnTag)));
    }

    public void OnUpdate(ref SystemState state)
    {
        UnityEngine.Debug.Log("SpawnerSystem onupdate");
        EntityCommandBuffer ecb = new EntityCommandBuffer(Allocator.Temp);

        // For each entity that has a Spawner component...
        foreach (var (spawner, entity) in SystemAPI.Query<RefRW<PlayerSpawner>>().WithEntityAccess())
        {
            // If Prefab is valid (not Entity.Null), instantiate
            if (spawner.ValueRO.Prefab != Entity.Null)
            {
                Entity instance = ecb.Instantiate(spawner.ValueRO.Prefab);

                ecb.AddComponent<PlayableCharacter>(instance);
                ecb.AddComponent<PathfindingRequest>(instance);
                ecb.AddComponent<VelocityComponent>(instance);

                ecb.SetComponent(instance, new PlayableCharacter { /* Initialize fields if needed */ });
                ecb.SetComponent(instance, new PathfindingRequest { /* Initialize fields if needed */ });
                ecb.SetComponent(instance, new VelocityComponent { Speed = 1.0f, IsMoving = false });
                ecb.SetComponent(instance, LocalTransform.FromPositionRotationScale(float3.zero, quaternion.identity, 1f));

                ecb.RemoveComponent<PlayerSpawnTag>(instance);
            }
        }

        ecb.Playback(state.EntityManager);
        ecb.Dispose();
    }
}
