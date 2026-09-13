import { useEffect, useMemo, useState } from "react";
import { FOCUS_AUTO, FocusDisabled } from "cs2/input";
import { Button, PanelFoldout, PanelSectionRow, Scrollable } from "cs2/ui";
import { panelActions } from "bindings";
import { DelayedTooltip } from "components/DelayedTooltip";
import { findShieldDefinition, useRouteShieldCatalog } from "components/routeShields/shieldCatalog";
import { RouteSettingsControls } from "components/selectedInfo/RouteSettingsControls";
import { RoadNameTextInput } from "components/selectedInfo/RoadNameTextInput";
import { resolveTrafficCharts } from "components/vanilla/Components";
import { useAdvancedRoadNamingLocalization } from "localization";
import { RouteNumberPlacement, RouteShieldSelection, RouteStatistics, SavedRoute } from "types";
import styles from "./advancedRoadRoutesContent.module.scss";

interface ManageRoutesContentProps {
    kind: "rename" | "routes";
    routes: SavedRoute[];
    selectedRouteId: number;
    manipulateMode: boolean;
    reviewRouteId: number;
    showAdvancedRouteDetails: boolean;
    applyCooldownActive: boolean;
    statisticsEnabled: boolean;
    routeStatistics: RouteStatistics | null;
}

type RouteListFilter = "All" | "M" | "A" | "B" | "C" | "I";
const ROUTE_LIST_FILTERS: RouteListFilter[] = ["All", "M", "A", "B", "C", "I"];

