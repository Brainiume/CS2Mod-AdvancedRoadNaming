import { bindLocalValue, bindValue } from "cs2/api";
import { Entity } from "cs2/bindings";
import { DEFAULT_PANEL_STATE, PANEL_GROUP } from "constants";
import { engine } from "engine";
import { RouteNumberPlacement, RoutePanelScreen, RouteShieldDefinition, RouteShieldOverlayItem, RouteShieldSelection, RouteStatistics, RouteToolModeCommand } from "types";

export const panelState$ = bindValue<string>(PANEL_GROUP, "state", DEFAULT_PANEL_STATE);
export const panelShortcutCommand$ = bindValue<string>(PANEL_GROUP, "panelShortcutCommand", "0|none");
export const routeStatistics$ = bindValue<RouteStatistics | null>(PANEL_GROUP, "routeStatistics", null);
export const routeShieldOverlays$ = bindValue<RouteShieldOverlayItem[]>(PANEL_GROUP, "routeShieldOverlays", []);
export const routeShieldCatalog$ = bindValue<RouteShieldDefinition[]>(PANEL_GROUP, "routeShieldCatalog", []);
export const selectedEntity$ = bindValue<Entity>("selectedInfo", "selectedEntity", { index: 0, version: 0 });
export const advancedRoadNamingPanelOpen$ = bindLocalValue(false);
export const advancedRoadNamingPanelKind$ = bindLocalValue<"rename" | "routes">("rename");
export const advancedRoadRoutesScreen$ = bindLocalValue<RoutePanelScreen>("menu");

export function openAdvancedRoadNamingPanel() {
    advancedRoadNamingPanelKind$.update("rename");
    advancedRoadRoutesScreen$.update("menu");
    advancedRoadNamingPanelOpen$.update(true);
    panelActions.activateRenameMenu();
}

export function openAdvancedRoadRoutesPanel() {
    advancedRoadNamingPanelKind$.update("routes");
    advancedRoadRoutesScreen$.update("menu");
    advancedRoadNamingPanelOpen$.update(true);
    panelActions.activateRouteMenu();
}

export function closeAdvancedRoadNamingPanel() {
    advancedRoadNamingPanelOpen$.update(false);
    panelActions.cancel();
}

export const panelActions = {
    activateRenameMenu() {
        engine.trigger(PANEL_GROUP, "activateRenameMenu");
    },
    activate() {
        engine.trigger(PANEL_GROUP, "activate");
    },
    activateRouteMenu() {
        engine.trigger(PANEL_GROUP, "activateRouteMenu");
    },
    activateSavedRoutes() {
        engine.trigger(PANEL_GROUP, "activateSavedRoutes");
    },
    activateSavedRenames() {
        engine.trigger(PANEL_GROUP, "activateSavedRenames");
    },
    cancel() {
        engine.trigger(PANEL_GROUP, "cancel");
    },
    openRouteMenu() {
        advancedRoadRoutesScreen$.update("menu");
    },
    startNewRoute() {
        advancedRoadRoutesScreen$.update("newRoute");
        engine.trigger(PANEL_GROUP, "activate");
        engine.trigger(PANEL_GROUP, "setMode", "assign");
    },
    startNewRenameRoute() {
        advancedRoadRoutesScreen$.update("newRoute");
        engine.trigger(PANEL_GROUP, "activate");
        engine.trigger(PANEL_GROUP, "setMode", "rename");
    },
    openManageRoutes() {
        advancedRoadRoutesScreen$.update("manageRoutes");
        engine.trigger(PANEL_GROUP, "activateSavedRoutes");
    },
    openManageRenames() {
        advancedRoadRoutesScreen$.update("manageRoutes");
        engine.trigger(PANEL_GROUP, "activateSavedRenames");
    },
    backToRouteMenu() {
        advancedRoadRoutesScreen$.update("menu");
        engine.trigger(PANEL_GROUP, "activateRouteMenu");
    },
    backToRenameMenu() {
        advancedRoadRoutesScreen$.update("menu");
        engine.trigger(PANEL_GROUP, "activateRenameMenu");
    },
    apply() {
        engine.trigger(PANEL_GROUP, "apply");
    },
    clear() {
        engine.trigger(PANEL_GROUP, "clear");
    },
    removeLast() {
        engine.trigger(PANEL_GROUP, "removeLast");
    },
    setMode(mode: RouteToolModeCommand) {
        engine.trigger(PANEL_GROUP, "setMode", mode);
    },
    setInput(value: string) {
        engine.trigger(PANEL_GROUP, "setInput", value);
    },
    setRouteNumberPlacement(value: RouteNumberPlacement) {
        engine.trigger(PANEL_GROUP, "setRouteNumberPlacement", value);
    },
    setRouteShieldStyle(value: RouteShieldSelection) {
        engine.trigger(PANEL_GROUP, "setRouteShieldStyle", value);
    },
    setUndergroundMode(enabled: boolean) {
        engine.trigger(PANEL_GROUP, "setUndergroundMode", enabled);
    },
    selectSavedRoute(routeId: number) {
        engine.trigger(PANEL_GROUP, "selectSavedRoute", routeId);
    },
    previewSavedRoute(routeId: number) {
        engine.trigger(PANEL_GROUP, "previewSavedRoute", routeId);
    },
    reapplySavedRoute(routeId: number) {
        engine.trigger(PANEL_GROUP, "reapplySavedRoute", routeId);
    },
    rebuildSavedRoute(routeId: number) {
        engine.trigger(PANEL_GROUP, "rebuildSavedRoute", routeId);
    },
    reapplyAllSavedRoutes() {
        engine.trigger(PANEL_GROUP, "reapplyAllSavedRoutes");
    },
    deleteSavedRoute(routeId: number) {
        engine.trigger(PANEL_GROUP, "deleteSavedRoute", routeId);
    },
    updateSavedRouteInput(routeId: number, value: string) {
        engine.trigger(PANEL_GROUP, "updateSavedRouteInput", `${routeId}|${value}`);
    },
    updateSavedRoutePlacement(routeId: number, value: RouteNumberPlacement) {
        engine.trigger(PANEL_GROUP, "updateSavedRoutePlacement", `${routeId}|${value}`);
    },
    updateSavedRouteShieldStyle(routeId: number, value: RouteShieldSelection) {
        engine.trigger(PANEL_GROUP, "updateSavedRouteShieldStyle", `${routeId}|${value}`);
    },
    toggleManipulateRoute(routeId: number, enabled: boolean) {
        engine.trigger(PANEL_GROUP, "toggleManipulateRoute", `${routeId}|${enabled ? "1" : "0"}`);
    },
    cleanupSavedRouteWaypoints(routeId: number) {
        engine.trigger(PANEL_GROUP, "cleanupSavedRouteWaypoints", routeId);
    },
    setRouteStatisticsExpanded(routeId: number, expanded: boolean) {
        engine.trigger(PANEL_GROUP, "setRouteStatisticsExpanded", `${routeId}|${expanded ? "1" : "0"}`);
    },
};
