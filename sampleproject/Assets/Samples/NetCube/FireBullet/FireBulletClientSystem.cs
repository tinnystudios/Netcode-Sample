using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Networking.Transport;
using Unity.Transforms;
using UnityEngine;

[UpdateInGroup(typeof(ClientSimulationSystemGroup))]
[UpdateBefore(typeof(AsteroidsCommandSendSystem))]
[UpdateAfter(typeof(GhostSimulationSystemGroup))]
public class FireBulletInputSystem : JobComponentSystem
{
    private BeginSimulationEntityCommandBufferSystem m_Barrier;
    private GhostPredictionSystemGroup m_GhostPredict;
    private int frameCount;

    protected override void OnCreate()
    {
        m_GhostPredict = World.GetOrCreateSystem<GhostPredictionSystemGroup>();
        m_Barrier = World.GetOrCreateSystem<BeginSimulationEntityCommandBufferSystem>();
    }

    protected override JobHandle OnUpdate(JobHandle inputDeps)
    {
        var playerJob = new PlayerInputJob();
        playerJob.shoot = 0;

        playerJob.commandBuffer = m_Barrier.CreateCommandBuffer().ToConcurrent();
        playerJob.inputFromEntity = GetBufferFromEntity<TestCommandData>();
        playerJob.inputTargetTick = m_GhostPredict.PredictingTick;

        if (Input.GetKeyDown("g"))
        {
            Debug.Log("g pressed");
            playerJob.shoot = 1;
        }

        var handle = playerJob.ScheduleSingle(this, inputDeps);
        m_Barrier.AddJobHandleForProducer(handle);
        return handle;
    }

    [ExcludeComponent(typeof(NetworkStreamDisconnected))]
    [RequireComponentTag(typeof(OutgoingRpcDataStreamBufferComponent))]
    struct PlayerInputJob : IJobForEachWithEntity<CommandTargetComponent>
    {
        public EntityCommandBuffer.Concurrent commandBuffer;
        public BufferFromEntity<TestCommandData> inputFromEntity;
        public uint inputTargetTick;
        public byte shoot;

        public void Execute(Entity entity, int index, [ReadOnly] ref CommandTargetComponent state)
        {
            if (shoot != 0)
            {
                var req = commandBuffer.CreateEntity(index);
                commandBuffer.AddComponent<FireBulletRequest>(index, req);
                commandBuffer.AddComponent(index, req, new SendRpcCommandRequestComponent { TargetConnection = entity });
            }

            if (state.targetEntity == Entity.Null)
            {

            }
            else
            {
                // If ship, store commands in network command buffer
                if (inputFromEntity.Exists(state.targetEntity))
                {
                    Debug.Log("Does this get called");

                    var input = inputFromEntity[state.targetEntity];
                    input.AddCommandData(new TestCommandData { tick = inputTargetTick, shoot = shoot });
                }
            }
        }
    }
}

public class FireBulletRequestSystem : RpcCommandRequestSystem<FireBulletRequest>
{
}

[BurstCompile]
public struct FireBulletRequest : IRpcCommand
{
    public void Deserialize(DataStreamReader reader, ref DataStreamReader.Context ctx)
    {
    }

    public void Serialize(DataStreamWriter writer)
    {
    }

    [BurstCompile]
    private static void InvokeExecute(ref RpcExecutor.Parameters parameters)
    {
        RpcExecutor.ExecuteCreateRequestComponent<FireBulletRequest>(ref parameters);
    }

    public PortableFunctionPointer<RpcExecutor.ExecuteDelegate> CompileExecute()
    {
        return new PortableFunctionPointer<RpcExecutor.ExecuteDelegate>(InvokeExecute);
    }
}

[UpdateInGroup(typeof(ServerSimulationSystemGroup))]
public class FireBulletServerSystem : ComponentSystem
{
    protected override void OnCreate()
    {
        RequireSingletonForUpdate<EnableNetCubeGhostSendSystemComponent>();
    }

    protected override void OnUpdate()
    {
        Entities.ForEach((Entity reqEnt, ref FireBulletRequest req, ref ReceiveRpcCommandRequestComponent reqSrc) =>
        {
            PostUpdateCommands.AddComponent<NetworkStreamInGame>(reqSrc.SourceConnection);
#if true
            var ghostCollection = GetSingleton<GhostPrefabCollectionComponent>();
            var ghostId = NetCubeGhostSerializerCollection.FindGhostType<CubeSnapshotData>();
            var prefab = EntityManager.GetBuffer<GhostPrefabBuffer>(ghostCollection.serverPrefabs)[ghostId].Value;
            var bullet = EntityManager.Instantiate(prefab);

            var p = UnityEngine.Random.insideUnitSphere;
            PostUpdateCommands.SetComponent(bullet, new Translation() { Value = new float3(p.x, p.y, p.z) });
#endif

            PostUpdateCommands.DestroyEntity(reqEnt);
        });
    }
}

[GhostDefaultComponent(GhostDefaultComponentAttribute.Type.PredictedClient)]
public struct TestCommandData : ICommandData<TestCommandData>
{
    public uint Tick => tick;
    public uint tick;
    public byte shoot;

    public void Serialize(DataStreamWriter writer)
    {
        writer.Write(shoot);
    }

    public void Deserialize(uint inputTick, DataStreamReader reader, ref DataStreamReader.Context ctx)
    {
        tick = inputTick;
        shoot = reader.ReadByte(ref ctx);
    }

    public void Serialize(DataStreamWriter writer, TestCommandData baseline, NetworkCompressionModel compressionModel)
    {
        writer.Write(shoot);
    }

    public void Deserialize(uint inputTick, DataStreamReader reader, ref DataStreamReader.Context ctx, TestCommandData baseline,
        NetworkCompressionModel compressionModel)
    {
        tick = inputTick;
        shoot = reader.ReadByte(ref ctx);
    }
}
