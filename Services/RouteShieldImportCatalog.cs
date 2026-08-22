using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using AdvancedRoadNaming.Domain;
using Colossal;

namespace AdvancedRoadNaming.Services
{
    public sealed class RouteShieldTextLayerDefinition
    {
        public string Id { get; set; }
        public string Source { get; set; }
        public string Text { get; set; }
        public string Color { get; set; }
        public float FontSizeRem { get; set; }
        public float OffsetXRem { get; set; }
        public float OffsetYRem { get; set; }
        public float ScalePercent { get; set; }
        public float RotationDegrees { get; set; }
    }

    public sealed class RouteShieldDefinition
    {
        public string Id { get; set; }
        public string SelectionId { get; set; }
        public string Name { get; set; }
        public string Region { get; set; }
        public string System { get; set; }
        public string Asset { get; set; }
        public string AssetRoot { get; set; }
        public float WidthRem { get; set; }
        public float HeightRem { get; set; }
        public bool IsImported { get; set; }
        public IReadOnlyList<RouteShieldTextLayerDefinition> TextLayers { get; set; }
    }

    [DataContract]
    internal sealed class RouteShieldConfiguration
    {
        [DataMember(Name = "schemaVersion", EmitDefaultValue = false)]
        public int? SchemaVersion { get; set; }

        [DataMember(Name = "id", EmitDefaultValue = false)]
        public string Id { get; set; }

        [DataMember(Name = "name", EmitDefaultValue = false)]
        public string Name { get; set; }

        [DataMember(Name = "region", EmitDefaultValue = false)]
        public string Region { get; set; }

        [DataMember(Name = "system", EmitDefaultValue = false)]
        public string System { get; set; }

        [DataMember(Name = "svg", EmitDefaultValue = false)]
        public string Svg { get; set; }

        [DataMember(Name = "widthRem", EmitDefaultValue = false)]
        public float? WidthRem { get; set; }

        [DataMember(Name = "heightRem", EmitDefaultValue = false)]
        public float? HeightRem { get; set; }

        [DataMember(Name = "textLayers", EmitDefaultValue = false)]
        public RouteShieldTextLayerConfiguration[] TextLayers { get; set; }

        // Schema-v0 compatibility fields.
        [DataMember(Name = "showPrefix", EmitDefaultValue = false)]
        public bool? ShowPrefix { get; set; }

        [DataMember(Name = "textColor", EmitDefaultValue = false)]
        public string TextColor { get; set; }
    }

    [DataContract]
    internal sealed class RouteShieldTextLayerConfiguration
    {
        [DataMember(Name = "id", EmitDefaultValue = false)]
        public string Id { get; set; }

        [DataMember(Name = "source", EmitDefaultValue = false)]
        public string Source { get; set; }

        [DataMember(Name = "text", EmitDefaultValue = false)]
        public string Text { get; set; }

        [DataMember(Name = "color", EmitDefaultValue = false)]
        public string Color { get; set; }

        [DataMember(Name = "fontSizeRem", EmitDefaultValue = false)]
        public float? FontSizeRem { get; set; }

        [DataMember(Name = "offsetXRem", EmitDefaultValue = false)]
        public float? OffsetXRem { get; set; }

        [DataMember(Name = "offsetYRem", EmitDefaultValue = false)]
        public float? OffsetYRem { get; set; }

        [DataMember(Name = "scalePercent", EmitDefaultValue = false)]
        public float? ScalePercent { get; set; }

        [DataMember(Name = "rotationDegrees", EmitDefaultValue = false)]
        public float? RotationDegrees { get; set; }
    }

    public static class RouteShieldImportCatalog
    {
        public const string HostName = "arn-route-shields";
        public const int MaximumShieldCount = 128;
        private const int SchemaVersion = 1;
        private const int MaximumTextLayers = 8;
        private const long MaximumSvgBytes = 2L * 1024L * 1024L;
        private const long MaximumJsonBytes = 256L * 1024L;
        private const string BuiltInAssetRoot = "coui://rst/RouteShields/";
        private const string ImportedAssetRoot = "coui://arn-route-shields/";

