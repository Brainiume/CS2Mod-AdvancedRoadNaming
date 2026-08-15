using System;
using Colossal.UI.Binding;
using Game;
using Game.Input;
using Game.Net;
using Game.SceneFlow;
using Game.Simulation;
using Game.Tools;
using Game.UI;
using AdvancedRoadNaming.Domain;
using AdvancedRoadNaming.Settings;
using AdvancedRoadNaming.Services;
using Unity.Mathematics;

namespace AdvancedRoadNaming.Systems
{
    public sealed partial class RoadRouteToolUISystem : UISystemBase
    {
        private struct RouteTrafficSample
        {
            public float Volume;
            public float Flow;
            public float Length;
        }

        private struct RouteStatisticsBucket
        {
            public float PrimaryVolume;
            public float PrimaryFlow;
            public float BusyVolume;
            public float CorridorLoad;
            public float VolumeWeightedFlow;
            public float BottleneckFlow;
            public float CongestedDistance;
        }

        private sealed class RouteTrafficSampleComparer : System.Collections.Generic.IComparer<RouteTrafficSample>
        {
            public static readonly RouteTrafficSampleComparer Instance = new RouteTrafficSampleComparer();

            public int Compare(RouteTrafficSample left, RouteTrafficSample right)
            {
                var volumeComparison = right.Volume.CompareTo(left.Volume);
                return volumeComparison != 0 ? volumeComparison : left.Flow.CompareTo(right.Flow);
            }
        }

        private const string PanelBindingGroup = "AdvancedRoadNaming";

        private RoadRouteToolSystem _toolSystem;
        private SegmentMetadataSystem _metadataSystem;
        private SimulationSystem _simulationSystem;
        private ToolSystem _gameToolSystem;
        private DefaultToolSystem _defaultToolSystem;
        private ValueBinding<string> _stateBinding;
        private ValueBinding<string> _panelShortcutCommandBinding;
        private RawValueBinding _routeStatisticsBinding;
        private RawValueBinding _routeShieldCatalogBinding;
        private ProxyAction _toggleRenameAction;
        private ProxyAction _toggleRoutesAction;
        private readonly float[] _routeStatisticsVolume = new float[5];
        private readonly float[] _routeStatisticsFlow = new float[5];
        private readonly float[] _routeStatisticsBusyVolume = new float[5];
        private readonly float[] _routeStatisticsCorridorLoad = new float[5];
        private readonly float[] _routeStatisticsVolumeWeightedFlow = new float[5];
        private readonly float[] _routeStatisticsBottleneckFlow = new float[5];
        private readonly float[] _routeStatisticsCongestedDistance = new float[5];
        private RouteTrafficSample[] _midnightTrafficSamples = Array.Empty<RouteTrafficSample>();
        private RouteTrafficSample[] _morningTrafficSamples = Array.Empty<RouteTrafficSample>();
        private RouteTrafficSample[] _noonTrafficSamples = Array.Empty<RouteTrafficSample>();
        private RouteTrafficSample[] _eveningTrafficSamples = Array.Empty<RouteTrafficSample>();
        private long _activeStatisticsRouteId;
        private long _publishedStatisticsRouteId;
        private int _statisticsStoredSegmentCount;
        private int _statisticsValidSegmentCount;
        private int _statisticsExcludedSegmentCount;
        private RouteVolumeAggregationMode _statisticsVolumeMode;
        private RouteFlowAggregationMode _statisticsFlowMode;
        private RouteStatisticsConfiguration _publishedStatisticsConfiguration;
        private uint _lastStatisticsRefreshFrame;
        private bool _hasStatisticsRefreshFrame;
        private bool _statisticsPayloadActive;
        private int _panelShortcutSequence;
        private string _lastState;
        private bool _panelVisible;
        private bool _lastGameplayAvailable;