export function ManageRoutesContent(props: ManageRoutesContentProps) {
    const { t } = useAdvancedRoadNamingLocalization();
    const shieldCatalog = useRouteShieldCatalog();
    const selectedRoute = props.routes.find((route) => route.id === props.selectedRouteId);
    const renameMode = props.kind === "rename";
    const selectedRouteId = selectedRoute?.id ?? 0;
    const [statisticsExpanded, setStatisticsExpanded] = useState(false);
    const [statisticsChartsAvailable, setStatisticsChartsAvailable] = useState<boolean | null>(null);
    const [cleanupPromptRouteId, setCleanupPromptRouteId] = useState(0);
    const [routeListFilter, setRouteListFilter] = useState<RouteListFilter>("All");
    const [districtFilter, setDistrictFilter] = useState("all");
    const districtFilters = useMemo(() => {
        const districts = new Map<string, string>();
        props.routes.forEach(route => route.districts?.forEach(district => districts.set(district.id, district.name)));
        return [
            { id: "all", name: "All" },
            { id: "none", name: "None" },
            ...Array.from(districts, ([id, name]) => ({ id, name }))
                .sort((a, b) => a.name.localeCompare(b.name) || a.id.localeCompare(b.id)),
        ];
    }, [props.routes]);
    const activeDistrictFilter = districtFilters.some(filter => filter.id === districtFilter) ? districtFilter : "all";
    useEffect(() => {
        if (districtFilter !== activeDistrictFilter) setDistrictFilter(activeDistrictFilter);
    }, [districtFilter, activeDistrictFilter]);
    const [renameNameDraft, setRenameNameDraft] = useState(selectedRoute?.input || "");
    const editRouteId = props.reviewRouteId || props.selectedRouteId;
    const hasSelection = !!selectedRoute;
    const orphanWaypointCount = selectedRoute?.orphanWaypointCount ?? 0;
    const showCleanupPrompt = !!selectedRoute && cleanupPromptRouteId === selectedRoute.id && orphanWaypointCount > 0;
    const cleanupWarningText = orphanWaypointCount === 1
        ? "This route has 1 waypoint anchor on a missing road segment. Clean up that waypoint before manipulating it."
        : `This route has ${orphanWaypointCount} waypoint anchors on missing road segments. Clean up those waypoints before manipulating it.`;
    const savedRouteInputs = props.routes.map((route) => route.routeCode || route.input || "").filter(Boolean);
    const filteredRoutes = renameMode
        ? props.routes.filter(route => activeDistrictFilter === "all"
            || (activeDistrictFilter === "none" ? (route.districts?.length ?? 0) === 0
                : route.districts?.some(district => district.id === activeDistrictFilter)))
        : routeListFilter === "All" ? props.routes : props.routes.filter(route => routePrefix(route) === routeListFilter);
    const canReapply = hasSelection && (!renameMode || renameNameDraft.trim().length > 0);
    const deleteSelectedRoute = () => {
        if (selectedRoute) {
            panelActions.deleteSavedRoute(selectedRoute.id);
        }
    };

    useEffect(() => {
        if (!props.statisticsEnabled && statisticsExpanded) {
            setStatisticsExpanded(false);
        }
    }, [props.statisticsEnabled, statisticsExpanded]);

    useEffect(() => {
        if (cleanupPromptRouteId !== 0 && (!selectedRoute || orphanWaypointCount <= 0 || cleanupPromptRouteId !== selectedRoute.id)) {
            setCleanupPromptRouteId(0);
        }
    }, [cleanupPromptRouteId, orphanWaypointCount, selectedRoute]);

    useEffect(() => {
        setRenameNameDraft(selectedRoute?.input || "");
    }, [selectedRoute?.id, selectedRoute?.input]);

    useEffect(() => {
        if (!props.statisticsEnabled || !statisticsExpanded || statisticsChartsAvailable !== true || selectedRouteId <= 0) {
            return undefined;
        }

        panelActions.setRouteStatisticsExpanded(selectedRouteId, true);
        return () => panelActions.setRouteStatisticsExpanded(selectedRouteId, false);
    }, [props.statisticsEnabled, selectedRouteId, statisticsChartsAvailable, statisticsExpanded]);

    const toggleStatistics = (expanded: boolean) => {
        if (expanded) {
            setStatisticsChartsAvailable(resolveTrafficCharts() !== null);
        }
        setStatisticsExpanded(expanded);
    };

    return (
        <div className={`${styles.content} ${styles.manageContent} ${renameMode ? styles.renameVariant : ""}`}>
            <div className={styles.manageHeader}>
                <Button variant="flat" focusKey={FOCUS_AUTO} className={styles.smallActionButton} onSelect={renameMode ? panelActions.backToRenameMenu : panelActions.backToRouteMenu}>
                    Back
                </Button>
                <div className={styles.manageHint}>
                    {props.routes.length === 0 ? (renameMode ? "No saved rename routes found." : "No saved routes found.") : (renameMode ? "Select a rename route in the world or from the list." : "Select a route in the world or from the list.")}
                </div>
            </div>

            <FocusDisabled>
                {renameMode && <Scrollable horizontal={true} vertical={false} trackVisibility="scrollable" className={styles.districtFilterScroll}>
                    <div className={styles.routeFilterRow}>
                        {districtFilters.map(filter => (
                            <Button key={filter.id} variant="flat" focusKey={FOCUS_AUTO}
                                className={`${styles.routeFilterChip} ${styles.districtFilterChip} ${activeDistrictFilter === filter.id ? styles.routeFilterChipSelected : ""}`}
                                onSelect={() => setDistrictFilter(filter.id)}>
                                {filter.name}
                            </Button>
                        ))}
                    </div>
                </Scrollable>}
                {!renameMode && <div className={styles.routeFilterRow}>
                    {ROUTE_LIST_FILTERS.map((filter) => (
                        <Button
                            key={filter}
                            variant="flat"
                            focusKey={FOCUS_AUTO}
                            className={`${styles.routeFilterChip} ${routeListFilter === filter ? styles.routeFilterChipSelected : ""}`}
                            onSelect={() => setRouteListFilter(filter)}
                        >
                            {filter}
                        </Button>
                    ))}
                </div>}
                <div className={styles.routeList}>
                    {filteredRoutes.map((route) => (
                        <button
                            key={route.id}
                            className={`${styles.routeListItem} ${route.id === props.selectedRouteId ? styles.routeListItemSelected : ""}`}
                            onClick={() => panelActions.selectSavedRoute(route.id)}
                        >
                            <RouteTitle route={route} />
                        </button>
                    ))}
                    {props.routes.length > 0 && filteredRoutes.length === 0 && (
                        <div className={styles.routeFilterEmpty}>Nothing here...</div>
                    )}
                </div>
            </FocusDisabled>

            {selectedRoute ? (
                <div className={styles.selectedRouteSummary}>
                    <div className={styles.selectedRouteTitle}>
                        <RouteTitle route={selectedRoute} />
                    </div>

                    {showCleanupPrompt && (
                        <div className={styles.cleanupWarning}>
                            <div className={styles.cleanupWarningText}>
                                {cleanupWarningText}
                            </div>
                            <div className={styles.cleanupWarningActions}>
                                <Button
                                    variant="flat"
                                    focusKey={FOCUS_AUTO}
                                    className={styles.cleanupWarningButton}
                                    onSelect={() => {
                                        setCleanupPromptRouteId(0);
                                        panelActions.cleanupSavedRouteWaypoints(selectedRoute.id);
                                    }}
                                >
                                    Clean Up
                                </Button>
                                <Button
                                    variant="flat"
                                    focusKey={FOCUS_AUTO}
                                    className={styles.cleanupWarningButton}
                                    onSelect={() => setCleanupPromptRouteId(0)}
                                >
                                    Cancel
                                </Button>
                            </div>
                        </div>
                    )}

                    <PanelFoldout
                        header={<PanelSectionRow uppercase={true} disableFocus={true} left={renameMode ? "Rename Settings" : "Route Settings"} />}
                        initialExpanded={false}
                        focusKey={FOCUS_AUTO}
                    >
                        {renameMode ? (
                            <PanelSectionRow
                                disableFocus={true}
                                subRow={true}
                                left="Road Name"
                                right={(
                                    <div className={styles.sectionInputLine}>
                                        <RoadNameTextInput
                                            className={`${styles.textInput} ${styles.sectionTextInput}`}
                                            debugName="AdvancedRoadNamingSavedRenameName"
                                            value={selectedRoute.input || ""}
                                            allowEmpty={false}
                                            onDraftChange={setRenameNameDraft}
                                            onChange={(value) => panelActions.updateSavedRouteInput(selectedRoute.id, value)}
                                        />
                                    </div>
                                )}
                            />
                        ) : (
                            <RouteSettingsControls
                                input={selectedRoute.routeCode || selectedRoute.input || ""}
                                routeNumberPlacement={selectedRoute.routeNumberPlacement || "AfterBaseName"}
                                routeShieldStyle={selectedRoute.routeShieldStyle || "None"}
                                savedRouteInputs={savedRouteInputs}
                                initialExpanded={true}
                                onInputChange={(value) => panelActions.updateSavedRouteInput(selectedRoute.id, value)}
                                onRouteNumberPlacementChange={(value: RouteNumberPlacement) => panelActions.updateSavedRoutePlacement(selectedRoute.id, value)}
                                onRouteShieldStyleChange={(value: RouteShieldSelection) => panelActions.updateSavedRouteShieldStyle(selectedRoute.id, value)}
                            />
                        )}
                    </PanelFoldout>

                    <PanelFoldout
                        header={<PanelSectionRow uppercase={true} disableFocus={true} left="Route Info" />}
                        initialExpanded={false}
                        focusKey={FOCUS_AUTO}
                    >
                        <PanelSectionRow disableFocus={true} subRow={true} left="Start district" right={selectedRoute.startDistrictName || "-"} />
                        <PanelSectionRow disableFocus={true} subRow={true} left="End district" right={selectedRoute.endDistrictName || "-"} />
                        <PanelSectionRow disableFocus={true} subRow={true} left="Roads" right={selectedRoute.streets || selectedRoute.startRoadName || "-"} />
                        {props.showAdvancedRouteDetails && (
                            <>
                                <PanelSectionRow disableFocus={true} subRow={true} left="Status" right={selectedRoute.status} />
                                <PanelSectionRow disableFocus={true} subRow={true} left="Segments" right={selectedRoute.segments.toString()} />
                                <PanelSectionRow disableFocus={true} subRow={true} left="Waypoints" right={selectedRoute.waypoints.toString()} />
                                {!renameMode && <PanelSectionRow disableFocus={true} subRow={true} left="Placement" right={selectedRoute.routeNumberPlacement || "-"} />}
                                {!renameMode && <PanelSectionRow disableFocus={true} subRow={true} left="Shield" right={shieldStyleLabel(selectedRoute.routeShieldStyle || "None", shieldCatalog)} />}
                                <PanelSectionRow disableFocus={true} subRow={true} left="Updated" right={selectedRoute.updated || "-"} />
                            </>
                        )}
                    </PanelFoldout>

                    {!renameMode && props.statisticsEnabled && (
                        <PanelFoldout
                            header={<PanelSectionRow uppercase={true} disableFocus={true} left={t("AdvancedRoadNaming.UI[RouteStatistics]")} />}
                            initialExpanded={false}
                            focusKey={FOCUS_AUTO}
                            onToggleExpanded={toggleStatistics}
                        >
                            {statisticsExpanded && (
                                <RouteStatisticsContent
                                    routeId={selectedRoute.id}
                                    statistics={props.routeStatistics}
                                />
                            )}
                        </PanelFoldout>
                    )}
                </div>
            ) : (
                <div className={styles.noSelection}>{renameMode ? "Select a rename route in the world or from the list." : "Select a route in the world or from the list."}</div>
            )}

            <FocusDisabled>
                <div className={styles.actions}>
                    <div className={styles.actionButtonCell}>
                        <DelayedTooltip tooltip={renameMode ? "Apply this saved road name to the city. If Manipulate is on, the current geometry edits are kept and applied now." : "Apply this route to the city. If Manipulate is on, the current edits are kept and applied now."}>
                            <Button
                                variant="flat"
                                className={styles.actionButton}
                                disabled={!canReapply || props.applyCooldownActive}
                                onSelect={() => selectedRoute && panelActions.reapplySavedRoute(selectedRoute.id)}
                            >
                                Reapply
                            </Button>
                        </DelayedTooltip>
                    </div>
                    <div className={styles.actionButtonCell}>
                        <DelayedTooltip tooltip="Toggle route editing. Edits are temporary unless you click Reapply; closing the menu or selecting another route discards them.">
                            <Button
                                variant="flat"
                                className={`${styles.actionButton} ${props.manipulateMode ? styles.activeActionButton : ""}`}
                                disabled={!hasSelection && !props.manipulateMode}
                                onSelect={() => {
                                    if (!props.manipulateMode && selectedRoute && orphanWaypointCount > 0) {
                                        setCleanupPromptRouteId(selectedRoute.id);
                                        return;
                                    }

                                    panelActions.toggleManipulateRoute(editRouteId, !props.manipulateMode);
                                }}
                            >
                                Manipulate
                            </Button>
                        </DelayedTooltip>
                    </div>
                    <div className={styles.actionButtonCell}>
                        <DelayedTooltip tooltip={renameMode ? "Rebuild this saved rename corridor from its waypoint anchors. Review the purple preview, then click Reapply to commit it." : "Rebuild this saved route from its waypoint anchors. Review the preview, then click Reapply to commit it."}>
                            <Button
                                variant="flat"
                                className={`${styles.actionButton} ${props.reviewRouteId === selectedRouteId && !props.manipulateMode ? styles.activeActionButton : ""}`}
                                disabled={!hasSelection || props.manipulateMode}
                                onSelect={() => selectedRoute && panelActions.rebuildSavedRoute(selectedRoute.id)}
                            >
                                Rebuild
                            </Button>
                        </DelayedTooltip>
                    </div>
                    <div className={styles.actionButtonCell}>
                        <DelayedTooltip tooltip={renameMode ? "Delete this saved rename route and restore the next saved rename or original road name." : "Delete this saved route and remove its route number from affected roads."}>
                            <Button
                                variant="flat"
                                className={styles.actionButton}
                                disabled={!hasSelection}
                                onClick={deleteSelectedRoute}
                                onSelect={deleteSelectedRoute}
                            >
                                Delete
                            </Button>
                        </DelayedTooltip>
                    </div>
                </div>
            </FocusDisabled>
        </div>
    );
}

