import React, { useEffect, useMemo, useRef, useState } from "react";
import { useValue } from "cs2/api";
import { useRem } from "cs2/utils";
import { routeShieldOverlays$ } from "bindings";
import { RouteShieldOverlayItem } from "types";
import { RouteShieldArtwork } from "./RouteShieldPreview";
import { findShieldDefinition, useRouteShieldCatalog } from "./shieldCatalog";
import styles from "./routeShieldOverlay.module.scss";
import { isAustralianRectangle, rectangleWidth, selectNonOverlappingShields } from "./routeShieldLayout";

const MemoizedArtwork = React.memo(RouteShieldArtwork);

export function RouteShieldOverlay() {
    const shields = useValue(routeShieldOverlays$);
    const shieldCatalog = useRouteShieldCatalog();
    const rem = useRem();
    const visibleShields = useMemo(() => selectNonOverlappingShields(shields, shieldCatalog, rem), [shields, shieldCatalog, rem]);
    const [cameraMoving, setCameraMoving] = useState(true);
    const settleTimer = useRef<number | undefined>(undefined);
    const positionSignature = useMemo(
        () => shields.map((shield) => `${shield.id}:${Math.round(shield.left * 2)}:${Math.round(shield.top * 2)}`).join("|"),
        [shields],
    );

    useEffect(() => {
        setCameraMoving(true);
        if (settleTimer.current !== undefined) {
            window.clearTimeout(settleTimer.current);
        }
        settleTimer.current = window.setTimeout(() => {
            settleTimer.current = undefined;
            setCameraMoving(false);
        }, 580);
        return () => {
            if (settleTimer.current !== undefined) {
                window.clearTimeout(settleTimer.current);
            }
        };
    }, [positionSignature]);

    if (!shields.length) {
        return null;
    }

    return (
        <div className={`${styles.overlay} ${cameraMoving ? styles.cameraMoving : ""}`} aria-hidden={true}>
            {visibleShields.map((shield) => <RouteShield key={shield.id} shield={shield} catalog={shieldCatalog} />)}
        </div>
    );
}

function RouteShield({ shield, catalog }: { shield: RouteShieldOverlayItem; catalog: ReturnType<typeof useRouteShieldCatalog> }) {
    const presetScale = Number.isFinite(shield.scale) ? shield.scale : 1;
    if (isAustralianRectangle(shield.style)) {
        return (
            <div
                className={`${styles.shield} ${styles.rectangle} ${shield.occluded ? styles.occluded : ""}`}
                style={{
                    transform: `translate(${shield.left}px, ${shield.top}px) translate(-50%, -50%) scale(${presetScale})`,
                    width: `${rectangleWidth(shield.label)}rem`,
                }}
            >
                <div className={styles.label}>{shield.label}</div>
            </div>
        );
    }

    const definition = findShieldDefinition(shield.style, catalog);
    if (!definition) {
        return null;
    }

    const surfaceScale = resolveWorldSurfaceScale(presetScale);

    return (
        <div
            className={`${styles.shield} ${shield.occluded ? styles.occluded : ""}`}
            style={{
                width: `${definition.widthRem}rem`,
                height: `${definition.heightRem}rem`,
                transform: `translate(${shield.left}px, ${shield.top}px) translate(-50%, -50%) scale(${presetScale})`,
            }}
        >
            <MemoizedArtwork definition={definition} routeCode={shield.label} surfaceScale={surfaceScale} />
        </div>
    );
}

function resolveWorldSurfaceScale(displayScale: number): 2 | 3 {
    return displayScale > 1 ? 3 : 2;
}