        protected override void OnCreate()
        {
            base.OnCreate();
            _toolSystem = World.GetOrCreateSystemManaged<RoadRouteToolSystem>();
            _metadataSystem = World.GetOrCreateSystemManaged<SegmentMetadataSystem>();
            _simulationSystem = World.GetOrCreateSystemManaged<SimulationSystem>();
            _gameToolSystem = World.GetOrCreateSystemManaged<ToolSystem>();
            _defaultToolSystem = World.GetOrCreateSystemManaged<DefaultToolSystem>();
            _toggleRenameAction = Mod.Settings?.GetAction(AdvancedRoadNamingSettings.ToggleRenameActionName);
            _toggleRoutesAction = Mod.Settings?.GetAction(AdvancedRoadNamingSettings.ToggleRoutesActionName);

            _lastState = BuildClosedState(false);
            _stateBinding = new ValueBinding<string>(PanelBindingGroup, "state", _lastState, ValueWriters.Create<string>(), System.Collections.Generic.EqualityComparer<string>.Default);
            _panelShortcutCommandBinding = new ValueBinding<string>(PanelBindingGroup, "panelShortcutCommand", "0|none", ValueWriters.Create<string>(), System.Collections.Generic.EqualityComparer<string>.Default);

            AddBinding(_stateBinding);
            AddBinding(_panelShortcutCommandBinding);
            AddBinding(_routeStatisticsBinding = new RawValueBinding(PanelBindingGroup, "routeStatistics", WriteRouteStatistics));
            AddBinding(_routeShieldCatalogBinding = new RawValueBinding(PanelBindingGroup, "routeShieldCatalog", WriteRouteShieldCatalog));
            AddBinding(new TriggerBinding(PanelBindingGroup, "activateRenameMenu", ActivateRenameMenu));
            AddBinding(new TriggerBinding(PanelBindingGroup, "activateRouteMenu", ActivateRouteMenu));
            AddBinding(new TriggerBinding(PanelBindingGroup, "activate", ActivateTool));
            AddBinding(new TriggerBinding(PanelBindingGroup, "activateSavedRenames", ActivateSavedRenames));
            AddBinding(new TriggerBinding(PanelBindingGroup, "activateSavedRoutes", ActivateSavedRoutes));
            AddBinding(new TriggerBinding(PanelBindingGroup, "cancel", CancelTool));
            AddBinding(new TriggerBinding(PanelBindingGroup, "apply", Apply));
            AddBinding(new TriggerBinding(PanelBindingGroup, "clear", Clear));
            AddBinding(new TriggerBinding(PanelBindingGroup, "removeLast", RemoveLast));
            AddBinding(new TriggerBinding<string>(PanelBindingGroup, "setMode", SetMode, ValueReaders.Create<string>()));
            AddBinding(new TriggerBinding<string>(PanelBindingGroup, "setInput", SetInput, ValueReaders.Create<string>()));
            AddBinding(new TriggerBinding<string>(PanelBindingGroup, "setRouteNumberPlacement", SetRouteNumberPlacement, ValueReaders.Create<string>()));
            AddBinding(new TriggerBinding<string>(PanelBindingGroup, "setRouteShieldStyle", SetRouteShieldStyle, ValueReaders.Create<string>()));
            AddBinding(new TriggerBinding<bool>(PanelBindingGroup, "setUndergroundMode", SetUndergroundMode, ValueReaders.Create<bool>()));
            AddBinding(new TriggerBinding<long>(PanelBindingGroup, "selectSavedRoute", SelectSavedRoute, ValueReaders.Create<long>()));
            AddBinding(new TriggerBinding<long>(PanelBindingGroup, "previewSavedRoute", SelectSavedRoute, ValueReaders.Create<long>()));
            AddBinding(new TriggerBinding<long>(PanelBindingGroup, "reapplySavedRoute", ReapplySavedRoute, ValueReaders.Create<long>()));
            AddBinding(new TriggerBinding<long>(PanelBindingGroup, "rebuildSavedRoute", RebuildSavedRoute, ValueReaders.Create<long>()));
            AddBinding(new TriggerBinding(PanelBindingGroup, "reapplyAllSavedRoutes", ReapplyAllSavedRoutes));
            AddBinding(new TriggerBinding<long>(PanelBindingGroup, "deleteSavedRoute", DeleteSavedRoute, ValueReaders.Create<long>()));
            AddBinding(new TriggerBinding<string>(PanelBindingGroup, "updateSavedRouteInput", UpdateSavedRouteInput, ValueReaders.Create<string>()));
            AddBinding(new TriggerBinding<string>(PanelBindingGroup, "updateSavedRoutePlacement", UpdateSavedRoutePlacement, ValueReaders.Create<string>()));
            AddBinding(new TriggerBinding<string>(PanelBindingGroup, "updateSavedRouteShieldStyle", UpdateSavedRouteShieldStyle, ValueReaders.Create<string>()));
            AddBinding(new TriggerBinding<string>(PanelBindingGroup, "toggleManipulateRoute", ToggleManipulateRoute, ValueReaders.Create<string>()));
            AddBinding(new TriggerBinding<long>(PanelBindingGroup, "cleanupSavedRouteWaypoints", CleanupSavedRouteWaypoints, ValueReaders.Create<long>()));
            AddBinding(new TriggerBinding<string>(PanelBindingGroup, "setRouteStatisticsExpanded", SetRouteStatisticsExpanded, ValueReaders.Create<string>()));
            Mod.log.Info("RoadRouteToolUISystem selected-info bindings registered");
        }

        protected override void OnUpdate()
        {
            var gameplayAvailable = IsGameplayUiContextAvailable();
            SetShortcutActionsEnabled(gameplayAvailable);

            if (gameplayAvailable)
            {
                if (_toggleRenameAction?.WasPerformedThisFrame() == true)
                    ToggleRenameShortcut();
                else if (_toggleRoutesAction?.WasPerformedThisFrame() == true)
                    ToggleRoutesShortcut();
            }

            if (_lastGameplayAvailable != gameplayAvailable)
            {
                Mod.log.Info(gameplayAvailable
                    ? "Road Naming: gameplay UI context detected."
                    : "Road Naming: gameplay UI context lost.");
                _lastGameplayAvailable = gameplayAvailable;
            }

            UpdateRouteStatistics(gameplayAvailable, false);

            var state = gameplayAvailable && _panelVisible
                ? BuildState(gameplayAvailable)
                : BuildClosedState(gameplayAvailable);
            if (state != _lastState)
            {
                _lastState = state;
                _stateBinding.Update(state);
            }
        }

        private void SetShortcutActionsEnabled(bool enabled)
        {
            if (_toggleRenameAction != null)
                _toggleRenameAction.shouldBeEnabled = enabled;
            if (_toggleRoutesAction != null)
                _toggleRoutesAction.shouldBeEnabled = enabled;
        }

        private void ToggleRenameShortcut()
        {
            var renameActive = _panelVisible
                && IsToolOpen()
                && _toolSystem?.Mode == RoadRouteToolMode.RenameSelectedSegments;
            if (renameActive)
            {
                CancelTool();
                PublishPanelShortcutCommand("close");
                return;
            }

            ActivateRenameMenu();
            PublishPanelShortcutCommand("rename");
        }

        private void ToggleRoutesShortcut()
        {
            var routesActive = _panelVisible
                && IsToolOpen()
                && _toolSystem?.Mode == RoadRouteToolMode.AssignMajorRouteNumber;
            if (routesActive)
            {
                CancelTool();
                PublishPanelShortcutCommand("close");
                return;
            }

            ActivateRouteMenu();
            PublishPanelShortcutCommand("routes");
        }

        private void PublishPanelShortcutCommand(string command)
        {
            _panelShortcutSequence++;
            _panelShortcutCommandBinding?.Update($"{_panelShortcutSequence}|{command}");
        }

        private void ActivateTool()
        {
            if (!CanUseRouteTool())
            {
                Mod.log.Warn("Road Naming: activation skipped because the gameplay tool systems are not available.");
                return;
            }

            try
            {
                _gameToolSystem.activeTool = _toolSystem;
                _toolSystem?.SetRouteMenuActive(false);
                _toolSystem?.SetSavedRoutesViewActive(false);
                _panelVisible = true;
                Mod.log.Info("Road Naming: selected-info panel activated the route tool.");
            }
            catch (Exception ex)
            {
                Mod.log.Error(ex, "Failed to activate Road Naming: route tool.");
            }
        }

