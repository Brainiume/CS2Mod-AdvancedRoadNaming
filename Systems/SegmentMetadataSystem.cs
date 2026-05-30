using System.Collections.Generic;
using System.Text.RegularExpressions;
using Colossal.Serialization.Entities;
using Game;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Game.SceneFlow;
using Game.UI;
using AdvancedRoadNaming.Components;
using AdvancedRoadNaming.Domain;
using AdvancedRoadNaming.Services;
using Unity.Collections;
using Unity.Entities;

namespace AdvancedRoadNaming.Systems
{
    public sealed partial class SegmentMetadataSystem : GameSystemBase, IDefaultSerializable
    {
        private const int SaveVersion = 6;
        private const int AggregateStabilityInitialDelayTicks = 2;
        private const int AggregateStabilityRetryDelayTicks = 10;
        private const int AggregateStabilityStableChecksRequired = 3;
        private const int AggregateStabilityMaxReapplyAttempts = 3;
        private const int DeferredNameReapplyDelayTicks = 5;
        private const int DeferredNameReapplyRetryDelayTicks = 10;
        private const int DeferredNameReapplyMaxAttempts = 10;
        private const float RebuildCandidateOverlapThreshold = 0.8f;

        private SegmentMetadataRepository _repository;
        private SegmentValidationService _validation;
        private SegmentDisplayNameResolver _resolver;
        private RouteCodeService _routeCodeService;
        private RouteApplyService _applyService;
        private RouteDatabaseService _routeDatabase;
        private NameSystem _nameSystem;
        private EntityQuery _aggregatedRoadEdgeQuery;
        private readonly List<AggregateSplitStabilityCheck> _aggregateStabilityChecks = new List<AggregateSplitStabilityCheck>();
        private bool _pendingPostLoadNameReapply;
        private int _pendingPostLoadNameReapplyDelayTicks;
        private int _pendingPostLoadNameReapplyAttempts;
        private bool _pendingPostLoadNameReapplyWaitingLogged;

        public SegmentMetadataRepository Repository => _repository;

        public RouteDatabaseService RouteDatabase => _routeDatabase;

        public SegmentValidationService Validation => _validation;

        public RoadNetworkPathingService Pathing { get; private set; }

        // initialise the services and gets Name system, 
        // as well as making an entity query for road edge aggregates; 
        // which then creates a new route apply service
        protected override void OnCreate()
        {
            base.OnCreate();
            _repository = new SegmentMetadataRepository();
            _validation = new SegmentValidationService(EntityManager);
            Pathing = new RoadNetworkPathingService(EntityManager, _validation);
            _resolver = new SegmentDisplayNameResolver();
            _routeCodeService = new RouteCodeService();
            _routeDatabase = new RouteDatabaseService();
            _nameSystem = World.GetOrCreateSystemManaged<NameSystem>();
            _aggregatedRoadEdgeQuery = GetEntityQuery(ComponentType.ReadOnly<Edge>(), ComponentType.ReadOnly<Road>(), ComponentType.ReadOnly<Aggregated>());
            _applyService = new RouteApplyService(_repository, _validation, _resolver, _routeCodeService, GetBaseName, SetSegmentDisplayName, message => Mod.log.Info(message));
            Mod.log.Info("SegmentMetadataSystem created");
        }

        
        // Runs 2 functions each update to ensure everything is valid & up to date
        // Checks if the mod needs to reapply names after loading a save and another to check the stability of da
        // aggregates after splitting them for a name change and reapplying the split if needed
        protected override void OnUpdate()
        {
            if (!IsSafeForLiveAggregateMaintenance())
                return;

            ProcessDeferredPostLoadNameReapply();
            UpdateAggregateStabilityChecks();
        }

        private bool IsSafeForLiveAggregateMaintenance()
        {
            return IsSafeForPostLoadNameWrites(out _);
        }
        
        // gets Apply Service to apply the given new road name to the provided segments. 
        public bool ApplyRename(System.Collections.Generic.IReadOnlyList<Entity> selectedSegments, string newRoadName, out string message)
        {
            var result = _applyService.ApplyRename(selectedSegments, newRoadName, CurrentDisplaySettings(), out message);
            if (result)
                ApplyAuthoritativeVisibleNames(selectedSegments, "Rename");
            return result;
        }

        // Does the same as the Apply Rename func but for route numbers instead.
        public bool ApplyRouteNumber(System.Collections.Generic.IReadOnlyList<Entity> selectedSegments, string routeCode, RouteNumberPlacement placement, out string message)
        {
            var result = _applyService.ApplyRouteNumber(selectedSegments, routeCode, placement, CurrentDisplaySettings(), out message);
            if (result)
                ApplyAuthoritativeVisibleNames(selectedSegments, "AssignRouteNumber");
            return result;
        }

        // Previews the name that would be applied to a segment TEMPORY NOT ACTUALLY APPLYING here.
        public string ResolvePreviewName(Entity segment, string pendingInput, RoadRouteToolMode mode, RouteNumberPlacement placement)
        {
            var settings = CurrentDisplaySettings();
            var metadata = _repository.TryGet(segment, out var existing)
                ? CloneMetadata(existing)
                : new SegmentRouteMetadata(segment) { BaseNameSnapshot = GetBaseName(segment) };

            if (mode == RoadRouteToolMode.RenameSelectedSegments && !string.IsNullOrWhiteSpace(pendingInput))
                metadata.OptionalCustomRoadName = pendingInput.Trim();
            else if (mode == RoadRouteToolMode.AssignMajorRouteNumber && _routeCodeService.TryNormalize(pendingInput, out var routeCode, out _))
            {
                metadata.RouteNumberPlacement = placement;
                var adapter = new RouteCodeService.SegmentRouteMetadataAdapter(metadata.RouteNumbers);
                _routeCodeService.AddRouteCode(adapter, routeCode, settings.AllowMultipleRouteNumbers);
            }

            return _resolver.Resolve(GetBaseName(segment), metadata, settings);
        }

        // Apples the correct names to road segments after a rename or route number change, 
        // including handling vanilla aggregate naming behavior by splitting aggregates when 
        // needed. Allows for selected segments and unselected segments of a road to be separated.
        private void ApplyAuthoritativeVisibleNames(System.Collections.Generic.IReadOnlyList<Entity> selectedSegments, string operation)
        {
            if (selectedSegments == null || selectedSegments.Count == 0)
                return;

            var settings = CurrentDisplaySettings();
            var selectedSet = new HashSet<Entity>();
            var groups = new Dictionary<Entity, List<Entity>>();

            for (var i = 0; i < selectedSegments.Count; i++)
            {
                var segment = selectedSegments[i];
                if (!_validation.IsValidRoadSegment(segment))
                    continue;

                selectedSet.Add(segment);
                var aggregate = GetAuthoritativeNameEntity(segment);
                if (!groups.TryGetValue(aggregate, out var groupSegments))
                {
                    groupSegments = new List<Entity>();
                    groups.Add(aggregate, groupSegments);
                }

                groupSegments.Add(segment);
            }

            foreach (var pair in groups)
            {
                var nameEntity = pair.Key;
                var routeSegmentsInGroup = pair.Value;
                if (nameEntity == Entity.Null || !EntityManager.Exists(nameEntity) || routeSegmentsInGroup.Count == 0)
                    continue;

                var totalAggregateEdges = GetAggregateRoadEdges(nameEntity);
                var completeAggregate = totalAggregateEdges.Count > 0 && AllEdgesSelected(totalAggregateEdges, selectedSet);
                var firstSegment = routeSegmentsInGroup[0];
                var aggregateInfo = nameEntity == firstSegment ? "SegmentNameEntity" : "AggregateNameEntity";

                if (!_repository.TryGet(firstSegment, out var metadata))
                {
                    Mod.log.Warn(() => $"Could not find metadata for visible-name update. Operation={operation}, Segment={firstSegment.Index}, NameEntity={nameEntity.Index}.");
                    continue;
                }

                var finalName = _resolver.Resolve(GetBaseName(firstSegment), metadata, settings);
                if (!completeAggregate && nameEntity != firstSegment)
                {
                    var originalVisibleName = GetCurrentAuthoritativeName(nameEntity, metadata.BaseNameSnapshot ?? GetBaseName(firstSegment));
                    if (!TryPartitionAggregateForSelectedEdges(nameEntity, routeSegmentsInGroup, selectedSet, operation, finalName, originalVisibleName))
                        Mod.log.Warn(() => $"Aggregate partition failed. Operation={operation}, Aggregate={nameEntity.Index}, SelectedEdgesInAggregate={routeSegmentsInGroup.Count}, TotalAggregateEdges={totalAggregateEdges.Count}. Segment metadata remains updated, but vanilla visible naming may remain cooked or unchanged.");
                    continue;
                }

                SetAuthoritativeName(nameEntity, finalName, operation, aggregateInfo, routeSegmentsInGroup.Count, totalAggregateEdges.Count);
            }
        }

        // When only a part of a street aggregate is selected, 
        // this function splits the aggregate into selected and the remainder groups, 
        // preserves the old name on the remainder and applies the new name only to the selected section.
        private bool TryPartitionAggregateForSelectedEdges(Entity sourceAggregate, List<Entity> routeSegmentsInGroup, HashSet<Entity> selectedSet, string operation, string selectedFinalName, string originalVisibleName, bool scheduleStabilityCheck = true)
        {
            if (sourceAggregate == Entity.Null || !EntityManager.Exists(sourceAggregate) || !EntityManager.HasBuffer<AggregateElement>(sourceAggregate))
                return false;

            var allAggregateEdges = GetAggregateRoadEdges(sourceAggregate);
            if (allAggregateEdges.Count == 0)
                return false;

            var selectedEdges = new List<Entity>();
            var remainderEdges = new List<Entity>();
            for (var i = 0; i < allAggregateEdges.Count; i++)
            {
                var edge = allAggregateEdges[i];
                if (selectedSet.Contains(edge))
                    selectedEdges.Add(edge);
                else
                    remainderEdges.Add(edge);
            }

            if (selectedEdges.Count == 0 || remainderEdges.Count == 0)
                return false;

            var selectedComponents = BuildConnectedEdgeComponents(selectedEdges);
            var remainderComponents = BuildConnectedEdgeComponents(remainderEdges);
            Mod.log.Info(() => $"Road Naming: aggregate partition. Operation={operation}, Aggregate={sourceAggregate.Index}, Selected={selectedEdges.Count}, Remainder={remainderEdges.Count}, SelectedComponents={selectedComponents.Count}, RemainderComponents={remainderComponents.Count}, OriginalVisibleName='{originalVisibleName}', SelectedVisibleName='{selectedFinalName}'.");

            var sourceAssigned = false;
            var remainderAggregateCount = 0;
            var remainderAggregateSet = new HashSet<Entity>();
            for (var i = 0; i < remainderComponents.Count; i++)
            {
                var remainderAggregate = sourceAssigned ? CreateAggregateClone(sourceAggregate, operation, "Remainder") : sourceAggregate;
                if (remainderAggregate == Entity.Null)
                    continue;

                sourceAssigned = true;
                remainderAggregateCount++;
                remainderAggregateSet.Add(remainderAggregate);
                AssignAggregateEdges(remainderAggregate, remainderComponents[i], operation, "Remainder");
                Mod.log.Info(() => $"Road Naming: remainder preserved. SourceAggregate={sourceAggregate.Index}, RemainderAggregate={remainderAggregate.Index}, Edges={remainderComponents[i].Count}, RetainedName='{originalVisibleName}'.");
            }

            var selectedAggregateCount = 0;
            var selectedAggregateSet = new HashSet<Entity>();
            for (var i = 0; i < selectedComponents.Count; i++)
            {
                var selectedAggregate = CreateAggregateClone(sourceAggregate, operation, "Selected");
                if (selectedAggregate == Entity.Null)
                    continue;

                selectedAggregateCount++;
                selectedAggregateSet.Add(selectedAggregate);
                AssignAggregateEdges(selectedAggregate, selectedComponents[i], operation, "Selected");
                SetAuthoritativeName(selectedAggregate, selectedFinalName, operation, "SelectedAggregate", selectedComponents[i].Count, selectedEdges.Count);
                Mod.log.Info(() => $"Road Naming: selected subsection assigned. SourceAggregate={sourceAggregate.Index}, NewSelectedAggregate={selectedAggregate.Index}, Edges={selectedComponents[i].Count}, AssignedName='{selectedFinalName}'.");
            }

            var verified = VerifyPartitionOwnership(sourceAggregate, selectedEdges, remainderEdges, selectedAggregateSet, remainderAggregateSet, selectedFinalName, originalVisibleName, operation);
            if (scheduleStabilityCheck && selectedAggregateCount > 0 && remainderAggregateCount > 0)
                RegisterAggregateStabilityCheck(sourceAggregate, selectedEdges, remainderEdges, selectedFinalName, originalVisibleName, operation);

            Mod.log.Info(() => $"Road Naming: aggregate partition complete. Operation={operation}, SourceAggregate={sourceAggregate.Index}, SelectedAggregatesCreated={selectedAggregateCount}, RemainderAggregates={remainderAggregateCount}, OwnershipVerified={verified}.");
            return selectedAggregateCount > 0 && remainderAggregateCount > 0 && verified;
        }


        //  Checks whether the selected edges now belong to the created selected aggregates 
        private bool VerifyPartitionOwnership(Entity sourceAggregate, List<Entity> selectedEdges, List<Entity> remainderEdges, HashSet<Entity> selectedAggregateSet, HashSet<Entity> remainderAggregateSet, string selectedFinalName, string originalVisibleName, string operation)
        {
            var ok = true;
            for (var i = 0; i < selectedEdges.Count; i++)
            {
                var edge = selectedEdges[i];
                var owner = GetAuthoritativeNameEntity(edge);
                var ownerName = GetCurrentAuthoritativeName(owner, string.Empty);
                if (!selectedAggregateSet.Contains(owner) || !string.Equals(ownerName, selectedFinalName, System.StringComparison.Ordinal))
                {
                    ok = false;
                    Mod.log.Warn(() => $"Road Naming: selected ownership check failed. Operation={operation}, Edge={edge.Index}, Owner={owner.Index}, OwnerName='{ownerName}', ExpectedName='{selectedFinalName}', SourceAggregate={sourceAggregate.Index}.");
                }
            }

            for (var i = 0; i < remainderEdges.Count; i++)
            {
                var edge = remainderEdges[i];
                var owner = GetAuthoritativeNameEntity(edge);
                var ownerName = GetCurrentAuthoritativeName(owner, string.Empty);
                if (!remainderAggregateSet.Contains(owner) || selectedAggregateSet.Contains(owner) || string.Equals(ownerName, selectedFinalName, System.StringComparison.Ordinal))
                {
                    ok = false;
                    Mod.log.Warn(() => $"Remainder ownership check failed. Operation={operation}, Edge={edge.Index}, Owner={owner.Index}, OwnerName='{ownerName}', OriginalVisibleName='{originalVisibleName}', SelectedName='{selectedFinalName}', SourceAggregate={sourceAggregate.Index}.");
                }
            }

            if (ok)
                Mod.log.Info(() => $"Aggregate partition ownership verified. Operation={operation}, SourceAggregate={sourceAggregate.Index}, SelectedEdges={selectedEdges.Count}, RemainderEdges={remainderEdges.Count}.");

            return ok;
        }

