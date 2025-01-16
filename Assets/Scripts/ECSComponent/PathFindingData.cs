using Unity.Entities;
using Unity.Mathematics;

/*
 * Which Should You Use?

    Use a Tag Component (PathfindingActiveTag) if:
        Most entities with a PathfindingRequest component are inactive at any given time.
        You prefer archetype-based filtering for cleaner separation of active and inactive entities.
        You want to use event-based activation and modular systems.

    Use a Boolean (IsActive) if:
        Many entities frequently toggle between active and inactive states, and performance of archetype changes is a concern.
        Pathfinding activity is a part of a broader component that holds other state data.
        Simplicity is a priority.
 */
//tags active pathfinding requests
//TODO: Consider switching to bool
public struct PathfindingRequestActiveTag : IComponentData { }

public struct PathfindingConfigSingleton : IComponentData
{
    public int ChunkSize;
}

// Pathfinding request component
public struct PathfindingRequest : IComponentData
{
    public float2 GlobalStartPosition;
    public float2 GlobalTargetPosition;
}

[InternalBufferCapacity(64)] // Adjust capacity as needed
public struct PathPointBufferElement : IBufferElementData
{
    public float2 Position;
}

// Pathfinding result component
public struct PathfindingResult : IComponentData
{
    public int PathLength;
    public bool Success;
    public int NextUnprocessedIndex;
    public bool FinishedProcessing;
}