        private void ActivateRouteMenu()
        {
            ActivateMenu(RoadRouteToolMode.AssignMajorRouteNumber);
        }

        private void ActivateRenameMenu()
        {
            if (Mod.Settings?.EnableSavedRenameRoutes != true)
            {
                ActivateTool();
                _toolSystem?.SetMode(RoadRouteToolMode.RenameSelectedSegments);
                return;
            }

            ActivateMenu(RoadRouteToolMode.RenameSelectedSegments);
        }

        private void ActivateMenu(RoadRouteToolMode mode)
        {
            if (!CanUseRouteTool())
            {
                Mod.log.Warn("Road Naming: route menu activation skipped because the gameplay tool systems are not available.");
                return;
            }

            try
            {
                _gameToolSystem.activeTool = _toolSystem;
                _toolSystem?.SetMode(mode);
                _toolSystem?.SetRouteMenuActive(true);
                _panelVisible = true;
                Mod.log.Info(() => $"Road Naming: selected-info panel activated the menu. Mode={mode}.");
            }
            catch (Exception ex)
            {
                Mod.log.Error(ex, "Failed to activate Road Naming route menu.");
            }
        }

        private void ActivateSavedRoutes()
        {
            ActivateSavedRoutes(RoadRouteToolMode.AssignMajorRouteNumber);
        }

        private void ActivateSavedRenames()
        {
            if (Mod.Settings?.EnableSavedRenameRoutes != true)
            {
                ActivateRenameMenu();
                return;
            }

            ActivateSavedRoutes(RoadRouteToolMode.RenameSelectedSegments);
        }

        private void ActivateSavedRoutes(RoadRouteToolMode mode)
        {
            if (!CanUseRouteTool())
            {
                Mod.log.Warn("Road Naming: saved-routes activation skipped because the gameplay tool systems are not available.");
                return;
            }

            try
            {
                _gameToolSystem.activeTool = _toolSystem;
                _toolSystem?.SetRouteMenuActive(false);
                _toolSystem?.SetMode(mode);
                _toolSystem?.SetSavedRoutesViewActive(true, false);
                _panelVisible = true;
                Mod.log.Info(() => $"Road Naming: selected-info panel activated saved routes view. Mode={mode}.");
            }
            catch (Exception ex)
            {
                Mod.log.Error(ex, "Failed to activate Road Naming saved routes view.");
            }
        }

        private void CancelTool()
        {
            try
            {
                DeactivateRouteStatistics();
                _toolSystem?.SetSavedRoutesViewActive(false);
                _toolSystem?.SetRouteMenuActive(false);
                _toolSystem?.ClearSelection();

                if (_gameToolSystem != null && _defaultToolSystem != null && IsToolOpen())
                    _gameToolSystem.activeTool = _defaultToolSystem;

                _panelVisible = false;
                Mod.log.Info("Road Naming: selected-info panel closed.");
            }
            catch (Exception ex)
            {
                Mod.log.Error(ex, "Failed to cancel Road Naming: route tool.");
            }
        }

        private void Apply()
        {
            if (!IsGameplayContextAvailable())
            {
                Mod.log.Warn("Road Naming: Apply ignored because gameplay context is unavailable.");
                return;
            }

            var segmentCount = _toolSystem?.SelectedSegments?.Count ?? 0;
            Mod.log.Info(() => $"Road Naming: Apply clicked. Mode={_toolSystem?.Mode}, Input='{_toolSystem?.InputText ?? string.Empty}', CommittedSegments={segmentCount}.");
            _toolSystem?.Apply();
        }

        private void Clear()
        {
            Mod.log.Info("Road Naming: Clear clicked.");
            _toolSystem?.ClearSelection();
        }

        private void RemoveLast()
        {
            Mod.log.Info("Road Naming: Undo Waypoint clicked.");
            _toolSystem?.RemoveLastSegment();
        }

        private void SetMode(string mode)
        {
            Mod.log.Info(() => $"Road Naming: SetMode received. Mode='{mode ?? string.Empty}'.");
            if (_toolSystem == null)
                return;

            if (string.Equals(mode, "rename", StringComparison.OrdinalIgnoreCase))
                _toolSystem.SetMode(RoadRouteToolMode.RenameSelectedSegments);
            else
                _toolSystem.SetMode(RoadRouteToolMode.AssignMajorRouteNumber);
        }

        private void SetInput(string value)
        {
            _toolSystem?.SetInputText(value);
        }

        private void SetRouteNumberPlacement(string value)
        {
            Mod.log.Info(() => $"Road Naming: SetRouteNumberPlacement received. Value='{value ?? string.Empty}'.");
            var placement = string.Equals(value, RouteNumberPlacement.BeforeBaseName.ToString(), StringComparison.OrdinalIgnoreCase)
                ? RouteNumberPlacement.BeforeBaseName
                : RouteNumberPlacement.AfterBaseName;
            _toolSystem?.SetRouteNumberPlacement(placement);
        }

        private void SetRouteShieldStyle(string value)
        {
            ParseRouteShieldSelection(value, out var shieldStyle, out var importId);
            Mod.log.Info(() => $"Road Naming: SetRouteShieldStyle received. Value='{value ?? string.Empty}', ShieldStyle={shieldStyle}, ImportId='{importId}'.");
            _toolSystem?.SetRouteShieldStyle(shieldStyle, importId);
        }

        private void SetUndergroundMode(bool enabled)
        {
            Mod.log.Info(() => $"Road Naming: SetUndergroundMode received. Enabled={enabled}.");
            _toolSystem?.SetUndergroundMode(enabled);
        }