        // Montitor the sabiliity of the split aggregates
        private sealed class AggregateSplitStabilityCheck
        {
            public Entity SourceAggregate;
            public readonly List<Entity> SelectedEdges = new List<Entity>();
            public readonly List<Entity> RemainderEdges = new List<Entity>();
            public string SelectedFinalName;
            public string OriginalVisibleName;
            public string Operation;
            public int TicksUntilNextCheck;
            public int ReapplyAttemptsRemaining;
            public int StableChecks;
        }


        // Create a new monitoring job using the class above
        private void RegisterAggregateStabilityCheck(Entity sourceAggregate, List<Entity> selectedEdges, List<Entity> remainderEdges, string selectedFinalName, string originalVisibleName, string operation)
        {
            var check = new AggregateSplitStabilityCheck
            {
                SourceAggregate = sourceAggregate,
                SelectedFinalName = selectedFinalName ?? string.Empty,
                OriginalVisibleName = originalVisibleName ?? string.Empty,
                Operation = operation ?? string.Empty,
                TicksUntilNextCheck = AggregateStabilityInitialDelayTicks,
                ReapplyAttemptsRemaining = AggregateStabilityMaxReapplyAttempts
            };
            check.SelectedEdges.AddRange(selectedEdges);
            check.RemainderEdges.AddRange(remainderEdges);
            _aggregateStabilityChecks.Add(check);
            Mod.log.Info(() => $"Aggregate stability check scheduled. Operation={operation}, SourceAggregate={sourceAggregate.Index}, SelectedEdges={selectedEdges.Count}, RemainderEdges={remainderEdges.Count}, FirstCheckInTicks={AggregateStabilityInitialDelayTicks}, ReapplyAttempts={AggregateStabilityMaxReapplyAttempts}.");
        }

        // Monitors the created jobs &  removes dead ones, 
        // confirms stable splits, or triggers reapply attempts if vanilla systems merged things back >:( .
        private void UpdateAggregateStabilityChecks()
        {
            if (_aggregateStabilityChecks.Count == 0)
                return;

            for (var i = _aggregateStabilityChecks.Count - 1; i >= 0; i--)
            {
                var check = _aggregateStabilityChecks[i];
                check.TicksUntilNextCheck--;
                if (check.TicksUntilNextCheck > 0)
                    continue;

                if (!HasAnyValidRoadEdge(check.SelectedEdges))
                {
                    Mod.log.Warn(() => $"Aggregate stability check removed because selected edges no longer exist. Operation={check.Operation}, SourceAggregate={check.SourceAggregate.Index}.");
                    _aggregateStabilityChecks.RemoveAt(i);
                    continue;
                }

                var stable = CheckAggregateStability(check, "PostUpdateTick");
                if (stable)
                {
                    check.StableChecks++;
                    if (check.StableChecks >= AggregateStabilityStableChecksRequired)
                    {
                        Mod.log.Info(() => $"Aggregate stability confirmed. Operation={check.Operation}, SourceAggregate={check.SourceAggregate.Index}, StableChecks={check.StableChecks}.");
                        _aggregateStabilityChecks.RemoveAt(i);
                    }
                    else
                    {
                        check.TicksUntilNextCheck = AggregateStabilityRetryDelayTicks;
                    }

                    continue;
                }

                check.StableChecks = 0;
                if (check.ReapplyAttemptsRemaining <= 0)
                {
                    Mod.log.Warn(() => $"Road Naming: aggregate stability could not be restored. Operation={check.Operation}, SourceAggregate={check.SourceAggregate.Index}, SelectedFinalName='{check.SelectedFinalName}', OriginalVisibleName='{check.OriginalVisibleName}'.");
                    _aggregateStabilityChecks.RemoveAt(i);
                    continue;
                }

                check.ReapplyAttemptsRemaining--;
                Mod.log.Warn(() => $"Road Naming: detected aggregate merge/recompute after apply; reapplying split. Operation={check.Operation}, SourceAggregate={check.SourceAggregate.Index}, AttemptsRemaining={check.ReapplyAttemptsRemaining}.");
                ReapplyAggregateStabilityCheck(check);
                check.TicksUntilNextCheck = AggregateStabilityRetryDelayTicks;
            }
        }

        // reads runtime ECS state and confirms that selected edges 
        // still point to selected name owners and remainder edges still point elsewhere.
        private bool CheckAggregateStability(AggregateSplitStabilityCheck check, string phase)
        {
            var ok = true;
            var selectedOwners = new HashSet<Entity>();
            var remainderOwners = new HashSet<Entity>();
            var selectedEdgeSet = new HashSet<Entity>(check.SelectedEdges);
            var remainderEdgeSet = new HashSet<Entity>(check.RemainderEdges);
            var validSelected = 0;
            var validRemainder = 0;

            for (var i = 0; i < check.SelectedEdges.Count; i++)
            {
                var edge = check.SelectedEdges[i];
                if (!_validation.IsValidRoadSegment(edge))
                    continue;

                validSelected++;
                var owner = GetAuthoritativeNameEntity(edge);
                selectedOwners.Add(owner);
                var ownerName = GetCurrentAuthoritativeName(owner, string.Empty);
                if (owner == Entity.Null || !string.Equals(ownerName, check.SelectedFinalName, System.StringComparison.Ordinal))
                {
                    ok = false;
                    Mod.log.Warn(() => $"Aggregate stability selected-edge mismatch. Phase={phase}, Operation={check.Operation}, Edge={edge.Index}, Owner={owner.Index}, OwnerName='{ownerName}', ExpectedSelectedName='{check.SelectedFinalName}'.");
                }
            }

            for (var i = 0; i < check.RemainderEdges.Count; i++)
            {
                var edge = check.RemainderEdges[i];
                if (!_validation.IsValidRoadSegment(edge))
                    continue;

                validRemainder++;
                var owner = GetAuthoritativeNameEntity(edge);
                remainderOwners.Add(owner);
                var ownerName = GetCurrentAuthoritativeName(owner, string.Empty);
                if (owner == Entity.Null || selectedOwners.Contains(owner) || string.Equals(ownerName, check.SelectedFinalName, System.StringComparison.Ordinal))
                {
                    ok = false;
                    Mod.log.Warn(() => $"Aggregate stability remainder-edge mismatch. Phase={phase}, Operation={check.Operation}, Edge={edge.Index}, Owner={owner.Index}, OwnerName='{ownerName}', OriginalVisibleName='{check.OriginalVisibleName}', SelectedFinalName='{check.SelectedFinalName}'.");
                }
            }

            foreach (var owner in selectedOwners)
            {
                if (owner == Entity.Null || !EntityManager.Exists(owner))
                    continue;

                if (Mod.IsVerboseLoggingEnabled)
                    LogVisibleNameSource(owner, check.Operation, phase + ":SelectedOwner");
                var ownerEdges = GetAggregateRoadEdges(owner);
                if (ContainsAny(ownerEdges, remainderEdgeSet))
                {
                    ok = false;
                    Mod.log.Warn(() => $"Aggregate stability detected selected/remainder merge. Phase={phase}, Operation={check.Operation}, SelectedOwner={owner.Index}, OwnerEdges={ownerEdges.Count}, RemainderEdges={check.RemainderEdges.Count}.");
                }
            }

            foreach (var owner in remainderOwners)
            {
                if (owner == Entity.Null || !EntityManager.Exists(owner))
                    continue;

                if (Mod.IsVerboseLoggingEnabled)
                    LogVisibleNameSource(owner, check.Operation, phase + ":RemainderOwner");
                var ownerEdges = GetAggregateRoadEdges(owner);
                if (ContainsAny(ownerEdges, selectedEdgeSet))
                {
                    ok = false;
                    Mod.log.Warn(() => $"Aggregate stability detected remainder/selected merge. Phase={phase}, Operation={check.Operation}, RemainderOwner={owner.Index}, OwnerEdges={ownerEdges.Count}, SelectedEdges={check.SelectedEdges.Count}.");
                }
            }

            if (check.SelectedEdges.Count == 1)
                Mod.log.Info(() => $"Tiny subsection independent naming = {ok}. Phase={phase}, Operation={check.Operation}, Edge={check.SelectedEdges[0].Index}, SelectedOwners=[{FormatEntitySet(selectedOwners)}], RemainderOwners=[{FormatEntitySet(remainderOwners)}].");

            Mod.log.Info(() => $"Aggregate stability readback. Phase={phase}, Operation={check.Operation}, SourceAggregate={check.SourceAggregate.Index}, Stable={ok}, ValidSelected={validSelected}, ValidRemainder={validRemainder}, SelectedOwners=[{FormatEntitySet(selectedOwners)}], RemainderOwners=[{FormatEntitySet(remainderOwners)}], SelectedName='{check.SelectedFinalName}', OriginalName='{check.OriginalVisibleName}'.");
            return ok && validSelected > 0;
        }

        // Reapplies the split or renaming if the game recomputed the aggregate after the mod changed it.
        private void ReapplyAggregateStabilityCheck(AggregateSplitStabilityCheck check)
        {
            var validSelectedEdges = FilterValidRoadEdges(check.SelectedEdges);
            if (validSelectedEdges.Count == 0)
                return;

            var selectedSet = new HashSet<Entity>(validSelectedEdges);
            var selectedGroups = new Dictionary<Entity, List<Entity>>();
            for (var i = 0; i < validSelectedEdges.Count; i++)
            {
                var edge = validSelectedEdges[i];
                var owner = GetAuthoritativeNameEntity(edge);
                if (owner == Entity.Null || !EntityManager.Exists(owner))
                    continue;

                if (!selectedGroups.TryGetValue(owner, out var group))
                {
                    group = new List<Entity>();
                    selectedGroups.Add(owner, group);
                }

                group.Add(edge);
            }

            foreach (var pair in selectedGroups)
            {
                var owner = pair.Key;
                var ownerEdges = GetAggregateRoadEdges(owner);
                if (ownerEdges.Count == 0 || AllEdgesSelected(ownerEdges, selectedSet))
                {
                    SetAuthoritativeName(owner, check.SelectedFinalName, "PostUpdateReapply:" + check.Operation, "SelectedAggregateReapply", pair.Value.Count, ownerEdges.Count);
                    continue;
                }

                TryPartitionAggregateForSelectedEdges(owner, pair.Value, selectedSet, "PostUpdateReapply:" + check.Operation, check.SelectedFinalName, check.OriginalVisibleName, false);
            }
        }

        // Small helper that keeps only road edges that still properly exist.
        private List<Entity> FilterValidRoadEdges(List<Entity> edges)
        {
            var result = new List<Entity>();
            for (var i = 0; i < edges.Count; i++)
            {
                if (_validation.IsValidRoadSegment(edges[i]))
                    result.Add(edges[i]);
            }

            return result;
        }

        // Quick sanity check to see if a tracked edge list still has anything useful in it.
        private bool HasAnyValidRoadEdge(List<Entity> edges)
        {
            for (var i = 0; i < edges.Count; i++)
            {
                if (_validation.IsValidRoadSegment(edges[i]))
                    return true;
            }

            return false;
        }

        // Checks whether the two edge collections overlap at all.
        private static bool ContainsAny(List<Entity> ownerEdges, HashSet<Entity> candidateEdges)
        {
            for (var i = 0; i < ownerEdges.Count; i++)
            {
                if (candidateEdges.Contains(ownerEdges[i]))
                    return true;
            }

            return false;
        }

        // Just formats entity ids for the debug logs so they are easier to read.
        private static string FormatEntitySet(HashSet<Entity> entities)
        {
            var values = new List<string>();
            foreach (var entity in entities)
                values.Add(entity.Index.ToString());

            return string.Join(",", values.ToArray());
        }
        // Duplicates an aggregate entity so the selected and remainder bits
        // can each keep their own visible name owner.
        private Entity CreateAggregateClone(Entity sourceAggregate, string operation, string role)
        {
            var clone = EntityManager.Instantiate(sourceAggregate);
            if (clone == Entity.Null || !EntityManager.Exists(clone) || !EntityManager.HasBuffer<AggregateElement>(clone))
            {
                Mod.log.Warn(() => $"Road Naming: aggregate clone failed. Operation={operation}, SourceAggregate={sourceAggregate.Index}, Role={role}.");
                return Entity.Null;
            }

            EntityManager.GetBuffer<AggregateElement>(clone).Clear();
            InvalidateAggregateLabelState(clone);
            EnsureRefreshTag<Updated>(clone);
            EnsureRefreshTag<BatchesUpdated>(clone);
            Mod.log.Info(() => $"Road Naming: aggregate clone created. Operation={operation}, SourceAggregate={sourceAggregate.Index}, CloneAggregate={clone.Index}, Role={role}.");
            return clone;
        }

        // Reassigns the given road edges to the supplied aggregate owner,
        // then tags things for a visual refresh without touching vanilla too hard.
        private void AssignAggregateEdges(Entity aggregate, List<Entity> edges, string operation, string role)
        {
            UnmarkManagedAggregate(aggregate);
            var buffer = EntityManager.GetBuffer<AggregateElement>(aggregate);
            buffer.Clear();
            for (var i = 0; i < edges.Count; i++)
            {
                var edge = edges[i];
                if (!_validation.IsValidRoadSegment(edge))
                    continue;

                buffer.Add(new AggregateElement { m_Edge = edge });
                if (EntityManager.HasComponent<Aggregated>(edge))
                    EntityManager.SetComponentData(edge, new Aggregated { m_Aggregate = aggregate });
                else
                    EntityManager.AddComponentData(edge, new Aggregated { m_Aggregate = aggregate });

                UnmarkManagedAggregateEdge(edge);
                EnsureRefreshTag<BatchesUpdated>(edge);
                Mod.log.Info(() => $"Road Naming: aggregate edge assignment. Operation={operation}, Role={role}, Edge={edge.Index}, Aggregate={aggregate.Index}, EdgeUpdatedTagSkipped=True.");
            }

            InvalidateAggregateLabelState(aggregate);
            EnsureRefreshTag<Updated>(aggregate);
            EnsureRefreshTag<BatchesUpdated>(aggregate);
        }

        private void UnmarkManagedAggregate(Entity aggregate)
        {
            if (aggregate != Entity.Null && EntityManager.Exists(aggregate) && EntityManager.HasComponent<AdvancedRoadNamingManagedAggregate>(aggregate))
                EntityManager.RemoveComponent<AdvancedRoadNamingManagedAggregate>(aggregate);
        }

        private void UnmarkManagedAggregateEdge(Entity edge)
        {
            if (edge != Entity.Null && EntityManager.Exists(edge) && EntityManager.HasComponent<AdvancedRoadNamingAggregateMember>(edge))
                EntityManager.RemoveComponent<AdvancedRoadNamingAggregateMember>(edge);
        }

        // Breaks a set of edges into connected chunks so each isolated bit
        // can get its own aggregate when needed.
        private List<List<Entity>> BuildConnectedEdgeComponents(List<Entity> edges)
        {
            var components = new List<List<Entity>>();
            var allowed = new HashSet<Entity>(edges);
            var remaining = new HashSet<Entity>(edges);
            for (var i = 0; i < edges.Count; i++)
            {
                var start = edges[i];
                if (!remaining.Contains(start))
                    continue;

                var component = new List<Entity>();
                var queue = new List<Entity> { start };
                remaining.Remove(start);
                var readIndex = 0;
                while (readIndex < queue.Count)
                {
                    var current = queue[readIndex++];
                    component.Add(current);
                    AddConnectedNeighbors(current, allowed, remaining, queue);
                }

                components.Add(component);
            }

            return components;
        }

