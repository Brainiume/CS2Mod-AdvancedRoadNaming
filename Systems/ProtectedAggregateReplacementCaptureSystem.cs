using Game;
using Game.Net;
using Game.Tools;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;

namespace AdvancedRoadNaming.Systems
{
    internal struct ProtectedRoadReplacement
    {
        public Entity Original;
        public Entity Replacement;
    }

    public sealed partial class ProtectedAggregateReplacementCaptureSystem : GameSystemBase
    {
        private EntityQuery _replacementQuery;
        private NativeQueue<ProtectedRoadReplacement> _replacements;

        internal NativeQueue<ProtectedRoadReplacement> Replacements => _replacements;

        internal JobHandle ProducerHandle => Dependency;

        protected override void OnCreate()
        {
            base.OnCreate();
            _replacementQuery = GetEntityQuery(
                ComponentType.ReadOnly<Temp>(),
                ComponentType.ReadOnly<Edge>(),
                ComponentType.ReadOnly<Road>());
            _replacements = new NativeQueue<ProtectedRoadReplacement>(Allocator.Persistent);
            RequireForUpdate(_replacementQuery);
        }

        protected override void OnDestroy()
        {
            Dependency.Complete();
            if (_replacements.IsCreated)
                _replacements.Dispose();
            base.OnDestroy();
        }

        internal void AddConsumer(JobHandle consumer)
        {
            Dependency = JobHandle.CombineDependencies(Dependency, consumer);
        }

        protected override void OnUpdate()
        {
            if (Mod.Settings?.CombineRoadAggregates != true)
                return;

            var captureHandle = new CaptureReplacementJob
            {
                EntityType = SystemAPI.GetEntityTypeHandle(),
                TempType = SystemAPI.GetComponentTypeHandle<Temp>(true),
                Replacements = _replacements.AsParallelWriter()
            }.ScheduleParallel(_replacementQuery, Dependency);

            Dependency = captureHandle;
        }

        [BurstCompile]
        private struct CaptureReplacementJob : IJobChunk
        {
            [ReadOnly]
            public EntityTypeHandle EntityType;

            [ReadOnly]
            public ComponentTypeHandle<Temp> TempType;

            public NativeQueue<ProtectedRoadReplacement>.ParallelWriter Replacements;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                var entities = chunk.GetNativeArray(EntityType);
                var temps = chunk.GetNativeArray(ref TempType);
                var enumerator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
                while (enumerator.NextEntityIndex(out var index))
                {
                    var temp = temps[index];
                    if (temp.m_Original == Entity.Null
                        || (temp.m_Flags & (TempFlags.Replace | TempFlags.Combine)) == 0
                        || (temp.m_Flags & (TempFlags.Delete | TempFlags.Cancel)) != 0)
                    {
                        continue;
                    }

                    Replacements.Enqueue(new ProtectedRoadReplacement
                    {
                        Original = temp.m_Original,
                        Replacement = entities[index]
                    });
                }
            }
        }
    }
}