        private void SelectSavedRoute(long routeId)
        {
            Mod.log.Info(() => $"Road Naming: SelectSavedRoute received. RouteId={routeId}.");
            if (_activeStatisticsRouteId != 0 && _activeStatisticsRouteId != routeId)
                DeactivateRouteStatistics();
            _toolSystem?.SelectSavedRoute(routeId);
        }

        private void ReapplySavedRoute(long routeId)
        {
            Mod.log.Info(() => $"Road Naming: ReapplySavedRoute received. RouteId={routeId}.");
            _toolSystem?.ReapplySavedRoute(routeId);
        }

        private void RebuildSavedRoute(long routeId)
        {
            Mod.log.Info(() => $"Road Naming: RebuildSavedRoute received. RouteId={routeId}.");
            _toolSystem?.BeginRebuildSavedRoute(routeId);
        }

        private void ReapplyAllSavedRoutes()
        {
            Mod.log.Info("Road Naming: ReapplyAllSavedRoutes received.");
            _toolSystem?.ReapplyAllSavedRoutes();
        }

        private void DeleteSavedRoute(long routeId)
        {
            Mod.log.Info(() => $"Road Naming: DeleteSavedRoute received. RouteId={routeId}.");
            _toolSystem?.DeleteSavedRoute(routeId);
        }

        private void UpdateSavedRouteInput(string payload)
        {
            if (!TryParseRouteStringPayload(payload, out var routeId, out var value))
                return;

            Mod.log.Info(() => $"Road Naming: UpdateSavedRouteInput received. RouteId={routeId}, Value='{value}'.");
            _toolSystem?.UpdateSavedRouteInput(routeId, value);
        }

        private void UpdateSavedRoutePlacement(string payload)
        {
            if (!TryParseRouteStringPayload(payload, out var routeId, out var value))
                return;

            var placement = string.Equals(value, RouteNumberPlacement.BeforeBaseName.ToString(), StringComparison.Ordinal)
                ? RouteNumberPlacement.BeforeBaseName
                : RouteNumberPlacement.AfterBaseName;

            Mod.log.Info(() => $"Road Naming: UpdateSavedRoutePlacement received. RouteId={routeId}, Placement={placement}.");
            _toolSystem?.UpdateSavedRoutePlacement(routeId, placement);
        }

        private void UpdateSavedRouteShieldStyle(string payload)
        {
            if (!TryParseRouteStringPayload(payload, out var routeId, out var value))
                return;

            ParseRouteShieldSelection(value, out var shieldStyle, out var importId);
            Mod.log.Info(() => $"Road Naming: UpdateSavedRouteShieldStyle received. RouteId={routeId}, ShieldStyle={shieldStyle}, ImportId='{importId}'.");
            _toolSystem?.UpdateSavedRouteShieldStyle(routeId, shieldStyle, importId);
        }

        private void ToggleManipulateRoute(string payload)
        {
            var routeId = 0L;
            var enabled = false;
            try
            {
                var parts = (payload ?? string.Empty).Split('|');
                if (parts.Length > 0)
                    long.TryParse(parts[0], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out routeId);
                enabled = parts.Length > 1 && parts[1] == "1";
            }
            catch
            {
                routeId = 0;
                enabled = false;
            }

            Mod.log.Info(() => $"Road Naming: ToggleManipulateRoute received. RouteId={routeId}, Enabled={enabled}.");
            _toolSystem?.SetSavedRouteManipulateMode(routeId, enabled);
        }

        private void CleanupSavedRouteWaypoints(long routeId)
        {
            Mod.log.Info(() => $"Road Naming: CleanupSavedRouteWaypoints received. RouteId={routeId}.");
            _toolSystem?.CleanupSavedRouteWaypointsAndBeginManipulate(routeId);
        }

        private void SetRouteStatisticsExpanded(string payload)
        {
            var routeId = 0L;
            var expanded = false;
            var parts = (payload ?? string.Empty).Split(new[] { '|' }, 2);
            if (parts.Length > 0)
                long.TryParse(parts[0], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out routeId);
            expanded = parts.Length > 1 && parts[1] == "1";

            if (!expanded)
            {
                if (routeId == 0 || routeId == _activeStatisticsRouteId)
                    DeactivateRouteStatistics();
                return;
            }

            if (Mod.Settings?.EnableRouteStatistics != true || routeId <= 0)
            {
                DeactivateRouteStatistics();
                return;
            }

            _activeStatisticsRouteId = routeId;
            _hasStatisticsRefreshFrame = false;
            UpdateRouteStatistics(IsGameplayUiContextAvailable(), true);
        }

