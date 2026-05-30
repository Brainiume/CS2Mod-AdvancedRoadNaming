import { ChangeEvent } from "react";
import { FocusDisabled } from "cs2/input";
import { Button, Icon, Tooltip } from "cs2/ui";
import { panelActions } from "bindings";
import { DelayedTooltip } from "components/DelayedTooltip";
import { useAdvancedRoadNamingLocalization } from "localization";
import styles from "./advancedRoadNamingContent.module.scss";

const undergroundIcon = "Media/Tools/Net Tool/Underground.svg";

interface AdvancedRoadNamingContentProps {
    input: string;
    undergroundMode: boolean;
    canUndo: boolean;
    canClear: boolean;
    canApply: boolean;
}

export function AdvancedRoadNamingContent(props: AdvancedRoadNamingContentProps) {
    const { t } = useAdvancedRoadNamingLocalization();

    const onInputChange = (event: ChangeEvent<HTMLInputElement>) => {
        // Accept all characters - validation happens in C#
        panelActions.setInput(event.currentTarget.value);
    };

    return (
        <div className={styles.content}>
            <label className={styles.field}>
                <span className={styles.fieldLabel}>Road Name</span>
                <div className={styles.inputLine}>
                    <input
                        className={styles.textInput}
                        type="text"
                        value={props.input}
                        onChange={onInputChange}
                    />
                    <FocusDisabled>
                        <Tooltip tooltip="Underground mode">
                            <Button
                                id="rst-road-depth-underground"
                                variant="flat"
                                className={`${styles.iconButton} ${props.undergroundMode ? styles.active : ""}`}
                                onSelect={() => panelActions.setUndergroundMode(!props.undergroundMode)}
                            >
                                <Icon src={undergroundIcon} />
                            </Button>
                        </Tooltip>
                    </FocusDisabled>
                </div>
            </label>
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
            <div className={styles.divider} />
            <section className={styles.instructionsSection}>
                <p>
                    Define your route by clicking to place waypoints, then choose your road name and apply it.
                </p>
            </section>
        </div>
    );
}
