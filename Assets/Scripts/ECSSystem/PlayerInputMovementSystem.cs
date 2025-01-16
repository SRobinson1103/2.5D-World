using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

[UpdateInGroup(typeof(SimulationSystemGroup))]
public partial class PlayerInputMovementSystem : SystemBase
{
    protected override void OnUpdate()
    {
        float deltaTime = SystemAPI.Time.DeltaTime;

        Entities
        .ForEach((ref LocalTransform transform, ref VelocityComponent velocity) =>
        {
            if (!velocity.IsMoving)
                return;

            transform.Position += velocity.Direction * velocity.Speed * deltaTime;

        }).ScheduleParallel();
    }
}