        private void UpdateRouteStatistics(bool gameplayAvailable, bool force)
        {
            if (!CanCollectRouteStatistics(gameplayAvailable))
            {
                DeactivateRouteStatistics();
                return;
            }

            var frameIndex = _simulationSystem?.frameIndex ?? 0;
            var configuration = Mod.Settings.GetRouteStatisticsConfiguration();
            if (!force
                && _hasStatisticsRefreshFrame
                && !HasStatisticsConfigurationChanged(configuration)
                && unchecked(frameIndex - _lastStatisticsRefreshFrame) < configuration.RefreshInterval)
            {
                return;
            }

            _lastStatisticsRefreshFrame = frameIndex;
            _hasStatisticsRefreshFrame = true;

            if (_metadataSystem?.RouteDatabase == null
                || !_metadataSystem.RouteDatabase.TryGet(_activeStatisticsRouteId, out var route)
                || route == null
                || route.IsDeleted)
            {
                DeactivateRouteStatistics();
                return;
            }

            var storedCount = route.OrderedSegmentIds.Count;
            var validCount = 0;
            EnsureRouteStatisticsSampleCapacity(storedCount);

            for (var i = 0; i < storedCount; i++)
            {
                var segment = route.OrderedSegmentIds[i];
                if (!_metadataSystem.Validation.IsValidRoadSegment(segment))
                    continue;

                var road = EntityManager.GetComponentData<Road>(segment);
                var length = EntityManager.GetComponentData<Curve>(segment).m_Length;
                var edgeFlow = NetUtils.GetTrafficFlowSpeed(road) * 100f;
                var edgeVolume = (road.m_TrafficFlowDistance0 + road.m_TrafficFlowDistance1) * (16f * 4f / 24f);
                _midnightTrafficSamples[validCount] = new RouteTrafficSample { Volume = edgeVolume.x, Flow = edgeFlow.x, Length = length };
                _morningTrafficSamples[validCount] = new RouteTrafficSample { Volume = edgeVolume.y, Flow = edgeFlow.y, Length = length };
                _noonTrafficSamples[validCount] = new RouteTrafficSample { Volume = edgeVolume.z, Flow = edgeFlow.z, Length = length };
                _eveningTrafficSamples[validCount] = new RouteTrafficSample { Volume = edgeVolume.w, Flow = edgeFlow.w, Length = length };
                validCount++;
            }

            var midnight = CalculateBucketStatistics(_midnightTrafficSamples, validCount, configuration);
            var morning = CalculateBucketStatistics(_morningTrafficSamples, validCount, configuration);
            var noon = CalculateBucketStatistics(_noonTrafficSamples, validCount, configuration);
            var evening = CalculateBucketStatistics(_eveningTrafficSamples, validCount, configuration);

            PublishRouteStatistics(
                route.RouteId,
                storedCount,
                validCount,
                storedCount - validCount,
                configuration,
                new float4(midnight.PrimaryFlow, morning.PrimaryFlow, noon.PrimaryFlow, evening.PrimaryFlow),
                new float4(midnight.PrimaryVolume, morning.PrimaryVolume, noon.PrimaryVolume, evening.PrimaryVolume),
                new float4(midnight.BusyVolume, morning.BusyVolume, noon.BusyVolume, evening.BusyVolume),
                new float4(midnight.CorridorLoad, morning.CorridorLoad, noon.CorridorLoad, evening.CorridorLoad),
                new float4(midnight.VolumeWeightedFlow, morning.VolumeWeightedFlow, noon.VolumeWeightedFlow, evening.VolumeWeightedFlow),
                new float4(midnight.BottleneckFlow, morning.BottleneckFlow, noon.BottleneckFlow, evening.BottleneckFlow),
                new float4(midnight.CongestedDistance, morning.CongestedDistance, noon.CongestedDistance, evening.CongestedDistance));
        }

        private void EnsureRouteStatisticsSampleCapacity(int required)
        {
            if (_midnightTrafficSamples.Length >= required)
                return;

            var capacity = 16;
            while (capacity < required)
                capacity *= 2;

            _midnightTrafficSamples = new RouteTrafficSample[capacity];
            _morningTrafficSamples = new RouteTrafficSample[capacity];
            _noonTrafficSamples = new RouteTrafficSample[capacity];
            _eveningTrafficSamples = new RouteTrafficSample[capacity];
        }

        private static RouteStatisticsBucket CalculateBucketStatistics(
            RouteTrafficSample[] samples,
            int count,
            RouteStatisticsConfiguration configuration)
        {
            if (count <= 0)
                return default(RouteStatisticsBucket);

            Array.Sort(samples, 0, count, RouteTrafficSampleComparer.Instance);
            var busiestCount = math.max(1, (int)math.ceil(count * configuration.BusySegmentShare / 100f));
            var bottleneckCount = math.max(1, (int)math.ceil(count * configuration.BottleneckSegmentShare / 100f));
            var corridorLoad = 0f;
            var allFlow = 0f;
            var weightedFlow = 0f;
            var totalLength = 0f;
            var congestedLength = 0f;
            var busiestVolume = 0f;
            var busiestFlow = 0f;
            var bottleneckFlow = float.MaxValue;

            for (var i = 0; i < count; i++)
            {
                var sample = samples[i];
                corridorLoad += sample.Volume;
                allFlow += sample.Flow;
                weightedFlow += sample.Flow * sample.Volume;
                totalLength += sample.Length;
                if (sample.Flow < configuration.CongestionFlowThreshold)
                    congestedLength += sample.Length;
                if (i < busiestCount)
                {
                    busiestVolume += sample.Volume;
                    busiestFlow += sample.Flow;
                }
                if (i < bottleneckCount && sample.Flow < bottleneckFlow)
                    bottleneckFlow = sample.Flow;
            }

            var averageVolume = corridorLoad / count;
            var averageFlow = allFlow / count;
            var busiestAverageVolume = busiestVolume / busiestCount;
            var busiestAverageFlow = busiestFlow / busiestCount;
            var volumeWeightedFlow = corridorLoad > 0.0001f ? weightedFlow / corridorLoad : averageFlow;
            var busyVolume = GetDescendingPercentile(samples, count, configuration.BusyVolumePercentile);
            var congestedDistance = totalLength > 0.0001f ? congestedLength / totalLength * 100f : 0f;

            return new RouteStatisticsBucket
            {
                PrimaryVolume = ResolvePrimaryVolume(configuration.VolumeMode, busyVolume, busiestAverageVolume, averageVolume, corridorLoad),
                PrimaryFlow = ResolvePrimaryFlow(configuration.FlowMode, volumeWeightedFlow, busiestAverageFlow, averageFlow, bottleneckFlow),
                BusyVolume = busyVolume,
                CorridorLoad = corridorLoad,
                VolumeWeightedFlow = volumeWeightedFlow,
                BottleneckFlow = bottleneckFlow,
                CongestedDistance = congestedDistance,
            };
        }

        private static float GetDescendingPercentile(RouteTrafficSample[] samples, int count, int percentile)
        {
            if (count == 1)
                return samples[0].Volume;

            var position = (100f - percentile) / 100f * (count - 1);
            var lower = (int)math.floor(position);
            var upper = (int)math.ceil(position);
            return math.lerp(samples[lower].Volume, samples[upper].Volume, position - lower);
        }

