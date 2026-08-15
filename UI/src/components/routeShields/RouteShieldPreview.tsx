import React from "react";
import { RouteShieldDefinition } from "types";
import { resolveShieldLayerText } from "./shieldCatalog";
import browserStyles from "./routeShieldBrowser.module.scss";
import overlayStyles from "./routeShieldOverlay.module.scss";

interface ArtworkProps {
    definition: RouteShieldDefinition;
    routeCode: string;
}

export function RouteShieldArtwork({ definition, routeCode }: ArtworkProps) {
    return (
        <div
            className={overlayStyles.artwork}
            style={{ width: `${definition.widthRem}rem`, height: `${definition.heightRem}rem` }}
        >
            <img
                className={overlayStyles.body}
                src={`${definition.assetRoot}${definition.asset}`}
                width="100%"
                height="100%"
                alt=""
            />
            {definition.textLayers.map((layer) => (
                <div
                    key={layer.id}
                    className={overlayStyles.textLayer}
                    style={{
                        color: layer.color,
                        fontSize: `${layer.fontSizeRem}rem`,
                        transform: `translate(${layer.offsetXRem}rem, ${layer.offsetYRem}rem) scale(${layer.scalePercent / 100}) rotate(${layer.rotationDegrees}deg)`,
                    }}
                >
                    {resolveShieldLayerText(layer, routeCode)}
                </div>
            ))}
        </div>
    );
}

export function RouteShieldPreview({ definition, routeCode, compact = false }: ArtworkProps & { compact?: boolean }) {
    if (definition.id === "AustralianARectangle") {
        const label = (routeCode || "1").trim().toUpperCase();
        return <div className={browserStyles.previewAustralianRectangle}>{label}</div>;
    }

    const frameWidth = compact ? 52 : 72;
    const frameHeight = compact ? 44 : 54;
    const scale = Math.min(frameWidth / definition.widthRem, frameHeight / definition.heightRem);
    return (
        <div className={`${browserStyles.previewFrame} ${compact ? browserStyles.previewFrameCompact : ""}`}>
            <div className={browserStyles.previewCanvas} style={{ transform: `translate(-50%, -50%) scale(${scale})` }}>
                <RouteShieldArtwork definition={definition} routeCode={routeCode} />
            </div>
        </div>
    );
}
