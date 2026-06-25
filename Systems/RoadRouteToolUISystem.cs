using System;
using Colossal.UI.Binding;
using Game;
using Game.SceneFlow;
using Game.Tools;
using Game.UI;
using AdvancedRoadNaming.Domain;

namespace AdvancedRoadNaming.Systems
{
    public sealed partial class RoadRouteToolUISystem : UISystemBase
    {
        private const string PanelBindingGroup = "AdvancedRoadNaming";

        private RoadRouteToolSystem _toolSystem;
        private ToolSystem _gameToolSystem;
        private DefaultToolSystem _defaultToolSystem;
        private ValueBinding<string> _stateBinding;
        private string _lastState;
        private bool _panelVisible;
        private bool _lastGameplayAvailable;

        protected override void OnCreate()
        {
            base.OnCreate();
            _toolSystem = World.GetOrCreateSystemManaged<RoadRouteToolSystem>();
            _gameToolSystem = World.GetOrCreateSystemManaged<ToolSystem>();
            _defaultToolSystem = World.GetOrCreateSystemManaged<DefaultToolSystem>();

            _lastState = BuildClosedState(false);
            _stateBinding = new ValueBinding<string>(PanelBindingGroup, "state", _lastState, ValueWriters.Create<string>(), System.Collections.Generic.EqualityComparer<string>.Default);

            AddBinding(_stateBinding);
            AddBinding(new TriggerBinding(PanelBindingGroup, "activateRouteMenu", ActivateRouteMenu));
            AddBinding(new TriggerBinding(PanelBindingGroup, "activate", ActivateTool));
            AddBinding(new TriggerBinding(PanelBindingGroup, "activateSavedRoutes", ActivateSavedRoutes));
            AddBinding(new TriggerBinding(PanelBindingGroup, "cancel", CancelTool));
            AddBinding(new TriggerBinding(PanelBindingGroup, "apply", Apply));
            AddBinding(new TriggerBinding(PanelBindingGroup, "clear", Clear));
            AddBinding(new TriggerBinding(PanelBindingGroup, "removeLast", RemoveLast));
            AddBinding(new TriggerBinding<string>(PanelBindingGroup, "setMode", SetMode, ValueReaders.Create<string>()));
            AddBinding(new TriggerBinding<string>(PanelBindingGroup, "setInput", SetInput, ValueReaders.Create<string>()));
            AddBinding(new TriggerBinding<string>(PanelBindingGroup, "setRouteNumberPlacement", SetRouteNumberPlacement, ValueReaders.Create<string>()));
            AddBinding(new TriggerBinding<bool>(PanelBindingGroup, "setUndergroundMode", SetUndergroundMode, ValueReaders.Create<bool>()));
            AddBinding(new TriggerBinding<long>(PanelBindingGroup, "selectSavedRoute", SelectSavedRoute, ValueReaders.Create<long>()));
            AddBinding(new TriggerBinding<long>(PanelBindingGroup, "previewSavedRoute", SelectSavedRoute, ValueReaders.Create<long>()));
            AddBinding(new TriggerBinding<long>(PanelBindingGroup, "reapplySavedRoute", ReapplySavedRoute, ValueReaders.Create<long>()));
            AddBinding(new TriggerBinding<long>(PanelBindingGroup, "deleteSavedRoute", DeleteSavedRoute, ValueReaders.Create<long>()));
            AddBinding(new TriggerBinding<string>(PanelBindingGroup, "updateSavedRouteInput", UpdateSavedRouteInput, ValueReaders.Create<string>()));
            AddBinding(new TriggerBinding<string>(PanelBindingGroup, "updateSavedRoutePlacement", UpdateSavedRoutePlacement, ValueReaders.Create<string>()));
            AddBinding(new TriggerBinding<string>(PanelBindingGroup, "toggleManipulateRoute", ToggleManipulateRoute, ValueReaders.Create<string>()));
            Mod.log.Info("RoadRouteToolUISystem selected-info bindings registered");
        }

        protected override void OnUpdate()
        {
            var gameplayAvailable = IsGameplayUiContextAvailable();

            if (_lastGameplayAvailable != gameplayAvailable)
            {
                Mod.log.Info(gameplayAvailable
                    ? "Road Naming: gameplay UI context detected."
                    : "Road Naming: gameplay UI context lost.");
                _lastGameplayAvailable = gameplayAvailable;
            }

            var state = gameplayAvailable && _panelVisible
                ? BuildState(gameplayAvailable)
                : BuildClosedState(gameplayAvailable);
            if (state != _lastState)
            {
                _lastState = state;
                _stateBinding.Update(state);
            }
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
            if (!CanUseRouteTool())
            {
                Mod.log.Warn("Road Naming: route menu activation skipped because the gameplay tool systems are not available.");
                return;
            }

            try
            {
                _gameToolSystem.activeTool = _toolSystem;
                _toolSystem?.SetRouteMenuActive(true);
                _panelVisible = true;
                Mod.log.Info("Road Naming: selected-info panel activated the route menu.");
            }
            catch (Exception ex)
            {
                Mod.log.Error(ex, "Failed to activate Road Naming route menu.");
            }
        }

        private void ActivateSavedRoutes()
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
                _toolSystem?.SetMode(RoadRouteToolMode.AssignMajorRouteNumber);
                _toolSystem?.SetSavedRoutesViewActive(true, false);
                _panelVisible = true;
                Mod.log.Info("Road Naming: selected-info panel activated saved routes view.");
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
            Mod.log.Info(() => 
            {
                if (string.IsNullOrEmpty(value))
                    return "Road Naming: SetInput received empty value.";
                
                var hasNonAscii = value.Length != System.Text.Encoding.ASCII.GetByteCount(value);
                return $"Road Naming: SetInput received. Value='{value}', HasUnicode={hasNonAscii}, Length={value.Length}.";
            });
            
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

        private void SetUndergroundMode(bool enabled)
        {
            Mod.log.Info(() => $"Road Naming: SetUndergroundMode received. Enabled={enabled}.");
            _toolSystem?.SetUndergroundMode(enabled);
        }

        private void SelectSavedRoute(long routeId)
        {
            Mod.log.Info(() => $"Road Naming: SelectSavedRoute received. RouteId={routeId}.");
            _toolSystem?.SelectSavedRoute(routeId);
        }

        private void ReapplySavedRoute(long routeId)
        {
            Mod.log.Info(() => $"Road Naming: ReapplySavedRoute received. RouteId={routeId}.");
            _toolSystem?.ReapplySavedRoute(routeId);
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
                    Escape(_toolSystem?.ApplyCooldownActive == true ? "1" : "0")
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
                Escape("0")
            });
        }

        private string BuildSavedRoutesPayloadForActiveMode()
        {
            if (_toolSystem == null || _toolSystem.Mode != RoadRouteToolMode.AssignMajorRouteNumber)
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
