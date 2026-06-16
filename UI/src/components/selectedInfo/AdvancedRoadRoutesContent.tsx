import { FocusDisabled } from "cs2/input";
import { Button } from "cs2/ui";
import { panelActions } from "bindings";
import { DelayedTooltip } from "components/DelayedTooltip";
import { RouteSettingsControls } from "components/selectedInfo/RouteSettingsControls";
import { useAdvancedRoadNamingLocalization } from "localization";
import { RouteNumberPlacement } from "types";
import styles from "./advancedRoadRoutesContent.module.scss";

interface AdvancedRoadRoutesContentProps {
    input: string;
    routeNumberPlacement: RouteNumberPlacement;
    savedRouteInputs: string[];
    canUndo: boolean;
    canClear: boolean;
    canApply: boolean;
}

export function AdvancedRoadRoutesContent(props: AdvancedRoadRoutesContentProps) {
    const { t } = useAdvancedRoadNamingLocalization();

    return (
        <div className={styles.content}>
            <div className={styles.wipNotice}>{t("AdvancedRoadNaming.UI[AdvancedRoadRoutesWip]")}</div>
            <RouteSettingsControls
                input={props.input}
                routeNumberPlacement={props.routeNumberPlacement}
                savedRouteInputs={props.savedRouteInputs}
                initialExpanded={true}
                onInputChange={panelActions.setInput}
                onRouteNumberPlacementChange={panelActions.setRouteNumberPlacement}
            />

            <div className={styles.divider} />
            <FocusDisabled>
                <div className={styles.actions}>
                    <div className={styles.actionButtonCell}>
                        <DelayedTooltip tooltip={t("AdvancedRoadNaming.UI[UndoWaypointTooltip]")}>
                            <Button
                                variant="flat"
                                className={styles.actionButton}
                                disabled={!props.canUndo}
                                onSelect={panelActions.removeLast}
                            >
                                Undo Waypoint
                            </Button>
                        </DelayedTooltip>
                    </div>
                    <div className={styles.actionButtonCell}>
                        <DelayedTooltip tooltip={t("AdvancedRoadNaming.UI[ClearTooltip]")}>
                            <Button
                                variant="flat"
                                className={styles.actionButton}
                                disabled={!props.canClear}
                                onSelect={panelActions.clear}
                            >
                                Clear
                            </Button>
                        </DelayedTooltip>
                    </div>
                    <div className={styles.actionButtonCell}>
                        <DelayedTooltip tooltip={t("AdvancedRoadNaming.UI[ApplyTooltip]")}>
                            <Button
                                variant="flat"
                                className={styles.actionButton}
                                disabled={!props.canApply}
                                onSelect={panelActions.apply}
                            >
                                Apply
                            </Button>
                        </DelayedTooltip>
                    </div>
                </div>
            </FocusDisabled>
        </div>
    );
}