        // Walks outward from a segment through both end nodes and queues up neighbours
        // that belong to the current allowed set.
        private void AddConnectedNeighbors(Entity segment, HashSet<Entity> allowed, HashSet<Entity> remaining, List<Entity> queue)
        {
            if (!_validation.IsValidRoadSegment(segment))
                return;

            var edge = EntityManager.GetComponentData<Edge>(segment);
            AddConnectedNeighborsFromNode(edge.m_Start, allowed, remaining, queue);
            AddConnectedNeighborsFromNode(edge.m_End, allowed, remaining, queue);
        }

        // Pulls connected edges off a node buffer and feeds the BFS queue.
        private void AddConnectedNeighborsFromNode(Entity node, HashSet<Entity> allowed, HashSet<Entity> remaining, List<Entity> queue)
        {
            if (node == Entity.Null || !EntityManager.Exists(node) || !EntityManager.HasBuffer<ConnectedEdge>(node))
                return;

            var connectedEdges = EntityManager.GetBuffer<ConnectedEdge>(node, true);
            for (var i = 0; i < connectedEdges.Length; i++)
            {
                var neighbor = connectedEdges[i].m_Edge;
                if (!allowed.Contains(neighbor) || !remaining.Contains(neighbor))
                    continue;

                remaining.Remove(neighbor);
                queue.Add(neighbor);
            }
        }

        // Reads the live visible name from the entity that currently owns it,
        // falling back to a safe label if the game does not give us one.
        private string GetCurrentAuthoritativeName(Entity nameEntity, string fallback)
        {
            if (nameEntity != Entity.Null && EntityManager.Exists(nameEntity) && _nameSystem != null)
            {
                if (_nameSystem.TryGetCustomName(nameEntity, out var customName) && !string.IsNullOrWhiteSpace(customName))
                    return customName.Trim();

                var renderedName = _nameSystem.GetRenderedLabelName(nameEntity);
                if (!string.IsNullOrWhiteSpace(renderedName))
                    return renderedName.Trim();
            }

            return string.IsNullOrWhiteSpace(fallback) ? $"Road Segment {nameEntity.Index}" : fallback.Trim();
        }
        // Nudges the aggregate label state so vanilla refreshes the visuals.
        // Do not clear generated label buffers here; the base game render jobs own them.
        private void InvalidateAggregateLabelState(Entity aggregate)
        {
            if (aggregate == Entity.Null || !EntityManager.Exists(aggregate))
                return;

            EnsureRefreshTag<Updated>(aggregate);
            EnsureRefreshTag<BatchesUpdated>(aggregate);
        }
        // Returns the actual entity whose visible name matters:
        // the aggregate when one exists, otherwise the segment itself.
        private Entity GetAuthoritativeNameEntity(Entity segment)
        {
            if (segment != Entity.Null && EntityManager.Exists(segment) && EntityManager.HasComponent<Aggregated>(segment))
            {
                var aggregate = EntityManager.GetComponentData<Aggregated>(segment).m_Aggregate;
                if (aggregate != Entity.Null && EntityManager.Exists(aggregate))
                    return aggregate;
            }

            return segment;
        }

        // Resolves all road edges currently owned by a name entity.
        // It checks both the aggregate buffer and the reverse edge to aggregate link just to be safe.
        private List<Entity> GetAggregateRoadEdges(Entity nameEntity)
        {
            var edges = new List<Entity>();
            if (nameEntity == Entity.Null || !EntityManager.Exists(nameEntity))
                return edges;

            var seen = new HashSet<Entity>();
            if (EntityManager.HasBuffer<AggregateElement>(nameEntity))
            {
                var elements = EntityManager.GetBuffer<AggregateElement>(nameEntity, true);
                for (var i = 0; i < elements.Length; i++)
                    AddAggregateRoadEdgeIfMissing(edges, seen, elements[i].m_Edge);
            }

            var bufferEdgeCount = edges.Count;
            var candidates = _aggregatedRoadEdgeQuery.ToEntityArray(Allocator.Temp);
            try
            {
                for (var i = 0; i < candidates.Length; i++)
                {
                    var edge = candidates[i];
                    if (!EntityManager.Exists(edge) || !EntityManager.HasComponent<Aggregated>(edge))
                        continue;

                    var aggregate = EntityManager.GetComponentData<Aggregated>(edge).m_Aggregate;
                    if (aggregate == nameEntity)
                        AddAggregateRoadEdgeIfMissing(edges, seen, edge);
                }
            }
            finally
            {
                candidates.Dispose();
            }

            if (edges.Count != bufferEdgeCount)
                Mod.log.Info(() => $"Road Naming: aggregate membership expanded from reverse lookup. Aggregate={nameEntity.Index}, BufferEdges={bufferEdgeCount}, ResolvedEdges={edges.Count}.");

            return edges;
        }

        // Tiny helper to avoid duplicates and ignore invalid edges while building aggregate membership.
        private void AddAggregateRoadEdgeIfMissing(List<Entity> edges, HashSet<Entity> seen, Entity edge)
        {
            if (_validation.IsValidRoadSegment(edge) && seen.Add(edge))
                edges.Add(edge);
        }

        // True only when the entire aggregate is part of the current selected set.
        private static bool AllEdgesSelected(List<Entity> aggregateEdges, HashSet<Entity> selectedSet)
        {
            for (var i = 0; i < aggregateEdges.Count; i++)
            {
                if (!selectedSet.Contains(aggregateEdges[i]))
                    return false;
            }

            return true;
        }

        // Writes the final visible name onto the real owner entity and tags bits for refresh,
        // so the label shown in-game lines up with the repository state.
        private void SetAuthoritativeName(Entity nameEntity, string finalName, string operation, string source, int selectedCount, int totalCount)
        {
            var safeName = string.IsNullOrWhiteSpace(finalName) ? $"Road Segment {nameEntity.Index}" : finalName.Trim();
            _nameSystem.SetCustomName(nameEntity, safeName);
            EnsureRefreshTag<CustomName>(nameEntity);
            EnsureRefreshTag<Updated>(nameEntity);
            EnsureRefreshTag<BatchesUpdated>(nameEntity);

            if (EntityManager.HasBuffer<AggregateElement>(nameEntity))
            {
                var elements = EntityManager.GetBuffer<AggregateElement>(nameEntity, true);
                for (var i = 0; i < elements.Length; i++)
                {
                    var edge = elements[i].m_Edge;
                    if (EntityManager.Exists(edge))
                    {
                        // BatchesUpdated refreshes label/render batches without feeding AggregateSystem an Updated edge.
                        EnsureRefreshTag<BatchesUpdated>(edge);
                    }
                }
            }

            if (Mod.IsVerboseLoggingEnabled)
            {
                VerifyAuthoritativeNameOwner(nameEntity, safeName, operation, source, selectedCount, totalCount);
                LogVisibleNameSource(nameEntity, operation, source);
            }
        }

        // Logs what sort of entity currently owns the visible label and what label buffers it has.
        private void LogVisibleNameSource(Entity nameEntity, string operation, string source)
        {
            if (!Mod.IsVerboseLoggingEnabled)
                return;

            if (nameEntity == Entity.Null || !EntityManager.Exists(nameEntity))
            {
                Mod.log.Warn(() => $"Road Naming: visible-name source missing. Operation={operation}, Source={source}, NameEntity={nameEntity.Index}.");
                return;
            }

            var aggregateElementCount = EntityManager.HasBuffer<AggregateElement>(nameEntity)
                ? EntityManager.GetBuffer<AggregateElement>(nameEntity, true).Length
                : 0;
            var labelPositionCount = EntityManager.HasBuffer<LabelPosition>(nameEntity)
                ? EntityManager.GetBuffer<LabelPosition>(nameEntity, true).Length
                : 0;
            var labelVertexCount = EntityManager.HasBuffer<LabelVertex>(nameEntity)
                ? EntityManager.GetBuffer<LabelVertex>(nameEntity, true).Length
                : 0;
            var visibleName = GetCurrentAuthoritativeName(nameEntity, string.Empty);

            Mod.log.Info(() => $"Road Naming: visible-name source readback. Operation={operation}, Source={source}, NameEntity={nameEntity.Index}, HasAggregate={EntityManager.HasComponent<Aggregate>(nameEntity)}, HasLabelMaterial={EntityManager.HasComponent<LabelMaterial>(nameEntity)}, HasLabelExtents={EntityManager.HasComponent<LabelExtents>(nameEntity)}, HasDeleted={EntityManager.HasComponent<Deleted>(nameEntity)}, HasTemp={EntityManager.HasComponent<Game.Tools.Temp>(nameEntity)}, AggregateElements={aggregateElementCount}, LabelPositions={labelPositionCount}, LabelVertices={labelVertexCount}, Name='{visibleName}'.");
        }
        // Readback check after naming writes so we can see whether NameSystem
        // stored and rendered what we expected.
        private void VerifyAuthoritativeNameOwner(Entity nameEntity, string expectedName, string operation, string source, int selectedCount, int totalCount)
        {
            if (!Mod.IsVerboseLoggingEnabled)
                return;

            var hasCustomName = _nameSystem.TryGetCustomName(nameEntity, out var storedName);
            var renderedName = _nameSystem.GetRenderedLabelName(nameEntity);
            var matched = hasCustomName && string.Equals(storedName, expectedName, System.StringComparison.Ordinal);
            var level = matched ? "passed" : "needs-check";
            Mod.log.Info(() => $"Road Naming: label visibility check {level}. Operation={operation}, Source={source}, NameEntity={nameEntity.Index}, SelectedEdges={selectedCount}, TotalAggregateEdges={totalCount}, Expected='{expectedName}', Stored='{storedName ?? string.Empty}', Rendered='{renderedName ?? string.Empty}'.");
        }
        // Saves the current applied route intent into the route database,
        // including waypoints, ordered segments and some display metadata for the UI.
        public SavedRouteRecord SaveAppliedRoute(IReadOnlyList<Entity> selectedSegments, IReadOnlyList<RoadRouteWaypoint> waypoints, RoadRouteToolMode mode, string inputValue, RouteNumberPlacement placement)
        {
            var segmentList = FilterValidRouteSegments(selectedSegments);
            var streetNames = BuildStreetNameSnapshot(segmentList);
            var metadata = BuildRouteCorridorMetadata(segmentList, streetNames);
            var title = BuildRouteRecordTitle(mode, inputValue, metadata);
            var route = _routeDatabase.CreateRoute(title, mode, inputValue, placement, waypoints, segmentList, streetNames);
            ApplyRouteCorridorMetadata(route, metadata);
            Mod.log.Info(() => $"Road Naming: route saved. RouteId={route.RouteId}, Title='{route.DisplayTitle}', Mode={route.Mode}, Input='{route.BaseInputValue}', Segments={route.SegmentCount}, Waypoints={route.WaypointCount}, Corridor='{route.DerivedDisplayCorridor}', Streets='{BuildStreetSummary(route)}'.");
            return route;
        }

        // Works out whether a saved route still looks healthy,
        // partly broken, missing, or in need of a rebuild review.
        public SavedRouteStatus EvaluateSavedRouteStatus(SavedRouteRecord route)
        {
            if (route == null || route.IsDeleted)
                return SavedRouteStatus.Deleted;

            try
            {
                if (route.OrderedSegmentIds == null || route.OrderedSegmentIds.Count == 0)
                    return route.Waypoints.Count >= 2 ? SavedRouteStatus.RebuildNeeded : SavedRouteStatus.MissingSegments;

                var validSegments = FilterValidRouteSegments(route.OrderedSegmentIds);
                if (validSegments.Count == 0)
                    return route.Waypoints.Count >= 2 ? SavedRouteStatus.RebuildNeeded : SavedRouteStatus.MissingSegments;

                if (validSegments.Count != route.OrderedSegmentIds.Count)
                    return SavedRouteStatus.PartiallyValid;

                if (HasAggregateExtentDrift(route, validSegments) || HasDuplicateDesignationArtefacts(route, validSegments))
                    return SavedRouteStatus.PartiallyValid;

                return SavedRouteStatus.Valid;
            }
            catch (System.Exception ex)
            {
                Mod.log.Warn(() => $"Road Naming: saved route status evaluation failed for route {route.RouteId}. Error='{ex.Message}'.");
                return SavedRouteStatus.PartiallyValid;
            }
        }

        // Builds a preview review session for rebuilding a saved route from its waypoint anchors.
        public bool TryCreateRebuildReview(long routeId, out SavedRouteReviewSession review, out string message)
        {
            review = null;
            if (!_routeDatabase.TryGet(routeId, out var route))
            {
                message = $"Saved route {routeId} was not found.";
                return false;
            }

            if (!TryBuildRouteCandidate(route, out var candidateSegments, out message))
                return false;

            review = new SavedRouteReviewSession
            {
                RouteId = route.RouteId,
                Mode = SavedRouteReviewMode.RebuildPreview,
                RouteMode = route.Mode,
                InputValue = route.BaseInputValue ?? string.Empty,
                RouteNumberPlacement = route.RouteNumberPlacement,
                Message = BuildRebuildReviewMessage(route, candidateSegments),
                IsDirty = false
            };
            CopyWaypoints(review.CandidateWaypoints, route.Waypoints);
            AddSegments(review.CandidateSegments, candidateSegments);
            var reviewCandidateSegmentCount = review.CandidateSegments.Count;
            Mod.log.Info(() => $"Road Naming: rebuild review prepared. RouteId={route.RouteId}, CandidateSegments={reviewCandidateSegmentCount}, StoredSegments={route.OrderedSegmentIds.Count}, Status={EvaluateSavedRouteStatus(route)}.");
            message = review.Message;
            return true;
        }

        // Prepares an edit session for an existing saved route so the player can tweak it before committing.
        public bool TryCreateModifyReview(long routeId, out SavedRouteReviewSession review, out string message)
        {
            review = null;
            if (!_routeDatabase.TryGet(routeId, out var route))
            {
                message = $"Saved route {routeId} was not found.";
                return false;
            }

            var candidateSegments = FilterValidRouteSegments(route.OrderedSegmentIds);
            if (candidateSegments.Count == 0 && !TryBuildRouteCandidate(route, out candidateSegments, out _))
            {
                message = $"Saved route '{route.DisplayTitle}' could not be loaded for editing because its path could not be reconstructed.";
                return false;
            }

            review = new SavedRouteReviewSession
            {
                RouteId = route.RouteId,
                Mode = SavedRouteReviewMode.Modify,
                RouteMode = route.Mode,
                InputValue = route.BaseInputValue ?? string.Empty,
                RouteNumberPlacement = route.RouteNumberPlacement,
                Message = $"Modify mode active for '{BuildRouteDisplayTitle(route)}'. Drag existing waypoints or the route line to edit, then commit or cancel.",
                IsDirty = false
            };
            CopyWaypoints(review.CandidateWaypoints, route.Waypoints);
            AddSegments(review.CandidateSegments, candidateSegments);
            var reviewCandidateSegmentCount = review.CandidateSegments.Count;
            var reviewCandidateWaypointCount = review.CandidateWaypoints.Count;
            Mod.log.Info(() => $"Road Naming: modify review prepared. RouteId={route.RouteId}, CandidateSegments={reviewCandidateSegmentCount}, Waypoints={reviewCandidateWaypointCount}.");
            message = review.Message;
            return true;
        }

