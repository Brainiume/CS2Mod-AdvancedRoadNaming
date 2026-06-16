import { FOCUS_AUTO, FocusDisabled } from "cs2/input";
import { Button } from "cs2/ui";
import { panelActions } from "bindings";
import { useAdvancedRoadNamingLocalization } from "localization";
import styles from "./advancedRoadRoutesContent.module.scss";

export function AdvancedRoadRoutesMenu() {
    const { t } = useAdvancedRoadNamingLocalization();

    return (
        <FocusDisabled>
            <div className={`${styles.content} ${styles.menuContent}`}>
                <div className={styles.wipNotice}>{t("AdvancedRoadNaming.UI[AdvancedRoadRoutesWip]")}</div>
                <div className={styles.routeMenuCards}>
                    <Button
                        variant="flat"
                        focusKey={FOCUS_AUTO}
                        className={styles.routeMenuCard}
                        onSelect={panelActions.startNewRoute}
                    >
                        <div className={styles.routeMenuCardInner}>
                            <img className={styles.routeMenuIcon} src="coui://rst/Route.svg" />
                            <div className={styles.routeMenuLabel}>New Route</div>
                        </div>
                    </Button>
                    <Button
                        variant="flat"
                        focusKey={FOCUS_AUTO}
                        className={styles.routeMenuCard}
                        onSelect={panelActions.openManageRoutes}
                    >
                        <div className={styles.routeMenuCardInner}>
                            <img className={styles.routeMenuIcon} src="coui://rst/Manage.svg" />
                            <div className={styles.routeMenuLabel}>Manage Routes</div>
                        </div>
                    </Button>
                </div>
            </div>
        </FocusDisabled>
    );
}