        private static readonly List<RouteShieldDefinition> DefinitionsInternal = new List<RouteShieldDefinition>();
        private static readonly Dictionary<string, RouteShieldDefinition> ImportedDefinitionsById = new Dictionary<string, RouteShieldDefinition>(StringComparer.Ordinal);
        private static readonly Dictionary<string, RouteShieldDefinition> LastValidBySvgPath = new Dictionary<string, RouteShieldDefinition>(StringComparer.OrdinalIgnoreCase);

        public static string BuiltInRootPath { get; private set; }
        public static string RootPath { get; private set; }
        public static int Version { get; private set; }
        public static int BuiltInCount { get; private set; }
        public static int ImportedCount { get; private set; }
        public static int ErrorCount { get; private set; }
        public static string LatestError { get; private set; } = string.Empty;
        public static string Status => $"Built-in: {BuiltInCount}, Custom: {ImportedCount}, Errors: {ErrorCount}"
            + (string.IsNullOrWhiteSpace(LatestError) ? string.Empty : $". Latest: {LatestError}");
        public static IReadOnlyList<RouteShieldDefinition> Definitions => DefinitionsInternal;

        public static void Initialize(string builtInRootPath, string importedRootPath)
        {
            BuiltInRootPath = builtInRootPath;
            RootPath = importedRootPath;
            if (!string.IsNullOrWhiteSpace(RootPath))
            {
                Directory.CreateDirectory(RootPath);
                WriteDocumentationFiles();
            }
            Refresh();
        }

        public static void Refresh()
        {
            var nextDefinitions = new List<RouteShieldDefinition>(64);
            var nextImported = new Dictionary<string, RouteShieldDefinition>(StringComparer.Ordinal);
            var selectionIds = new HashSet<string>(StringComparer.Ordinal);
            var errors = new List<string>();
            var builtInCount = ScanRoot(BuiltInRootPath, false, int.MaxValue, nextDefinitions, nextImported, selectionIds, errors);
            var importedCount = ScanRoot(RootPath, true, MaximumShieldCount, nextDefinitions, nextImported, selectionIds, errors);

            DefinitionsInternal.Clear();
            DefinitionsInternal.AddRange(nextDefinitions);
            ImportedDefinitionsById.Clear();
            foreach (var pair in nextImported)
                ImportedDefinitionsById.Add(pair.Key, pair.Value);

            BuiltInCount = builtInCount;
            ImportedCount = importedCount;
            ErrorCount = errors.Count;
            LatestError = errors.Count == 0 ? string.Empty : errors[errors.Count - 1];
            Version++;
        }

        public static void OpenDesigner()
        {
            try
            {
                if (string.IsNullOrWhiteSpace(RootPath))
                {
                    Mod.WarnAlways("Road Naming: the custom route shield directory is unavailable, so the designer could not be opened.");
                    return;
                }

                Directory.CreateDirectory(RootPath);
                var designerPath = Path.Combine(RootPath, "RouteShieldDesigner.html");
                if (!File.Exists(designerPath))
                {
                    var documentationRoot = string.IsNullOrWhiteSpace(BuiltInRootPath)
                        ? string.Empty
                        : Path.Combine(BuiltInRootPath, "Documentation");
                    CopyDocumentationFile(documentationRoot, "RouteShieldDesigner.html", "RouteShieldDesigner.html");
                }

                if (!File.Exists(designerPath))
                {
                    Mod.WarnAlways("Road Naming: RouteShieldDesigner.html is missing from the custom route shield directory.");
                    return;
                }

                UnityEngine.Application.OpenURL(new Uri(designerPath).AbsoluteUri);
            }
            catch (Exception exception)
            {
                Mod.WarnAlways(exception, "Road Naming: the custom route shield designer could not be opened.");
            }
        }