        // This is the safe commit pipeline for saved routes:
        // clear affected segments back to base state, replay overlapping routes, then apply the final reviewed route.
        public bool CommitSavedRouteReview(long routeId, IReadOnlyList<RoadRouteWaypoint> finalWaypoints, IReadOnlyList<Entity> finalSegments, RouteNumberPlacement placement, out string message)
        {
            if (!_routeDatabase.TryGet(routeId, out var route))
            {
                message = $"Saved route {routeId} was not found.";
                return false;
            }

            var normalizedFinalSegments = FilterValidRouteSegments(finalSegments);
            if (normalizedFinalSegments.Count == 0)
            {
                message = $"Saved route '{route.DisplayTitle}' could not be committed because the final route path is empty.";
                return false;
            }

            var normalizedWaypoints = CloneWaypoints(finalWaypoints);
            if (normalizedWaypoints.Count < 2)
            {
                message = $"Saved route '{route.DisplayTitle}' needs at least two waypoints before it can be committed.";
                return false;
            }

            var affectedSegments = BuildAffectedRouteMutationSet(route, normalizedFinalSegments);
            var fallbackBaseNames = BuildBaseNameFallbackMap(route, affectedSegments);
            ResetSegmentsToBaseMetadata(new List<Entity>(affectedSegments), fallbackBaseNames, route);

            var remainingRoutes = GetReplayOrderedRoutesExcept(affectedSegments, route.RouteId);
            for (var i = 0; i < remainingRoutes.Count; i++)
                ReplaySavedRouteContribution(remainingRoutes[i], affectedSegments);

            var streetNames = BuildStreetNameSnapshot(normalizedFinalSegments);
            _routeDatabase.ReplaceRouteIntent(route, placement, normalizedWaypoints, normalizedFinalSegments, streetNames);
            ApplyRouteCorridorMetadata(route, BuildRouteCorridorMetadata(normalizedFinalSegments, streetNames));
            if (!route.IsUserDefinedTitle)
                route.DisplayTitle = BuildRouteRecordTitle(route.Mode, route.BaseInputValue, BuildRouteCorridorMetadata(normalizedFinalSegments, streetNames));

            ReplaySavedRouteContribution(route, new HashSet<Entity>(normalizedFinalSegments));

            var refreshSegments = new List<Entity>(affectedSegments);
            for (var i = 0; i < normalizedFinalSegments.Count; i++)
                AddEntityIfMissing(refreshSegments, normalizedFinalSegments[i]);

            ApplyResolvedMetadataToSegments(refreshSegments, "CommitSavedRouteReview");
            route.UpdatedAtUtcTicks = System.DateTime.UtcNow.Ticks;
            route.LastAppliedUtcTicks = route.UpdatedAtUtcTicks;
            message = $"Committed saved route '{BuildRouteDisplayTitle(route)}' using {normalizedFinalSegments.Count} corrected segment(s).";
            Mod.log.Info(() => $"Road Naming: saved route review committed. RouteId={route.RouteId}, FinalSegments={normalizedFinalSegments.Count}, AffectedSegments={refreshSegments.Count}, ReplayedRoutes={remainingRoutes.Count}.");
            return true;
        }

        // Reapplies a saved route by feeding it back through the same safe review-commit path.
        public bool ReapplySavedRoute(long routeId, out string message)
        {
            if (!_routeDatabase.TryGet(routeId, out var route))
            {
                message = $"Saved route {routeId} was not found.";
                Mod.log.Warn(() => $"Road Naming: route reapply failed; missing route. RouteId={routeId}.");
                return false;
            }

            var validSegments = FilterValidRouteSegments(route.OrderedSegmentIds);
            if (validSegments.Count == 0)
            {
                message = $"Saved route '{route.DisplayTitle}' has no valid road segments. Try Rebuild.";
                Mod.log.Warn(() => $"route reapply failed; no valid segments. RouteId={routeId}, StoredSegments={route.OrderedSegmentIds.Count}.");
                return false;
            }

            var result = CommitSavedRouteReview(routeId, route.Waypoints, validSegments, route.RouteNumberPlacement, out message);
            var reapplyMessage = message;
            if (result)
                Mod.log.Info(() => $"route reapplied through safe commit pipeline. RouteId={route.RouteId}, Mode={route.Mode}, Input='{route.BaseInputValue}', Segments={validSegments.Count}.");
            else
                Mod.log.Warn(() => $"safe route reapply failed. RouteId={route.RouteId}, Message='{reapplyMessage}'.");

            return result;
        }

        // Rebuilds a candidate path by pathfinding from waypoint to waypoint.
        // This is preview-first on purpose, because the rebuilt path may not be a perfect match.
        private bool TryBuildRouteCandidate(SavedRouteRecord route, out List<Entity> rebuilt, out string message)
        {
            rebuilt = new List<Entity>();
            if (route == null)
            {
                message = "Saved route could not be loaded.";
                return false;
            }

            if (route.Waypoints.Count < 2)
            {
                message = $"Saved route '{route.DisplayTitle}' does not have enough waypoints to rebuild.";
                return false;
            }

            if (!_validation.IsValidRoadSegment(route.Waypoints[0].Segment))
            {
                message = $"Saved route '{route.DisplayTitle}' starts on a missing road segment.";
                return false;
            }

            AddEntityIfMissing(rebuilt, route.Waypoints[0].Segment);
            for (var i = 1; i < route.Waypoints.Count; i++)
            {
                if (!_validation.IsValidRoadSegment(route.Waypoints[i - 1].Segment) || !_validation.IsValidRoadSegment(route.Waypoints[i].Segment))
                {
                    message = $"Saved route '{route.DisplayTitle}' has missing waypoint anchors.";
                    return false;
                }

                var path = Pathing.FindPath(route.Waypoints[i - 1].Segment, route.Waypoints[i].Segment, 512);
                if (path.Count == 0)
                {
                    message = $"Could not rebuild saved route '{route.DisplayTitle}' between waypoint {i} and {i + 1}.";
                    Mod.log.Warn(() => $"Road Naming: route candidate build failed; no path. RouteId={route.RouteId}, From={route.Waypoints[i - 1].Segment.Index}, To={route.Waypoints[i].Segment.Index}.");
                    return false;
                }

                for (var pathIndex = 0; pathIndex < path.Count; pathIndex++)
                    AddEntityIfMissing(rebuilt, path[pathIndex]);
            }

            message = $"Prepared a rebuild candidate with {rebuilt.Count} segment(s). Review before committing.";
            return true;
        }

        // Builds the little summary text shown in rebuild review so the player knows what changed.
        private string BuildRebuildReviewMessage(SavedRouteRecord route, IReadOnlyList<Entity> candidateSegments)
        {
            var storedSegments = FilterValidRouteSegments(route.OrderedSegmentIds);
            var changed = CandidateDiffersMaterially(storedSegments, candidateSegments);
            if (HasAggregateExtentDrift(route, storedSegments))
                return $"Rebuild review ready for '{BuildRouteDisplayTitle(route)}'. The current named extent appears to drift beyond the saved route anchors. Review the candidate, then accept or modify.";

            if (changed)
                return $"Rebuild review ready for '{BuildRouteDisplayTitle(route)}'. The candidate path differs from the stored route and must be reviewed before it is reapplied.";

            return $"Rebuild review ready for '{BuildRouteDisplayTitle(route)}'. The saved anchors still resolve cleanly, but commit is still preview-first.";
        }

        // Deletes a saved route record, then rolls back its contribution and replays any overlapping routes.
        public bool DeleteSavedRoute(long routeId, out string message)
        {
            if (!_routeDatabase.TryGet(routeId, out var route))
            {
                message = $"Saved route {routeId} was not found.";
                Mod.log.Warn(() => $"Road Naming: route delete failed; missing route. RouteId={routeId}.");
                return false;
            }

            var affectedSegments = FilterValidRouteSegments(route.OrderedSegmentIds);
            Mod.log.Info(() => $"Road Naming: route removal requested. RouteId={routeId}, Mode={route.Mode}, Input='{route.BaseInputValue}', StoredSegments={route.OrderedSegmentIds.Count}, ValidSegments={affectedSegments.Count}.");

            if (!_routeDatabase.Delete(routeId))
            {
                message = $"Saved route {routeId} could not be deleted.";
                Mod.log.Warn(() => $"Road Naming: route delete failed during database removal. RouteId={routeId}.");
                return false;
            }

            var replayedRoutes = RebuildSegmentsAfterRouteDeletion(route, affectedSegments);
            message = affectedSegments.Count > 0
                ? $"Deleted saved route {routeId} and reverted {affectedSegments.Count} affected road segment(s)."
                : $"Deleted saved route {routeId}. No valid affected road segments remained to revert.";
            Mod.log.Info(() => $"Road Naming: route deleted and reverted. RouteId={routeId}, RevertedSegments={affectedSegments.Count}, ReplayedRoutes={replayedRoutes}.");
            return true;
        }

        // Clears affected segments back to base metadata after a route delete,
        // then reapplies the remaining saved routes in order.
        private int RebuildSegmentsAfterRouteDeletion(SavedRouteRecord deletedRoute, IReadOnlyList<Entity> affectedSegments)
        {
            if (deletedRoute == null || affectedSegments == null || affectedSegments.Count == 0)
                return 0;

            var affectedSet = new HashSet<Entity>(affectedSegments);
            var fallbackBaseNames = BuildBaseNameFallbackMap(deletedRoute, affectedSet);
            ResetSegmentsToBaseMetadata(affectedSegments, fallbackBaseNames, deletedRoute);

            var remainingRoutes = GetReplayOrderedRoutes(affectedSet);
            for (var i = 0; i < remainingRoutes.Count; i++)
                ReplaySavedRouteContribution(remainingRoutes[i], affectedSet);

            ApplyResolvedMetadataToSegments(affectedSegments, "DeleteSavedRouteRevert");
            Mod.log.Info(() => $"Road Naming: route revert completed. DeletedRouteId={deletedRoute.RouteId}, AffectedSegments={affectedSegments.Count}, RemainingRoutesReplayed={remainingRoutes.Count}.");
            return remainingRoutes.Count;
        }

        // Collects every segment that could be touched by this route mutation,
        // including old segments, new segments, and any live spill-over from aggregate naming.
        private HashSet<Entity> BuildAffectedRouteMutationSet(SavedRouteRecord route, IReadOnlyList<Entity> finalSegments)
        {
            var affected = new HashSet<Entity>();
            if (route != null)
            {
                for (var i = 0; i < route.OrderedSegmentIds.Count; i++)
                {
                    if (_validation.IsValidRoadSegment(route.OrderedSegmentIds[i]))
                        affected.Add(route.OrderedSegmentIds[i]);
                }
            }

            if (finalSegments != null)
            {
                for (var i = 0; i < finalSegments.Count; i++)
                {
                    if (_validation.IsValidRoadSegment(finalSegments[i]))
                        affected.Add(finalSegments[i]);
                }
            }

            foreach (var carriedSegment in FindSegmentsCurrentlyCarryingRouteDesignation(route))
                affected.Add(carriedSegment);

            return affected;
        }

        // Builds a fallback map of base road names so reverts have something sensible to restore to.
        private Dictionary<Entity, string> BuildBaseNameFallbackMap(SavedRouteRecord deletedRoute, HashSet<Entity> affectedSet)
        {
            var fallbackNames = new Dictionary<Entity, string>();
            CollectBaseNameFallbacks(deletedRoute, affectedSet, fallbackNames);
            foreach (var route in _routeDatabase.Routes)
                CollectBaseNameFallbacks(route, affectedSet, fallbackNames);
            return fallbackNames;
        }

        // Pulls original street-name snapshots out of a saved route for any affected segments we care about.
        private static void CollectBaseNameFallbacks(SavedRouteRecord route, HashSet<Entity> affectedSet, Dictionary<Entity, string> fallbackNames)
        {
            if (route == null || affectedSet == null || fallbackNames == null)
                return;

            for (var i = 0; i < route.OrderedSegmentIds.Count; i++)
            {
                var segment = route.OrderedSegmentIds[i];
                if (!affectedSet.Contains(segment) || fallbackNames.ContainsKey(segment))
                    continue;

                var fallbackName = i < route.OriginalStreetNamesSnapshot.Count ? route.OriginalStreetNamesSnapshot[i] ?? string.Empty : string.Empty;
                if (!string.IsNullOrWhiteSpace(fallbackName))
                    fallbackNames[segment] = fallbackName.Trim();
            }
        }

        // Resets affected segments back to their plain base metadata,
        // clearing route numbers and custom names before replay happens.
        private void ResetSegmentsToBaseMetadata(IReadOnlyList<Entity> affectedSegments, Dictionary<Entity, string> fallbackBaseNames, SavedRouteRecord route)
        {
            for (var i = 0; i < affectedSegments.Count; i++)
            {
                var segment = affectedSegments[i];
                if (!_validation.IsValidRoadSegment(segment))
                    continue;

                var metadata = _repository.GetOrCreate(segment);
                if (string.IsNullOrWhiteSpace(metadata.BaseNameSnapshot))
                {
                    if (fallbackBaseNames != null && fallbackBaseNames.TryGetValue(segment, out var fallbackName) && !string.IsNullOrWhiteSpace(fallbackName))
                        metadata.BaseNameSnapshot = SanitizeBaseNameForRoute(route, fallbackName);
                    else
                        metadata.BaseNameSnapshot = SanitizeBaseNameForRoute(route, GetBaseName(segment));
                }
                else
                {
                    metadata.BaseNameSnapshot = SanitizeBaseNameForRoute(route, metadata.BaseNameSnapshot);
                }

                metadata.OptionalCustomRoadName = null;
                metadata.RouteNumbers.Clear();
                metadata.Touch();
            }
        }

        // Finds the saved routes that still touch the affected area and sorts them by apply order.
        private List<SavedRouteRecord> GetReplayOrderedRoutes(HashSet<Entity> affectedSet)
        {
            var routes = new List<SavedRouteRecord>();
            foreach (var route in _routeDatabase.Routes)
            {
                if (RouteTouchesAnyAffectedSegment(route, affectedSet))
                    routes.Add(route);
            }

            routes.Sort((left, right) =>
            {
                var leftTicks = EffectiveApplyTicks(left);
                var rightTicks = EffectiveApplyTicks(right);
                var compare = leftTicks.CompareTo(rightTicks);
                if (compare != 0)
                    return compare;
                return left.RouteId.CompareTo(right.RouteId);
            });
            return routes;
        }

        // Same replay list as above, just leaving one route out of the mix.
        private List<SavedRouteRecord> GetReplayOrderedRoutesExcept(HashSet<Entity> affectedSet, long excludedRouteId)
        {
            var routes = GetReplayOrderedRoutes(affectedSet);
            routes.RemoveAll(route => route.RouteId == excludedRouteId);
            return routes;
        }

        // Picks the best timestamp to sort replay order with.
        private static long EffectiveApplyTicks(SavedRouteRecord route)
        {
            if (route == null)
                return 0;

            if (route.LastAppliedUtcTicks > 0)
                return route.LastAppliedUtcTicks;
            if (route.CreatedAtUtcTicks > 0)
                return route.CreatedAtUtcTicks;
            return route.UpdatedAtUtcTicks;
        }

        // Quick overlap test for whether a saved route touches the current affected set.
        private static bool RouteTouchesAnyAffectedSegment(SavedRouteRecord route, HashSet<Entity> affectedSet)
        {
            if (route == null || affectedSet == null)
                return false;

            for (var i = 0; i < route.OrderedSegmentIds.Count; i++)
            {
                if (affectedSet.Contains(route.OrderedSegmentIds[i]))
                    return true;
            }

            return false;
        }