        private static float ResolvePrimaryVolume(
            RouteVolumeAggregationMode mode,
            float busyPercentile,
            float busiestShareAverage,
            float allSegmentsAverage,
            float corridorLoad)
        {
            switch (mode)
            {
                case RouteVolumeAggregationMode.BusiestShareAverage:
                    return busiestShareAverage;
                case RouteVolumeAggregationMode.AllSegmentsAverage:
                    return allSegmentsAverage;
                case RouteVolumeAggregationMode.CorridorLoad:
                    return corridorLoad;
                default:
                    return busyPercentile;
            }
        }

        private static float ResolvePrimaryFlow(
            RouteFlowAggregationMode mode,
            float volumeWeighted,
            float busiestShareAverage,
            float allSegmentsAverage,
            float bottleneck)
        {
            switch (mode)
            {
                case RouteFlowAggregationMode.BusiestShareAverage:
                    return busiestShareAverage;
                case RouteFlowAggregationMode.AllSegmentsAverage:
                    return allSegmentsAverage;
                case RouteFlowAggregationMode.Bottleneck:
                    return bottleneck;
                default:
                    return volumeWeighted;
            }
        }

        private bool CanCollectRouteStatistics(bool gameplayAvailable)
        {
            return _activeStatisticsRouteId > 0
                && Mod.Settings?.EnableRouteStatistics == true
                && gameplayAvailable
                && _panelVisible
                && _toolSystem != null
                && _toolSystem.Mode == RoadRouteToolMode.AssignMajorRouteNumber
                && _toolSystem.SavedRoutesViewActive
                && _toolSystem.SelectedSavedRouteId == _activeStatisticsRouteId
                && IsToolOpen();
        }

        private void PublishRouteStatistics(
            long routeId,
            int storedCount,
            int validCount,
            int excludedCount,
            RouteStatisticsConfiguration configuration,
            float4 flow,
            float4 volume,
            float4 busyVolume,
            float4 corridorLoad,
            float4 volumeWeightedFlow,
            float4 bottleneckFlow,
            float4 congestedDistance)
        {
            var changed = !_statisticsPayloadActive
                || _publishedStatisticsRouteId != routeId
                || _statisticsStoredSegmentCount != storedCount
                || _statisticsValidSegmentCount != validCount
                || _statisticsExcludedSegmentCount != excludedCount
                || HasStatisticsConfigurationChanged(configuration);

            for (var i = 0; i < 4; i++)
            {
                changed |= _routeStatisticsFlow[i] != flow[i] || _routeStatisticsVolume[i] != volume[i];
                _routeStatisticsFlow[i] = flow[i];
                _routeStatisticsVolume[i] = volume[i];
                changed |= SetStatisticsValue(_routeStatisticsBusyVolume, i, busyVolume[i]);
                changed |= SetStatisticsValue(_routeStatisticsCorridorLoad, i, corridorLoad[i]);
                changed |= SetStatisticsValue(_routeStatisticsVolumeWeightedFlow, i, volumeWeightedFlow[i]);
                changed |= SetStatisticsValue(_routeStatisticsBottleneckFlow, i, bottleneckFlow[i]);
                changed |= SetStatisticsValue(_routeStatisticsCongestedDistance, i, congestedDistance[i]);
            }

            changed |= _routeStatisticsFlow[4] != flow.x || _routeStatisticsVolume[4] != volume.x;
            _routeStatisticsFlow[4] = flow.x;
            _routeStatisticsVolume[4] = volume.x;
            changed |= SetStatisticsClosingValue(_routeStatisticsBusyVolume, busyVolume.x);
            changed |= SetStatisticsClosingValue(_routeStatisticsCorridorLoad, corridorLoad.x);
            changed |= SetStatisticsClosingValue(_routeStatisticsVolumeWeightedFlow, volumeWeightedFlow.x);
            changed |= SetStatisticsClosingValue(_routeStatisticsBottleneckFlow, bottleneckFlow.x);
            changed |= SetStatisticsClosingValue(_routeStatisticsCongestedDistance, congestedDistance.x);
            _publishedStatisticsRouteId = routeId;
            _statisticsStoredSegmentCount = storedCount;
            _statisticsValidSegmentCount = validCount;
            _statisticsExcludedSegmentCount = excludedCount;
            _statisticsVolumeMode = configuration.VolumeMode;
            _statisticsFlowMode = configuration.FlowMode;
            _publishedStatisticsConfiguration = configuration;
            _statisticsPayloadActive = true;

            if (changed)
                _routeStatisticsBinding?.Update();
        }

        private bool HasStatisticsConfigurationChanged(RouteStatisticsConfiguration configuration)
        {
            return _publishedStatisticsConfiguration.VolumeMode != configuration.VolumeMode
                || _publishedStatisticsConfiguration.FlowMode != configuration.FlowMode
                || _publishedStatisticsConfiguration.BusySegmentShare != configuration.BusySegmentShare
                || _publishedStatisticsConfiguration.BusyVolumePercentile != configuration.BusyVolumePercentile
                || _publishedStatisticsConfiguration.BottleneckSegmentShare != configuration.BottleneckSegmentShare
                || _publishedStatisticsConfiguration.CongestionFlowThreshold != configuration.CongestionFlowThreshold
                || _publishedStatisticsConfiguration.RefreshInterval != configuration.RefreshInterval;
        }

        private static bool SetStatisticsValue(float[] target, int index, float value)
        {
            var changed = target[index] != value;
            target[index] = value;
            return changed;
        }

        private static bool SetStatisticsClosingValue(float[] target, float value)
        {
            return SetStatisticsValue(target, 4, value);
        }

        private void DeactivateRouteStatistics()
        {
            _activeStatisticsRouteId = 0;
            _hasStatisticsRefreshFrame = false;
            if (!_statisticsPayloadActive)
                return;

            _statisticsPayloadActive = false;
            _publishedStatisticsRouteId = 0;
            _statisticsStoredSegmentCount = 0;
            _statisticsValidSegmentCount = 0;
            _statisticsExcludedSegmentCount = 0;
            _routeStatisticsBinding?.Update();
        }