function routePrefix(route: SavedRoute): string {
    const structuredPrefix = (route.routePrefixType || "").toUpperCase();
    if (structuredPrefix === "M" || structuredPrefix === "A" || structuredPrefix === "B" || structuredPrefix === "C" || structuredPrefix === "I") {
        return structuredPrefix;
    }

    const code = (route.routeCode || route.input || "").trim().toUpperCase();
    const firstCharacter = code.charAt(0);
    return firstCharacter === "M" || firstCharacter === "A" || firstCharacter === "B" || firstCharacter === "C" || firstCharacter === "I"
        ? firstCharacter
        : "";
}

function RouteStatisticsContent(props: { routeId: number; statistics: RouteStatistics | null }) {
    const { t } = useAdvancedRoadNamingLocalization();
    const [volumeMetric, setVolumeMetric] = useState<"busy" | "load">("busy");
    const [flowMetric, setFlowMetric] = useState<"weighted" | "bottleneck" | "congested">("weighted");
    const charts = resolveTrafficCharts();
    const statistics = props.statistics?.routeId === props.routeId ? props.statistics : null;

    if (!charts) {
        return <div className={styles.statisticsStatus}>{t("AdvancedRoadNaming.UI[RouteStatisticsUnavailable]")}</div>;
    }

    if (!statistics) {
        return <div className={styles.statisticsStatus}>{t("AdvancedRoadNaming.UI[RouteStatisticsLoading]")}</div>;
    }

    if (statistics.validSegmentCount === 0) {
        return <div className={styles.statisticsStatus}>{t("AdvancedRoadNaming.UI[RouteStatisticsNoValidRoads]")}</div>;
    }

    const excludedWarning = t("AdvancedRoadNaming.UI[RouteStatisticsExcludedRoads]")
        .replace("{EXCLUDED}", statistics.excludedSegmentCount.toString())
        .replace("{TOTAL}", statistics.storedSegmentCount.toString());
    const { TrafficFlowChart, TrafficVolumeChart } = charts;
    const volumeData = volumeMetric === "load"
        ? statistics.corridorLoadData
        : statistics.busyVolumeData;
    const volumeLabel = volumeMetric === "load"
        ? t("AdvancedRoadNaming.UI[RouteStatisticsCorridorLoad]")
        : t("AdvancedRoadNaming.UI[RouteStatisticsBusyVolume]");
    const flowData = flowMetric === "bottleneck"
        ? statistics.bottleneckFlowData
        : flowMetric === "congested"
            ? statistics.congestedDistanceData
            : statistics.volumeWeightedFlowData;
    const flowLabel = flowMetric === "bottleneck"
        ? t("AdvancedRoadNaming.UI[RouteStatisticsBottleneckFlow]")
        : flowMetric === "congested"
            ? t("AdvancedRoadNaming.UI[RouteStatisticsCongestedDistance]")
            : t("AdvancedRoadNaming.UI[RouteStatisticsVolumeWeightedFlow]");

    return (
        <div className={styles.statisticsContent}>
            {statistics.excludedSegmentCount > 0 && (
                <div className={styles.statisticsWarning}>{excludedWarning}</div>
            )}
            <div className={`${styles.routeButtonRow} ${styles.statisticsMetricRow}`}>
                <StatisticsMetricButton
                    label={t("AdvancedRoadNaming.UI[RouteStatisticsVolumeWeightedFlow]")}
                    tooltip={t("AdvancedRoadNaming.UI[RouteStatisticsVolumeWeightedFlowTooltip]")}
                    active={flowMetric === "weighted"}
                    onSelect={() => setFlowMetric("weighted")}
                />
                <StatisticsMetricButton
                    label={t("AdvancedRoadNaming.UI[RouteStatisticsBottleneckFlow]")}
                    tooltip={t("AdvancedRoadNaming.UI[RouteStatisticsBottleneckFlowTooltip]")}
                    active={flowMetric === "bottleneck"}
                    onSelect={() => setFlowMetric("bottleneck")}
                />
                <StatisticsMetricButton
                    label={t("AdvancedRoadNaming.UI[RouteStatisticsCongestedDistance]")}
                    tooltip={t("AdvancedRoadNaming.UI[RouteStatisticsCongestedDistanceTooltip]")}
                    active={flowMetric === "congested"}
                    onSelect={() => setFlowMetric("congested")}
                />
            </div>
            <PanelSectionRow uppercase={true} disableFocus={true} left={flowLabel} />
            <TrafficFlowChart data={flowData} />
            <div className={`${styles.routeButtonRow} ${styles.statisticsMetricRow}`}>
                <StatisticsMetricButton
                    label={t("AdvancedRoadNaming.UI[RouteStatisticsBusyVolume]")}
                    tooltip={t("AdvancedRoadNaming.UI[RouteStatisticsBusyVolumeTooltip]")}
                    active={volumeMetric === "busy"}
                    onSelect={() => setVolumeMetric("busy")}
                />
                <StatisticsMetricButton
                    label={t("AdvancedRoadNaming.UI[RouteStatisticsCorridorLoad]")}
                    tooltip={t("AdvancedRoadNaming.UI[RouteStatisticsCorridorLoadTooltip]")}
                    active={volumeMetric === "load"}
                    onSelect={() => setVolumeMetric("load")}
                />
            </div>
            <PanelSectionRow
                uppercase={true}
                disableFocus={true}
                left={volumeLabel}
            />
            <TrafficVolumeChart data={volumeData} />
        </div>
    );
}