        public static void OpenImportFolder()
        {
            try
            {
                if (string.IsNullOrWhiteSpace(RootPath))
                {
                    Mod.WarnAlways("Road Naming: the custom route shield directory is unavailable, so it could not be opened.");
                    return;
                }

                Directory.CreateDirectory(RootPath);
                RemoteProcess.OpenFolder(RootPath);
            }
            catch (Exception exception)
            {
                Mod.WarnAlways(exception, "Road Naming: the custom route shield directory could not be opened.");
            }
        }

        public static bool Contains(string id)
        {
            return !string.IsNullOrWhiteSpace(id) && ImportedDefinitionsById.ContainsKey(id);
        }

        public static bool TryGet(string id, out RouteShieldDefinition definition)
        {
            return ImportedDefinitionsById.TryGetValue(id ?? string.Empty, out definition);
        }

        public static string SelectionValue(string importId)
        {
            return "Imported:" + (importId ?? string.Empty);
        }

        private static int ScanRoot(
            string rootPath,
            bool imported,
            int maximumCount,
            List<RouteShieldDefinition> definitions,
            Dictionary<string, RouteShieldDefinition> importedById,
            HashSet<string> selectionIds,
            List<string> errors)
        {
            if (string.IsNullOrWhiteSpace(rootPath) || !Directory.Exists(rootPath))
                return 0;

            string[] files;
            try
            {
                files = Directory.GetFiles(rootPath, "*.svg", SearchOption.AllDirectories);
                Array.Sort(files, StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception exception)
            {
                AddError(errors, rootPath, "Shield directory scan failed: " + exception.Message, exception);
                return 0;
            }

            var loadedCount = 0;
            for (var i = 0; i < files.Length && loadedCount < maximumCount; i++)
            {
                var svgPath = Path.GetFullPath(files[i]);
                if (!IsWithinRoot(rootPath, svgPath))
                    continue;

                RouteShieldDefinition definition;
                string error;
                if (!TryLoadDefinition(rootPath, svgPath, imported, out definition, out error))
                {
                    RouteShieldDefinition previous;
                    if (LastValidBySvgPath.TryGetValue(svgPath, out previous))
                    {
                        definition = previous;
                        AddError(errors, svgPath, error + " Previous valid definition retained.");
                    }
                    else if (imported)
                    {
                        var fallback = BuildDefaultImportedConfiguration(rootPath, svgPath);
                        try
                        {
                            WriteConfiguration(Path.ChangeExtension(svgPath, ".json"), fallback);
                        }
                        catch (Exception exception)
                        {
                            AddError(errors, svgPath, "Default JSON could not be written: " + exception.Message, exception);
                        }

                        if (!TryValidateConfiguration(rootPath, svgPath, fallback, true, out definition, out error))
                        {
                            AddError(errors, svgPath, error);
                            continue;
                        }
                        AddError(errors, svgPath, "Invalid JSON was replaced with generated defaults.");
                    }
                    else
                    {
                        AddError(errors, svgPath, error);
                        continue;
                    }
                }

                if (!selectionIds.Add(definition.SelectionId))
                {
                    AddError(errors, svgPath, "Duplicate shield ID '" + definition.SelectionId + "'.");
                    continue;
                }

                definitions.Add(definition);
                LastValidBySvgPath[svgPath] = definition;
                if (definition.IsImported)
                    importedById[definition.Id] = definition;
                loadedCount++;
            }

            return loadedCount;
        }

        private static bool TryLoadDefinition(string rootPath, string svgPath, bool imported, out RouteShieldDefinition definition, out string error)
        {
            definition = null;
            error = string.Empty;
            var svg = new FileInfo(svgPath);
            if (!svg.Exists || svg.Length <= 0 || svg.Length > MaximumSvgBytes)
            {
                error = "SVG is empty or exceeds 2 MB.";
                return false;
            }

            var jsonPath = Path.ChangeExtension(svgPath, ".json");
            if (!File.Exists(jsonPath))
            {
                if (!imported)
                {
                    error = "Bundled shield JSON is missing.";
                    return false;
                }

                var generated = BuildDefaultImportedConfiguration(rootPath, svgPath);
                try
                {
                    WriteConfiguration(jsonPath, generated);
                }
                catch (Exception exception)
                {
                    error = "Default JSON could not be written: " + exception.Message;
                    return false;
                }
                return TryValidateConfiguration(rootPath, svgPath, generated, true, out definition, out error);
            }

            var json = new FileInfo(jsonPath);
            if (json.Length <= 0 || json.Length > MaximumJsonBytes)
            {
                error = "JSON is empty or exceeds 256 KB.";
                return false;
            }

            RouteShieldConfiguration configuration;
            try
            {
                var serializer = new DataContractJsonSerializer(typeof(RouteShieldConfiguration));
                using (var stream = File.OpenRead(jsonPath))
                    configuration = serializer.ReadObject(stream) as RouteShieldConfiguration;
            }
            catch (Exception exception)
            {
                error = "JSON could not be parsed: " + exception.Message;
                return false;
            }

            if (configuration == null)
            {
                error = "JSON did not contain a shield definition.";
                return false;
            }

            if (!configuration.SchemaVersion.HasValue && imported && (configuration.ShowPrefix.HasValue || !string.IsNullOrWhiteSpace(configuration.TextColor)))
            {
                configuration = MigrateLegacyConfiguration(rootPath, svgPath, configuration);
                try
                {
                    WriteConfiguration(jsonPath, configuration);
                }
                catch (Exception exception)
                {
                    error = "Schema-v0 JSON could not be rewritten: " + exception.Message;
                    return false;
                }
            }

            return TryValidateConfiguration(rootPath, svgPath, configuration, imported, out definition, out error);
        }

        private static bool TryValidateConfiguration(
            string rootPath,
            string svgPath,
            RouteShieldConfiguration configuration,
            bool imported,
            out RouteShieldDefinition definition,
            out string error)
        {
            definition = null;
            error = string.Empty;
            if (configuration.SchemaVersion != SchemaVersion)
                return Fail("schemaVersion must be 1.", out error);

            var relativePath = GetRelativePath(rootPath, svgPath);
            var expectedId = imported ? BuildStableId(relativePath) : configuration.Id;
            if (string.IsNullOrWhiteSpace(configuration.Id))
                return Fail("id is required.", out error);
            if (imported && !string.Equals(configuration.Id, expectedId, StringComparison.Ordinal))
                return Fail("Imported id must remain '" + expectedId + "'.", out error);
            if (!imported && !IsSupportedBuiltInId(configuration.Id))
                return Fail("id is not a supported built-in route shield style.", out error);
            if (string.IsNullOrWhiteSpace(configuration.Name) || configuration.Name.Length > 100)
                return Fail("name is required and must not exceed 100 characters.", out error);
            if (string.IsNullOrWhiteSpace(configuration.Region) || configuration.Region.Length > 60)
                return Fail("region is required and must not exceed 60 characters.", out error);
            if (!IsSupportedSystem(configuration.System))
                return Fail("system must be National, Motorway, State, or Custom.", out error);

            var expectedSvgName = Path.GetFileName(svgPath);
            if (!string.Equals(configuration.Svg, expectedSvgName, StringComparison.OrdinalIgnoreCase))
                return Fail("svg must reference the same-name sibling file '" + expectedSvgName + "'.", out error);
            if (!IsInRange(configuration.WidthRem, 8f, 256f) || !IsInRange(configuration.HeightRem, 8f, 256f))
                return Fail("widthRem and heightRem must be between 8 and 256.", out error);
            if (configuration.TextLayers == null || configuration.TextLayers.Length == 0 || configuration.TextLayers.Length > MaximumTextLayers)
                return Fail("textLayers must contain between 1 and 8 layers.", out error);

            var layerIds = new HashSet<string>(StringComparer.Ordinal);
            var layers = new List<RouteShieldTextLayerDefinition>(configuration.TextLayers.Length);
            for (var i = 0; i < configuration.TextLayers.Length; i++)
            {
                var layer = configuration.TextLayers[i];
                if (layer == null || string.IsNullOrWhiteSpace(layer.Id) || layer.Id.Length > 60 || !layerIds.Add(layer.Id))
                    return Fail("Every text layer requires a unique id of at most 60 characters.", out error);
                if (!IsSupportedTextSource(layer.Source))
                    return Fail("Text layer '" + layer.Id + "' has an unsupported source.", out error);
                if (string.Equals(layer.Source, "literal", StringComparison.Ordinal) && (layer.Text == null || layer.Text.Length > 64))
                    return Fail("Literal layer '" + layer.Id + "' requires text of at most 64 characters.", out error);
                if (!IsHexColor(layer.Color))
                    return Fail("Text layer '" + layer.Id + "' color must use #RRGGBB.", out error);
                if (!IsInRange(layer.FontSizeRem, 1f, 128f))
                    return Fail("Text layer '" + layer.Id + "' fontSizeRem must be between 1 and 128.", out error);
                if (!IsInRange(layer.OffsetXRem, -256f, 256f) || !IsInRange(layer.OffsetYRem, -256f, 256f))
                    return Fail("Text layer '" + layer.Id + "' offsets must be between -256 and 256.", out error);
                if (!IsInRange(layer.ScalePercent, 10f, 400f))
                    return Fail("Text layer '" + layer.Id + "' scalePercent must be between 10 and 400.", out error);
                if (!IsInRange(layer.RotationDegrees, -360f, 360f))
                    return Fail("Text layer '" + layer.Id + "' rotationDegrees must be between -360 and 360.", out error);

                layers.Add(new RouteShieldTextLayerDefinition
                {
                    Id = layer.Id,
                    Source = layer.Source,
                    Text = layer.Text ?? string.Empty,
                    Color = layer.Color.ToUpperInvariant(),
                    FontSizeRem = layer.FontSizeRem.Value,
                    OffsetXRem = layer.OffsetXRem.Value,
                    OffsetYRem = layer.OffsetYRem.Value,
                    ScalePercent = layer.ScalePercent.Value,
                    RotationDegrees = layer.RotationDegrees.Value
                });
            }

            definition = new RouteShieldDefinition
            {
                Id = expectedId,
                SelectionId = imported ? SelectionValue(expectedId) : configuration.Id,
                Name = configuration.Name.Trim(),
                Region = configuration.Region.Trim(),
                System = configuration.System,
                Asset = EncodeRelativePath(relativePath),
                AssetRoot = imported ? ImportedAssetRoot : BuiltInAssetRoot,
                WidthRem = configuration.WidthRem.Value,
                HeightRem = configuration.HeightRem.Value,
                IsImported = imported,
                TextLayers = layers
            };
            return true;
        }

        private static RouteShieldConfiguration MigrateLegacyConfiguration(string rootPath, string svgPath, RouteShieldConfiguration legacy)
        {
            var configuration = BuildDefaultImportedConfiguration(rootPath, svgPath);
            configuration.TextLayers[0].Source = legacy.ShowPrefix == true ? "routeCode" : "number";
            configuration.TextLayers[0].Color = string.Equals(legacy.TextColor, "black", StringComparison.OrdinalIgnoreCase) ? "#000000" : "#FFFFFF";
            return configuration;
        }

        private static RouteShieldConfiguration BuildDefaultImportedConfiguration(string rootPath, string svgPath)
        {
            var relativePath = GetRelativePath(rootPath, svgPath);
            float width;
            float height;
            ReadDefaultDimensions(svgPath, out width, out height);
            return new RouteShieldConfiguration
            {
                SchemaVersion = SchemaVersion,
                Id = BuildStableId(relativePath),
                Name = Humanize(Path.GetFileNameWithoutExtension(svgPath)),
                Region = "Custom",
                System = "Custom",
                Svg = Path.GetFileName(svgPath),
                WidthRem = width,
                HeightRem = height,
                TextLayers = new[]
                {
                    new RouteShieldTextLayerConfiguration
                    {
                        Id = "route-number",
                        Source = "number",
                        Text = string.Empty,
                        Color = "#FFFFFF",
                        FontSizeRem = 21f,
                        OffsetXRem = 0f,
                        OffsetYRem = 0f,
                        ScalePercent = 100f,
                        RotationDegrees = 0f
                    }
                }
            };
        }

        private static void WriteConfiguration(string jsonPath, RouteShieldConfiguration configuration)
        {
            var builder = new StringBuilder(1024);
            builder.AppendLine("{");
            AppendProperty(builder, "schemaVersion", configuration.SchemaVersion.GetValueOrDefault(), true, 2);
            AppendProperty(builder, "id", configuration.Id, true, 2);
            AppendProperty(builder, "name", configuration.Name, true, 2);
            AppendProperty(builder, "region", configuration.Region, true, 2);
            AppendProperty(builder, "system", configuration.System, true, 2);
            AppendProperty(builder, "svg", configuration.Svg, true, 2);
            AppendProperty(builder, "widthRem", configuration.WidthRem.GetValueOrDefault(), true, 2);
            AppendProperty(builder, "heightRem", configuration.HeightRem.GetValueOrDefault(), true, 2);
            builder.AppendLine("  \"textLayers\": [");
            for (var i = 0; i < configuration.TextLayers.Length; i++)
            {
                var layer = configuration.TextLayers[i];
                builder.AppendLine("    {");
                AppendProperty(builder, "id", layer.Id, true, 6);
                AppendProperty(builder, "source", layer.Source, true, 6);
                AppendProperty(builder, "text", layer.Text ?? string.Empty, true, 6);
                AppendProperty(builder, "color", layer.Color, true, 6);
                AppendProperty(builder, "fontSizeRem", layer.FontSizeRem.GetValueOrDefault(), true, 6);
                AppendProperty(builder, "offsetXRem", layer.OffsetXRem.GetValueOrDefault(), true, 6);
                AppendProperty(builder, "offsetYRem", layer.OffsetYRem.GetValueOrDefault(), true, 6);
                AppendProperty(builder, "scalePercent", layer.ScalePercent.GetValueOrDefault(), true, 6);
                AppendProperty(builder, "rotationDegrees", layer.RotationDegrees.GetValueOrDefault(), false, 6);
                builder.Append("    }");
                builder.AppendLine(i + 1 < configuration.TextLayers.Length ? "," : string.Empty);
            }
            builder.AppendLine("  ]");
            builder.AppendLine("}");
            File.WriteAllText(jsonPath, builder.ToString());
        }

        private static void AppendProperty(StringBuilder builder, string name, string value, bool comma, int indent)
        {
            builder.Append(' ', indent).Append('"').Append(name).Append("\": \"").Append(EscapeJson(value ?? string.Empty)).Append('"');
            if (comma)
                builder.Append(',');
            builder.AppendLine();
        }

        private static void AppendProperty(StringBuilder builder, string name, float value, bool comma, int indent)
        {
            builder.Append(' ', indent).Append('"').Append(name).Append("\": ").Append(value.ToString("0.###", CultureInfo.InvariantCulture));
            if (comma)
                builder.Append(',');
            builder.AppendLine();
        }

        private static string EscapeJson(string value)
        {
            var builder = new StringBuilder(value.Length + 8);
            foreach (var character in value)
            {
                switch (character)
                {
                    case '"': builder.Append("\\\""); break;
                    case '\\': builder.Append("\\\\"); break;
                    case '\b': builder.Append("\\b"); break;
                    case '\f': builder.Append("\\f"); break;
                    case '\n': builder.Append("\\n"); break;
                    case '\r': builder.Append("\\r"); break;
                    case '\t': builder.Append("\\t"); break;
                    default:
                        if (character < 32)
                            builder.Append("\\u").Append(((int)character).ToString("x4", CultureInfo.InvariantCulture));
                        else
                            builder.Append(character);
                        break;
                }
            }
            return builder.ToString();
        }

        private static void WriteDocumentationFiles()
        {
            var documentationRoot = string.IsNullOrWhiteSpace(BuiltInRootPath)
                ? string.Empty
                : Path.Combine(BuiltInRootPath, "Documentation");
            var textCopied = CopyDocumentationFile(documentationRoot, "CUSTOM_ROUTE_SHIELDS.txt", "README.txt");
            CopyDocumentationFile(documentationRoot, "RouteShieldDesigner.html", "RouteShieldDesigner.html");

            if (!textCopied)
                WriteFallbackReadme();
        }

        private static bool CopyDocumentationFile(string documentationRoot, string sourceName, string destinationName)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(documentationRoot))
                    return false;

