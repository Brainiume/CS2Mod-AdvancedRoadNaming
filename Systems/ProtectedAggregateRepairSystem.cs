using System;
using System.Collections.Generic;
using AdvancedRoadNaming.Components;
using Game;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Game.Tools;
using Game.UI;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;

namespace AdvancedRoadNaming.Systems
{
    public sealed partial class ProtectedAggregateRepairSystem : GameSystemBase
    {
        private const int MaxGroupsPerUpdate = 32;

        private EntityQuery _modifiedRoadQuery;
        private EntityQuery _modifiedAggregateQuery;
        private EntityQuery _nameRequestQuery;
        private ModificationBarrier3 _modificationBarrier;
        private EndFrameBarrier _endFrameBarrier;
        private SegmentMetadataSystem _metadataSystem;
        private ProtectedAggregateReplacementCaptureSystem _replacementCaptureSystem;
        private NameSystem _nameSystem;
        private Entity _runtimeEntity;

        private NativeParallelMultiHashMap<Entity, int> _triggerGroups;
        private NativeParallelHashMap<Entity, int> _protectedEdgeGroups;
        private NativeArray<ProtectedGroupData> _groups;
        private NativeArray<Entity> _groupEdges;
        private NativeQueue<int> _dirtyGroups;
        private NativeArray<int> _collectorCounts;
        private int _observedRevision = -1;
        private bool _observedEnabled;
        private bool _observedSavedRenameRoutesEnabled;
        private bool _triggerMapDirty = true;
        private int _auditCursor;