function StatisticsMetricButton(props: { label: string; tooltip: string; active: boolean; onSelect: () => void }) {
    return (
        <div className={styles.routeButtonCell}>
            <DelayedTooltip tooltip={props.tooltip}>
                <Button
                    variant="flat"
                    focusKey={FOCUS_AUTO}
                    className={`${styles.routeChoiceButton} ${props.active ? styles.active : ""}`}
                    onSelect={props.onSelect}
                >
                    {props.label}
                </Button>
            </DelayedTooltip>
        </div>
    );
}

function RouteTitle(props: { route: SavedRoute }) {
    const parts = splitRouteTitle(props.route);
    if (!parts.code) {
        return <span>{parts.name}</span>;
    }

    return (
        <span>
            <strong>{parts.code}</strong> {parts.name}
        </span>
    );
}

function splitRouteTitle(route: SavedRoute): { code: string; name: string } {
    if (route.mode === "RenameSelectedSegments") {
        const name = (route.title || route.input || "").trim();
        return { code: "", name: name || "Unnamed rename route" };
    }

    const code = (route.routeCode || route.input || "").trim();
    const rawTitle = (route.title || "").trim();
    let name = rawTitle || "Unnamed route";

    if (code && name.toUpperCase().startsWith(code.toUpperCase())) {
        name = name.substring(code.length).trim();
    }

    return {
        code,
        name: name || rawTitle || "Unnamed route",
    };
}

function shieldStyleLabel(style: RouteShieldSelection, catalog: ReturnType<typeof useRouteShieldCatalog>): string {
    if (style.startsWith("Imported:")) {
        return "Custom shield";
    }
    const catalogShield = findShieldDefinition(style, catalog);
    if (catalogShield) {
        return catalogShield.name;
    }

    switch (style) {
        case "AustralianMRectangle":
        case "AustralianARectangle":
        case "AustralianBRectangle":
        case "AustralianCRectangle":
            return "Australian rectangle";
        case "AustralianNationalShield":
            return "National shield";
        case "BlueHighwayShield":
            return "Blue highway shield";
        case "BlackWhiteShield":
            return "Black-white shield";
        case "InterstateGeneric":
        case "InterstateState":
            return "Interstate";
        default:
            return "No shield";
    }
}
