using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

public struct VelocityComponent : IComponentData
{
    public float Speed;
    public float3 Direction;
    public bool IsMoving;
}

public struct PlayableCharacter : IComponentData
{
    public int CharacterId; // Unique ID for each character
    public float Speed; // Movement speed
    public bool IsSelected; // For player interaction
}

public struct CharacterStats : IComponentData
{
    public int Stamina;
    public int MaxStamina;
    public int Strength;
    public int Dexterity;
    public int Endurance;
    public int Intelligence;
    public int Charisma;
}

public struct CharacterData : IComponentData
{
    FixedString64Bytes Name; //unicode apparently
}

public struct RenderComponent : IComponentData
{
    public Entity RenderEntity; // Reference to a child GameObject entity for rendering
}
