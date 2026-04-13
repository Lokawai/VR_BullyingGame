using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using Convai.Domain.Abstractions;
using Convai.Domain.EventSystem;
using Convai.Domain.Logging;
using Convai.Runtime.Logging;
using Convai.Runtime.Presentation.Services;
using Convai.Runtime.Utilities;
using Convai.Shared;
using Convai.Shared.Abstractions;
using Convai.Shared.DependencyInjection;
using Convai.Shared.Interfaces;
using Convai.Shared.Types;
using UnityEngine;
using UnityEngine.SceneManagement;
using ILogger = Convai.Domain.Logging.ILogger;

namespace Convai.Runtime
{
    /// <summary>
    ///     Composition root for dependency injection in Unity.
    ///     Discovers all <see cref="IInjectable" /> components in the scene and injects their
    ///     dependencies from the service container.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Runs after <see cref="ConvaiServiceBootstrap" /> (execution order -500 vs -1000) to
    ///         ensure all services are registered before injection begins.
    ///     </para>
    ///     <para>
    ///         Injection flow:
    ///         <list type="number">
    ///             <item>Register pre-injection services (e.g., INarrativeSectionNameResolver via reflection)</item>
    ///             <item>
    ///                 Discover interface implementations via targeted type queries (IInjectable, ITranscriptUI,
    ///                 ITranscriptListener)
    ///             </item>
    ///             <item>Sort IInjectables by <see cref="IInjectable.InjectionOrder" /> (lower values first)</item>
    ///             <item>Call <see cref="IInjectable.InjectServices" /> on each — components resolve their own dependencies</item>
    ///             <item>Register ITranscriptUI and ITranscriptListener implementations with TranscriptUIController</item>
    ///             <item>Update runtime transcript mode capabilities</item>
    ///         </list>
    ///     </para>
    ///     <para>
    ///         Adding a new injectable component requires only two steps:
    ///         <list type="number">
    ///             <item>Implement <see cref="IInjectable" /> on your MonoBehaviour</item>
    ///             <item>Resolve your dependencies in <see cref="IInjectable.InjectServices" /></item>
    ///         </list>
    ///         No changes to this class are needed.
    ///     </para>
    /// </remarks>
    [AddComponentMenu("")]
    [DefaultExecutionOrder(-500)]
    internal class ConvaiCompositionRoot : MonoBehaviour
    {
        private static ConvaiCompositionRoot _instance;

        [Header("Composition Root Settings")]
        [Tooltip("Enable to log detailed injection diagnostics.")]
        [SerializeField]
        private bool _debugLogging;

        [Tooltip("If true, throw exception if required services are missing")] [SerializeField]
        private bool _strictMode;

        [Header("Component Discovery")]
        [Tooltip("If true, automatically discover and inject components on Awake")]
        [SerializeField]
        private bool _autoInject = true;

        [Tooltip(
            "If true, include inactive GameObjects in component discovery. Required for UI components that start inactive.")]
        [SerializeField]
        private bool _includeInactive = true;

        #region Unity Lifecycle

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                if (_debugLogging)
                {
                    ConvaiLogger.Debug("[ConvaiCompositionRoot] Duplicate detected, destroying copy.",
                        LogCategory.Bootstrap);
                }

                DestroyImmediate(gameObject);
                return;
            }

            _instance = this;

            ValidatePrerequisites();