                var sourcePath = Path.Combine(documentationRoot, sourceName);
                if (!File.Exists(sourcePath))
                    return false;

                File.Copy(sourcePath, Path.Combine(RootPath, destinationName), true);
                return true;
            }
            catch (Exception exception)
            {
                Mod.WarnAlways(exception, "Road Naming route shield documentation could not be copied: " + destinationName);
                return false;
            }
        }

        private static void WriteFallbackReadme()
        {
            File.WriteAllText(Path.Combine(RootPath, "README.txt"),
                "Advanced Road Naming custom route shields (work in progress)\r\n\r\n" +
                "Drop an SVG into this folder or a nested folder, then restart the game or use Refresh Route Shields in the mod settings.\r\n" +
                "A same-name schema-v1 JSON file is generated when one is missing. The generated id is path-derived and must not be changed.\r\n\r\n" +
                "Schema-v1 fields:\r\n" +
                "  schemaVersion: must be 1\r\n" +
                "  id: generated stable id; do not edit\r\n" +
                "  name: browser tooltip name\r\n" +
                "  region: use Custom for the Custom browser filter\r\n" +
                "  system: National, Motorway, State, or Custom\r\n" +
                "  svg: same-name SVG filename only\r\n" +
                "  widthRem, heightRem: shield canvas dimensions\r\n" +
                "  textLayers: ordered array; later layers draw above earlier layers\r\n\r\n" +
                "Each text layer supports id, source, text, color, fontSizeRem, offsetXRem, offsetYRem, scalePercent, and rotationDegrees.\r\n" +
                "Sources: routeCode (full code), prefix (leading letters/separator), number (remaining value), literal (the text field).\r\n" +
                "Colors must use #RRGGBB. Font sizes and offsets use rem; scale uses percent; rotation uses degrees.\r\n" +
                "Limits: dimensions 8-256, font size 1-128, offsets -256 to 256, scale 10-400, rotation -360 to 360, maximum 8 layers.\r\n\r\n" +
                "Example layer:\r\n" +
                "  { \"id\": \"route-number\", \"source\": \"number\", \"text\": \"\", \"color\": \"#FFFFFF\", \"fontSizeRem\": 21, \"offsetXRem\": 0, \"offsetYRem\": 0, \"scalePercent\": 100, \"rotationDegrees\": 0 }\r\n\r\n" +
                "Malformed files are reported in Player.log and the settings status. Built-in JSON files live with the mod assets and edits to them may be overwritten by mod updates.\r\n");
        }

        private static void ReadDefaultDimensions(string path, out float width, out float height)
        {
            width = 34f;
            height = 34f;
            try
            {
                var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
                using (var reader = XmlReader.Create(path, settings))
                {
                    while (reader.Read())
                    {
                        if (reader.NodeType != XmlNodeType.Element || !string.Equals(reader.LocalName, "svg", StringComparison.OrdinalIgnoreCase))
                            continue;
                        var viewBox = reader.GetAttribute("viewBox");
                        if (!string.IsNullOrWhiteSpace(viewBox))
                        {
                            var values = viewBox.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
                            float svgWidth;
                            float svgHeight;
                            if (values.Length == 4
                                && float.TryParse(values[2], NumberStyles.Float, CultureInfo.InvariantCulture, out svgWidth)
                                && float.TryParse(values[3], NumberStyles.Float, CultureInfo.InvariantCulture, out svgHeight)
                                && svgWidth > 0f && svgHeight > 0f)
                            {
                                var ratio = svgWidth / svgHeight;
                                if (ratio > 1.25f) { width = 46f; height = 28f; }
                                else if (ratio < 0.8f) { width = 27f; height = 39f; }
                            }
                        }
                        break;
                    }
                }
            }
            catch
            {
            }
        }

        private static bool IsSupportedBuiltInId(string id)
        {
            RouteShieldStyle style;
            if (!Enum.TryParse(id, false, out style))
                return false;
            return style == RouteShieldStyle.AustralianNationalShield
                || style == RouteShieldStyle.BlueHighwayShield
                || style == RouteShieldStyle.BlackWhiteShield
                || (style >= RouteShieldStyle.USInterstate && style <= RouteShieldStyle.NewZealandStateHighway)
                || style == RouteShieldStyle.GermanyFederalRoad
                || style == RouteShieldStyle.ThailandHighway
                || style == RouteShieldStyle.ThailandMotorwayBlue
                || style == RouteShieldStyle.ThailandMotorwayGreen;
        }

        private static bool IsSupportedSystem(string value)
        {
            return value == "National" || value == "Motorway" || value == "State" || value == "Custom";
        }

        private static bool IsSupportedTextSource(string value)
        {
            return value == "routeCode" || value == "prefix" || value == "number" || value == "literal";
        }

        private static bool IsHexColor(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length != 7 || value[0] != '#')
                return false;
            for (var i = 1; i < value.Length; i++)
            {
                var c = value[i];
                if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F')))
                    return false;
            }
            return true;
        }

        private static bool IsInRange(float? value, float minimum, float maximum)
        {
            return value.HasValue && !float.IsNaN(value.Value) && !float.IsInfinity(value.Value) && value.Value >= minimum && value.Value <= maximum;
        }

        private static bool Fail(string message, out string error)
        {
            error = message;
            return false;
        }

        private static string GetRelativePath(string rootPath, string filePath)
        {
            return Path.GetFullPath(filePath).Substring(Path.GetFullPath(rootPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Length)
                .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Replace('\\', '/');
        }

        private static bool IsWithinRoot(string rootPath, string path)
        {
            var root = Path.GetFullPath(rootPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return Path.GetFullPath(path).StartsWith(root, StringComparison.OrdinalIgnoreCase);
        }

        private static string BuildStableId(string relativePath)
        {
            using (var sha = SHA256.Create())
            {
                var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(relativePath.ToLowerInvariant()));
                var builder = new StringBuilder("user-");
                for (var i = 0; i < 10; i++)
                    builder.Append(bytes[i].ToString("x2", CultureInfo.InvariantCulture));
                return builder.ToString();
            }
        }

        private static string EncodeRelativePath(string relativePath)
        {
            var parts = relativePath.Split(new[] { '/' }, StringSplitOptions.None);
            for (var i = 0; i < parts.Length; i++)
                parts[i] = Uri.EscapeDataString(parts[i]);
            return string.Join("/", parts);
        }

        private static string Humanize(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "Imported shield" : value.Replace('_', ' ').Replace('-', ' ').Trim();
        }

        private static void AddError(List<string> errors, string path, string message, Exception exception = null)
        {
            var text = Path.GetFileName(path) + ": " + message;
            errors.Add(text);
            if (exception == null)
                Mod.WarnAlways("Road Naming route shield JSON: " + path + ": " + message);
            else
                Mod.WarnAlways(exception, "Road Naming route shield JSON: " + path + ": " + message);
        }
    }
}
