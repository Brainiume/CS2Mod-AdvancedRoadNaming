import { FOCUS_AUTO, FocusDisabled } from "cs2/input";
import { Button } from "cs2/ui";
import { panelActions } from "bindings";
import { DelayedTooltip } from "components/DelayedTooltip";
import { useAdvancedRoadNamingLocalization } from "localization";
import styles from "./advancedRoadRoutesContent.module.scss";

interface AdvancedRoadRoutesMenuProps {
    kind: "rename" | "routes";
    applyCooldownActive: boolean;
    hasSavedRoutes: boolean;
}

export function AdvancedRoadRoutesMenu(props: AdvancedRoadRoutesMenuProps) {
    const { t } = useAdvancedRoadNamingLocalization();
    const renameMode = props.kind === "rename";

    return (
        <FocusDisabled>
            <div className={`${styles.content} ${styles.menuContent} ${renameMode ? styles.renameVariant : ""}`}>
                <div className={styles.wipNotice}>{renameMode ? "SAVED RENAME ROUTES" : t("AdvancedRoadNaming.UI[AdvancedRoadRoutesWip]")}</div>
                <div className={styles.routeMenuCards}>
                    <Button
                        variant="flat"
                        focusKey={FOCUS_AUTO}
                        className={styles.routeMenuCard}
                        onSelect={renameMode ? panelActions.startNewRenameRoute : panelActions.startNewRoute}
                    >
                        <div className={styles.routeMenuCardInner}>
                            <img className={styles.routeMenuIcon} src={renameMode ? "coui://rst/PencilEdit.svg" : "coui://rst/Route.svg"} />
                            <div className={styles.routeMenuLabel}>{renameMode ? "New Rename Route" : "New Route"}</div>
                        </div>
                    </Button>
                    <Button
                        variant="flat"
                        focusKey={FOCUS_AUTO}
                        className={styles.routeMenuCard}
                        onSelect={renameMode ? panelActions.openManageRenames : panelActions.openManageRoutes}
                    >
                        <div className={styles.routeMenuCardInner}>
                            <img className={styles.routeMenuIcon} src="coui://rst/Manage.svg" />
                            <div className={styles.routeMenuLabel}>{renameMode ? "Manage Rename Routes" : "Manage Routes"}</div>
                        </div>
                    </Button>
                </div>
                <div className={styles.routeMenuWideActionRow}>
                    <DelayedTooltip tooltip={renameMode ? "Reapply every saved rename route to the current road network. Rename routes with missing roads are skipped." : "Reapply every saved route to the current road network. Routes with missing roads are skipped."}>
                        <Button
                            variant="flat"
                            focusKey={FOCUS_AUTO}
                            className={styles.routeMenuWideAction}
                            disabled={!props.hasSavedRoutes || props.applyCooldownActive}
                            onSelect={panelActions.reapplyAllSavedRoutes}
                        >
                            {renameMode ? "Reapply All Saved Rename Routes" : "Reapply All Saved Routes"}
                        </Button>
                    </DelayedTooltip>
                </div>
            </div>
        </FocusDisabled>
    );
}
