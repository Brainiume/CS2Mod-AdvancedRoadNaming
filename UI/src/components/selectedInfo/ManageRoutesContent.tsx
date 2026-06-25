import { FOCUS_AUTO, FocusDisabled } from "cs2/input";
import { Button, PanelFoldout, PanelSectionRow } from "cs2/ui";
import { panelActions } from "bindings";
import { DelayedTooltip } from "components/DelayedTooltip";
import { RouteSettingsControls } from "components/selectedInfo/RouteSettingsControls";
import { RouteNumberPlacement, SavedRoute } from "types";
import styles from "./advancedRoadRoutesContent.module.scss";

interface ManageRoutesContentProps {
    routes: SavedRoute[];
    selectedRouteId: number;
    manipulateMode: boolean;
    reviewRouteId: number;
    showAdvancedRouteDetails: boolean;
    applyCooldownActive: boolean;
}

export function ManageRoutesContent(props: ManageRoutesContentProps) {
    const selectedRoute = props.routes.find((route) => route.id === props.selectedRouteId);
    const editRouteId = props.reviewRouteId || props.selectedRouteId;
    const hasSelection = !!selectedRoute;
    const savedRouteInputs = props.routes.map((route) => route.routeCode || route.input || "").filter(Boolean);
    const deleteSelectedRoute = () => {
        if (selectedRoute) {
            panelActions.deleteSavedRoute(selectedRoute.id);
        }
    };

    return (
        <div className={`${styles.content} ${styles.manageContent}`}>
            <div className={styles.manageHeader}>
                <Button variant="flat" focusKey={FOCUS_AUTO} className={styles.smallActionButton} onSelect={panelActions.backToRouteMenu}>
                    Back
                </Button>
                <div className={styles.manageHint}>
                    {props.routes.length === 0 ? "No saved routes found." : "Select a route in the world or from the list."}
                </div>
            </div>

            <FocusDisabled>
                <div className={styles.routeList}>
                    {props.routes.map((route) => (
                        <button
                            key={route.id}
                            className={`${styles.routeListItem} ${route.id === props.selectedRouteId ? styles.routeListItemSelected : ""}`}
                            onClick={() => panelActions.selectSavedRoute(route.id)}
                        >
                            <RouteTitle route={route} />
                        </button>
                    ))}
                </div>
            </FocusDisabled>

            {selectedRoute ? (
                <div className={styles.selectedRouteSummary}>
                    <div className={styles.selectedRouteTitle}>
                        <RouteTitle route={selectedRoute} />
                    </div>

                    <PanelFoldout
                        header={<PanelSectionRow uppercase={true} disableFocus={true} left="Route Settings" />}
                        initialExpanded={false}
                        focusKey={FOCUS_AUTO}
                    >
                        <RouteSettingsControls
                            input={selectedRoute.routeCode || selectedRoute.input || ""}
                            routeNumberPlacement={selectedRoute.routeNumberPlacement || "AfterBaseName"}
                            savedRouteInputs={savedRouteInputs}
                            initialExpanded={true}
                            onInputChange={(value) => panelActions.updateSavedRouteInput(selectedRoute.id, value)}
                            onRouteNumberPlacementChange={(value: RouteNumberPlacement) => panelActions.updateSavedRoutePlacement(selectedRoute.id, value)}
                        />
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
                                <PanelSectionRow disableFocus={true} subRow={true} left="Placement" right={selectedRoute.routeNumberPlacement || "-"} />
                                <PanelSectionRow disableFocus={true} subRow={true} left="Updated" right={selectedRoute.updated || "-"} />
                            </>
                        )}
                    </PanelFoldout>
                </div>
            ) : (
                <div className={styles.noSelection}>Select a route in the world or from the list.</div>
            )}

            <FocusDisabled>
                <div className={styles.actions}>
                    <div className={styles.actionButtonCell}>
                        <DelayedTooltip tooltip="Apply this route to the city. If Manipulate is on, the current edits are kept and applied now.">
                            <Button
                                variant="flat"
                                className={styles.actionButton}
                                disabled={!hasSelection || props.applyCooldownActive}
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
                                onSelect={() => panelActions.toggleManipulateRoute(editRouteId, !props.manipulateMode)}
                            >
                                Manipulate
                            </Button>
                        </DelayedTooltip>
                    </div>
                    <div className={styles.actionButtonCell}>
                        <DelayedTooltip tooltip="Delete this saved route and remove its route number from affected roads.">
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
