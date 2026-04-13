using System.Collections;
using Convai.Runtime.Presentation.Views.Settings;
using Convai.Shared;
using Convai.Shared.Abstractions;
using Convai.Shared.DependencyInjection;
using UnityEngine;

namespace Convai.Sample.UI.Settings
{
    /// <summary>
    ///     Sample settings handler that spawns and injects the settings panel view.
    /// </summary>
    public class SettingsHandler : MonoBehaviour, IInjectable
    {
        [SerializeField] private SettingsPanel settingsPanelPrefab;

        private IServiceContainer _container;
        private Coroutine _dependencyWaitRoutine;
        private IMicrophoneDeviceService _microphoneDeviceService;

        private SettingsPanel _panel;

        private IConvaiSettingsPanelController _panelController;
        private bool _panelInjected;
        private IConvaiRuntimeSettingsService _runtimeSettingsService;

        private void Awake()
        {
            if (settingsPanelPrefab == null)
            {
                Debug.LogWarning("[SettingsHandler] Settings panel prefab is not assigned.");
                return;
            }

            _panel = Instantiate(settingsPanelPrefab, transform);

            if (!TryInitialize())
            {
                if (_dependencyWaitRoutine == null) _dependencyWaitRoutine = StartCoroutine(WaitForDependencies());

                Debug.LogWarning(
                    "[SettingsHandler] Runtime settings dependencies are unavailable; settings panel wiring will be deferred.");
            }
        }

        private void OnDestroy()
        {
            if (_dependencyWaitRoutine != null)
            {
                StopCoroutine(_dependencyWaitRoutine);
                _dependencyWaitRoutine = null;
            }
        }

        /// <inheritdoc />
        public void InjectServices(IServiceContainer container)
        {
            _container = container;
            container.TryGet(out _panelController);
            container.TryGet(out _runtimeSettingsService);
            container.TryGet(out _microphoneDeviceService);
        }

        public void Inject(
            IConvaiSettingsPanelController panelController = null,
            IConvaiRuntimeSettingsService runtimeSettingsService = null,
            IMicrophoneDeviceService microphoneDeviceService = null)
        {
            _panelController = panelController;
            _runtimeSettingsService = runtimeSettingsService;
            _microphoneDeviceService = microphoneDeviceService;
        }

        private bool TryInitialize()
        {
            EnsureDependencies();

            if (_panel == null ||
                _panelController == null ||
                _runtimeSettingsService == null ||
                _microphoneDeviceService == null)
                return false;

            if (!_panelInjected)
            {
                _panel.Inject(_panelController, _runtimeSettingsService, _microphoneDeviceService);
                _panelInjected = true;
            }

            return true;
        }

        private void EnsureDependencies()
        {
            if (_container == null) return;

            if (_panelController == null) _container.TryGet(out _panelController);

            if (_runtimeSettingsService == null) _container.TryGet(out _runtimeSettingsService);

            if (_microphoneDeviceService == null) _container.TryGet(out _microphoneDeviceService);
        }

        private IEnumerator WaitForDependencies()
        {
            yield return new WaitUntil(() =>
            {
                if (_container == null) return false;

                EnsureDependencies();
                return _panelController != null && _runtimeSettingsService != null && _microphoneDeviceService != null;
            });

            if (!TryInitialize())
            {
                Debug.LogWarning(
                    "[SettingsHandler] Dependencies still unavailable after service locator initialization.");
            }

            _dependencyWaitRoutine = null;
        }
    }
}