        // Replays one saved route's intent back into repository metadata.
        // This lets overlapping saved routes stack back up in the right order after a reset.
        private void ReplaySavedRouteContribution(SavedRouteRecord route, HashSet<Entity> affectedSet)
        {
            if (route == null || affectedSet == null)
                return;

            var settings = CurrentDisplaySettings();
            if (route.Mode == RoadRouteToolMode.RenameSelectedSegments)
            {
                if (string.IsNullOrWhiteSpace(route.BaseInputValue))
                    return;

                for (var i = 0; i < route.OrderedSegmentIds.Count; i++)
                {
                    var segment = route.OrderedSegmentIds[i];
                    if (!affectedSet.Contains(segment) || !_validation.IsValidRoadSegment(segment))
                        continue;

                    var metadata = _repository.GetOrCreate(segment);
                    if (string.IsNullOrWhiteSpace(metadata.BaseNameSnapshot) && i < route.OriginalStreetNamesSnapshot.Count && !string.IsNullOrWhiteSpace(route.OriginalStreetNamesSnapshot[i]))
                        metadata.BaseNameSnapshot = route.OriginalStreetNamesSnapshot[i].Trim();
                    metadata.OptionalCustomRoadName = route.BaseInputValue.Trim();
                    metadata.Touch();
                }

                Mod.log.Info(() => $"Road Naming: replayed rename contribution. RouteId={route.RouteId}, Input='{route.BaseInputValue}'.");
                return;
            }

            if (route.Mode != RoadRouteToolMode.AssignMajorRouteNumber || !_routeCodeService.TryNormalize(route.BaseInputValue, out var routeCode, out _))
                return;

            for (var i = 0; i < route.OrderedSegmentIds.Count; i++)
            {
                var segment = route.OrderedSegmentIds[i];
                if (!affectedSet.Contains(segment) || !_validation.IsValidRoadSegment(segment))
                    continue;

                var metadata = _repository.GetOrCreate(segment);
                if (string.IsNullOrWhiteSpace(metadata.BaseNameSnapshot) && i < route.OriginalStreetNamesSnapshot.Count && !string.IsNullOrWhiteSpace(route.OriginalStreetNamesSnapshot[i]))
                    metadata.BaseNameSnapshot = route.OriginalStreetNamesSnapshot[i].Trim();

                _routeCodeService.AddRouteCode(new RouteCodeService.SegmentRouteMetadataAdapter(metadata.RouteNumbers), routeCode, settings.AllowMultipleRouteNumbers);
                metadata.RouteNumberPlacement = route.RouteNumberPlacement;

                metadata.Touch();
            }

            Mod.log.Info(() => $"Road Naming: replayed route-code contribution. RouteId={route.RouteId}, Mode={route.Mode}, RouteCode='{routeCode}'.");
        }

        // Looks at live visible-name owners to find segments still carrying this route's label,
        // even if the stored segment list no longer tells the full story.
        private HashSet<Entity> FindSegmentsCurrentlyCarryingRouteDesignation(SavedRouteRecord route)
        {
            var carried = new HashSet<Entity>();
            if (route == null)
                return carried;

            var visitedOwners = new HashSet<Entity>();
            var seedSegments = FilterValidRouteSegments(route.OrderedSegmentIds);
            if (seedSegments.Count == 0)
            {
                if (_validation.IsValidRoadSegment(route.StartAnchorSegment))
                    seedSegments.Add(route.StartAnchorSegment);
                if (_validation.IsValidRoadSegment(route.EndAnchorSegment))
                    AddEntityIfMissing(seedSegments, route.EndAnchorSegment);
            }

            for (var i = 0; i < seedSegments.Count; i++)
            {
                var owner = GetAuthoritativeNameEntity(seedSegments[i]);
                if (!visitedOwners.Add(owner))
                    continue;

                var currentName = GetCurrentAuthoritativeName(owner, string.Empty);
                if (!VisibleNameMatchesRoute(route, currentName))
                    continue;

                var ownerEdges = GetAggregateRoadEdges(owner);
                for (var edgeIndex = 0; edgeIndex < ownerEdges.Count; edgeIndex++)
                    carried.Add(ownerEdges[edgeIndex]);
            }

            return carried;
        }

        // Detects when vanilla aggregate naming has drifted wider than the route should really cover.
        private bool HasAggregateExtentDrift(SavedRouteRecord route, IReadOnlyList<Entity> validSegments)
        {
            if (route == null)
                return false;

            if (validSegments == null || validSegments.Count == 0)
                return false;

            try
            {
                var intended = new HashSet<Entity>();
                for (var i = 0; i < validSegments.Count; i++)
                {
                    if (validSegments[i] != Entity.Null)
                        intended.Add(validSegments[i]);
                }

                if (intended.Count == 0)
                    return false;

                var authoritativeOwners = GetDistinctAuthoritativeNameOwners(validSegments);
                for (var i = 0; i < authoritativeOwners.Count; i++)
                {
                    var owner = authoritativeOwners[i];
                    var currentName = GetCurrentAuthoritativeName(owner, string.Empty) ?? string.Empty;
                    if (!VisibleNameMatchesRoute(route, currentName))
                        continue;

                    var ownerEdges = GetAggregateRoadEdges(owner);
                    if (ownerEdges == null || ownerEdges.Count == 0)
                        continue;

                    for (var edgeIndex = 0; edgeIndex < ownerEdges.Count; edgeIndex++)
                    {
                        var extraEdge = ownerEdges[edgeIndex];
                        if (extraEdge == Entity.Null)
                            continue;

                        if (!intended.Contains(extraEdge))
                            return true;
                    }
                }

                return false;
            }
            catch (System.Exception ex)
            {
                Mod.log.Warn(() => $"Road Naming: HasAggregateExtentDrift failed for route {route.RouteId}. Error='{ex.Message}'.");
                return false;
            }
        }

        // Detects duplicate route-code artefacts in live visible names,
        // which is a good sign something has gone a bit sideways.
        private bool HasDuplicateDesignationArtefacts(SavedRouteRecord route, IReadOnlyList<Entity> validSegments)
        {
            if (route == null || validSegments == null || validSegments.Count == 0)
                return false;

            try
            {
                if (!_routeCodeService.TryNormalize(route.BaseInputValue, out var routeCode, out _))
                    return false;

                var authoritativeOwners = GetDistinctAuthoritativeNameOwners(validSegments);
                for (var i = 0; i < authoritativeOwners.Count; i++)
                {
                    var owner = authoritativeOwners[i];
                    var currentName = GetCurrentAuthoritativeName(owner, string.Empty) ?? string.Empty;
                    if (CountRouteCodeOccurrences(currentName, routeCode) > 1)
                    {
                        Mod.log.Warn(() => $"Road Naming: duplicate route designation artefacts detected. RouteId={route.RouteId}, Aggregate={owner.Index}, VisibleName='{currentName}'.");
                        return true;
                    }
                }

                return false;
            }
            catch (System.Exception ex)
            {
                Mod.log.Warn(() => $"Road Naming: duplicate designation artefact check failed for route {route.RouteId}. Error='{ex.Message}'.");
                return false;
            }
        }

        // Resolves the unique visible-name owners for a segment list so checks can work
        // at the aggregate level without repeating the same owner over and over.
        private List<Entity> GetDistinctAuthoritativeNameOwners(IReadOnlyList<Entity> segments)
        {
            var owners = new List<Entity>();
            if (segments == null || segments.Count == 0)
                return owners;

            var seenOwners = new HashSet<Entity>();
            for (var i = 0; i < segments.Count; i++)
            {
                var segment = segments[i];
                if (segment == Entity.Null)
                    continue;

                var owner = GetAuthoritativeNameEntity(segment);
                if (owner == Entity.Null || !EntityManager.Exists(owner) || !seenOwners.Add(owner))
                    continue;

                owners.Add(owner);
            }

            return owners;
        }

        // Compares stored and rebuilt paths and decides whether the difference is meaningful enough to matter.
        private static bool CandidateDiffersMaterially(IReadOnlyList<Entity> storedSegments, IReadOnlyList<Entity> candidateSegments)
        {
            if (storedSegments == null || candidateSegments == null || storedSegments.Count == 0 || candidateSegments.Count == 0)
                return true;

            if (storedSegments[0] != candidateSegments[0] || storedSegments[storedSegments.Count - 1] != candidateSegments[candidateSegments.Count - 1])
                return true;

            var storedSet = new HashSet<Entity>(storedSegments);
            var overlap = 0;
            for (var i = 0; i < candidateSegments.Count; i++)
            {
                if (storedSet.Contains(candidateSegments[i]))
                    overlap++;
            }

            var denominator = storedSet.Count > candidateSegments.Count ? storedSet.Count : candidateSegments.Count;
            if (denominator == 0)
                return false;

            return overlap / (float)denominator < RebuildCandidateOverlapThreshold;
        }

        // Checks whether a live visible label still looks like it belongs to a given saved route.
        private bool VisibleNameMatchesRoute(SavedRouteRecord route, string currentName)
        {
            if (route == null || string.IsNullOrWhiteSpace(currentName))
                return false;

            if (route.Mode == RoadRouteToolMode.RenameSelectedSegments)
                return string.Equals(currentName.Trim(), (route.BaseInputValue ?? string.Empty).Trim(), System.StringComparison.OrdinalIgnoreCase);

            return _routeCodeService.TryNormalize(route.BaseInputValue, out var routeCode, out _) && CountRouteCodeOccurrences(currentName, routeCode) > 0;
        }

        // Counts whole-token route code matches inside a visible name.
        private static int CountRouteCodeOccurrences(string value, string routeCode)
        {
            if (string.IsNullOrWhiteSpace(value) || string.IsNullOrWhiteSpace(routeCode))
                return 0;

            var count = 0;
            try
            {
                // Unicode-aware pattern: split on any non-letter, non-number, non-hyphen
                var tokens = Regex.Split(value, @"[^\p{L}\p{N}\-]+");
                for (var i = 0; i < tokens.Length; i++)
                {
                    if (string.Equals(tokens[i], routeCode, System.StringComparison.OrdinalIgnoreCase))
                        count++;
                }
            }
            catch (System.Exception ex)
            {
                Mod.log.Warn(() => $"Road Naming: Unicode regex processing failed on value '{value}'. Error: {ex.Message}");
                return 0;
            }

            return count;
        }

        // Strips route code suffixes back off a base name snapshot so we do not keep baking them in.
        private string SanitizeBaseNameForRoute(SavedRouteRecord route, string baseName)
        {
            var sanitized = string.IsNullOrWhiteSpace(baseName) ? string.Empty : baseName.Trim();
            if (route == null || route.Mode != RoadRouteToolMode.AssignMajorRouteNumber)
                return sanitized;

            if (!_routeCodeService.TryNormalize(route.BaseInputValue, out var routeCode, out _))
                return sanitized;

            try
            {
                // Unicode-aware regex patterns
                sanitized = Regex.Replace(
                    sanitized, 
                    @"(\s*-\s*" + Regex.Escape(routeCode) + @")+$", 
                    string.Empty, 
                    RegexOptions.IgnoreCase
                ).Trim();
                sanitized = Regex.Replace(sanitized, @"\s{2,}", " ").Trim();
            }
            catch (System.Exception ex)
            {
                Mod.log.Warn(() => $"Road Naming: Unicode sanitization failed. Error: {ex.Message}");
            }

            return sanitized;
        }

        // Makes a plain copy of waypoint data for review sessions and route updates.
        private static List<RoadRouteWaypoint> CloneWaypoints(IReadOnlyList<RoadRouteWaypoint> source)
        {
            var result = new List<RoadRouteWaypoint>();
            CopyWaypoints(result, source);
            return result;
        }

        // Copies waypoint entries across without any extra fuss.
        private static void CopyWaypoints(List<RoadRouteWaypoint> target, IReadOnlyList<RoadRouteWaypoint> source)
        {
            if (target == null || source == null)
                return;

            for (var i = 0; i < source.Count; i++)
                target.Add(source[i]);
        }

        // Adds segments to a list while keeping duplicates out of the way.
        private static void AddSegments(List<Entity> target, IReadOnlyList<Entity> source)
        {
            if (target == null || source == null)
                return;

            for (var i = 0; i < source.Count; i++)
            {
                if (!target.Contains(source[i]))
                    target.Add(source[i]);
            }
        }

        // Resolves repository metadata back into final display names
        // and pushes the results into the live world for the affected segments.
        private void ApplyResolvedMetadataToSegments(IReadOnlyList<Entity> affectedSegments, string operation)
        {
            if (affectedSegments == null || affectedSegments.Count == 0)
                return;

            var settings = CurrentDisplaySettings();
            var appliedSegments = new List<Entity>();
            for (var i = 0; i < affectedSegments.Count; i++)
            {
                var segment = affectedSegments[i];
                if (!_validation.IsValidRoadSegment(segment) || !_repository.TryGet(segment, out var metadata))
                    continue;

                var resolvedName = _resolver.Resolve(GetBaseName(segment), metadata, settings);
                SetSegmentDisplayName(segment, resolvedName);
                AddEntityIfMissing(appliedSegments, segment);
            }

            ApplyAuthoritativeVisibleNames(appliedSegments, operation);
            Mod.log.Info(() => $"Road Naming: world labels refreshed after route revert. Operation={operation}, Segments={appliedSegments.Count}.");
        }

        // Re-captures the current live road names into a saved route's metadata snapshots.
        public bool CaptureSavedRouteRoadNames(long routeId, out string message)
        {
            if (!_routeDatabase.TryGet(routeId, out var route))
            {
                message = $"Saved route {routeId} was not found.";
                return false;
            }

            var validSegments = FilterValidRouteSegments(route.OrderedSegmentIds);
            if (validSegments.Count == 0)
            {
                message = $"Saved route '{route.DisplayTitle}' has no valid road segments available for road-name capture.";
                return false;
            }

            var updated = 0;
            for (var i = 0; i < validSegments.Count; i++)
            {
                var segment = validSegments[i];
                if (!TryResolveGameStreetName(segment, out var currentName, out _))
                    continue;

                var sanitized = SanitizeBaseNameForRoute(route, currentName);
                if (string.IsNullOrWhiteSpace(sanitized))
                    continue;

                var metadata = _repository.GetOrCreate(segment);
                if (!string.Equals(metadata.BaseNameSnapshot, sanitized, System.StringComparison.Ordinal)
                    || !string.IsNullOrWhiteSpace(metadata.OptionalCustomRoadName))
                {
                    updated++;
                }

                metadata.BaseNameSnapshot = sanitized;
                metadata.OptionalCustomRoadName = null;
                metadata.Touch();
            }

            var streetNames = BuildStreetNameSnapshot(validSegments);
            _routeDatabase.ReplaceRouteIntent(route, route.RouteNumberPlacement, route.Waypoints, validSegments, streetNames);
            ApplyRouteCorridorMetadata(route, BuildRouteCorridorMetadata(validSegments, streetNames));
            if (!route.IsUserDefinedTitle)
                route.DisplayTitle = BuildRouteRecordTitle(route.Mode, route.BaseInputValue, BuildRouteCorridorMetadata(validSegments, streetNames));

            ApplyResolvedMetadataToSegments(validSegments, "CaptureSavedRouteRoadNames");
            route.UpdatedAtUtcTicks = System.DateTime.UtcNow.Ticks;
            message = $"Captured current road names for '{BuildRouteDisplayTitle(route)}'.";
            Mod.log.Info(() => $"Road Naming: saved route road-name capture complete. RouteId={route.RouteId}, Segments={validSegments.Count}, UpdatedSnapshots={updated}.");
            return true;
        }

