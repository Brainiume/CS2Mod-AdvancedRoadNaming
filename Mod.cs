using Colossal.IO.AssetDatabase;
using Colossal.Logging;
using Colossal.UI;
using Game;
using Game.Modding;
using Game.Net;
using Game.SceneFlow;
using AdvancedRoadNaming.L10N;
using AdvancedRoadNaming.Settings;
using AdvancedRoadNaming.Systems;
using System;
using System.Reflection;

namespace AdvancedRoadNaming
{
    public class Mod : IMod
    {
        public static readonly string Id = nameof(AdvancedRoadNaming);
        private static readonly ILog BaseLog = LogManager.GetLogger($"{nameof(AdvancedRoadNaming)}.{nameof(Mod)}").SetShowsErrorsInUI(false);

        public static readonly ConditionalLog log = new ConditionalLog(BaseLog);

        public static Mod Instance { get; private set; }

        public static AdvancedRoadNamingSettings Settings { get; private set; }

        public static string ModRootPath { get; private set; }

        public string Version => Assembly.GetExecutingAssembly().GetName().Version?.ToString(4) ?? "0.0.0.0";

        public static bool IsVerboseLoggingEnabled => Settings?.EnableLogging == true;

        public static bool IsLoggingEnabled => IsVerboseLoggingEnabled;

        public void OnLoad(UpdateSystem updateSystem)
        {
            Instance = this;
            log.Info(nameof(OnLoad));

            if (GameManager.instance.modManager.TryGetExecutableAsset(this, out var asset))
            {
                log.Info($"Current mod asset at {asset.path}");
                ModRootPath = System.IO.File.Exists(asset.path) ? System.IO.Path.GetDirectoryName(asset.path) : asset.path;
            }

            RegisterUiAssetHost();

            Settings = new AdvancedRoadNamingSettings(this);
            AdvancedRoadNamingLocalization.Register(this, Settings);
            Settings.RegisterInOptionsUI();
            AssetDatabase.global.LoadSettings(AdvancedRoadNamingSettings.SettingsAssetName, Settings, new AdvancedRoadNamingSettings(this));
            Settings.EnsureCombineRoadAggregatesDefaultEnabled();

            updateSystem.UpdateAt<SegmentMetadataSystem>(SystemUpdatePhase.Deserialize);
            updateSystem.UpdateAt<SegmentMetadataSystem>(SystemUpdatePhase.Serialize);
            updateSystem.UpdateAfter<SegmentMetadataSystem, AggregateSystem>(SystemUpdatePhase.ModificationEnd);
            updateSystem.UpdateBefore<RoadAggregateProtectionSystem, AggregateSystem>(SystemUpdatePhase.ModificationEnd);
            updateSystem.UpdateAt<RoadRouteToolSystem>(SystemUpdatePhase.ToolUpdate);
            updateSystem.UpdateAt<RoadRouteToolTooltipSystem>(SystemUpdatePhase.UITooltip);
            updateSystem.UpdateAt<RoadRouteToolUISystem>(SystemUpdatePhase.UIUpdate);
            updateSystem.UpdateAfter<RoadSelectionInfoSectionSystem>(SystemUpdatePhase.UIUpdate);
            updateSystem.UpdateAt<RoadRouteOverlayGeometrySystem>(SystemUpdatePhase.Rendering);
            updateSystem.UpdateAt<RoadRouteHighlightSystem>(SystemUpdatePhase.Rendering);
            log.Info("Road Naming: systems registered");
        }

        private static void RegisterUiAssetHost()
        {
            if (string.IsNullOrWhiteSpace(ModRootPath))
            {
                log.Warn("Road Naming: UI asset host was not registered because the mod root path is unavailable.");
                return;
            }

            if (UIManager.defaultUISystem == null)
            {
                log.Warn("Road Naming: UI asset host was not registered because the default UI system is unavailable.");
                return;
            }

            var assetRoot = System.IO.Path.Combine(ModRootPath, "Assets").Replace('\\', '/') + "/";
            UIManager.defaultUISystem.AddHostLocation("rst", assetRoot);
            log.Info($"Road Naming: UI asset host registered at coui://rst/ -> {assetRoot}");
        }

        public void OnDispose()
        {
            log.Info(nameof(OnDispose));
            AdvancedRoadNamingLocalization.Unregister();
            Settings?.UnregisterInOptionsUI();
            Settings = null;
            ModRootPath = null;
            Instance = null;
        }

        public sealed class ConditionalLog
        {
            private readonly ILog _inner;

            public ConditionalLog(ILog inner)
            {
                _inner = inner;
            }

            private static bool Enabled => Mod.IsLoggingEnabled;

            public bool IsEnabled => Enabled;

            public void Info(object message)
            {
                if (Enabled)
                    _inner.Info(message);
            }

            public void Info(Func<object> messageFactory)
            {
                if (Enabled && messageFactory != null)
                    _inner.Info(messageFactory());
            }

            public void Warn(object message)
            {
                if (Enabled)
                    _inner.Warn(message);
            }

            public void Warn(Func<object> messageFactory)
            {
                if (Enabled && messageFactory != null)
                    _inner.Warn(messageFactory());
            }

            public void Warn(System.Exception exception, object message)
            {
                if (Enabled)
                    _inner.Warn(exception, message);
            }

            public void Warn(System.Exception exception, Func<object> messageFactory)
            {
                if (Enabled && messageFactory != null)
                    _inner.Warn(exception, messageFactory());
            }

            public void Error(object message)
            {
                if (Enabled)
                    _inner.Error(message);
            }

            public void Error(Func<object> messageFactory)
            {
                if (Enabled && messageFactory != null)
                    _inner.Error(messageFactory());
            }

            public void Error(System.Exception exception, object message)
            {
                if (Enabled)
                    _inner.Error(exception, message);
            }

            public void Error(System.Exception exception, Func<object> messageFactory)
            {
                if (Enabled && messageFactory != null)
                    _inner.Error(exception, messageFactory());
            }
        }
    }
}
