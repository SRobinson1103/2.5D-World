using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

//mark entities to be spawned
public struct PlayerSpawnTag : IComponentData 
{
    public float3 Position;
}

public struct PlayerSpawner : IComponentData
{
    public Entity Prefab;
    // Add whatever else you need here (e.g., spawn count, random seed, etc.)
}

/// Attach this to a GameObject (e.g., a prefab or object in a SubScene)
/// to mark it for conversion into an ECS entity with a Spawner component.
[DisallowMultipleComponent]
public class PlayerSpawnerAuthoring : MonoBehaviour
{
    [Tooltip("A reference to the prefab you want to spawn as an Entity.")]
    public GameObject prefab;

    /// This nested Baker class runs at edit time (or Live Conversion)
    /// to convert the above fields into ECS components.
    class Baker : Baker<PlayerSpawnerAuthoring>
    {
        public override void Bake(PlayerSpawnerAuthoring authoring)
        {
            // Retrieve the Entity representing *this* GameObject
            // in the conversion world, with appropriate Transform usage flags.
            var entity = GetEntity(TransformUsageFlags.Dynamic);

            // Convert the referenced prefab into an Entity as well.
            var prefabEntity = GetEntity(authoring.prefab, TransformUsageFlags.Dynamic);

            // Add the Spawner IComponentData to the entity.
            AddComponent(entity, new PlayerSpawner
            {
                Prefab = prefabEntity,
            });
        }
    }
}