        private void WriteRouteStatistics(IJsonWriter writer)
        {
            if (!_statisticsPayloadActive)
            {
                writer.WriteNull();
                return;
            }

            writer.TypeBegin("AdvancedRoadNaming.RouteStatistics");
            writer.PropertyName("routeId");
            writer.Write(_publishedStatisticsRouteId);
            writer.PropertyName("storedSegmentCount");
            writer.Write(_statisticsStoredSegmentCount);
            writer.PropertyName("validSegmentCount");
            writer.Write(_statisticsValidSegmentCount);
            writer.PropertyName("excludedSegmentCount");
            writer.Write(_statisticsExcludedSegmentCount);
            writer.PropertyName("flowData");
            WriteStatisticsArray(writer, _routeStatisticsFlow);
            writer.PropertyName("volumeData");
            WriteStatisticsArray(writer, _routeStatisticsVolume);
            writer.PropertyName("busyVolumeData");
            WriteStatisticsArray(writer, _routeStatisticsBusyVolume);
            writer.PropertyName("corridorLoadData");
            WriteStatisticsArray(writer, _routeStatisticsCorridorLoad);
            writer.PropertyName("volumeWeightedFlowData");
            WriteStatisticsArray(writer, _routeStatisticsVolumeWeightedFlow);
            writer.PropertyName("bottleneckFlowData");
            WriteStatisticsArray(writer, _routeStatisticsBottleneckFlow);
            writer.PropertyName("congestedDistanceData");
            WriteStatisticsArray(writer, _routeStatisticsCongestedDistance);
            writer.PropertyName("primaryVolumeMode");
            writer.Write(_statisticsVolumeMode.ToString());
            writer.PropertyName("primaryFlowMode");
            writer.Write(_statisticsFlowMode.ToString());
            writer.TypeEnd();
        }

        private static void WriteStatisticsArray(IJsonWriter writer, float[] values)
        {
            writer.ArrayBegin(values.Length);
            for (var i = 0; i < values.Length; i++)
                writer.Write(values[i]);
            writer.ArrayEnd();
        }

        private static bool TryParseRouteStringPayload(string payload, out long routeId, out string value)
        {
            routeId = 0;
            value = string.Empty;
            var parts = (payload ?? string.Empty).Split(new[] { '|' }, 2);
            if (parts.Length == 0 || !long.TryParse(parts[0], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out routeId))
                return false;

            value = parts.Length > 1 ? parts[1] ?? string.Empty : string.Empty;
            return routeId > 0;
        }

        private static RouteShieldStyle ParseRouteShieldStyle(string value)
        {
            if (Enum.TryParse<RouteShieldStyle>(value ?? string.Empty, true, out var shieldStyle)
                && shieldStyle >= RouteShieldStyle.None
                && shieldStyle <= RouteShieldStyle.Imported)
            {
                return shieldStyle;
            }

            return RouteShieldStyle.None;
        }

        private static void ParseRouteShieldSelection(string value, out RouteShieldStyle style, out string importId)
        {
            importId = string.Empty;
            const string prefix = "Imported:";
            if (!string.IsNullOrWhiteSpace(value) && value.StartsWith(prefix, StringComparison.Ordinal))
            {
                var candidate = value.Substring(prefix.Length);
                if (RouteShieldImportCatalog.Contains(candidate))
                {
                    style = RouteShieldStyle.Imported;
                    importId = candidate;
                    return;
                }
            }

            style = ParseRouteShieldStyle(value);
            if (style == RouteShieldStyle.Imported)
                style = RouteShieldStyle.None;
        }

        private static string BuildRouteShieldSelection(RouteShieldStyle style, string importId)
        {
            return style == RouteShieldStyle.Imported && !string.IsNullOrWhiteSpace(importId)
                ? RouteShieldImportCatalog.SelectionValue(importId)
                : style.ToString();
        }

        public void RefreshRouteShields()
        {
            RouteShieldImportCatalog.Refresh();
            _routeShieldCatalogBinding?.Update();
        }

        private static void WriteRouteShieldCatalog(IJsonWriter writer)
        {
            var definitions = RouteShieldImportCatalog.Definitions;
            writer.ArrayBegin(definitions.Count);
            for (var i = 0; i < definitions.Count; i++)
            {
                var definition = definitions[i];
                writer.TypeBegin("AdvancedRoadNaming.RouteShieldDefinition");
                writer.PropertyName("id");
                writer.Write(definition.SelectionId);
                writer.PropertyName("name");
                writer.Write(definition.Name);
                writer.PropertyName("region");
                writer.Write(definition.Region);
                writer.PropertyName("system");
                writer.Write(definition.System);
                writer.PropertyName("asset");
                writer.Write(definition.Asset);
                writer.PropertyName("assetRoot");
                writer.Write(definition.AssetRoot);
                writer.PropertyName("widthRem");
                writer.Write(definition.WidthRem);
                writer.PropertyName("heightRem");
                writer.Write(definition.HeightRem);
                writer.PropertyName("textLayers");
                writer.ArrayBegin(definition.TextLayers.Count);
                for (var layerIndex = 0; layerIndex < definition.TextLayers.Count; layerIndex++)
                {
                    var layer = definition.TextLayers[layerIndex];
                    writer.TypeBegin("AdvancedRoadNaming.RouteShieldTextLayerDefinition");
                    writer.PropertyName("id");
                    writer.Write(layer.Id);
                    writer.PropertyName("source");
                    writer.Write(layer.Source);
                    writer.PropertyName("text");
                    writer.Write(layer.Text);
                    writer.PropertyName("color");
                    writer.Write(layer.Color);
                    writer.PropertyName("fontSizeRem");
                    writer.Write(layer.FontSizeRem);
                    writer.PropertyName("offsetXRem");
                    writer.Write(layer.OffsetXRem);
                    writer.PropertyName("offsetYRem");
                    writer.Write(layer.OffsetYRem);
                    writer.PropertyName("scalePercent");
                    writer.Write(layer.ScalePercent);
                    writer.PropertyName("rotationDegrees");
                    writer.Write(layer.RotationDegrees);
                    writer.TypeEnd();
                }
                writer.ArrayEnd();
                writer.TypeEnd();
            }
            writer.ArrayEnd();
        }

