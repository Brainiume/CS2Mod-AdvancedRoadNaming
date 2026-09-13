using Game;
using Game.Net;
using Game.Tools;
using Colossal.Mathematics;
using Game.Prefabs;
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
        public Bezier4x3 OriginalCurve;
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
                Curves = SystemAPI.GetComponentLookup<Curve>(true),
                Temps = SystemAPI.GetComponentLookup<Temp>(true),
                AggregatedData = SystemAPI.GetComponentLookup<Aggregated>(true),
                AggregateElements = SystemAPI.GetBufferLookup<AggregateElement>(true),
                BuildOrders = SystemAPI.GetComponentLookup<Game.Net.BuildOrder>(true),
                Prefabs = SystemAPI.GetComponentLookup<PrefabRef>(true),
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

            [ReadOnly] public ComponentLookup<Curve> Curves;
            [ReadOnly] public ComponentLookup<Temp> Temps;
            [ReadOnly] public ComponentLookup<Aggregated> AggregatedData;
            [ReadOnly] public BufferLookup<AggregateElement> AggregateElements;
            [ReadOnly] public ComponentLookup<Game.Net.BuildOrder> BuildOrders;
            [ReadOnly] public ComponentLookup<PrefabRef> Prefabs;

            public NativeQueue<ProtectedRoadReplacement>.ParallelWriter Replacements;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                var entities = chunk.GetNativeArray(EntityType);
                var temps = chunk.GetNativeArray(ref TempType);
                var enumerator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
                while (enumerator.NextEntityIndex(out var index))
                {
                    var temp = temps[index];
                    if ((temp.m_Flags & (TempFlags.Delete | TempFlags.Cancel)) != 0)
                        continue;

                    var original = temp.m_Original;
                    if (original == Entity.Null && (temp.m_Flags & TempFlags.Essential) != 0)
                        original = FindSplitOriginal(entities[index]);
                    else if ((temp.m_Flags & (TempFlags.Replace | TempFlags.Combine)) == 0)
                        continue;
                    if (original == Entity.Null || !Curves.HasComponent(original)) continue;

                    Replacements.Enqueue(new ProtectedRoadReplacement
                    {
                        Original = original,
                        Replacement = entities[index],
                        OriginalCurve = Curves[original].m_Bezier
                    });
                }
            }

            private Entity FindSplitOriginal(Entity piece)
            {
                // GenerateEdgesSystem.SplitEdge/CreateTempEdge copies Aggregated and
                // subdivides BuildOrder, but deliberately leaves Temp.m_Original null.
                if (!AggregatedData.HasComponent(piece) || !BuildOrders.HasComponent(piece)
                    || !Curves.HasComponent(piece) || !Prefabs.HasComponent(piece)) return Entity.Null;
                var owner = AggregatedData[piece].m_Aggregate;
                if (!AggregateElements.HasBuffer(owner)) return Entity.Null;
                var elements = AggregateElements[owner];
                var range = BuildOrders[piece];
                var curve = Curves[piece].m_Bezier;
                var match = Entity.Null;
                for (var i = 0; i < elements.Length; i++)
                {
                    var source = elements[i].m_Edge;
                    if (Temps.HasComponent(source) || !BuildOrders.HasComponent(source)
                        || !Curves.HasComponent(source) || !Prefabs.HasComponent(source)
                        || Prefabs[source].m_Prefab != Prefabs[piece].m_Prefab) continue;
                    var sourceRange = BuildOrders[source];
                    if (!Services.RoadReplacementGeometry.IsSplitPiece(Curves[source].m_Bezier, curve,
                        sourceRange.m_Start, sourceRange.m_End, range.m_Start, range.m_End)) continue;
                    if (match != Entity.Null && match != source) return Entity.Null;
                    match = source;
                }
                return match;
            }
        }
    }
}
