using Unity.Entities;
using UnityEngine;

public class WorldManager : Singleton<WorldManager>
{
    [SerializeField] private int seed = 12345;
    public bool initializeSeedForECS = true;

    protected override void OnAwake()
    {

        if (World.DefaultGameObjectInjectionWorld == null)
        {
            Debug.LogError("WorldManager: No GameObjectInjectionWorld found. Ensure ECS is initialized.");
            return;
        }

        if (initializeSeedForECS)
            InitECSWorldSeed();
    }

    private void InitECSWorldSeed()
    {
        EntityManager entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;

        // Create the seed entity for ECS
        Entity seedEntity = entityManager.CreateEntity(typeof(WorldSeed));
        entityManager.SetComponentData(seedEntity, new WorldSeed { Seed = seed });

        Debug.Log("WorldManager : World seed initialized: " + seed);
    }

    public int GetWorldSeed()
    {
        return seed;
    }
}
