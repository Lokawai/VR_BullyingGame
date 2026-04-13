using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Convai.Tests.EditMode.Architecture
{
    /// <summary>
    ///     Architectural guardrails for API exposure, namespace conventions, and error-code ownership.
    /// </summary>
    public class ArchitectureGuardTests
    {
        private static readonly Dictionary<string, string> ArchitecturePrefixes = new(StringComparer.Ordinal)
        {
            ["Domain"] = "Convai.Domain",
            ["Application"] = "Convai.Application",
            ["Shared"] = "Convai.Shared",
            ["Infrastructure"] = "Convai.Infrastructure",
            ["Runtime"] = "Convai.Runtime",
            ["Modules"] = "Convai.Modules",
            ["Editor"] = "Convai.Editor"
        };

        private static string PackageRoot => Path.Combine(Directory.GetCurrentDirectory(), "Packages",
            "com.convai.convai-sdk-for-unity");

        private static string SdkRoot => Path.Combine(PackageRoot, "SDK");

        private static string ToRelativePath(string fullPath) =>
            Path.GetRelativePath(PackageRoot, fullPath).Replace('\\', '/');

        private static Assembly FindOrLoadAssembly(string name)
        {
            Assembly loaded = AppDomain.CurrentDomain
                .GetAssemblies()
                .FirstOrDefault(a => string.Equals(a.GetName().Name, name, StringComparison.Ordinal));

            if (loaded != null) return loaded;

            try
            {
                return Assembly.Load(name);
            }
            catch
            {
                return null;
            }
        }

        private static Type FindType(string fullName, params string[] preferredAssemblies)
        {
            if (preferredAssemblies != null)
            {
                foreach (string assemblyName in preferredAssemblies)
                {
                    Assembly assembly = FindOrLoadAssembly(assemblyName);
                    Type preferredType = assembly?.GetType(fullName, false);
                    if (preferredType != null) return preferredType;
                }
            }

            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type type = assembly.GetType(fullName, false);
                if (type != null) return type;
            }

            return null;
        }

        private static string FormatViolations(string header, IReadOnlyList<string> violations)
        {
            const int maxToPrint = 20;
            var sb = new StringBuilder();
            sb.AppendLine(header);

            for (int i = 0; i < violations.Count && i < maxToPrint; i++) sb.AppendLine($"- {violations[i]}");

            if (violations.Count > maxToPrint) sb.AppendLine($"... and {violations.Count - maxToPrint} more");

            return sb.ToString();
        }

        [Test]
        [Category("Architecture")]
        public void Namespaces_Use_ArchitectureLayer_Prefixes()
        {
            Assert.IsTrue(Directory.Exists(SdkRoot), $"SDK root not found: {SdkRoot}");

            var violations = new List<string>();
            var namespacePattern = new Regex(@"^\s*namespace\s+([A-Za-z_][A-Za-z0-9_.]*)", RegexOptions.Multiline);

            foreach (string filePath in Directory.EnumerateFiles(SdkRoot, "*.cs", SearchOption.AllDirectories))
            {
                if (string.Equals(Path.GetFileName(filePath), "AssemblyInfo.cs", StringComparison.Ordinal)) continue;

                string relativeToSdk = Path.GetRelativePath(SdkRoot, filePath).Replace('\\', '/');
                if (relativeToSdk.StartsWith("Runtime/SampleCommon/", StringComparison.Ordinal)) continue;

                string rootFolder = relativeToSdk.Split('/')[0];

                if (!ArchitecturePrefixes.TryGetValue(rootFolder, out string expectedPrefix)) continue;

                string source = File.ReadAllText(filePath);
                Match nsMatch = namespacePattern.Match(source);
                if (!nsMatch.Success) continue;

                string declaredNamespace = nsMatch.Groups[1].Value;
                bool valid = declaredNamespace.Equals(expectedPrefix, StringComparison.Ordinal) ||
                             declaredNamespace.StartsWith(expectedPrefix + ".", StringComparison.Ordinal);

                if (!valid)
                    violations.Add(
                        $"{ToRelativePath(filePath)} declares '{declaredNamespace}' (expected prefix '{expectedPrefix}')");
            }

            Assert.IsEmpty(violations, FormatViolations("Architecture namespace prefix violations:", violations));
        }

        [Test]
        [Category("Architecture")]
        public void Domain_Layer_Must_Not_Reference_Outer_Layers()
        {
            Assembly domainAssembly = FindOrLoadAssembly("Convai.Domain");
            if (domainAssembly == null)
            {
                Assert.Inconclusive("Convai.Domain assembly is not loaded.");
                return;
            }

            string[] forbidden =
            {
                "Convai.Application", "Convai.Infrastructure", "Convai.Infrastructure.Networking",
                "Convai.Infrastructure.Protocol", "Convai.Runtime", "Convai.Runtime.Behaviors",
                "Convai.Modules.Vision", "Convai.Modules.Narrative", "Convai.Editor"
            };

            HashSet<string> referencedNames = domainAssembly
                .GetReferencedAssemblies()
                .Select(a => a.Name)
                .ToHashSet(StringComparer.Ordinal);

            List<string> violations = forbidden
                .Where(referencedNames.Contains)
                .ToList();

            Assert.IsEmpty(violations,
                $"Convai.Domain has forbidden outer-layer dependencies: [{string.Join(", ", violations)}]");
        }

        [Test]
        [Category("Architecture")]
        public void Canonical_Errors_Are_Single_Source_Of_Truth()
        {
            Type canonicalErrorCodesType = FindType(
                "Convai.Domain.Errors.SessionErrorCodes",
                "Convai.Domain");

            Assert.NotNull(canonicalErrorCodesType, "Could not locate SessionErrorCodes.");

            FieldInfo[] canonicalFields = canonicalErrorCodesType.GetFields(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance |
                BindingFlags.DeclaredOnly);

            List<string> localConstStringFields = canonicalFields
                .Where(f => f.FieldType == typeof(string) && f.IsLiteral && !f.IsInitOnly)
                .Select(f => f.Name)
                .ToList();

            Assert.IsNotEmpty(localConstStringFields,
                "SessionErrorCodes should expose canonical string constants.");

            string infraNetworkingRoot = Path.Combine(SdkRoot, "Infrastructure", "Networking");
            Assert.IsTrue(Directory.Exists(infraNetworkingRoot),
                $"Infrastructure networking path not found: {infraNetworkingRoot}");

            var canonicalCodeConstPattern = new Regex(
                @"const\s+string\s+\w+\s*=\s*""([a-z][a-z0-9_]*\.[a-z0-9_.]+)""",
                RegexOptions.Multiline);

            var infraViolations = new List<string>();

            foreach (string filePath in Directory.EnumerateFiles(infraNetworkingRoot, "*.cs",
                         SearchOption.AllDirectories))
            {
                string source = File.ReadAllText(filePath);
                MatchCollection matches = canonicalCodeConstPattern.Matches(source);

                foreach (Match match in matches)
                    if (match.Success)
                        infraViolations.Add($"{ToRelativePath(filePath)} => \"{match.Groups[1].Value}\"");
            }

            Assert.IsEmpty(infraViolations,
                FormatViolations("Infrastructure networking declares canonical-style const error codes:",
                    infraViolations));
        }

        [Test]
        [Category("Architecture")]
        public void LegacyConnectionStrategyLayer_Is_Removed()
        {
            Type strategyInterface = FindType(
                "Convai.Runtime.Strategies.IConvaiConnectionStrategy",
                "Convai.Runtime");
            Type strategyImplementation = FindType(
                "Convai.Runtime.Strategies.DefaultConnectionStrategy",
                "Convai.Runtime");

            Assert.IsNull(strategyInterface,
                "IConvaiConnectionStrategy should not exist once connection retry logic lives in the runtime orchestration path.");
            Assert.IsNull(strategyImplementation,
                "DefaultConnectionStrategy should not exist once connection retry logic lives in the runtime orchestration path.");

            Type legacyResumable = FindType(
                "Convai.Runtime.Strategies.IConvaiResumableConnectionStrategy",
                "Convai.Runtime");
            Type legacyReconnectable = FindType(
                "Convai.Runtime.Strategies.IConvaiReconnectableConnectionStrategy",
                "Convai.Runtime");

            Assert.IsNull(legacyResumable, "IConvaiResumableConnectionStrategy should not exist.");
            Assert.IsNull(legacyReconnectable, "IConvaiReconnectableConnectionStrategy should not exist.");
        }

        [Test]
        [Category("Architecture")]
        public void LegacyRoomOrchestrationLayer_Is_Removed()
        {
            Type orchestrationAdapter = FindType(
                "Convai.Runtime.Adapters.Networking.ConnectionOrchestrationAdapter",
                "Convai.Runtime");
            Type reconnectionServiceInterface = FindType(
                "Convai.Infrastructure.Networking.Services.IReconnectionService",
                "Convai.Infrastructure.Networking.Abstractions",
                "Convai.Infrastructure.Networking");
            Type reconnectionService = FindType(
                "Convai.Infrastructure.Networking.Services.ReconnectionService",
                "Convai.Infrastructure.Networking.Abstractions",
                "Convai.Infrastructure.Networking");

            Assert.IsNull(orchestrationAdapter,
                "ConnectionOrchestrationAdapter should not exist once ConvaiRoomManager owns the connection lifecycle.");
            Assert.IsNull(reconnectionServiceInterface,
                "IReconnectionService should not exist once reconnect state is represented by ConnectionContext and ReconnectPolicy.");
            Assert.IsNull(reconnectionService,
                "ReconnectionService should not exist once reconnect state is represented by ConnectionContext and ReconnectPolicy.");

            var legacyFiles = Directory
                .EnumerateFiles(PackageRoot, "*.cs", SearchOption.AllDirectories)
                .Where(path =>
                {
                    string fileName = Path.GetFileName(path);
                    return fileName == "ConnectionOrchestrationAdapter.cs" ||
                           fileName == "IReconnectionService.cs" ||
                           fileName == "ReconnectionService.cs";
                })
                .Select(ToRelativePath)
                .ToList();

            Assert.IsEmpty(legacyFiles,
                FormatViolations("Legacy room orchestration files should be deleted:", legacyFiles));
        }

        [Test]
        [Category("Architecture")]
        public void SessionResume_Is_PerCharacter_Not_Global()
        {
            Type legacyCapability = FindType(
                "Convai.Runtime.Behaviors.ISessionResumable",
                "Convai.Runtime.Behaviors",
                "Convai.Runtime");
            Assert.IsNull(legacyCapability, "ISessionResumable should not exist.");

            Type characterAgentType = FindType(
                "Convai.Runtime.Behaviors.IConvaiCharacterAgent",
                "Convai.Runtime.Behaviors",
                "Convai.Runtime");
            Assert.NotNull(characterAgentType, "Could not locate IConvaiCharacterAgent.");

            PropertyInfo resumeProperty =
                characterAgentType.GetProperty("EnableSessionResume", BindingFlags.Public | BindingFlags.Instance);
            Assert.NotNull(resumeProperty, "IConvaiCharacterAgent must expose EnableSessionResume.");
            Assert.AreEqual(typeof(bool), resumeProperty.PropertyType, "EnableSessionResume must be a bool.");

            Type settingsType = FindType(
                "Convai.Runtime.ConvaiSettings",
                "Convai.Runtime");
            Assert.NotNull(settingsType, "Could not locate ConvaiSettings.");

            PropertyInfo legacyGlobalProperty =
                settingsType.GetProperty("SessionResumeEnabled", BindingFlags.Public | BindingFlags.Instance);
            Assert.IsNull(legacyGlobalProperty, "ConvaiSettings.SessionResumeEnabled should not exist.");

            string runtimeRoot = Path.Combine(SdkRoot, "Runtime");
            Assert.IsTrue(Directory.Exists(runtimeRoot), $"Runtime path not found: {runtimeRoot}");

            List<string> legacyReferences = Directory
                .EnumerateFiles(runtimeRoot, "*.cs", SearchOption.AllDirectories)
                .Where(path => File.ReadAllText(path).Contains("ISessionResumable", StringComparison.Ordinal))
                .Select(ToRelativePath)
                .ToList();

            Assert.IsEmpty(legacyReferences,
                FormatViolations("Runtime still references ISessionResumable:", legacyReferences));
        }

        [Test]
        [Category("Architecture")]
        public void FutureMultiplayer_Seams_Are_Internal()
        {
            var targets = new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                ["Convai.Runtime.Networking.Media.IAudioTrackManager"] = new[] { "Convai.Runtime" },
                ["Convai.Infrastructure.Networking.ITranscriptBroadcaster"] =
                    new[] { "Convai.Infrastructure.Networking.Abstractions", "Convai.Infrastructure.Networking" },
                ["Convai.Infrastructure.Networking.IRemotePlayerRegistry"] =
                    new[] { "Convai.Infrastructure.Networking.Abstractions", "Convai.Infrastructure.Networking" }
            };

            var missing = new List<string>();
            var publicTypes = new List<string>();

            foreach (KeyValuePair<string, string[]> target in targets)
            {
                Type type = FindType(target.Key, target.Value);
                if (type == null)
                {
                    missing.Add(target.Key);
                    continue;
                }

                if (type.IsPublic) publicTypes.Add(target.Key);
            }

            Assert.IsEmpty(missing, $"Could not find expected seam types: [{string.Join(", ", missing)}]");
            Assert.IsEmpty(publicTypes, $"Future seam types must be internal: [{string.Join(", ", publicTypes)}]");
        }

        [Test]
        [Category("Architecture")]
        public void Internal_Implementations_Are_Not_Public()
        {
            var targets = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Convai.Runtime.Networking.Media.AudioTrackManager"] = "Convai.Runtime",
                ["Convai.Runtime.Vision.VideoTrackManager"] = "Convai.Runtime",
                ["Convai.Runtime.Adapters.Networking.PlayerSessionAdapter"] = "Convai.Runtime",
                ["Convai.Runtime.Adapters.Networking.CharacterRegistryAdapter"] = "Convai.Runtime",
                ["Convai.Runtime.Adapters.Networking.ConfigurationProviderAdapter"] = "Convai.Runtime",
                ["Convai.Runtime.Adapters.Vision.VideoTrackUnpublisherAdapter"] = "Convai.Runtime",
                ["Convai.Runtime.Adapters.Platform.ConvaiPermissionService"] = "Convai.Runtime"
            };

            var missing = new List<string>();
            var publicTypes = new List<string>();

            foreach (KeyValuePair<string, string> target in targets)
            {
                string fullName = target.Key;
                string assemblyName = target.Value;
                Type type = FindType(fullName, assemblyName);
                if (type == null)
                {
                    missing.Add(fullName);
                    continue;
                }

                if (type.IsPublic) publicTypes.Add(fullName);
            }

            Assert.IsEmpty(missing,
                $"Could not find target internal implementation types: [{string.Join(", ", missing)}]");
            Assert.IsEmpty(publicTypes,
                $"Internal implementation types must not be public: [{string.Join(", ", publicTypes)}]");
        }
    }
}