        // Renames just the saved route record title in the database.
        public bool RenameSavedRoute(long routeId, string title, out string message)
        {
            if (_routeDatabase.Rename(routeId, title))
            {
                message = $"Renamed saved route {routeId}.";
                Mod.log.Info(() => $"Road Naming: route record renamed. RouteId={routeId}, Title='{title}'.");
                return true;
            }

            message = $"Saved route {routeId} was not found or the title was blank.";
            return false;
        }

        // Updates the saved route's base input value, like the route code or rename text.
        public bool UpdateSavedRouteInput(long routeId, string inputValue, out string message)
        {
            if (_routeDatabase.UpdateInput(routeId, inputValue))
            {
                message = $"Updated saved route {routeId} input value.";
                Mod.log.Info(() => $"Road Naming: route input updated. RouteId={routeId}, Input='{inputValue}'.");
                return true;
            }

            message = $"Saved route {routeId} was not found.";
            return false;
        }

        // Intentionally blocked direct rebuild path.
        // The player has to go through preview/review first so nothing dodgy gets applied blind.
        public bool RebuildSavedRoute(long routeId, out string message)
        {
            message = $"Direct rebuild is disabled for saved route {routeId}. Start rebuild review instead.";
            Mod.log.Warn(() => $"Road Naming: direct rebuild call blocked. RouteId={routeId}. Use preview-first rebuild review.");
            return false;
        }

        // Builds the Saved Routes payload consumed by the UI panel.
        public string BuildSavedRoutesJson()
        {
            var parts = new List<string>();
            foreach (var route in _routeDatabase.Routes)
            {
                if (route == null)
                {
                    Mod.log.Warn("Road Naming: skipped a null saved route record while building Saved Routes JSON.");
                    continue;
                }

                try
                {
                    EnsureRouteCorridorMetadata(route);
                    var status = EvaluateSavedRouteStatus(route).ToString();
                    parts.Add("{" +
                        "\"id\":" + route.RouteId.ToString(System.Globalization.CultureInfo.InvariantCulture) + "," +
                        "\"title\":" + JsonString(BuildRouteDisplayTitle(route)) + "," +
                        "\"savedTitle\":" + JsonString(route.DisplayTitle) + "," +
                        "\"userTitle\":" + (route.IsUserDefinedTitle ? "true" : "false") + "," +
                        "\"mode\":" + JsonString(route.Mode.ToString()) + "," +
                        "\"input\":" + JsonString(route.BaseInputValue) + "," +
                        "\"routeCode\":" + JsonString(route.RouteCode ?? route.BaseInputValue) + "," +
                        "\"routePrefixType\":" + JsonString(route.RoutePrefixType) + "," +
                        "\"segments\":" + route.SegmentCount.ToString(System.Globalization.CultureInfo.InvariantCulture) + "," +
                        "\"waypoints\":" + route.WaypointCount.ToString(System.Globalization.CultureInfo.InvariantCulture) + "," +
                        "\"status\":" + JsonString(status) + "," +
                        "\"streets\":" + JsonString(BuildStreetSummary(route)) + "," +
                        "\"startDistrictName\":" + JsonString(route.StartDistrictName) + "," +
                        "\"endDistrictName\":" + JsonString(route.EndDistrictName) + "," +
                        "\"startRoadName\":" + JsonString(route.StartRoadName) + "," +
                        "\"endRoadName\":" + JsonString(route.EndRoadName) + "," +
                        "\"derivedDisplayCorridor\":" + JsonString(route.DerivedDisplayCorridor) + "," +
                        "\"districtSummary\":" + JsonString(route.DistrictSummary) + "," +
                        "\"subtitle\":" + JsonString(BuildRouteSubtitle(route)) + "," +
                        "\"updated\":" + JsonString(FormatUtcTicks(route.UpdatedAtUtcTicks)) +
                        "}");
                }
                catch (System.Exception ex)
                {
                    Mod.log.Warn(() => $"Road Naming: failed to serialize saved route {route.RouteId} for Saved Routes JSON. Error='{ex.Message}'.");
                    parts.Add("{" +
                        "\"id\":" + route.RouteId.ToString(System.Globalization.CultureInfo.InvariantCulture) + "," +
                        "\"title\":" + JsonString(string.IsNullOrWhiteSpace(route.DisplayTitle) ? "Corrupt Saved Route" : route.DisplayTitle) + "," +
                        "\"savedTitle\":" + JsonString(route.DisplayTitle) + "," +
                        "\"userTitle\":" + (route.IsUserDefinedTitle ? "true" : "false") + "," +
                        "\"mode\":" + JsonString(route.Mode.ToString()) + "," +
                        "\"input\":" + JsonString(route.BaseInputValue) + "," +
                        "\"routeCode\":" + JsonString(route.RouteCode ?? route.BaseInputValue) + "," +
                        "\"routePrefixType\":" + JsonString(route.RoutePrefixType) + "," +
                        "\"segments\":" + route.SegmentCount.ToString(System.Globalization.CultureInfo.InvariantCulture) + "," +
                        "\"waypoints\":" + route.WaypointCount.ToString(System.Globalization.CultureInfo.InvariantCulture) + "," +
                        "\"status\":" + JsonString(SavedRouteStatus.PartiallyValid.ToString()) + "," +
                        "\"streets\":" + JsonString(string.Empty) + "," +
                        "\"startDistrictName\":" + JsonString(route.StartDistrictName) + "," +
                        "\"endDistrictName\":" + JsonString(route.EndDistrictName) + "," +
                        "\"startRoadName\":" + JsonString(route.StartRoadName) + "," +
                        "\"endRoadName\":" + JsonString(route.EndRoadName) + "," +
                        "\"derivedDisplayCorridor\":" + JsonString(route.DerivedDisplayCorridor) + "," +
                        "\"districtSummary\":" + JsonString(route.DistrictSummary) + "," +
                        "\"subtitle\":" + JsonString("This saved route has malformed data and needs attention.") + "," +
                        "\"updated\":" + JsonString(FormatUtcTicks(route.UpdatedAtUtcTicks)) +
                        "}");
                }
            }

            return "[" + string.Join(",", parts.ToArray()) + "]";
        }

        // Filters a route's segment list down to valid, unique road edges.
        private List<Entity> FilterValidRouteSegments(IReadOnlyList<Entity> segments)
        {
            var result = new List<Entity>();
            if (segments == null)
                return result;

            for (var i = 0; i < segments.Count; i++)
            {
                if (_validation.IsValidRoadSegment(segments[i]))
                    AddEntityIfMissing(result, segments[i]);
            }

            return result;
        }

        // Takes a snapshot of base street names for the given ordered segment list.
        private List<string> BuildStreetNameSnapshot(IReadOnlyList<Entity> segments)
        {
            var result = new List<string>();
            for (var i = 0; i < segments.Count; i++)
                result.Add(GetBaseName(segments[i]));

            return result;
        }

        // Bundles the bits of route corridor info the UI wants to show,
        // such as endpoint roads, districts and the derived corridor label.
        private sealed class RouteCorridorMetadata
        {
            public string StartDistrictName;
            public string EndDistrictName;
            public string StartRoadName;
            public string EndRoadName;
            public string DerivedDisplayCorridor;
            public string DistrictSummary;
        }

        // Derives corridor metadata from segment endpoints and their street-name snapshots.
        private RouteCorridorMetadata BuildRouteCorridorMetadata(IReadOnlyList<Entity> segments, IReadOnlyList<string> streetNames)
        {
            var metadata = new RouteCorridorMetadata
            {
                StartRoadName = FirstMeaningfulName(streetNames, false),
                EndRoadName = FirstMeaningfulName(streetNames, true)
            };

            if (segments != null && segments.Count > 0)
            {
                if (string.IsNullOrWhiteSpace(metadata.StartRoadName))
                    metadata.StartRoadName = GetBaseName(segments[0]);
                if (string.IsNullOrWhiteSpace(metadata.EndRoadName))
                    metadata.EndRoadName = GetBaseName(segments[segments.Count - 1]);

                TryResolveDistrictName(segments[0], out metadata.StartDistrictName);
                TryResolveDistrictName(segments[segments.Count - 1], out metadata.EndDistrictName);
                metadata.DistrictSummary = BuildDistrictSummary(segments);
            }

            metadata.DerivedDisplayCorridor = BuildEndpointCorridor(metadata.StartDistrictName, metadata.EndDistrictName);
            if (string.IsNullOrWhiteSpace(metadata.DerivedDisplayCorridor))
                metadata.DerivedDisplayCorridor = BuildEndpointCorridor(metadata.StartRoadName, metadata.EndRoadName);

            return metadata;
        }

        // Copies corridor metadata onto the saved route record.
        private void ApplyRouteCorridorMetadata(SavedRouteRecord route, RouteCorridorMetadata metadata)
        {
            if (route == null || metadata == null)
                return;

            route.RouteCode = route.BaseInputValue ?? string.Empty;
            route.RoutePrefixType = ResolveRoutePrefixType(route.BaseInputValue);
            route.StartDistrictName = metadata.StartDistrictName ?? string.Empty;
            route.EndDistrictName = metadata.EndDistrictName ?? string.Empty;
            route.StartRoadName = metadata.StartRoadName ?? string.Empty;
            route.EndRoadName = metadata.EndRoadName ?? string.Empty;
            route.DerivedDisplayCorridor = metadata.DerivedDisplayCorridor ?? string.Empty;
            route.DistrictSummary = metadata.DistrictSummary ?? string.Empty;
        }

        // Backfills anchor metadata from the saved waypoints if an older route record is missing it.
        private static void EnsureRouteAnchorMetadata(SavedRouteRecord route)
        {
            if (route == null || route.Waypoints.Count == 0)
                return;

            if (route.StartAnchorSegment == Entity.Null)
            {
                var start = route.Waypoints[0];
                route.StartAnchorSegment = start.Segment;
                route.StartAnchorCurvePosition = start.CurvePosition;
                route.StartAnchorPositionX = start.Position.x;
                route.StartAnchorPositionY = start.Position.y;
                route.StartAnchorPositionZ = start.Position.z;
            }

            if (route.EndAnchorSegment == Entity.Null)
            {
                var end = route.Waypoints[route.Waypoints.Count - 1];
                route.EndAnchorSegment = end.Segment;
                route.EndAnchorCurvePosition = end.CurvePosition;
                route.EndAnchorPositionX = end.Position.x;
                route.EndAnchorPositionY = end.Position.y;
                route.EndAnchorPositionZ = end.Position.z;
            }

            if (route.LastKnownResolvedSegmentCount <= 0)
                route.LastKnownResolvedSegmentCount = route.OrderedSegmentIds.Count;
        }

        // Makes sure route display metadata exists, mainly for older saves that did not store all of it yet.
        private void EnsureRouteCorridorMetadata(SavedRouteRecord route)
        {
            if (route == null)
                return;

            if (string.IsNullOrWhiteSpace(route.RouteCode))
                route.RouteCode = route.BaseInputValue ?? string.Empty;
            if (string.IsNullOrWhiteSpace(route.RoutePrefixType))
                route.RoutePrefixType = ResolveRoutePrefixType(route.BaseInputValue);

            if (!string.IsNullOrWhiteSpace(route.DerivedDisplayCorridor) && (!string.IsNullOrWhiteSpace(route.StartRoadName) || route.OrderedSegmentIds.Count == 0))
                return;

            ApplyRouteCorridorMetadata(route, BuildRouteCorridorMetadata(route.OrderedSegmentIds, route.OriginalStreetNamesSnapshot));
        }

        // Builds the stored title for a route record when the user has not supplied a custom one.
        private string BuildRouteRecordTitle(RoadRouteToolMode mode, string inputValue, RouteCorridorMetadata metadata)
        {
            if (mode == RoadRouteToolMode.RenameSelectedSegments)
                return string.IsNullOrWhiteSpace(inputValue) ? "Renamed route" : inputValue.Trim();

            return BuildResolvedRouteTitle(mode, inputValue, metadata?.DerivedDisplayCorridor, string.Empty, string.Empty, string.Empty);
        }

        // Builds the final title shown in the UI, preferring the user's custom title when present.
        private static string BuildRouteDisplayTitle(SavedRouteRecord route)
        {
            if (route == null)
                return string.Empty;

            if (route.IsUserDefinedTitle && !string.IsNullOrWhiteSpace(route.DisplayTitle))
                return route.DisplayTitle.Trim();

            return BuildResolvedRouteTitle(route.Mode, route.RouteCode ?? route.BaseInputValue, route.DerivedDisplayCorridor, route.StartRoadName, route.EndRoadName, route.DisplayTitle);
        }

        // Builds the secondary line shown under a saved route title in the UI.
        private static string BuildRouteSubtitle(SavedRouteRecord route)
        {
            if (route == null)
                return string.Empty;

            var corridor = !string.IsNullOrWhiteSpace(route.DerivedDisplayCorridor)
                ? route.DerivedDisplayCorridor.Trim()
                : BuildEndpointCorridor(route.StartRoadName, route.EndRoadName);
            if (string.IsNullOrWhiteSpace(corridor))
                corridor = BuildStreetSummary(route);
            if (string.IsNullOrWhiteSpace(corridor))
                corridor = string.IsNullOrWhiteSpace(route.BaseInputValue) ? "Saved route" : route.BaseInputValue.Trim();

            return corridor + " | " + route.SegmentCount.ToString(System.Globalization.CultureInfo.InvariantCulture) + " segments";
        }
        // Combines route code and corridor info into a tidy display title.
        private static string BuildResolvedRouteTitle(RoadRouteToolMode mode, string routeCode, string derivedDisplayCorridor, string startRoadName, string endRoadName, string fallbackTitle)
        {
            var corridor = !string.IsNullOrWhiteSpace(derivedDisplayCorridor)
                ? derivedDisplayCorridor.Trim()
                : BuildEndpointCorridor(startRoadName, endRoadName);

            if (mode == RoadRouteToolMode.RenameSelectedSegments)
                return !string.IsNullOrWhiteSpace(fallbackTitle) ? fallbackTitle.Trim() : corridor;

            var normalizedRouteCode = NormalizeRouteCode(routeCode);
            if (!string.IsNullOrWhiteSpace(normalizedRouteCode) && !string.IsNullOrWhiteSpace(corridor))
                return normalizedRouteCode + " " + corridor;
            if (!string.IsNullOrWhiteSpace(normalizedRouteCode))
                return normalizedRouteCode;
            if (!string.IsNullOrWhiteSpace(corridor))
                return corridor;
            if (!string.IsNullOrWhiteSpace(fallbackTitle))
                return fallbackTitle.Trim();
            return "Saved Route";
        }

        // Normalises route codes into the display shape we expect.
        private static string NormalizeRouteCode(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToUpperInvariant();
        }

        // Tries to pull the district name for a segment from either current or border district data.
        private bool TryResolveDistrictName(Entity segment, out string districtName)
        {
            districtName = string.Empty;
            if (segment == Entity.Null || !EntityManager.Exists(segment))
                return false;

            if (EntityManager.HasComponent<Game.Areas.CurrentDistrict>(segment))
            {
                var currentDistrict = EntityManager.GetComponentData<Game.Areas.CurrentDistrict>(segment).m_District;
                if (TryResolveDistrictEntityName(currentDistrict, out districtName))
                    return true;
            }

            if (EntityManager.HasComponent<Game.Areas.BorderDistrict>(segment))
            {
                var borderDistrict = EntityManager.GetComponentData<Game.Areas.BorderDistrict>(segment);
                if (TryResolveDistrictEntityName(borderDistrict.m_Left, out districtName))
                    return true;
                if (TryResolveDistrictEntityName(borderDistrict.m_Right, out districtName))
                    return true;
            }

            return false;
        }