            if (_autoInject) InjectAllComponents();
        }

        private void Start() => SceneManager.sceneLoaded += OnSceneLoaded;

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;

            if (_instance == this) _instance = null;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (_autoInject && _instance == this)
            {
                if (_debugLogging)
                {
                    ConvaiLogger.Debug(
                        $"[ConvaiCompositionRoot] Scene '{scene.name}' loaded, re-injecting components...",
                        LogCategory.Bootstrap);
                }

                InjectAllComponents();
            }
        }

        #endregion

        #region Injection

        /// <summary>
        ///     Discovers and injects dependencies into all <see cref="IInjectable" /> components in the scene.
        /// </summary>
        public void InjectAllComponents()
        {
            var sw = Stopwatch.StartNew();

            if (_debugLogging)
                ConvaiLogger.Debug("[ConvaiCompositionRoot] Starting dependency injection...", LogCategory.Bootstrap);

            try
            {
                IServiceContainer container = ConvaiServiceLocator.GetContainer();

                // Register services that must exist before any IInjectable runs
                RegisterPreInjectionServices(container);

                FindObjectsInactive includeInactive =
                    _includeInactive ? FindObjectsInactive.Include : FindObjectsInactive.Exclude;

                IReadOnlyList<IInjectable> discoveredInjectables =
                    InterfaceComponentQuery.FindObjects<IInjectable>(includeInactive);
                IReadOnlyList<ITranscriptUI> discoveredTranscriptUIs =
                    InterfaceComponentQuery.FindObjects<ITranscriptUI>(includeInactive);
                IReadOnlyList<ITranscriptListener> discoveredTranscriptListeners =
                    InterfaceComponentQuery.FindObjects<ITranscriptListener>(includeInactive);

                var injectables = new List<IInjectable>(discoveredInjectables.Count);
                for (int i = 0; i < discoveredInjectables.Count; i++) injectables.Add(discoveredInjectables[i]);

                var transcriptUIs = new List<ITranscriptUI>(discoveredTranscriptUIs.Count);
                for (int i = 0; i < discoveredTranscriptUIs.Count; i++) transcriptUIs.Add(discoveredTranscriptUIs[i]);

                var transcriptListeners = new List<ITranscriptListener>(discoveredTranscriptListeners.Count);
                for (int i = 0; i < discoveredTranscriptListeners.Count; i++)
                    transcriptListeners.Add(discoveredTranscriptListeners[i]);

                // Sort by injection order — infrastructure components (negative order) inject first
                injectables.Sort((a, b) => a.InjectionOrder.CompareTo(b.InjectionOrder));

                if (_debugLogging)
                {
                    ConvaiLogger.Debug(
                        "[ConvaiCompositionRoot] Discovered " +
                        $"{injectables.Count} injectable(s), " +
                        $"{transcriptUIs.Count} transcript UI(s), " +
                        $"{transcriptListeners.Count} transcript listener(s)",
                        LogCategory.Bootstrap);
                }

                // Inject all IInjectable components
                int injectedCount = 0;
                foreach (IInjectable injectable in injectables)
                {
                    var mb = injectable as MonoBehaviour;
                    try
                    {
                        injectable.InjectServices(container);
                        injectedCount++;

                        if (_debugLogging)
                        {
                            string componentInfo = mb != null
                                ? $"{mb.GetType().Name} on '{mb.gameObject.name}'"
                                : injectable.GetType().Name;
                            ConvaiLogger.Debug(
                                $"[ConvaiCompositionRoot] Injected: {componentInfo} (order: {injectable.InjectionOrder})",
                                LogCategory.Bootstrap);
                        }
                    }
                    catch (Exception ex)
                    {
                        string componentInfo = mb != null
                            ? $"{mb.GetType().Name} on '{mb.gameObject.name}'"
                            : injectable.GetType().Name;
                        ConvaiLogger.Error(
                            $"[ConvaiCompositionRoot] Failed to inject {componentInfo}: {ex.Message}",
                            LogCategory.Bootstrap);

                        if (_strictMode) throw;
                    }
                }

                // Inject LipSync components (separate interface — uses IEventHub/ILogger, not IServiceContainer)
                InjectLipSyncComponents(container, includeInactive);

                // Register transcript UIs and listeners with the controller (not injection — registration)
                RegisterTranscriptUIs(transcriptUIs, transcriptListeners);

                sw.Stop();

                if (_debugLogging)
                {
                    ConvaiLogger.Debug(
                        $"[ConvaiCompositionRoot] Injection complete in {sw.ElapsedMilliseconds}ms: " +
                        $"{injectedCount}/{injectables.Count} injectable(s) injected",
                        LogCategory.Bootstrap);
                }
            }
            catch (Exception ex)
            {
                ConvaiLogger.Error(
                    $"[ConvaiCompositionRoot] Error during dependency injection: {ex.Message}",
                    LogCategory.Bootstrap);

                if (_strictMode) throw;
            }
        }

        /// <summary>
        ///     Discovers all <see cref="IInjectableLipSyncComponent" /> instances in the scene and
        ///     calls <see cref="IInjectableLipSyncComponent.Inject" /> with the resolved <see cref="IEventHub" /> and logger.
        /// </summary>
        private void InjectLipSyncComponents(IServiceContainer container, FindObjectsInactive includeInactive)
        {
            IReadOnlyList<IInjectableLipSyncComponent> discovered =
                InterfaceComponentQuery.FindObjects<IInjectableLipSyncComponent>(includeInactive);

            if (discovered.Count == 0) return;

            ConvaiServiceLocator.TryGet(out IEventHub eventHub);
            ConvaiServiceLocator.TryGet(out ILogger lipSyncLogger);

            if (_debugLogging)
            {
                ConvaiLogger.Debug(
                    $"[ConvaiCompositionRoot] Injecting {discovered.Count} LipSync component(s).",
                    LogCategory.LipSync);
            }

            for (int i = 0; i < discovered.Count; i++)
            {
                IInjectableLipSyncComponent component = discovered[i];
                var mb = component as MonoBehaviour;
                try
                {
                    component.Inject(eventHub, lipSyncLogger);

                    if (_debugLogging)
                    {
                        string name = mb != null
                            ? $"{mb.GetType().Name} on '{mb.gameObject.name}'"
                            : component.GetType().Name;
                        ConvaiLogger.Debug($"[ConvaiCompositionRoot] LipSync injected: {name}", LogCategory.LipSync);
                    }
                }
                catch (Exception ex)
                {
                    string name = mb != null
                        ? $"{mb.GetType().Name} on '{mb.gameObject.name}'"
                        : component.GetType().Name;
                    ConvaiLogger.Error(
                        $"[ConvaiCompositionRoot] Failed to inject LipSync component '{name}': {ex.Message}",
                        LogCategory.LipSync);

                    if (_strictMode) throw;
                }
            }
        }

        /// <summary>
        ///     Registers services that must be available before IInjectable components run.
        ///     Currently registers INarrativeSectionNameResolver via reflection (avoids circular assembly deps).
        /// </summary>
        private void RegisterPreInjectionServices(IServiceContainer container)
        {
            if (!container.IsRegistered<INarrativeSectionNameResolver>())
            {
                INarrativeSectionNameResolver resolver = CreateSectionNameResolver();
                if (resolver != null)
                {
                    container.Register(ServiceDescriptor.Singleton(resolver));

                    if (_debugLogging)
                    {
                        ConvaiLogger.Debug(
                            "[ConvaiCompositionRoot] Registered INarrativeSectionNameResolver for section name resolution.",
                            LogCategory.Bootstrap);
                    }
                }
            }
        }

        #endregion

        #region Transcript Registration

        /// <summary>
        ///     Registers ITranscriptUI and ITranscriptListener implementations with the TranscriptUIController.
        ///     This is registration (connecting implementations to a controller), not dependency injection.
        /// </summary>
        private void RegisterTranscriptUIs(List<ITranscriptUI> transcriptUIs,
            List<ITranscriptListener> transcriptListeners)
        {
            if (!ConvaiServiceLocator.TryGet(out TranscriptUIController controller))
            {
                if (_debugLogging)
                {
                    ConvaiLogger.Debug(
                        "[ConvaiCompositionRoot] TranscriptUIController not available, skipping transcript registration",
                        LogCategory.Bootstrap);
                }

                return;
            }

            foreach (ITranscriptUI transcriptUI in transcriptUIs)
            {
                try
                {
                    controller.Register(transcriptUI);

                    if (_debugLogging)
                    {
                        ConvaiLogger.Debug(
                            $"[ConvaiCompositionRoot] Registered ITranscriptUI: {transcriptUI.Identifier}",
                            LogCategory.Bootstrap);
                    }
                }
                catch (Exception ex)
                {
                    ConvaiLogger.Error(
                        $"[ConvaiCompositionRoot] Failed to register ITranscriptUI '{transcriptUI.Identifier}': {ex.Message}",
                        LogCategory.Bootstrap);
                    if (_strictMode) throw;
                }
            }

            foreach (ITranscriptListener listener in transcriptListeners)
            {
                try
                {
                    controller.RegisterListener(listener);

                    if (_debugLogging)
                    {
                        ConvaiLogger.Debug(
                            $"[ConvaiCompositionRoot] Registered ITranscriptListener: {listener.GetType().Name}",
                            LogCategory.Bootstrap);
                    }
                }
                catch (Exception ex)
                {
                    ConvaiLogger.Error(
                        $"[ConvaiCompositionRoot] Failed to register ITranscriptListener '{listener.GetType().Name}': {ex.Message}",
                        LogCategory.Bootstrap);
                    if (_strictMode) throw;
                }
            }

            if (_debugLogging)
            {
                ConvaiLogger.Debug(
                    $"[ConvaiCompositionRoot] Transcript registration: {transcriptUIs.Count} UI(s), {transcriptListeners.Count} listener(s)",
                    LogCategory.Bootstrap);
            }

            UpdateRuntimeTranscriptCapabilities(transcriptUIs);
        }

        /// <summary>
        ///     Updates runtime transcript mode capabilities based on registered transcript UIs.
        /// </summary>
        private void UpdateRuntimeTranscriptCapabilities(List<ITranscriptUI> transcriptUIs)
        {
            if (!ConvaiServiceLocator.TryGet(out IConvaiRuntimeSettingsService settingsService)) return;

            var supportedModes = new HashSet<ConvaiTranscriptMode> { ConvaiTranscriptMode.Chat };

            foreach (ITranscriptUI transcriptUI in transcriptUIs)
            {
                if (transcriptUI != null &&
                    TryMapIdentifierToTranscriptMode(transcriptUI.Identifier, out ConvaiTranscriptMode mode))
                    supportedModes.Add(mode);
            }

            settingsService.SetSupportedTranscriptModes(new List<ConvaiTranscriptMode>(supportedModes));
        }

        private static bool TryMapIdentifierToTranscriptMode(string identifier, out ConvaiTranscriptMode mode)
        {
            mode = ConvaiTranscriptMode.Chat;
            if (string.IsNullOrWhiteSpace(identifier)) return false;

            if (identifier.StartsWith("Subtitle", StringComparison.OrdinalIgnoreCase))
            {
                mode = ConvaiTranscriptMode.Subtitle;
                return true;
            }

            if (identifier.StartsWith("QuestionAnswer", StringComparison.OrdinalIgnoreCase) ||
                identifier.StartsWith("Question Answer", StringComparison.OrdinalIgnoreCase) ||
                identifier.StartsWith("QA", StringComparison.OrdinalIgnoreCase))
            {
                mode = ConvaiTranscriptMode.QuestionAnswer;
                return true;
            }

            if (identifier.StartsWith("Chat", StringComparison.OrdinalIgnoreCase))
            {
                mode = ConvaiTranscriptMode.Chat;
                return true;
            }

            return false;
        }

        #endregion

        #region Helpers

        /// <summary>
        ///     Validates that ConvaiServiceBootstrap has run and services are registered.
        ///     Provides clear, actionable error messages for common setup issues.
        /// </summary>
        private void ValidatePrerequisites()
        {
            if (!ConvaiServiceBootstrap.IsBootstrapped)
            {
                string message =
                    "[Convai SDK Setup Error] ConvaiServiceBootstrap has not run!\n\n" +
                    "ConvaiCompositionRoot requires ConvaiServiceBootstrap to run first.\n\n" +
                    "To fix this:\n" +
                    "1. Add ConvaiServiceBootstrap to your scene:\n" +
                    "   -> Use menu: GameObject > Convai > Setup Required Components\n" +
                    "   -> Or manually add ConvaiServiceBootstrap component to a GameObject\n\n" +
                    "2. Verify execution order:\n" +
                    "   -> ConvaiServiceBootstrap should have order -1000\n" +
                    "   -> ConvaiCompositionRoot should have order -500\n\n" +
                    "See: https://docs.convai.com/unity/quickstart";

                ConvaiLogger.Error(message, LogCategory.Bootstrap);
                if (_strictMode)
                    throw new InvalidOperationException("ConvaiServiceBootstrap has not run. See console for details.");
            }

            if (!ConvaiServiceLocator.IsInitialized)
            {
                string message =
                    "[Convai SDK Setup Error] ConvaiServiceLocator is not initialized!\n\n" +
                    "This usually means ConvaiServiceBootstrap failed to start.\n\n" +
                    "To fix this:\n" +
                    "1. Check the console for earlier errors from ConvaiServiceBootstrap\n" +
                    "2. Ensure ConvaiServiceBootstrap is enabled and active in the scene\n" +
                    "3. Try: GameObject > Convai > Setup Required Components\n\n" +
                    "See: https://docs.convai.com/unity/quickstart";

                ConvaiLogger.Error(message, LogCategory.Bootstrap);
                if (_strictMode)
                {
                    throw new InvalidOperationException(
                        "ConvaiServiceLocator is not initialized. See console for details.");
                }
            }
        }

        /// <summary>
        ///     Creates a section name resolver via reflection to avoid circular assembly dependencies.
        ///     The resolver lives in the Narrative module (Convai.Modules.Narrative) which cannot be
        ///     directly referenced by Runtime due to the dependency direction.
        /// </summary>
        /// <returns>The resolver instance, or null if the Narrative module is not available.</returns>
        private INarrativeSectionNameResolver CreateSectionNameResolver()
        {
            const string adapterTypeName = "Convai.Modules.Narrative.NarrativeSectionNameResolverAdapter";

            try
            {
                foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    Type adapterType = assembly.GetType(adapterTypeName, false);
                    if (adapterType != null && typeof(INarrativeSectionNameResolver).IsAssignableFrom(adapterType))
                    {
                        var resolver =
                            (INarrativeSectionNameResolver)Activator.CreateInstance(adapterType);

                        if (_debugLogging)
                        {
                            ConvaiLogger.Debug(
                                "[ConvaiCompositionRoot] Created NarrativeSectionNameResolverAdapter.",
                                LogCategory.Bootstrap);
                        }

                        return resolver;
                    }
                }

                if (_debugLogging)
                {
                    ConvaiLogger.Debug(
                        "[ConvaiCompositionRoot] Narrative module not found — section name resolution disabled.",
                        LogCategory.Bootstrap);
                }
            }
            catch (Exception ex)
            {
                ConvaiLogger.Warning(
                    $"[ConvaiCompositionRoot] Failed to create section name resolver: {ex.Message}",
                    LogCategory.Bootstrap);
            }

            return null;
        }

        #endregion
    }
}
