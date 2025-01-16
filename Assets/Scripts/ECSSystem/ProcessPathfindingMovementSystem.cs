using System.Diagnostics;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

[UpdateInGroup(typeof(SimulationSystemGroup))]
public partial class ProcessPathfindingMovementSystem : SystemBase
{
    protected override void OnCreate()
    {
        base.OnCreate();


        // For simplicity, we spawn entities on system creation
        // In a real game, you can trigger this with an event
        RequireForUpdate(GetEntityQuery(typeof(PathfindingResult)));
        RequireForUpdate(GetEntityQuery(typeof(VelocityComponent)));
    }

    protected override void OnUpdate()
    {
        float deltaTime = SystemAPI.Time.DeltaTime;
        float epsilon = 0.001f;
        BufferLookup<PathPointBufferElement> pathResultBufferLookup = GetBufferLookup<PathPointBufferElement>(false);

        Entities
        .WithBurst()
        .WithNativeDisableContainerSafetyRestriction(pathResultBufferLookup)
        .ForEach((Entity entity, ref LocalTransform transform, ref VelocityComponent velocity, ref PathfindingResult result) =>
        {
            UnityEngine.Debug.Log($"PathfindingEntityMovementSystem : Processing movement.");

            // Prevent reprocessing processed paths
            if (result.FinishedProcessing || !result.Success)
                return;

            // the first index is the current position of the entity
            // skip it to avoid math errors
            if (result.NextUnprocessedIndex == 0)
                result.NextUnprocessedIndex++;

            // Protect against single point path length
            if (result.PathLength == 1)
                return;

            // Make sure the buffer exists
            if (!pathResultBufferLookup.HasBuffer(entity))
                UnityEngine.Debug.Log($"PathfindingEntityMovementSystem: Entity does not contain pathfiniding results buffer");

            // Get the entities path buffer
            DynamicBuffer<PathPointBufferElement> pathResultBuffer = pathResultBufferLookup[entity];

            // Calculate the direction from the current position to the next waypoint
            transform.Position.y = 0f; //ensure y is always 0
            float2 destination2 = pathResultBuffer[result.NextUnprocessedIndex].Position;
            float3 destination3 = new float3(destination2.x, 0f, destination2.y);
            velocity.Direction = destination3 - transform.Position;
            velocity.Direction = math.normalize(velocity.Direction);

            //UnityEngine.Debug.Log($"transform.Position: {transform.Position}, destination3: {destination3}, velocity.Direction: {velocity.Direction}, final: {final}");

            // Update the position
            transform.Position += velocity.Direction * velocity.Speed * deltaTime;

            // Check if the waypoint in the path has been reached
            if (math.length(destination3 - transform.Position) < epsilon)
                result.NextUnprocessedIndex++;

            // Finish processing if the final index has been processed
            if (result.NextUnprocessedIndex == result.PathLength)
                result.FinishedProcessing = true;

        }).ScheduleParallel();

        Dependency.Complete();
    }
}