        protected override void OnCreate()
        {
            base.OnCreate();
            _metadataSystem = World.GetOrCreateSystemManaged<SegmentMetadataSystem>();
            _replacementCaptureSystem = World.GetOrCreateSystemManaged<ProtectedAggregateReplacementCaptureSystem>();
            _nameSystem = World.GetOrCreateSystemManaged<NameSystem>();
            _modificationBarrier = World.GetOrCreateSystemManaged<ModificationBarrier3>();
            _endFrameBarrier = World.GetOrCreateSystemManaged<EndFrameBarrier>();

            _modifiedRoadQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Edge>(), ComponentType.ReadOnly<Road>() },
                Any = new[] { ComponentType.ReadOnly<Created>(), ComponentType.ReadOnly<Updated>(), ComponentType.ReadOnly<Deleted>() }
            });
            _modifiedAggregateQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Aggregate>() },
                Any = new[] { ComponentType.ReadOnly<Created>(), ComponentType.ReadOnly<Updated>(), ComponentType.ReadOnly<Deleted>() }
            });
            _nameRequestQuery = GetEntityQuery(ComponentType.ReadOnly<AdvancedRoadNamingAggregateNameRequest>());

            _runtimeEntity = EntityManager.CreateEntity(typeof(AdvancedRoadNamingAggregateRepairRuntime));
            EntityManager.AddBuffer<AdvancedRoadNamingAggregateRepairResult>(_runtimeEntity);
            EntityManager.AddBuffer<AdvancedRoadNamingAggregateReplacementResult>(_runtimeEntity);
        }

        protected override void OnDestroy()
        {
            Dependency.Complete();
            DisposeRegistryImmediately();
            base.OnDestroy();
        }

        protected override void OnUpdate()
        {
            var enabled = Mod.Settings?.CombineRoadAggregates == true;
            if (enabled != _observedEnabled)
            {
                _observedEnabled = enabled;
                _observedRevision = -1;
                _triggerMapDirty = true;
                _metadataSystem.InvalidateProtectedAggregateRegistry(enabled ? "SettingEnabled" : "SettingDisabled");
            }

            var savedRenameRoutesEnabled = Mod.Settings?.EnableSavedRenameRoutes != false;
            if (savedRenameRoutesEnabled != _observedSavedRenameRoutesEnabled)
            {
                _observedSavedRenameRoutesEnabled = savedRenameRoutesEnabled;
                _observedRevision = -1;
                _triggerMapDirty = true;
                _metadataSystem.InvalidateProtectedAggregateRegistry(savedRenameRoutesEnabled ? "SavedRenameRoutesEnabled" : "SavedRenameRoutesDisabled");
            }

            if (!enabled)
            {
                if (RegistryIsCreated)
                    DisposeRegistryDeferred();
                return;
            }

            var revision = _metadataSystem.AggregateProtectionRevision;
            if (!RegistryIsCreated || revision != _observedRevision || _triggerMapDirty)
                RebuildRegistry(revision);

            ScheduleCollectionAndRepair();
        }

        internal void ProcessPostModificationRequests()
        {
            if (_nameSystem == null || _runtimeEntity == Entity.Null || !EntityManager.Exists(_runtimeEntity))
                return;

            var commandBuffer = _endFrameBarrier.CreateCommandBuffer();
            var validatedEdges = new Dictionary<int, HashSet<Entity>>();
            var invariantFailures = 0;
            if (!_nameRequestQuery.IsEmptyIgnoreFilter)
            {
                var entities = _nameRequestQuery.ToEntityArray(Allocator.Temp);
                try
                {
                    for (var i = 0; i < entities.Length; i++)
                    {
                        var entity = entities[i];
                        if (!EntityManager.Exists(entity) || !EntityManager.HasComponent<AdvancedRoadNamingAggregateNameRequest>(entity))
                            continue;

                        var request = EntityManager.GetComponentData<AdvancedRoadNamingAggregateNameRequest>(entity);
                        if (request.Clear != 0)
                        {
                            if (_nameSystem.TryGetCustomName(entity, out _))
                                _nameSystem.SetCustomName(entity, null);
                        }
                        else
                        {
                            if (!ValidateProtectedOwner(entity, request, validatedEdges))
                            {
                                invariantFailures++;
                            }
                            else
                            {
                                ClearProtectedChildNames(entity);
                                var expectedName = request.Name.ToString();
                                if (!_nameSystem.TryGetCustomName(entity, out var currentName)
                                    || !string.Equals(currentName, expectedName, StringComparison.Ordinal))
                                {
                                    _nameSystem.SetCustomName(entity, expectedName);
                                }
                            }
                        }

                        commandBuffer.RemoveComponent<AdvancedRoadNamingAggregateNameRequest>(entity);
                    }
                }
                finally
                {
                    entities.Dispose();
                }
            }

            foreach (var pair in validatedEdges)
            {
                if (pair.Key < 0 || pair.Key >= _groups.Length)
                {
                    invariantFailures++;
                    continue;
                }

                var group = _groups[pair.Key];
                for (var i = 0; i < group.EdgeCount; i++)
                {
                    var edge = _groupEdges[group.EdgeStart + i];
                    if (IsExpectedProtectedEdge(edge, group.Prefab) && !pair.Value.Contains(edge))
                        invariantFailures++;
                }
            }

            var replacements = EntityManager.GetBuffer<AdvancedRoadNamingAggregateReplacementResult>(_runtimeEntity);
            var migrated = 0;
            for (var i = 0; i < replacements.Length; i++)
            {
                var replacement = replacements[i];
                if (_metadataSystem.MigrateProtectedRoadReplacement(replacement.Original, replacement.Replacement))
                    migrated++;
            }
            replacements.Clear();

            var results = EntityManager.GetBuffer<AdvancedRoadNamingAggregateRepairResult>(_runtimeEntity);
            var inspected = 0;
            var queued = 0;
            var processed = 0;
            var created = 0;
            var reused = 0;
            var mixed = 0;
            var invalid = 0;
            var deferred = 0;
            for (var i = 0; i < results.Length; i++)
            {
                var result = results[i];
                inspected += result.ModifiedEntitiesInspected;
                queued += result.DirtyGroupsQueued;
                processed += result.GroupsProcessed;
                created += result.AggregatesCreated;
                reused += result.ExactOwnersReused;
                mixed += result.MixedOwnersSeparated;
                invalid += result.InvalidEdgesDropped;
                deferred += result.GroupsDeferred;
            }
            results.Clear();

            if (created > 0 || mixed > 0 || migrated > 0)
                _triggerMapDirty = true;
            if (invalid > 0)
                _metadataSystem.InvalidateProtectedAggregateRegistry("InvalidProtectedEdges");
            if (invariantFailures > 0)
            {
                _triggerMapDirty = true;
                _metadataSystem.InvalidateProtectedAggregateRegistry("InvariantFailure");
                Mod.log.Warn(() => $"Road Naming: protected aggregate invariant audit found {invariantFailures} ownership or membership failure(s); registry rebuild queued.");
            }

            if (Mod.IsVerboseLoggingEnabled && (inspected > 0 || processed > 0 || migrated > 0))
            {
                Mod.log.Info(() => $"Road Naming: protected aggregate repair summary. ModifiedEntitiesInspected={inspected}, DirtyGroupsQueued={queued}, GroupsProcessed={processed}, AggregatesCreated={created}, ExactOwnersReused={reused}, MixedOwnersSeparated={mixed}, InvalidEdgesDropped={invalid}, GroupsDeferred={deferred}, ReplacementsMigrated={migrated}, InvariantFailures={invariantFailures}.");
            }
        }

        private bool ValidateProtectedOwner(Entity owner, AdvancedRoadNamingAggregateNameRequest request, Dictionary<int, HashSet<Entity>> validatedEdges)
        {
            if (!RegistryIsCreated
                || request.GroupIndex < 0
                || request.GroupIndex >= _groups.Length
                || !EntityManager.Exists(owner)
                || !EntityManager.HasComponent<Aggregate>(owner)
                || !EntityManager.HasComponent<AdvancedRoadNamingManagedAggregate>(owner)
                || !EntityManager.HasComponent<PrefabRef>(owner)
                || !EntityManager.HasBuffer<AggregateElement>(owner))
            {
                return false;
            }

            var group = _groups[request.GroupIndex];
            if (EntityManager.GetComponentData<PrefabRef>(owner).m_Prefab != group.Prefab)
                return false;

            if (!validatedEdges.TryGetValue(request.GroupIndex, out var edges))
            {
                edges = new HashSet<Entity>();
                validatedEdges.Add(request.GroupIndex, edges);
            }

            var valid = true;
            var elements = EntityManager.GetBuffer<AggregateElement>(owner, true);
            for (var i = 0; i < elements.Length; i++)
            {
                var edge = elements[i].m_Edge;
                if (!_protectedEdgeGroups.TryGetValue(edge, out var edgeGroup)
                    || edgeGroup != request.GroupIndex
                    || !EntityManager.Exists(edge)
                    || !EntityManager.HasComponent<Aggregated>(edge)
                    || EntityManager.GetComponentData<Aggregated>(edge).m_Aggregate != owner)
                {
                    valid = false;
                    continue;
                }
                edges.Add(edge);
            }
            return valid;
        }

        private void ClearProtectedChildNames(Entity owner)
        {
            var elements = EntityManager.GetBuffer<AggregateElement>(owner, true);
            for (var i = 0; i < elements.Length; i++)
            {
                var edge = elements[i].m_Edge;
                if (edge != Entity.Null && EntityManager.Exists(edge) && _nameSystem.TryGetCustomName(edge, out _))
                    _nameSystem.SetCustomName(edge, null);
            }
        }

        private bool IsExpectedProtectedEdge(Entity edge, Entity expectedAggregatePrefab)
        {
            if (edge == Entity.Null
                || !EntityManager.Exists(edge)
                || !EntityManager.HasComponent<Edge>(edge)
                || !EntityManager.HasComponent<Road>(edge)
                || EntityManager.HasComponent<Deleted>(edge)
                || EntityManager.HasComponent<Temp>(edge)
                || !EntityManager.HasComponent<PrefabRef>(edge))
            {
                return false;
            }

            var roadPrefab = EntityManager.GetComponentData<PrefabRef>(edge).m_Prefab;
            return roadPrefab != Entity.Null
                && EntityManager.Exists(roadPrefab)
                && EntityManager.HasComponent<NetGeometryData>(roadPrefab)
                && EntityManager.GetComponentData<NetGeometryData>(roadPrefab).m_AggregateType == expectedAggregatePrefab;
        }

        private bool RegistryIsCreated => _triggerGroups.IsCreated
            && _protectedEdgeGroups.IsCreated
            && _groups.IsCreated
            && _groupEdges.IsCreated
            && _dirtyGroups.IsCreated
            && _collectorCounts.IsCreated;

        private void RebuildRegistry(int revision)
        {
            var definitions = _metadataSystem.GetProtectedAggregateGroups();
            var triggerEntries = new Dictionary<Entity, HashSet<int>>();
            var flattenedEdges = new List<Entity>();

            var newGroups = new NativeArray<ProtectedGroupData>(definitions.Count, Allocator.Persistent);
            var protectedEdgeCount = 0;
            for (var i = 0; i < definitions.Count; i++)
                protectedEdgeCount += definitions[i].RouteEdges.Count;

            var newGroupEdges = new NativeArray<Entity>(protectedEdgeCount, Allocator.Persistent);
            var newProtectedEdgeGroups = new NativeParallelHashMap<Entity, int>(Math.Max(1, protectedEdgeCount), Allocator.Persistent);
            for (var groupIndex = 0; groupIndex < definitions.Count; groupIndex++)
            {
                var definition = definitions[groupIndex];
                var edgeStart = flattenedEdges.Count;
                for (var edgeIndex = 0; edgeIndex < definition.RouteEdges.Count; edgeIndex++)
                {
                    var edge = definition.RouteEdges[edgeIndex];
                    if (newProtectedEdgeGroups.TryAdd(edge, groupIndex))
                        flattenedEdges.Add(edge);
                }

                newGroups[groupIndex] = new ProtectedGroupData
                {
                    Id = definition.Id,
                    RouteId = definition.RouteId,
                    Prefab = definition.IntendedPrefab,
                    Name = ToFixedName(definition.IntendedName),
                    EdgeStart = edgeStart,
                    EdgeCount = flattenedEdges.Count - edgeStart
                };
            }

            for (var i = 0; i < flattenedEdges.Count; i++)
            {
                var edge = flattenedEdges[i];
                newGroupEdges[i] = edge;
                if (!newProtectedEdgeGroups.TryGetValue(edge, out var groupIndex))
                    continue;

                AddTrigger(triggerEntries, edge, groupIndex);
                AddOwnerTriggers(triggerEntries, edge, groupIndex);
                AddBoundaryTriggers(triggerEntries, edge, groupIndex);
            }

            var triggerCount = 0;
            foreach (var pair in triggerEntries)
                triggerCount += pair.Value.Count;
            var newTriggerGroups = new NativeParallelMultiHashMap<Entity, int>(Math.Max(1, triggerCount), Allocator.Persistent);
            foreach (var pair in triggerEntries)
            {
                foreach (var groupIndex in pair.Value)
                    newTriggerGroups.Add(pair.Key, groupIndex);
            }

            var newDirtyGroups = new NativeQueue<int>(Allocator.Persistent);
            for (var i = 0; i < newGroups.Length; i++)
                newDirtyGroups.Enqueue(i);
            var newCollectorCounts = new NativeArray<int>(1, Allocator.Persistent, NativeArrayOptions.ClearMemory);

            DisposeRegistryDeferred();
            _groups = newGroups;
            _groupEdges = newGroupEdges;
            _protectedEdgeGroups = newProtectedEdgeGroups;
            _triggerGroups = newTriggerGroups;
            _dirtyGroups = newDirtyGroups;
            _collectorCounts = newCollectorCounts;
            _observedRevision = revision;
            _triggerMapDirty = false;
            _auditCursor = 0;

            if (Mod.IsVerboseLoggingEnabled)
                Mod.log.Info(() => $"Road Naming: protected aggregate runtime registry rebuilt. Revision={revision}, Groups={newGroups.Length}, ProtectedEdges={flattenedEdges.Count}, TriggerEntries={triggerCount}.");
        }

        private void AddOwnerTriggers(Dictionary<Entity, HashSet<int>> triggers, Entity edge, int groupIndex)
        {
            if (edge == Entity.Null || !EntityManager.Exists(edge) || !EntityManager.HasComponent<Aggregated>(edge))
                return;

            var owner = EntityManager.GetComponentData<Aggregated>(edge).m_Aggregate;
            if (owner == Entity.Null || !EntityManager.Exists(owner))
                return;

            AddTrigger(triggers, owner, groupIndex);
            if (!EntityManager.HasBuffer<AggregateElement>(owner))
                return;

            var elements = EntityManager.GetBuffer<AggregateElement>(owner, true);
            for (var i = 0; i < elements.Length; i++)
                AddTrigger(triggers, elements[i].m_Edge, groupIndex);
        }

        private void AddBoundaryTriggers(Dictionary<Entity, HashSet<int>> triggers, Entity edge, int groupIndex)
        {
            if (edge == Entity.Null || !EntityManager.Exists(edge) || !EntityManager.HasComponent<Edge>(edge))
                return;

            var edgeData = EntityManager.GetComponentData<Edge>(edge);
            AddBoundaryTriggersFromNode(triggers, edgeData.m_Start, groupIndex);
            AddBoundaryTriggersFromNode(triggers, edgeData.m_End, groupIndex);
        }

        private void AddBoundaryTriggersFromNode(Dictionary<Entity, HashSet<int>> triggers, Entity node, int groupIndex)
        {
            if (node == Entity.Null || !EntityManager.Exists(node) || !EntityManager.HasBuffer<ConnectedEdge>(node))
                return;

            var connected = EntityManager.GetBuffer<ConnectedEdge>(node, true);
            for (var i = 0; i < connected.Length; i++)
            {
                var neighbor = connected[i].m_Edge;
                AddTrigger(triggers, neighbor, groupIndex);
                AddOwnerTriggers(triggers, neighbor, groupIndex);
            }
        }

        private static void AddTrigger(Dictionary<Entity, HashSet<int>> triggers, Entity entity, int groupIndex)
        {
            if (entity == Entity.Null)
                return;

            if (!triggers.TryGetValue(entity, out var groups))
            {
                groups = new HashSet<int>();
                triggers.Add(entity, groups);
            }
            groups.Add(groupIndex);
        }

        private void ScheduleCollectionAndRepair()
        {
            var inputDependency = JobHandle.CombineDependencies(Dependency, _replacementCaptureSystem.ProducerHandle);
            var commandBuffer = _modificationBarrier.CreateCommandBuffer();
            var entityType = SystemAPI.GetEntityTypeHandle();
            var collector = new CollectDirtyGroupsJob
            {
                EntityType = entityType,
                TriggerGroups = _triggerGroups.AsReadOnly(),
                DirtyGroups = _dirtyGroups.AsParallelWriter(),
                CollectorCounts = _collectorCounts
            };
            var roadHandle = collector.Schedule(_modifiedRoadQuery, inputDependency);
            var aggregateHandle = collector.Schedule(_modifiedAggregateQuery, roadHandle);

            var auditIndex = _groups.Length == 0 ? -1 : _auditCursor++ % _groups.Length;
            var repairHandle = new RepairProtectedAggregatesJob
            {
                MaxGroups = MaxGroupsPerUpdate,
                AuditGroupIndex = auditIndex,
                RuntimeEntity = _runtimeEntity,
                Groups = _groups,
                GroupEdges = _groupEdges,
                ProtectedEdgeGroups = _protectedEdgeGroups,
                DirtyGroups = _dirtyGroups,
                CollectorCounts = _collectorCounts,
                Replacements = _replacementCaptureSystem.Replacements,
                EntityStorage = SystemAPI.GetEntityStorageInfoLookup(),
                EdgeData = SystemAPI.GetComponentLookup<Edge>(true),
                RoadData = SystemAPI.GetComponentLookup<Road>(true),
                DeletedData = SystemAPI.GetComponentLookup<Deleted>(true),
                TempData = SystemAPI.GetComponentLookup<Temp>(true),
                AggregateData = SystemAPI.GetComponentLookup<Aggregate>(true),
                AggregatedData = SystemAPI.GetComponentLookup<Aggregated>(true),
                PrefabRefData = SystemAPI.GetComponentLookup<PrefabRef>(true),
                GeometryData = SystemAPI.GetComponentLookup<NetGeometryData>(true),
                AggregatePrefabData = SystemAPI.GetComponentLookup<AggregateNetData>(true),
                ManagedAggregateData = SystemAPI.GetComponentLookup<AdvancedRoadNamingManagedAggregate>(true),
                NameRequestData = SystemAPI.GetComponentLookup<AdvancedRoadNamingAggregateNameRequest>(true),
                UpdatedData = SystemAPI.GetComponentLookup<Updated>(true),
                BatchesUpdatedData = SystemAPI.GetComponentLookup<BatchesUpdated>(true),
                ConnectedEdges = SystemAPI.GetBufferLookup<ConnectedEdge>(true),
                AggregateElements = SystemAPI.GetBufferLookup<AggregateElement>(true),
                CommandBuffer = commandBuffer
            }.Schedule(aggregateHandle);

            _replacementCaptureSystem.AddConsumer(repairHandle);
            _modificationBarrier.AddJobHandleForProducer(repairHandle);
            Dependency = repairHandle;
        }

        private void DisposeRegistryDeferred()
        {
            var handle = Dependency;
            if (_triggerGroups.IsCreated)
                handle = _triggerGroups.Dispose(handle);
            if (_protectedEdgeGroups.IsCreated)
                handle = _protectedEdgeGroups.Dispose(handle);
            if (_groups.IsCreated)
                handle = _groups.Dispose(handle);
            if (_groupEdges.IsCreated)
                handle = _groupEdges.Dispose(handle);
            if (_dirtyGroups.IsCreated)
                handle = _dirtyGroups.Dispose(handle);
            if (_collectorCounts.IsCreated)
                handle = _collectorCounts.Dispose(handle);
            Dependency = handle;
        }

        private void DisposeRegistryImmediately()
        {
            if (_triggerGroups.IsCreated)
                _triggerGroups.Dispose();
            if (_protectedEdgeGroups.IsCreated)
                _protectedEdgeGroups.Dispose();
            if (_groups.IsCreated)
                _groups.Dispose();
            if (_groupEdges.IsCreated)
                _groupEdges.Dispose();
            if (_dirtyGroups.IsCreated)
                _dirtyGroups.Dispose();
            if (_collectorCounts.IsCreated)
                _collectorCounts.Dispose();
        }

        private static FixedString4096Bytes ToFixedName(string value)
        {
            var safeValue = value ?? string.Empty;
            if (safeValue.Length > 1000)
                safeValue = safeValue.Substring(0, 1000);
            return new FixedString4096Bytes(safeValue);
        }

        private struct ProtectedGroupData
        {
            public int Id;
            public long RouteId;
            public Entity Prefab;
            public FixedString4096Bytes Name;
            public int EdgeStart;
            public int EdgeCount;
        }

        private struct OwnerEdgeRemoval
        {
            public Entity Owner;
            public Entity Edge;
        }

        [BurstCompile]
        private struct CollectDirtyGroupsJob : IJobChunk
        {
            [ReadOnly]
            public EntityTypeHandle EntityType;

            [ReadOnly]
            public NativeParallelMultiHashMap<Entity, int>.ReadOnly TriggerGroups;

            public NativeQueue<int>.ParallelWriter DirtyGroups;

            [NativeDisableParallelForRestriction]
            public NativeArray<int> CollectorCounts;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                CollectorCounts[0] += chunk.Count;
                var entities = chunk.GetNativeArray(EntityType);
                var enumerator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
                while (enumerator.NextEntityIndex(out var index))
                {
                    if (!TriggerGroups.TryGetFirstValue(entities[index], out var groupIndex, out var iterator))
                        continue;

                    do
                    {
                        DirtyGroups.Enqueue(groupIndex);
                    }
                    while (TriggerGroups.TryGetNextValue(out groupIndex, ref iterator));
                }
            }
        }

        [BurstCompile]
        private struct RepairProtectedAggregatesJob : IJob
        {
            public int MaxGroups;
            public int AuditGroupIndex;
            public Entity RuntimeEntity;

            [ReadOnly]
            public NativeArray<ProtectedGroupData> Groups;

            public NativeArray<Entity> GroupEdges;
            public NativeParallelHashMap<Entity, int> ProtectedEdgeGroups;
            public NativeQueue<int> DirtyGroups;

            [NativeDisableParallelForRestriction]
            public NativeArray<int> CollectorCounts;

            public NativeQueue<ProtectedRoadReplacement> Replacements;

            [ReadOnly] public EntityStorageInfoLookup EntityStorage;
            [ReadOnly] public ComponentLookup<Edge> EdgeData;
            [ReadOnly] public ComponentLookup<Road> RoadData;
            [ReadOnly] public ComponentLookup<Deleted> DeletedData;
            [ReadOnly] public ComponentLookup<Temp> TempData;
            [ReadOnly] public ComponentLookup<Aggregate> AggregateData;
            [ReadOnly] public ComponentLookup<Aggregated> AggregatedData;
            [ReadOnly] public ComponentLookup<PrefabRef> PrefabRefData;
            [ReadOnly] public ComponentLookup<NetGeometryData> GeometryData;
            [ReadOnly] public ComponentLookup<AggregateNetData> AggregatePrefabData;
            [ReadOnly] public ComponentLookup<AdvancedRoadNamingManagedAggregate> ManagedAggregateData;
            [ReadOnly] public ComponentLookup<AdvancedRoadNamingAggregateNameRequest> NameRequestData;
            [ReadOnly] public ComponentLookup<Updated> UpdatedData;
            [ReadOnly] public ComponentLookup<BatchesUpdated> BatchesUpdatedData;
            [ReadOnly] public BufferLookup<ConnectedEdge> ConnectedEdges;
            [ReadOnly] public BufferLookup<AggregateElement> AggregateElements;
            public EntityCommandBuffer CommandBuffer;

            public void Execute()
            {
                var result = new AdvancedRoadNamingAggregateRepairResult
                {
                    ModifiedEntitiesInspected = CollectorCounts[0]
                };
                CollectorCounts[0] = 0;

                MigrateVerifiedReplacements(ref result);
                if (AuditGroupIndex >= 0)
                    DirtyGroups.Enqueue(AuditGroupIndex);

                var requestCount = DirtyGroups.Count;
                result.DirtyGroupsQueued = requestCount;
                var seenGroups = new NativeParallelHashSet<int>(Math.Max(1, requestCount), Allocator.Temp);
                var ownerRemovals = new NativeList<OwnerEdgeRemoval>(Allocator.Temp);
                var mixedOwners = new NativeParallelHashSet<Entity>(Math.Max(1, requestCount), Allocator.Temp);

                for (var requestIndex = 0; requestIndex < requestCount; requestIndex++)
                {
                    if (!DirtyGroups.TryDequeue(out var groupIndex)
                        || groupIndex < 0
                        || groupIndex >= Groups.Length
                        || !seenGroups.Add(groupIndex))
                    {
                        continue;
                    }

                    if (result.GroupsProcessed >= MaxGroups)
                    {
                        DirtyGroups.Enqueue(groupIndex);
                        result.GroupsDeferred++;
                        continue;
                    }

                    RepairGroup(groupIndex, ref ownerRemovals, ref mixedOwners, ref result);
                    result.GroupsProcessed++;
                }

                ApplyDisplacedOwnerPlans(ref ownerRemovals);
                CommandBuffer.AppendToBuffer(RuntimeEntity, result);
                mixedOwners.Dispose();
                ownerRemovals.Dispose();
                seenGroups.Dispose();
            }

            private void MigrateVerifiedReplacements(ref AdvancedRoadNamingAggregateRepairResult result)
            {
                var replacementCount = Replacements.Count;
                for (var i = 0; i < replacementCount; i++)
                {
                    if (!Replacements.TryDequeue(out var replacement))
                        break;

                    if (!ProtectedEdgeGroups.TryGetValue(replacement.Original, out var groupIndex))
                        continue;

                    if (!IsValidPermanentReplacement(replacement.Original, replacement.Replacement))
                    {
                        if (EntityStorage.Exists(replacement.Replacement) && TempData.HasComponent(replacement.Replacement))
                            Replacements.Enqueue(replacement);
                        continue;
                    }

                    var group = Groups[groupIndex];
                    for (var edgeIndex = 0; edgeIndex < group.EdgeCount; edgeIndex++)
                    {
                        var flatIndex = group.EdgeStart + edgeIndex;
                        if (GroupEdges[flatIndex] == replacement.Original)
                            GroupEdges[flatIndex] = replacement.Replacement;
                    }

                    ProtectedEdgeGroups.Remove(replacement.Original);
                    ProtectedEdgeGroups.TryAdd(replacement.Replacement, groupIndex);
                    DirtyGroups.Enqueue(groupIndex);
                    CommandBuffer.AppendToBuffer(RuntimeEntity, new AdvancedRoadNamingAggregateReplacementResult
                    {
                        Original = replacement.Original,
                        Replacement = replacement.Replacement
                    });
                }
            }

            private bool IsValidPermanentReplacement(Entity original, Entity replacement)
            {
                return original != Entity.Null
                    && replacement != Entity.Null
                    && original != replacement
                    && EntityStorage.Exists(replacement)
                    && EdgeData.HasComponent(replacement)
                    && RoadData.HasComponent(replacement)
                    && !DeletedData.HasComponent(replacement)
                    && !TempData.HasComponent(replacement)
                    && DeletedData.HasComponent(original);
            }

            private void RepairGroup(
                int groupIndex,
                ref NativeList<OwnerEdgeRemoval> ownerRemovals,
                ref NativeParallelHashSet<Entity> mixedOwners,
                ref AdvancedRoadNamingAggregateRepairResult result)
            {
                var group = Groups[groupIndex];
                var validEdges = new NativeList<Entity>(group.EdgeCount, Allocator.Temp);
                var remaining = new NativeParallelHashSet<Entity>(Math.Max(1, group.EdgeCount), Allocator.Temp);
                for (var edgeIndex = 0; edgeIndex < group.EdgeCount; edgeIndex++)
                {
                    var edge = GroupEdges[group.EdgeStart + edgeIndex];
                    if (IsValidProtectedEdge(edge, group.Prefab) && remaining.Add(edge))
                        validEdges.Add(edge);
                    else
                        result.InvalidEdgesDropped++;
                }

                var queue = new NativeList<Entity>(Allocator.Temp);
                var unorderedComponent = new NativeList<Entity>(Allocator.Temp);
                var unvisitedComponent = new NativeParallelHashSet<Entity>(Math.Max(1, validEdges.Length), Allocator.Temp);
                var chainSet = new NativeParallelHashSet<Entity>(Math.Max(1, validEdges.Length), Allocator.Temp);
                var orderedComponent = new NativeList<Entity>(Allocator.Temp);
                for (var edgeIndex = 0; edgeIndex < validEdges.Length; edgeIndex++)
                {
                    var seed = validEdges[edgeIndex];
                    if (!remaining.Remove(seed))
                        continue;

                    queue.Clear();
                    unorderedComponent.Clear();
                    unvisitedComponent.Clear();
                    orderedComponent.Clear();
                    queue.Add(seed);
                    for (var readIndex = 0; readIndex < queue.Length; readIndex++)
                    {
                        var current = queue[readIndex];
                        unorderedComponent.Add(current);
                        var edgeData = EdgeData[current];
                        EnqueueConnected(edgeData.m_Start, ref remaining, ref queue);
                        EnqueueConnected(edgeData.m_End, ref remaining, ref queue);
                    }

                    for (var componentIndex = 0; componentIndex < unorderedComponent.Length; componentIndex++)
                        unvisitedComponent.Add(unorderedComponent[componentIndex]);

                    while (!unvisitedComponent.IsEmpty)
                    {
                        orderedComponent.Clear();
                        chainSet.Clear();
                        BuildOrderedChain(ref unorderedComponent, ref unvisitedComponent, ref orderedComponent, ref chainSet);
                        RepairComponent(groupIndex, group, ref orderedComponent, ref chainSet, ref ownerRemovals, ref mixedOwners, ref result);
                    }
                }

                orderedComponent.Dispose();
                chainSet.Dispose();
                unvisitedComponent.Dispose();
                unorderedComponent.Dispose();
                queue.Dispose();
                remaining.Dispose();
                validEdges.Dispose();
            }

            private void BuildOrderedChain(
                ref NativeList<Entity> component,
                ref NativeParallelHashSet<Entity> unvisited,
                ref NativeList<Entity> ordered,
                ref NativeParallelHashSet<Entity> chainSet)
            {
                var startEdge = Entity.Null;
                var nextNode = Entity.Null;
                for (var i = 0; i < component.Length; i++)
                {
                    var edge = component[i];
                    if (!unvisited.Contains(edge))
                        continue;

                    var edgeData = EdgeData[edge];
                    startEdge = edge;
                    nextNode = edgeData.m_End;
                    if (CountUnvisitedNeighbors(edgeData.m_Start, edge, ref unvisited) == 0)
                        break;
                    if (CountUnvisitedNeighbors(edgeData.m_End, edge, ref unvisited) == 0)
                    {
                        nextNode = edgeData.m_Start;
                        break;
                    }
                }

                var current = startEdge;
                while (current != Entity.Null && unvisited.Remove(current))
                {
                    ordered.Add(current);
                    chainSet.Add(current);
                    var next = FindUnvisitedNeighbor(nextNode, ref unvisited);
                    if (next == Entity.Null)
                        break;

                    var nextEdge = EdgeData[next];
                    nextNode = nextEdge.m_Start == nextNode ? nextEdge.m_End : nextEdge.m_Start;
                    current = next;
                }
            }

            private int CountUnvisitedNeighbors(Entity node, Entity current, ref NativeParallelHashSet<Entity> unvisited)
            {
                if (node == Entity.Null || !ConnectedEdges.HasBuffer(node))
                    return 0;

                var count = 0;
                var connected = ConnectedEdges[node];
                for (var i = 0; i < connected.Length; i++)
                {
                    var neighbor = connected[i].m_Edge;
                    if (neighbor != current && unvisited.Contains(neighbor))
                        count++;
                }
                return count;
            }

            private Entity FindUnvisitedNeighbor(Entity node, ref NativeParallelHashSet<Entity> unvisited)
            {
                if (node == Entity.Null || !ConnectedEdges.HasBuffer(node))
                    return Entity.Null;

                var connected = ConnectedEdges[node];
                for (var i = 0; i < connected.Length; i++)
                {
                    var neighbor = connected[i].m_Edge;
                    if (unvisited.Contains(neighbor))
                        return neighbor;
                }
                return Entity.Null;
            }

            private void EnqueueConnected(Entity node, ref NativeParallelHashSet<Entity> remaining, ref NativeList<Entity> queue)
            {
                if (node == Entity.Null || !ConnectedEdges.HasBuffer(node))
                    return;

                var connected = ConnectedEdges[node];
                for (var i = 0; i < connected.Length; i++)
                {
                    var neighbor = connected[i].m_Edge;
                    if (remaining.Remove(neighbor))
                        queue.Add(neighbor);
                }
            }

            private void RepairComponent(
                int groupIndex,
                ProtectedGroupData group,
                ref NativeList<Entity> component,
                ref NativeParallelHashSet<Entity> componentSet,
                ref NativeList<OwnerEdgeRemoval> ownerRemovals,
                ref NativeParallelHashSet<Entity> mixedOwners,
                ref AdvancedRoadNamingAggregateRepairResult result)
            {
                if (component.Length == 0)
                    return;

                var reusableOwner = Entity.Null;
                for (var i = 0; i < component.Length && reusableOwner == Entity.Null; i++)
                {
                    var owner = CurrentOwner(component[i]);
                    if (IsExactlyReusableOwner(owner, group.Prefab, ref componentSet, component.Length))
                        reusableOwner = owner;
                }

                if (reusableOwner != Entity.Null)
                {
                    QueueNameRequest(reusableOwner, group.Name, groupIndex, clear: false);
                    result.ExactOwnersReused++;
                    return;
                }

                if (!AggregatePrefabData.HasComponent(group.Prefab))
                    return;

                var aggregatePrefab = AggregatePrefabData[group.Prefab];
                var target = CommandBuffer.CreateEntity(aggregatePrefab.m_Archetype);
                CommandBuffer.SetComponent(target, new PrefabRef(group.Prefab));
                var targetElements = CommandBuffer.SetBuffer<AggregateElement>(target);
                CommandBuffer.AddComponent<AdvancedRoadNamingManagedAggregate>(target);
                CommandBuffer.AddComponent(target, new AdvancedRoadNamingAggregateNameRequest { Name = group.Name, GroupIndex = groupIndex });
                CommandBuffer.AddComponent<BatchesUpdated>(target);

                for (var i = 0; i < component.Length; i++)
                {
                    var edge = component[i];
                    var owner = CurrentOwner(edge);
                    if (IsAggregateOwner(owner))
                    {
                        AddOwnerRemoval(ref ownerRemovals, owner, edge);
                        if (OwnerContainsOutsideEdges(owner, ref componentSet) && mixedOwners.Add(owner))
                            result.MixedOwnersSeparated++;
                    }

                    targetElements.Add(new AggregateElement { m_Edge = edge });
                    var aggregated = new Aggregated { m_Aggregate = target };
                    if (AggregatedData.HasComponent(edge))
                        CommandBuffer.SetComponent(edge, aggregated);
                    else
                        CommandBuffer.AddComponent(edge, aggregated);
                    if (!BatchesUpdatedData.HasComponent(edge))
                        CommandBuffer.AddComponent<BatchesUpdated>(edge);
                }

                result.AggregatesCreated++;
            }

            private bool IsValidProtectedEdge(Entity edge, Entity expectedAggregatePrefab)
            {
                if (edge == Entity.Null
                    || !EntityStorage.Exists(edge)
                    || !EdgeData.HasComponent(edge)
                    || !RoadData.HasComponent(edge)
                    || DeletedData.HasComponent(edge)
                    || TempData.HasComponent(edge)
                    || !PrefabRefData.HasComponent(edge))
                {
                    return false;
                }

                var roadPrefab = PrefabRefData[edge].m_Prefab;
                return roadPrefab != Entity.Null
                    && GeometryData.HasComponent(roadPrefab)
                    && GeometryData[roadPrefab].m_AggregateType == expectedAggregatePrefab;
            }

            private Entity CurrentOwner(Entity edge)
            {
                return AggregatedData.HasComponent(edge) ? AggregatedData[edge].m_Aggregate : Entity.Null;
            }

            private bool IsAggregateOwner(Entity owner)
            {
                return owner != Entity.Null
                    && EntityStorage.Exists(owner)
                    && AggregateData.HasComponent(owner)
                    && AggregateElements.HasBuffer(owner);
            }

            private bool IsExactlyReusableOwner(Entity owner, Entity expectedPrefab, ref NativeParallelHashSet<Entity> component, int componentCount)
            {
                if (!IsAggregateOwner(owner)
                    || DeletedData.HasComponent(owner)
                    || TempData.HasComponent(owner)
                    || !ManagedAggregateData.HasComponent(owner)
                    || !PrefabRefData.HasComponent(owner)
                    || PrefabRefData[owner].m_Prefab != expectedPrefab)
                {
                    return false;
                }

                var elements = AggregateElements[owner];
                if (elements.Length != componentCount)
                    return false;
                for (var i = 0; i < elements.Length; i++)
                {
                    if (!component.Contains(elements[i].m_Edge))
                        return false;
                }
                return true;
            }

            private bool OwnerContainsOutsideEdges(Entity owner, ref NativeParallelHashSet<Entity> component)
            {
                if (!AggregateElements.HasBuffer(owner))
                    return false;
                var elements = AggregateElements[owner];
                for (var i = 0; i < elements.Length; i++)
                {
                    if (!component.Contains(elements[i].m_Edge))
                        return true;
                }
                return false;
            }

            private static void AddOwnerRemoval(ref NativeList<OwnerEdgeRemoval> removals, Entity owner, Entity edge)
            {
                for (var i = 0; i < removals.Length; i++)
                {
                    if (removals[i].Owner == owner && removals[i].Edge == edge)
                        return;
                }
                removals.Add(new OwnerEdgeRemoval { Owner = owner, Edge = edge });
            }

            private void ApplyDisplacedOwnerPlans(ref NativeList<OwnerEdgeRemoval> removals)
            {
                var processedOwners = new NativeParallelHashSet<Entity>(Math.Max(1, removals.Length), Allocator.Temp);
                var remainingEdges = new NativeList<AggregateElement>(Allocator.Temp);
                for (var removalIndex = 0; removalIndex < removals.Length; removalIndex++)
                {
                    var owner = removals[removalIndex].Owner;
                    if (!processedOwners.Add(owner) || !IsAggregateOwner(owner) || DeletedData.HasComponent(owner))
                        continue;

                    remainingEdges.Clear();
                    var elements = AggregateElements[owner];
                    for (var elementIndex = 0; elementIndex < elements.Length; elementIndex++)
                    {
                        var element = elements[elementIndex];
                        if (!IsRemoved(owner, element.m_Edge, ref removals))
                            remainingEdges.Add(element);
                    }

                    var targetBuffer = CommandBuffer.SetBuffer<AggregateElement>(owner);
                    for (var i = 0; i < remainingEdges.Length; i++)
                        targetBuffer.Add(remainingEdges[i]);

                    if (remainingEdges.Length == 0)
                    {
                        if (!DeletedData.HasComponent(owner))
                            CommandBuffer.AddComponent<Deleted>(owner);
                        continue;
                    }

                    if (!UpdatedData.HasComponent(owner))
                        CommandBuffer.AddComponent<Updated>(owner);
                    if (!BatchesUpdatedData.HasComponent(owner))
                        CommandBuffer.AddComponent<BatchesUpdated>(owner);

                    if (ManagedAggregateData.HasComponent(owner) && !ContainsProtectedEdge(ref remainingEdges))
                    {
                        CommandBuffer.RemoveComponent<AdvancedRoadNamingManagedAggregate>(owner);
                        QueueNameRequest(owner, default, -1, clear: true);
                    }
                }

                remainingEdges.Dispose();
                processedOwners.Dispose();
            }

            private static bool IsRemoved(Entity owner, Entity edge, ref NativeList<OwnerEdgeRemoval> removals)
            {
                for (var i = 0; i < removals.Length; i++)
                {
                    if (removals[i].Owner == owner && removals[i].Edge == edge)
                        return true;
                }
                return false;
            }

            private bool ContainsProtectedEdge(ref NativeList<AggregateElement> elements)
            {
                for (var i = 0; i < elements.Length; i++)
                {
                    if (ProtectedEdgeGroups.ContainsKey(elements[i].m_Edge))
                        return true;
                }
                return false;
            }

            private void QueueNameRequest(Entity owner, FixedString4096Bytes name, int groupIndex, bool clear)
            {
                var request = new AdvancedRoadNamingAggregateNameRequest
                {
                    Name = name,
                    GroupIndex = groupIndex,
                    Clear = clear ? (byte)1 : (byte)0
                };
                if (NameRequestData.HasComponent(owner))
                    CommandBuffer.SetComponent(owner, request);
                else
                    CommandBuffer.AddComponent(owner, request);
            }
        }
    }
}
