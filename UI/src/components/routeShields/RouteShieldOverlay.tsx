import React, { useEffect, useMemo, useRef, useState } from "react";
import { useValue } from "cs2/api";
import { routeShieldOverlays$ } from "bindings";
import { RouteShieldOverlayItem } from "types";
import { RouteShieldArtwork } from "./RouteShieldPreview";
import { findShieldDefinition, useRouteShieldCatalog } from "./shieldCatalog";
import styles from "./routeShieldOverlay.module.scss";

export function RouteShieldOverlay() {
    const shields = useValue(routeShieldOverlays$);
    const shieldCatalog = useRouteShieldCatalog();
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
            {shields.map((shield) => <RouteShield key={shield.id} shield={shield} catalog={shieldCatalog} />)}
        </div>
    );
}

function RouteShield({ shield, catalog }: { shield: RouteShieldOverlayItem; catalog: ReturnType<typeof useRouteShieldCatalog> }) {
    const presetScale = Number.isFinite(shield.scale) ? shield.scale : 1;
    if (isAustralianRectangle(shield.style)) {
        const rectangleWidth = Math.max(42, shield.label.length * 11 + 18);
        return (
            <div
                className={`${styles.shield} ${styles.rectangle} ${shield.occluded ? styles.occluded : ""}`}
                style={{
                    transform: `translate(${shield.left}px, ${shield.top}px) translate(-50%, -50%) scale(${presetScale})`,
                    width: `${rectangleWidth}rem`,
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

    return (
        <div
            className={`${styles.shield} ${shield.occluded ? styles.occluded : ""}`}
            style={{
                width: `${definition.widthRem}rem`,
                height: `${definition.heightRem}rem`,
                transform: `translate(${shield.left}px, ${shield.top}px) translate(-50%, -50%) scale(${presetScale})`,
            }}
        >
            <RouteShieldArtwork definition={definition} routeCode={shield.label} />
        </div>
    );
}

function isAustralianRectangle(style: string): boolean {
    return style === "AustralianARectangle"
        || style === "AustralianMRectangle"
        || style === "AustralianBRectangle"
        || style === "AustralianCRectangle";
}
