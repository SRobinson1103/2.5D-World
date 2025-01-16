using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using Unity.Burst;
using Unity.Collections;

[BurstCompile]
[UpdateInGroup(typeof(InitializationSystemGroup))]
public partial struct SpawnerSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate(state.GetEntityQuery(typeof(PlayerSpawner)));
        state.RequireForUpdate(state.GetEntityQuery(typeof(PlayerSpawnTag)));
    }

    public void OnUpdate(ref SystemState state)
    {
        if (!SystemAPI.HasSingleton<PlayerSpawner>())
            return;

        PlayerSpawner spawner = SystemAPI.GetSingleton<PlayerSpawner>();

        EntityCommandBuffer ecb = new EntityCommandBuffer(Allocator.Temp);

        // For each entity that has a PlayerSpawnTag, spawn a prefab
        // reading its Position from the PlayerSpawnTag
        foreach (var (spawnTag, spawnTagEntity) in SystemAPI.Query<RefRO<PlayerSpawnTag>>().WithEntityAccess())
        {
            Entity instance = ecb.Instantiate(spawner.Prefab);

            ecb.AddComponent<PlayableCharacter>(instance);
            ecb.AddComponent<PathfindingRequest>(instance);
            ecb.AddComponent<PathfindingResult>(instance);
            ecb.AddComponent<VelocityComponent>(instance);
            ecb.AddBuffer<PathPointBufferElement>(instance);

            float characterSpeed = 1.0f;
            //ecb.SetComponent(instance, new PathfindingRequest());

            ecb.SetComponent(instance, new PlayableCharacter { Speed = characterSpeed });
            ecb.SetComponent(instance, new VelocityComponent { Speed = characterSpeed });
            ecb.SetComponent(instance, LocalTransform.FromPositionRotationScale(spawnTag.ValueRO.Position, quaternion.identity, 1f));

            ecb.DestroyEntity(spawnTagEntity);
        }

        // Playback the ECB
        ecb.Playback(state.EntityManager);
        ecb.Dispose();
    }
}
