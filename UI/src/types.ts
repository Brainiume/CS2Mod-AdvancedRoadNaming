export type RouteToolMode =
    | "RenameSelectedSegments"
    | "AssignMajorRouteNumber";

export type RouteToolModeCommand = "rename" | "assign";

export type RoutePanelScreen = "menu" | "newRoute" | "manageRoutes";

export interface RouteStatistics {
    routeId: number;
    storedSegmentCount: number;
    validSegmentCount: number;
    excludedSegmentCount: number;
    flowData: number[];
    volumeData: number[];
    busyVolumeData: number[];
    corridorLoadData: number[];
    volumeWeightedFlowData: number[];
    bottleneckFlowData: number[];
    congestedDistanceData: number[];
    primaryVolumeMode: string;
    primaryFlowMode: string;
}

export type SavedRouteStatus =
    | "Valid"
    | "PartiallyValid"
    | "MissingSegments"
    | "RebuildNeeded"
    | "Deleted";

export type PrefixType = "M" | "A" | "B" | "C" | "I" | "Custom";

export type SavedRouteFilter = "M" | "A" | "B" | "C" | "I" | "None";

export type RouteNumberPlacement = "BeforeBaseName" | "AfterBaseName";

export type RouteShieldStyle =
    | "None"
    | "AustralianMRectangle"
    | "AustralianARectangle"
    | "AustralianBRectangle"
    | "AustralianCRectangle"
    | "AustralianNationalShield"
    | "BlueHighwayShield"
    | "BlackWhiteShield"
    | "InterstateGeneric"
    | "InterstateState"
    | "USInterstate"
    | "USRoute"
    | "CanadaTransCanada"
    | "CanadaHighway"
    | "UKARoad"
    | "FranceAutoroute"
    | "GermanyAutobahn"
    | "JapanNational"
    | "SouthKoreaHighway"
    | "MalaysiaExpressway"
    | "ColombiaNational"
    | "MexicoFederal"
    | "IndiaNational"
    | "SouthAfricaRegional"
    | "NewZealandStateHighway"
    | "GermanyFederalRoad"
    | "ThailandHighway"
    | "ThailandMotorwayBlue"
    | "ThailandMotorwayGreen"
    | "Imported";

export type RouteShieldSelection = RouteShieldStyle | `Imported:${string}`;

export interface RouteShieldOverlayItem {
    id: number;
    style: RouteShieldSelection;
    label: string;
    left: number;
    top: number;
    scale: number;
    occluded: boolean;
}

export type RouteShieldTextSource = "routeCode" | "prefix" | "number" | "literal";

export interface RouteShieldTextLayerDefinition {
    id: string;
    source: RouteShieldTextSource;
    text: string;
    color: string;
    fontSizeRem: number;
    offsetXRem: number;
    offsetYRem: number;
    scalePercent: number;
    rotationDegrees: number;
}

export interface RouteShieldDefinition {
    id: RouteShieldSelection;
    name: string;
    region: string;
    system: string;
    asset: string;
    assetRoot: string;
    widthRem: number;
    heightRem: number;
    textLayers: RouteShieldTextLayerDefinition[];
}

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
    routeShieldStyle?: RouteShieldSelection;
    segments: number;
    waypoints: number;
    orphanWaypointCount?: number;
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
    routeShieldStyle: RouteShieldSelection;
    undergroundMode: boolean;
    selectedSavedRouteId: number;
    savedRoutesViewActive: boolean;
    savedRouteManipulateMode: boolean;
    savedRouteReviewRouteId: number;
    showAdvancedRouteDetails: boolean;
    applyCooldownActive: boolean;
    routeStatisticsEnabled: boolean;
    savedRenameRoutesEnabled: boolean;
}

export interface RouteCodeDraft {
    prefixType: PrefixType;
    customPrefix: string;
    numberPart: string;
}