        // Reads the visible name of a district entity.
        private bool TryResolveDistrictEntityName(Entity district, out string districtName)
        {
            districtName = string.Empty;
            if (_nameSystem == null || district == Entity.Null || !EntityManager.Exists(district))
                return false;

            if (_nameSystem.TryGetCustomName(district, out var customName) && !string.IsNullOrWhiteSpace(customName))
            {
                districtName = customName.Trim();
                return true;
            }

            var renderedName = _nameSystem.GetRenderedLabelName(district);
            if (string.IsNullOrWhiteSpace(renderedName))
                return false;

            districtName = renderedName.Trim();
            return true;
        }

        // Builds a short district summary string from the route's segment list.
        private string BuildDistrictSummary(IReadOnlyList<Entity> segments)
        {
            if (segments == null || segments.Count == 0)
                return string.Empty;

            var names = new List<string>();
            for (var i = 0; i < segments.Count; i++)
            {
                if (!TryResolveDistrictName(segments[i], out var districtName) || string.IsNullOrWhiteSpace(districtName) || names.Contains(districtName))
                    continue;

                names.Add(districtName);
                if (names.Count == 4)
                    break;
            }

            var summary = string.Join(" - ", names.ToArray());
            if (names.Count == 4 && segments.Count > 4)
                summary += "...";
            return summary;
        }

        // Finds the first non-empty name from either end of a list.
        private static string FirstMeaningfulName(IReadOnlyList<string> names, bool fromEnd)
        {
            if (names == null || names.Count == 0)
                return string.Empty;

            if (fromEnd)
            {
                for (var i = names.Count - 1; i >= 0; i--)
                {
                    if (!string.IsNullOrWhiteSpace(names[i]))
                        return names[i].Trim();
                }

                return string.Empty;
            }

            for (var i = 0; i < names.Count; i++)
            {
                if (!string.IsNullOrWhiteSpace(names[i]))
                    return names[i].Trim();
            }

            return string.Empty;
        }

        // Builds a simple endpoint corridor label like "A - B".
        private static string BuildEndpointCorridor(string startName, string endName)
        {
            var start = (startName ?? string.Empty).Trim();
            var end = (endName ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(start))
                return end;
            if (string.IsNullOrWhiteSpace(end))
                return start;
            return string.Equals(start, end, System.StringComparison.OrdinalIgnoreCase) ? start : start + " - " + end;
        }

        // Pulls a simple route prefix category out of the input value for display/filtering.
        private static string ResolveRoutePrefixType(string inputValue)
        {
            var value = (inputValue ?? string.Empty).Trim().ToUpperInvariant();
            if (value.Length == 0)
                return "None";

            var first = value[0].ToString();
            return first == "M" || first == "A" || first == "B" || first == "C" ? first : "Custom";
        }

        // Small helper to keep entity lists unique.
        private static void AddEntityIfMissing(List<Entity> entities, Entity entity)
        {
            if (!entities.Contains(entity))
                entities.Add(entity);
        }

        // Route overload for building a compact street summary.
        private static string BuildStreetSummary(SavedRouteRecord route)
        {
            return BuildStreetSummary(route.OriginalStreetNamesSnapshot);
        }

        // Builds a short comma-separated street summary from a name snapshot list.
        private static string BuildStreetSummary(IReadOnlyList<string> streetNames)
        {
            if (streetNames == null || streetNames.Count == 0)
                return string.Empty;

            var unique = new List<string>();
            for (var i = 0; i < streetNames.Count; i++)
            {
                var name = streetNames[i];
                if (string.IsNullOrWhiteSpace(name) || unique.Contains(name))
                    continue;

                unique.Add(name);
                if (unique.Count == 3)
                    break;
            }

            var summary = string.Join(", ", unique.ToArray());
            if (unique.Count < streetNames.Count)
                summary += "...";
            return summary;
        }

        // Formats UTC ticks into something the UI can show without much drama.
        private static string FormatUtcTicks(long ticks)
        {
            if (ticks <= 0)
                return string.Empty;

            return new System.DateTime(ticks, System.DateTimeKind.Utc).ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture) + " UTC";
        }

        // Escapes a string for the hand-built JSON payload.
        private static string JsonString(string value)
        {
            return "\"" + (value ?? string.Empty)
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\r", string.Empty)
                .Replace("\n", " ") + "\"";
        }
        // Clears repository entries for segments that no longer exist in the world.
        public void CleanupOrphanedMetadata()
        {
            var orphaned = new System.Collections.Generic.List<Entity>();
            foreach (var metadata in _repository.All)
            {
                if (!_validation.IsValidRoadSegment(metadata.SegmentEntity))
                    orphaned.Add(metadata.SegmentEntity);
            }

            foreach (var entity in orphaned)
                _repository.Remove(entity);

            if (orphaned.Count > 0)
                Mod.log.Info(() => $"Removed {orphaned.Count} orphaned road-route metadata record(s).");
        }

        // Writes segment metadata and saved route data into the save file.
        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            CleanupOrphanedMetadata();
            writer.Write(SaveVersion);
            writer.Write(_repository.Count);
            foreach (var metadata in _repository.All)
            {
                writer.Write(metadata.SegmentEntity);
                writer.Write(metadata.BaseNameSnapshot ?? string.Empty);
                writer.Write(metadata.OptionalCustomRoadName ?? string.Empty);
                writer.Write(metadata.RouteNumbers.Count);
                for (var i = 0; i < metadata.RouteNumbers.Count; i++)
                    writer.Write(metadata.RouteNumbers[i] ?? string.Empty);
                writer.Write(metadata.LastModifiedUtcTicks);
                writer.Write(metadata.Flags);
                writer.Write((int)metadata.RouteNumberPlacement);
            }

