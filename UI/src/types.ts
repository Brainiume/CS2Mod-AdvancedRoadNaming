export type RouteToolMode =
    | "RenameSelectedSegments"
    | "AssignMajorRouteNumber";

export type RouteToolModeCommand = "rename" | "assign";

export type RoutePanelScreen = "menu" | "newRoute" | "manageRoutes";

export type SavedRouteStatus =
    | "Valid"
    | "PartiallyValid"
    | "MissingSegments"
    | "RebuildNeeded"
    | "Deleted";

export type PrefixType = "M" | "A" | "B" | "C" | "Custom";

export type SavedRouteFilter = "M" | "A" | "B" | "C" | "None";

export type RouteNumberPlacement = "BeforeBaseName" | "AfterBaseName";

export interface SavedRoute {
    id: number;
    title: string;
    savedTitle?: string;
    userTitle?: boolean;
    mode: RouteToolMode;
    input: string;
    routeCode?: string;
    routePrefixType?: SavedRouteFilter | "Custom";
    routeNumberPlacement?: RouteNumberPlacement;
    segments: number;
    waypoints: number;
    status: SavedRouteStatus;
    streets: string;
    startDistrictName?: string;
    endDistrictName?: string;
    startRoadName?: string;
    endRoadName?: string;
    derivedDisplayCorridor?: string;
    districtSummary?: string;
    subtitle?: string;
    updated: string;
}

export interface PanelState {
    isOpen: boolean;
    mode: RouteToolMode;
    input: string;
    selectedSegments: number;
    hoveredSegment: string;
    previewText: string;
    statusMessage: string;
    inGame: boolean;
    waypointCount: number;
    savedRoutes: SavedRoute[];
    routeNumberPlacement: RouteNumberPlacement;
    undergroundMode: boolean;
    selectedSavedRouteId: number;
    savedRoutesViewActive: boolean;
    savedRouteManipulateMode: boolean;
    savedRouteReviewRouteId: number;
    showAdvancedRouteDetails: boolean;
    applyCooldownActive: boolean;
}

export interface RouteCodeDraft {
    prefixType: PrefixType;
    customPrefix: string;
    numberPart: string;
}
