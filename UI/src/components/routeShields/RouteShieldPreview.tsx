import React from "react";
import { RouteShieldDefinition } from "types";
import { resolveShieldLayerText } from "./shieldCatalog";
import browserStyles from "./routeShieldBrowser.module.scss";
import overlayStyles from "./routeShieldOverlay.module.scss";

interface ArtworkProps {
    definition: RouteShieldDefinition;
    routeCode: string;
    layoutScale?: number;
    surfaceScale?: number;
}

const PREVIEW_SURFACE_SCALE = 2;

export function RouteShieldArtwork({ definition, routeCode, layoutScale = 1, surfaceScale = 1 }: ArtworkProps) {
    const layoutWidth = definition.widthRem * layoutScale;
    const layoutHeight = definition.heightRem * layoutScale;
    const surfaceWidth = layoutWidth * surfaceScale;
    const surfaceHeight = layoutHeight * surfaceScale;

    return (
        <div
            className={overlayStyles.artwork}
            style={{ width: `${layoutWidth}rem`, height: `${layoutHeight}rem` }}
        >
            <img
                className={overlayStyles.body}
                src={`${definition.assetRoot}${definition.asset}`}
                style={{
                    width: `${surfaceWidth}rem`,
                    height: `${surfaceHeight}rem`,
                    transform: `scale(${1 / surfaceScale})`,
                    transformOrigin: "0 0",
                }}
                alt=""
            />
            {definition.textLayers.map((layer) => (
                <div
                    key={layer.id}
                    className={overlayStyles.textLayer}
                    style={{
                        color: layer.color,
                        fontSize: `${layer.fontSizeRem * layoutScale}rem`,
                        transform: `translate(${layer.offsetXRem * layoutScale}rem, ${layer.offsetYRem * layoutScale}rem) scale(${layer.scalePercent / 100}) rotate(${layer.rotationDegrees}deg)`,
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
    const fitScale = Math.min(frameWidth / definition.widthRem, frameHeight / definition.heightRem);
    const finalWidth = definition.widthRem * fitScale;
    const finalHeight = definition.heightRem * fitScale;
    return (
        <div className={`${browserStyles.previewFrame} ${compact ? browserStyles.previewFrameCompact : ""}`}>
            <div
                className={browserStyles.previewCanvas}
                style={{
                    left: `${(frameWidth - finalWidth) / 2}rem`,
                    top: `${(frameHeight - finalHeight) / 2}rem`,
                    width: `${finalWidth}rem`,
                    height: `${finalHeight}rem`,
                }}
            >
                <RouteShieldArtwork
                    definition={definition}
                    routeCode={routeCode}
                    layoutScale={fitScale}
                    surfaceScale={PREVIEW_SURFACE_SCALE}
                />
            </div>
        </div>
    );
}
