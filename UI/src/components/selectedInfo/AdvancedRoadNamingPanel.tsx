import { ReactNode, useEffect, useRef } from "react";
import { useValue } from "cs2/api";
import { Portal } from "cs2/ui";
import { DelayedTooltip } from "components/DelayedTooltip";
import {
    advancedRoadNamingPanelKind$,
    advancedRoadNamingPanelOpen$,
    advancedRoadRoutesScreen$,
    closeAdvancedRoadNamingPanel,
    panelShortcutCommand$,
    routeStatistics$,
} from "bindings";
import { usePanelState } from "hooks/usePanelState";
import { useBindingValue } from "hooks/useBindingValue";
import { useAdvancedRoadNamingLocalization } from "localization";
import { RouteStatistics } from "types";
import {
    closeButtonClass,
    closeButtonImageClass,
    panelModule,
    selectedInfoThemeModule,
    selectedInfoWrapperClass,
} from "./selectedInfoPanelStyles";
import { AdvancedRoadNamingContent } from "./AdvancedRoadNamingContent";
import { AdvancedRoadRoutesContent } from "./AdvancedRoadRoutesContent";
import { AdvancedRoadRoutesMenu } from "./AdvancedRoadRoutesMenu";
import { ManageRoutesContent } from "./ManageRoutesContent";
import styles from "./advancedRoadPanelShell.module.scss";

interface SelectedInfoAdjacentPanelProps {
    header: string;
    icon: string;
    visible: boolean;
    children: ReactNode;
}

export function AdvancedRoadNamingPanel() {
    const localVisible = useValue(advancedRoadNamingPanelOpen$);
    const panelKind = useValue(advancedRoadNamingPanelKind$);
    const routeScreen = useValue(advancedRoadRoutesScreen$);
    const shortcutCommand = useValue(panelShortcutCommand$);
    const state = usePanelState();
    const backendOpenSeenRef = useRef(false);
    const visible = localVisible && (state.isOpen || !backendOpenSeenRef.current);
    const routeStatistics = useBindingValue<RouteStatistics | null>(routeStatistics$, null);

    useEffect(() => {
        const command = shortcutCommand.split("|", 2)[1];
        if (command === "rename") {
            advancedRoadNamingPanelKind$.update("rename");
            advancedRoadRoutesScreen$.update("menu");
            advancedRoadNamingPanelOpen$.update(true);
        } else if (command === "routes") {
            advancedRoadNamingPanelKind$.update("routes");
            advancedRoadRoutesScreen$.update("menu");
            advancedRoadNamingPanelOpen$.update(true);
        } else if (command === "close") {
            advancedRoadNamingPanelOpen$.update(false);
        }
    }, [shortcutCommand]);

    useEffect(() => {
        if (!localVisible) {
            backendOpenSeenRef.current = false;
            return;
        }

        if (state.isOpen) {
            backendOpenSeenRef.current = true;
            return;
        }

        if (backendOpenSeenRef.current) {
            backendOpenSeenRef.current = false;
            advancedRoadNamingPanelOpen$.update(false);
        }
    }, [localVisible, state.isOpen]);

    useEffect(() => {
        if (!visible) {
            return undefined;
        }

        const closeOnEscape = (event: KeyboardEvent) => {
            if (event.key !== "Escape" && event.key !== "Esc") {
                return;
            }

            event.preventDefault();
            event.stopPropagation();
            closeAdvancedRoadNamingPanel();
        };

        document.addEventListener("keydown", closeOnEscape, true);
        return () => document.removeEventListener("keydown", closeOnEscape, true);
    }, [visible]);

    useEffect(() => {
        if (panelKind === "rename" && !state.savedRenameRoutesEnabled && routeScreen !== "newRoute") {
            advancedRoadRoutesScreen$.update("newRoute");
        }
    }, [panelKind, routeScreen, state.savedRenameRoutesEnabled]);

    if (!visible) {
        return null;
    }

    const isRoutePanel = panelKind === "routes";
    const savedWorkflowEnabled = isRoutePanel || state.savedRenameRoutesEnabled;

    return (
        <SelectedInfoAdjacentPanel
            header={isRoutePanel ? "Advanced Road Routes" : "Advanced Road Naming"}
            icon={isRoutePanel ? "coui://rst/Route.svg" : "coui://rst/PencilEdit.svg"}
            visible={visible}
        >
            {savedWorkflowEnabled && routeScreen === "menu" ? (
                <AdvancedRoadRoutesMenu
                    kind={isRoutePanel ? "routes" : "rename"}
                    applyCooldownActive={state.applyCooldownActive}
                    hasSavedRoutes={state.savedRoutes.length > 0}
                />
            ) : savedWorkflowEnabled && routeScreen === "manageRoutes" ? (
                <ManageRoutesContent
                    kind={isRoutePanel ? "routes" : "rename"}
                    routes={state.savedRoutes}
                    selectedRouteId={state.selectedSavedRouteId}
                    manipulateMode={state.savedRouteManipulateMode}
                    reviewRouteId={state.savedRouteReviewRouteId}
                    showAdvancedRouteDetails={state.showAdvancedRouteDetails}
                    applyCooldownActive={state.applyCooldownActive}
                    statisticsEnabled={state.routeStatisticsEnabled}
                    routeStatistics={routeStatistics}
                />
            ) : isRoutePanel ? (
                <AdvancedRoadRoutesContent
                    input={state.input}
                    routeNumberPlacement={state.routeNumberPlacement}
                    routeShieldStyle={state.routeShieldStyle}
                    savedRouteInputs={state.savedRoutes.map((route) => route.input)}
                    canUndo={state.waypointCount > 0}
                    canClear={state.waypointCount > 0 || state.selectedSegments > 0 || !!state.input}
                    canApply={state.selectedSegments > 0 && !state.applyCooldownActive}
                />
            ) : (
                <AdvancedRoadNamingContent
                    input={state.input}
                    undergroundMode={state.undergroundMode}
                    canUndo={state.waypointCount > 0}
                    canClear={state.waypointCount > 0 || state.selectedSegments > 0 || !!state.input}
                    canApply={state.selectedSegments > 0 && state.input.trim().length > 0 && !state.applyCooldownActive}
                />
            )}
        </SelectedInfoAdjacentPanel>
    );
}

function SelectedInfoAdjacentPanel(props: SelectedInfoAdjacentPanelProps) {
    const { t } = useAdvancedRoadNamingLocalization();

    if (!props.visible) {
        return null;
    }

    return (
        <Portal>
            <div className={styles.panelStack}>
                <div
                    id="rst-advanced-road-naming-panel"
                    className={`${selectedInfoWrapperClass} ${styles.panel}`}
                >
                    <div className={selectedInfoThemeModule.header}>
                        <div className={panelModule.titleBar}>
                            <img className={panelModule.icon} src={props.icon} />
                            <div className={selectedInfoThemeModule.title}>{props.header}</div>
                            <DelayedTooltip tooltip={t("AdvancedRoadNaming.UI[ClosePanelTooltip]")} direction="left">
                                <button className={closeButtonClass} onClick={closeAdvancedRoadNamingPanel}>
                                    <div
                                        className={closeButtonImageClass}
                                        style={{ maskImage: "url(Media/Glyphs/Close.svg)" }}
                                    />
                                </button>
                            </DelayedTooltip>
                        </div>
                    </div>
                    <div className={`${selectedInfoThemeModule.content} ${styles.panelContent}`}>
                        <div className={styles.body}>{props.children}</div>
                    </div>
                </div>
            </div>
        </Portal>
    );
}
