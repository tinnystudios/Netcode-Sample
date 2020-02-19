using Unity.Entities;
using UnityEngine;

public class TestCommandDataAuthoringComponent : MonoBehaviour, IConvertGameObjectToEntity
{
    public void Convert(Entity entity, EntityManager dstManager, GameObjectConversionSystem conversionSystem)
    {
        dstManager.AddBuffer<TestCommandData>(entity);
    }
}
