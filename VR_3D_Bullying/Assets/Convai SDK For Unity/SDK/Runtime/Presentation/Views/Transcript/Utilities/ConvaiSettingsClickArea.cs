using Convai.Shared;
using Convai.Shared.Abstractions;
using Convai.Shared.DependencyInjection;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Convai.Runtime.Presentation.Views
{
    /// <summary>
    ///     Clickable area that opens the Convai runtime settings panel.
    ///     Used by Basic Sample transcript UIs (Chat / QA / Subtitle).
    /// </summary>
    [RequireComponent(typeof(Graphic))]
    public class ConvaiSettingsClickArea : MonoBehaviour, IPointerClickHandler, IInjectable
    {
        private IServiceContainer _container;
        private IConvaiSettingsPanelController _panelController;

        /// <inheritdoc />
        public void InjectServices(IServiceContainer container)
        {
            _container = container;
            container.TryGet(out IConvaiSettingsPanelController panelController);
            _panelController = panelController;
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            EnsurePanelController();
            _panelController?.Open();
        }

        public void Inject(IConvaiSettingsPanelController panelController = null) => _panelController = panelController;

        private void EnsurePanelController()
        {
            if (_panelController != null) return;

            _container?.TryGet(out _panelController);
        }
    }
}