            SerializeRoutes(writer);
            Mod.log.Info(() => $"Serialized {_repository.Count} segment route metadata record(s) and {_routeDatabase.Count} saved route record(s).");
        }

        // Restores segment metadata and saved routes from the save file,
        // then queues a delayed name reapply once the world is ready.
        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            _repository.Clear();
            _routeDatabase.Clear();
            var version = 0;
            reader.Read(out version);
            if (version <= 0 || version > SaveVersion)
            {
                Mod.log.Warn(() => $"Unsupported Road Naming: save data version {version}; metadata ignored.");
                return;
            }

            var count = 0;
            reader.Read(out count);
            for (var i = 0; i < count; i++)
            {
                var entity = Entity.Null;
                var baseName = string.Empty;
                var customName = string.Empty;
                var routeCount = 0;
                long ticks = 0;
                var flags = 0;
                var placementValue = (int)RouteNumberPlacement.AfterBaseName;

                reader.Read(out entity);
                reader.Read(out baseName);
                reader.Read(out customName);
                reader.Read(out routeCount);

                var metadata = _repository.GetOrCreate(entity);
                metadata.BaseNameSnapshot = string.IsNullOrEmpty(baseName) ? null : baseName;
                metadata.OptionalCustomRoadName = string.IsNullOrEmpty(customName) ? null : customName;
                for (var routeIndex = 0; routeIndex < routeCount; routeIndex++)
                {
                    var route = string.Empty;
                    reader.Read(out route);
                    if (!string.IsNullOrWhiteSpace(route))
                        metadata.RouteNumbers.Add(route);
                }

                reader.Read(out ticks);
                reader.Read(out flags);
                if (version >= 6)
                    reader.Read(out placementValue);
                metadata.LastModifiedUtcTicks = ticks;
                metadata.Flags = flags;
                metadata.RouteNumberPlacement = placementValue == (int)RouteNumberPlacement.BeforeBaseName
                    ? RouteNumberPlacement.BeforeBaseName
                    : RouteNumberPlacement.AfterBaseName;
            }

            if (version >= 2)
                DeserializeRoutes(reader, version);

            QueuePostLoadNameReapply("Deserialize");
            Mod.log.Info(() => $"Deserialized {_repository.Count} segment route metadata record(s) and {_routeDatabase.Count} saved route record(s); deferred post-load name reapply scheduled.");
        }

        // Resets runtime state when the serializer wants a clean default setup.
        public void SetDefaults(Context context)
        {
            _repository.Clear();
            _routeDatabase.Clear();
            _aggregateStabilityChecks.Clear();
            _pendingPostLoadNameReapply = false;
            _pendingPostLoadNameReapplyDelayTicks = 0;
            _pendingPostLoadNameReapplyAttempts = 0;
            _pendingPostLoadNameReapplyWaitingLogged = false;
        }

        // Queues a delayed post-load name restore rather than writing immediately during deserialisation.
        private void QueuePostLoadNameReapply(string source)
        {
            if (string.Equals(source, "Deserialize", System.StringComparison.OrdinalIgnoreCase))
                _aggregateStabilityChecks.Clear();

            _pendingPostLoadNameReapply = true;
            _pendingPostLoadNameReapplyDelayTicks = DeferredNameReapplyDelayTicks;
            _pendingPostLoadNameReapplyAttempts = 0;
            _pendingPostLoadNameReapplyWaitingLogged = false;
            Mod.log.Info(() => $"Road Naming: deferred post-load name reapply queued. Source={source}, MetadataRecords={_repository.Count}, SavedRoutes={_routeDatabase.Count}, DelayTicks={DeferredNameReapplyDelayTicks}.");
        }

        // Handles the delayed post-load name restore once the game world is actually safe for writes.
        private void ProcessDeferredPostLoadNameReapply()
        {
            if (!_pendingPostLoadNameReapply)
                return;

            if (_pendingPostLoadNameReapplyDelayTicks > 0)
            {
                _pendingPostLoadNameReapplyDelayTicks--;
                return;
            }

            if (!IsSafeForPostLoadNameWrites(out var reason))
            {
                if (!_pendingPostLoadNameReapplyWaitingLogged)
                {
                    _pendingPostLoadNameReapplyWaitingLogged = true;
                    Mod.log.Info(() => $"Road Naming: post-load name reapply waiting; world not ready. Reason={reason}.");
                }

                _pendingPostLoadNameReapplyDelayTicks = DeferredNameReapplyRetryDelayTicks;
                return;
            }

            _pendingPostLoadNameReapplyAttempts++;
            try
            {
                Mod.log.Info(() => $"Road Naming: post-load name reapply starting. Attempt={_pendingPostLoadNameReapplyAttempts}, MetadataRecords={_repository.Count}, SavedRoutes={_routeDatabase.Count}.");
                CleanupOrphanedMetadata();
                ReapplyAllResolvedNames(out var reapplied, out var skipped);
                _pendingPostLoadNameReapply = false;
                _pendingPostLoadNameReapplyDelayTicks = 0;
                _pendingPostLoadNameReapplyAttempts = 0;
                _pendingPostLoadNameReapplyWaitingLogged = false;
                Mod.log.Info(() => $"Road Naming: post-load name reapply completed. Reapplied={reapplied}, SkippedMissingOrInvalid={skipped}, SavedRoutes={_routeDatabase.Count}.");
            }
            catch (System.Exception ex)
            {
                if (_pendingPostLoadNameReapplyAttempts < DeferredNameReapplyMaxAttempts)
                {
                    _pendingPostLoadNameReapplyDelayTicks = DeferredNameReapplyRetryDelayTicks;
                    Mod.log.Warn(ex, () => $"Road Naming: post-load name reapply failed; retry scheduled. Attempt={_pendingPostLoadNameReapplyAttempts}, RetryDelayTicks={DeferredNameReapplyRetryDelayTicks}.");
                    return;
                }

                _pendingPostLoadNameReapply = false;
                _pendingPostLoadNameReapplyDelayTicks = 0;
                Mod.log.Error(ex, () => $"Road Naming: post-load name reapply failed after {_pendingPostLoadNameReapplyAttempts} attempt(s); giving up for this load.");
            }
        }

        // Sanity check so we only poke NameSystem once the game is fully in a proper playable state.
        private bool IsSafeForPostLoadNameWrites(out string reason)
        {
            reason = string.Empty;

            if (_nameSystem == null)
            {
                reason = "NameSystemMissing";
                return false;
            }

            var gameManager = GameManager.instance;
            if (gameManager == null)
            {
                reason = "GameManagerMissing";
                return false;
            }

            if (gameManager.gameMode != GameMode.Game)
            {
                reason = "NotInGameMode";
                return false;
            }

            if (gameManager.isGameLoading)
            {
                reason = "GameLoading";
                return false;
            }

            reason = "Ready";
            return true;
        }
        // Writes the saved-route database portion of the save payload.
        private void SerializeRoutes<TWriter>(TWriter writer) where TWriter : IWriter
        {
            writer.Write(_routeDatabase.NextRouteId);
            writer.Write(_routeDatabase.Count);
            foreach (var route in _routeDatabase.Routes)
            {
                writer.Write(route.RouteId);
                writer.Write(route.DisplayTitle ?? string.Empty);
                writer.Write((int)route.Mode);
                writer.Write(route.BaseInputValue ?? string.Empty);
                writer.Write(route.CreatedAtUtcTicks);
                writer.Write(route.UpdatedAtUtcTicks);
                writer.Write(route.LastAppliedUtcTicks);
                writer.Write(route.IsDeleted);
                writer.Write(route.IsUserDefinedTitle);
                writer.Write(route.Notes ?? string.Empty);
                writer.Write(route.RouteCode ?? string.Empty);
                writer.Write(route.RoutePrefixType ?? string.Empty);
                writer.Write((int)route.RouteNumberPlacement);
                writer.Write(route.StartDistrictName ?? string.Empty);
                writer.Write(route.EndDistrictName ?? string.Empty);
                writer.Write(route.StartRoadName ?? string.Empty);
                writer.Write(route.EndRoadName ?? string.Empty);
                writer.Write(route.DerivedDisplayCorridor ?? string.Empty);
                writer.Write(route.DistrictSummary ?? string.Empty);
                writer.Write(route.StartAnchorSegment);
                writer.Write(route.EndAnchorSegment);
                writer.Write(route.StartAnchorCurvePosition);
                writer.Write(route.EndAnchorCurvePosition);
                writer.Write(route.StartAnchorPositionX);
                writer.Write(route.StartAnchorPositionY);
                writer.Write(route.StartAnchorPositionZ);
                writer.Write(route.EndAnchorPositionX);
                writer.Write(route.EndAnchorPositionY);
                writer.Write(route.EndAnchorPositionZ);
                writer.Write(route.LastKnownResolvedSegmentCount);

                writer.Write(route.Waypoints.Count);
                for (var i = 0; i < route.Waypoints.Count; i++)
                {
                    var waypoint = route.Waypoints[i];
                    writer.Write(waypoint.Segment);
                    writer.Write(waypoint.Position.x);
                    writer.Write(waypoint.Position.y);
                    writer.Write(waypoint.Position.z);
                    writer.Write(waypoint.CurvePosition);
                }

                writer.Write(route.OrderedSegmentIds.Count);
                for (var i = 0; i < route.OrderedSegmentIds.Count; i++)
                {
                    writer.Write(route.OrderedSegmentIds[i]);
                    writer.Write(i < route.OriginalStreetNamesSnapshot.Count ? route.OriginalStreetNamesSnapshot[i] ?? string.Empty : string.Empty);
                }
            }
        }

        // Reads saved-route records back in, handling older save versions along the way.
        private void DeserializeRoutes<TReader>(TReader reader, int version) where TReader : IReader
        {
            long nextRouteId = 1;
            var routeCount = 0;
            reader.Read(out nextRouteId);
            reader.Read(out routeCount);
            _routeDatabase.NextRouteId = nextRouteId;

            for (var i = 0; i < routeCount; i++)
            {
                var route = new SavedRouteRecord();
                var mode = 0;
                var waypointCount = 0;
                var segmentCount = 0;

                long routeId = 0;
                var displayTitle = string.Empty;
                var inputValue = string.Empty;
                long createdTicks = 0;
                long updatedTicks = 0;
                long lastAppliedTicks = 0;
                var isDeleted = false;
                var notes = string.Empty;

                reader.Read(out routeId);
                reader.Read(out displayTitle);
                reader.Read(out mode);
                reader.Read(out inputValue);
                reader.Read(out createdTicks);
                reader.Read(out updatedTicks);
                if (version >= 4)
                    reader.Read(out lastAppliedTicks);
                reader.Read(out isDeleted);
                var isUserDefinedTitle = false;
                var routeCode = string.Empty;
                var routePrefixType = string.Empty;
                var routePlacementValue = (int)RouteNumberPlacement.AfterBaseName;
                var startDistrictName = string.Empty;
                var endDistrictName = string.Empty;
                var startRoadName = string.Empty;
                var endRoadName = string.Empty;
                var derivedDisplayCorridor = string.Empty;
                var districtSummary = string.Empty;
                var startAnchorSegment = Entity.Null;
                var endAnchorSegment = Entity.Null;
                float startAnchorCurvePosition = 0;
                float endAnchorCurvePosition = 0;
                float startAnchorX = 0;
                float startAnchorY = 0;
                float startAnchorZ = 0;
                float endAnchorX = 0;
                float endAnchorY = 0;
                float endAnchorZ = 0;
                var lastKnownResolvedSegmentCount = 0;

                if (version >= 3)
                {
                    reader.Read(out isUserDefinedTitle);
                    reader.Read(out notes);
                    reader.Read(out routeCode);
                    reader.Read(out routePrefixType);
                    if (version >= 6)
                        reader.Read(out routePlacementValue);
                    reader.Read(out startDistrictName);
                    reader.Read(out endDistrictName);
                    reader.Read(out startRoadName);
                    reader.Read(out endRoadName);
                    reader.Read(out derivedDisplayCorridor);
                    reader.Read(out districtSummary);
                    if (version >= 5)
                    {
                        reader.Read(out startAnchorSegment);
                        reader.Read(out endAnchorSegment);
                        reader.Read(out startAnchorCurvePosition);
                        reader.Read(out endAnchorCurvePosition);
                        reader.Read(out startAnchorX);
                        reader.Read(out startAnchorY);
                        reader.Read(out startAnchorZ);
                        reader.Read(out endAnchorX);
                        reader.Read(out endAnchorY);
                        reader.Read(out endAnchorZ);
                        reader.Read(out lastKnownResolvedSegmentCount);
                    }
                }
                else
                {
                    reader.Read(out notes);
                }

                route.RouteId = routeId;
                route.DisplayTitle = displayTitle;
                route.Mode = (RoadRouteToolMode)mode;
                route.BaseInputValue = inputValue;
                route.RouteCode = string.IsNullOrWhiteSpace(routeCode) ? inputValue : routeCode;
                route.RoutePrefixType = string.IsNullOrWhiteSpace(routePrefixType) ? ResolveRoutePrefixType(inputValue) : routePrefixType;
                route.RouteNumberPlacement = routePlacementValue == (int)RouteNumberPlacement.BeforeBaseName
                    ? RouteNumberPlacement.BeforeBaseName
                    : RouteNumberPlacement.AfterBaseName;
                route.CreatedAtUtcTicks = createdTicks;
                route.UpdatedAtUtcTicks = updatedTicks;
                route.LastAppliedUtcTicks = lastAppliedTicks > 0 ? lastAppliedTicks : (createdTicks > 0 ? createdTicks : updatedTicks);
                route.IsDeleted = isDeleted;
                route.IsUserDefinedTitle = isUserDefinedTitle;
                route.Notes = notes;
                route.StartDistrictName = startDistrictName;
                route.EndDistrictName = endDistrictName;
                route.StartRoadName = startRoadName;
                route.EndRoadName = endRoadName;
                route.DerivedDisplayCorridor = derivedDisplayCorridor;
                route.DistrictSummary = districtSummary;
                route.StartAnchorSegment = startAnchorSegment;
                route.EndAnchorSegment = endAnchorSegment;
                route.StartAnchorCurvePosition = startAnchorCurvePosition;
                route.EndAnchorCurvePosition = endAnchorCurvePosition;
                route.StartAnchorPositionX = startAnchorX;
                route.StartAnchorPositionY = startAnchorY;
                route.StartAnchorPositionZ = startAnchorZ;
                route.EndAnchorPositionX = endAnchorX;
                route.EndAnchorPositionY = endAnchorY;
                route.EndAnchorPositionZ = endAnchorZ;
                route.LastKnownResolvedSegmentCount = lastKnownResolvedSegmentCount;

                reader.Read(out waypointCount);
                for (var waypointIndex = 0; waypointIndex < waypointCount; waypointIndex++)
                {
                    var segment = Entity.Null;
                    float x = 0;
                    float y = 0;
                    float z = 0;
                    float curvePosition = 0;
                    reader.Read(out segment);
                    reader.Read(out x);
                    reader.Read(out y);
                    reader.Read(out z);
                    reader.Read(out curvePosition);
                    route.Waypoints.Add(new RoadRouteWaypoint(segment, new Unity.Mathematics.float3(x, y, z), curvePosition));
                }

                reader.Read(out segmentCount);
                for (var segmentIndex = 0; segmentIndex < segmentCount; segmentIndex++)
                {
                    var segment = Entity.Null;
                    var streetName = string.Empty;
                    reader.Read(out segment);
                    reader.Read(out streetName);
                    route.OrderedSegmentIds.Add(segment);
                    route.OriginalStreetNamesSnapshot.Add(streetName ?? string.Empty);
                }

                EnsureRouteAnchorMetadata(route);
                _routeDatabase.AddLoadedRoute(route);
                Mod.log.Info(() => $"Road Naming: route loaded. RouteId={route.RouteId}, Title='{route.DisplayTitle}', Segments={route.SegmentCount}, Status={EvaluateSavedRouteStatus(route)}.");
            }
        }
        // Re-resolves all repository metadata into live segment names after a save load.
        private void ReapplyAllResolvedNames(out int reapplied, out int skipped)
        {
            reapplied = 0;
            skipped = 0;
            var settings = CurrentDisplaySettings();
            var segments = new List<Entity>();
            foreach (var metadata in _repository.All)
            {
                if (!_validation.IsValidRoadSegment(metadata.SegmentEntity))
                {
                    skipped++;
                    Mod.log.Warn(() => $"Road Naming: post-load reapply skipped missing or invalid segment. Segment={metadata.SegmentEntity.Index}.");
                    continue;
                }

                SetSegmentDisplayName(metadata.SegmentEntity, _resolver.Resolve(GetBaseName(metadata.SegmentEntity), metadata, settings));
                segments.Add(metadata.SegmentEntity);
                reapplied++;
            }

            if (segments.Count > 0)
                ApplyAuthoritativeVisibleNames(segments, "PostLoadDeserializeReapply");
        }

        // Returns the mod's best idea of the segment's underlying base street name.
        // It prefers a good snapshot, then asks the game, then falls back to a safe placeholder.
        private string GetBaseName(Entity segment)
        {
            SegmentRouteMetadata metadata = null;
            _repository.TryGet(segment, out metadata);

            if (metadata != null && !string.IsNullOrWhiteSpace(metadata.BaseNameSnapshot) && !IsPrefabRenderedName(segment, metadata.BaseNameSnapshot))
                return metadata.BaseNameSnapshot.Trim();

            if (TryResolveGameStreetName(segment, out var gameStreetName, out var source))
            {
                if (metadata != null && !string.Equals(metadata.BaseNameSnapshot, gameStreetName, System.StringComparison.Ordinal))
                {
                    Mod.log.Info(() => $"Road Naming: base street snapshot updated. Segment={segment.Index}, Source={source}, GameStreetName='{gameStreetName}', PreviousSnapshot='{metadata.BaseNameSnapshot ?? string.Empty}'.");
                    metadata.BaseNameSnapshot = gameStreetName;
                    metadata.Touch();
                }

                return gameStreetName;
            }

            var fallback = $"Road Segment {segment.Index}";
            if (metadata != null && string.IsNullOrWhiteSpace(metadata.BaseNameSnapshot))
                metadata.BaseNameSnapshot = fallback;

            Mod.log.Warn(() => $"Road Naming: could not resolve an actual street name for segment {segment.Index}; using fallback '{fallback}'.");
            return fallback;
        }

        // Asks the live game for a street name, checking the aggregate first and then the segment itself.
        private bool TryResolveGameStreetName(Entity segment, out string streetName, out string source)
        {
            streetName = null;
            source = null;

            if (_nameSystem == null || segment == Entity.Null || !EntityManager.Exists(segment))
                return false;

            if (EntityManager.HasComponent<Aggregated>(segment))
            {
                var aggregate = EntityManager.GetComponentData<Aggregated>(segment).m_Aggregate;
                if (TryResolveNameFromEntity(aggregate, "Aggregate", segment, out streetName, out source))
                    return true;
            }

            if (TryResolveNameFromEntity(segment, "Segment", segment, out streetName, out source))
                return true;

            return false;
        }

        // Reads either a custom name or a rendered label from a specific entity
        // and reports where it came from.
        private bool TryResolveNameFromEntity(Entity nameEntity, string entitySource, Entity segment, out string streetName, out string source)
        {
            streetName = null;
            source = null;
            if (nameEntity == Entity.Null || !EntityManager.Exists(nameEntity))
                return false;

            if (_nameSystem.TryGetCustomName(nameEntity, out var customName) && IsUsableStreetName(segment, customName))
            {
                streetName = customName.Trim();
                source = entitySource + "CustomName";
                return true;
            }

            var renderedName = _nameSystem.GetRenderedLabelName(nameEntity);
            if (IsUsableStreetName(segment, renderedName))
            {
                streetName = renderedName.Trim();
                source = entitySource + "RenderedLabel";
                return true;
            }

            return false;
        }

        // Filters out empty names and prefab labels so only real street names get through.
        private bool IsUsableStreetName(Entity segment, string candidate)
        {
            return !string.IsNullOrWhiteSpace(candidate) && !IsPrefabRenderedName(segment, candidate);
        }

        // Checks whether a candidate label is really just the road prefab name rather than a proper street name.
        private bool IsPrefabRenderedName(Entity segment, string candidate)
        {
            if (string.IsNullOrWhiteSpace(candidate) || _nameSystem == null || segment == Entity.Null || !EntityManager.Exists(segment) || !EntityManager.HasComponent<PrefabRef>(segment))
                return false;

            var prefab = EntityManager.GetComponentData<PrefabRef>(segment).m_Prefab;
            if (prefab == Entity.Null || !EntityManager.Exists(prefab))
                return false;

            var prefabLabel = _nameSystem.GetRenderedLabelName(prefab);
            return (!string.IsNullOrWhiteSpace(prefabLabel) && string.Equals(candidate.Trim(), prefabLabel.Trim(), System.StringComparison.OrdinalIgnoreCase))
                || LooksLikeKnownRoadPrefabLabel(candidate);
        }

        // Cheap pattern check for common prefab-style road labels.
        private static bool LooksLikeKnownRoadPrefabLabel(string candidate)
        {
            if (string.IsNullOrWhiteSpace(candidate))
                return false;

            var value = candidate.Trim();
            return value.IndexOf("-Lane", System.StringComparison.OrdinalIgnoreCase) >= 0
                || value.IndexOf(" Asymmetric Road", System.StringComparison.OrdinalIgnoreCase) >= 0
                || value.IndexOf(" Oneway Road", System.StringComparison.OrdinalIgnoreCase) >= 0
                || value.IndexOf(" One-Way Road", System.StringComparison.OrdinalIgnoreCase) >= 0;
        }

        // Writes a custom name to the individual road edge,
        // then tags the edge and aggregate for the right sort of visual refresh.
        private void SetSegmentDisplayName(Entity segment, string displayName)
        {
            if (segment == Entity.Null || !_validation.IsValidRoadSegment(segment))
            {
                Mod.log.Warn(() => $"Skipped display-name update for invalid segment {segment}.");
                return;
            }

            var safeDisplayName = string.IsNullOrWhiteSpace(displayName) ? $"Road Segment {segment.Index}" : displayName.Trim();

            // CS2 integration point: this writes a custom name on the individual edge entity only.
            // Do not write to Aggregated.m_Aggregate here, because that would rename every edge in the aggregate/street.
            _nameSystem.SetCustomName(segment, safeDisplayName);
            EnsureRefreshTag<CustomName>(segment);
            // Avoid Updated on Aggregated road edges: AggregateSystem treats that as a topology/name-group recompute signal.
            EnsureRefreshTag<BatchesUpdated>(segment);
            RefreshAggregateWithoutRenaming(segment);

            var customNameVisible = _nameSystem.TryGetCustomName(segment, out var storedCustomName);
            var renderedName = _nameSystem.GetRenderedLabelName(segment);
            if (!customNameVisible || !string.Equals(storedCustomName, safeDisplayName, System.StringComparison.Ordinal))
                Mod.log.Warn(() => $"NameSystem did not echo the expected custom name for segment {segment.Index}. Expected='{safeDisplayName}', Stored='{storedCustomName ?? string.Empty}', Rendered='{renderedName ?? string.Empty}'.");
            else
                Mod.log.Info(() => $"Road Naming: display name updated. Segment={segment.Index}, Name='{safeDisplayName}', Rendered='{renderedName ?? string.Empty}'.");
        }


        // Refreshes the owning aggregate's label state without renaming the whole aggregate.
        private void RefreshAggregateWithoutRenaming(Entity segment)
        {
            if (!EntityManager.HasComponent<Aggregated>(segment))
                return;

            var aggregate = EntityManager.GetComponentData<Aggregated>(segment).m_Aggregate;
            if (aggregate == Entity.Null || !EntityManager.Exists(aggregate))
                return;

            EnsureRefreshTag<Updated>(aggregate);
            EnsureRefreshTag<BatchesUpdated>(aggregate);
        }
        // Small generic helper that adds a refresh tag component only if it is missing.
        private void EnsureRefreshTag<T>(Entity segment) where T : struct, IComponentData
        {
            if (!EntityManager.HasComponent<T>(segment))
                EntityManager.AddComponent<T>(segment);
        }

        // Makes a shallow-but-good-enough copy of segment metadata for preview work.
        private static SegmentRouteMetadata CloneMetadata(SegmentRouteMetadata source)
        {
            var clone = new SegmentRouteMetadata(source.SegmentEntity)
            {
                BaseNameSnapshot = source.BaseNameSnapshot,
                OptionalCustomRoadName = source.OptionalCustomRoadName,
                LastModifiedUtcTicks = source.LastModifiedUtcTicks,
                Flags = source.Flags,
                RouteNumberPlacement = source.RouteNumberPlacement
            };
            clone.RouteNumbers.AddRange(source.RouteNumbers);
            return clone;
        }

        // Pulls the current display settings from mod config, or defaults if settings are not ready yet.
        private static SegmentDisplaySettings CurrentDisplaySettings()
        {
            return Mod.Settings?.ToDisplaySettings() ?? new SegmentDisplaySettings();
        }
    }
}