        private string BuildState(bool gameplayAvailable)
        {
            try
            {
                var selectedCount = _toolSystem?.SelectedSegments?.Count ?? 0;
                var waypointCount = _toolSystem?.WaypointCount ?? 0;
                var hover = _toolSystem == null || _toolSystem.HoveredSegment == Unity.Entities.Entity.Null
                    ? "none"
                    : _toolSystem.HoveredSegment.Index.ToString(System.Globalization.CultureInfo.InvariantCulture);
                var savedRoutesJson = BuildSavedRoutesPayloadForActiveMode();

                return string.Join("|", new[]
                {
                    Escape("1"),
                    Escape((_toolSystem?.Mode ?? RoadRouteToolMode.AssignMajorRouteNumber).ToString()),
                    Escape(_toolSystem?.InputText ?? string.Empty),
                    Escape(selectedCount.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                    Escape(hover),
                    Escape(gameplayAvailable ? _toolSystem?.BuildPreviewText() ?? string.Empty : string.Empty),
                    Escape(gameplayAvailable ? _toolSystem?.StatusMessage ?? string.Empty : string.Empty),
                    Escape(gameplayAvailable ? "1" : "0"),
                    Escape(waypointCount.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                    Escape(savedRoutesJson),
                    Escape((_toolSystem?.RouteNumberPlacement ?? RouteNumberPlacement.AfterBaseName).ToString()),
                    Escape(_toolSystem?.UndergroundMode == true ? "1" : "0"),
                    Escape((_toolSystem?.SelectedSavedRouteId ?? 0).ToString(System.Globalization.CultureInfo.InvariantCulture)),
                    Escape(_toolSystem?.SavedRoutesViewActive == true ? "1" : "0"),
                    Escape(_toolSystem?.SavedRouteManipulateMode == true ? "1" : "0"),
                    Escape((_toolSystem?.SavedRouteReview?.RouteId ?? 0).ToString(System.Globalization.CultureInfo.InvariantCulture)),
                    Escape(Mod.Settings?.ShowAdvancedRouteDetails == true ? "1" : "0"),
                    Escape(_toolSystem?.ApplyCooldownActive == true ? "1" : "0"),
                    Escape(Mod.Settings?.EnableRouteStatistics == true ? "1" : "0"),
                    Escape(BuildRouteShieldSelection(_toolSystem?.RouteShieldStyle ?? RouteShieldStyle.None, _toolSystem?.RouteShieldImportId)),
                    Escape(Mod.Settings?.EnableSavedRenameRoutes != false ? "1" : "0")
                });
            }
            catch (Exception ex)
            {
                Mod.log.Warn(() => $"Road Naming: UI state build failed; using last known state. Error='{ex.Message}'.");
                return _lastState ?? BuildClosedState(false);
            }
        }

        private static string BuildClosedState(bool gameplayAvailable)
        {
            return string.Join("|", new[]
            {
                Escape("0"),
                Escape(RoadRouteToolMode.AssignMajorRouteNumber.ToString()),
                Escape(string.Empty),
                Escape("0"),
                Escape("none"),
                Escape(string.Empty),
                Escape(string.Empty),
                Escape(gameplayAvailable ? "1" : "0"),
                Escape("0"),
                Escape("[]"),
                Escape(RouteNumberPlacement.AfterBaseName.ToString()),
                Escape("0"),
                Escape("0"),
                Escape("0"),
                Escape("0"),
                Escape("0"),
                Escape("0"),
                Escape("0"),
                Escape(Mod.Settings?.EnableRouteStatistics == true ? "1" : "0"),
                Escape(RouteShieldStyle.None.ToString()),
                Escape(Mod.Settings?.EnableSavedRenameRoutes != false ? "1" : "0")
            });
        }

        private string BuildSavedRoutesPayloadForActiveMode()
        {
            if (_toolSystem == null
                || (_toolSystem.Mode == RoadRouteToolMode.RenameSelectedSegments && Mod.Settings?.EnableSavedRenameRoutes != true))
                return "[]";

            try
            {
                return _toolSystem.SavedRoutesJson ?? "[]";
            }
            catch (Exception ex)
            {
                Mod.log.Warn(() => $"Road Naming: Saved Routes JSON build failed during UI state update. Error='{ex.Message}'.");
                return "[]";
            }
        }

        private bool IsToolOpen()
        {
            try
            {
                return _gameToolSystem != null && _toolSystem != null && ReferenceEquals(_gameToolSystem.activeTool, _toolSystem);
            }
            catch (Exception ex)
            {
                Mod.log.Warn(ex, "Road Naming: could not read the active tool state; treating panel as closed.");
                return false;
            }
        }

        private bool IsGameplayContextAvailable()
        {
            return IsGameplayUiContextAvailable() && CanUseRouteTool();
        }

        private bool CanUseRouteTool()
        {
            try
            {
                if (!IsGameplayUiContextAvailable())
                    return false;

                if (_toolSystem == null || _gameToolSystem == null || _defaultToolSystem == null)
                {
                    Mod.log.Warn("Road Naming: route tool systems are not ready.");
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                Mod.log.Warn(ex, "Road Naming: route tool availability check failed.");
                return false;
            }
        }

        private bool IsGameplayUiContextAvailable()
        {
            try
            {
                var gameManager = GameManager.instance;
                if (gameManager == null)
                    return false;

                if (gameManager.gameMode != GameMode.Game || gameManager.isGameLoading)
                    return false;

                return gameManager.userInterface?.view?.View != null;
            }
            catch (Exception ex)
            {
                Mod.log.Warn(ex, "Road Naming: gameplay UI context check failed.");
                return false;
            }
        }

        private static string Escape(string value)
        {
            return (value ?? string.Empty).Replace("\\", "\\\\").Replace("|", "\\p").Replace("\n", "\\n").Replace("\r", string.Empty);
        }
    }
}
