using System.Collections.Generic;
using Convai.Runtime.Behaviors;
using Convai.Shared;
using Convai.Shared.DependencyInjection;
using UnityEngine;

namespace Convai.Runtime.Presentation.Services.Utilities
{
    /// <summary>
    ///     Base class for transcript filters that track Character visibility based on collider proximity.
    /// </summary>
    /// <remarks>
    ///     This class uses <see cref="IVisibleCharacterService" /> for character visibility tracking.
    ///     Part of the Unity layer infrastructure for transcript filtering.
    /// </remarks>
    public class TranscriptFilterBase : MonoBehaviour, IInjectable
    {
        /// <summary>List of character agents currently inside the filter collider.</summary>
        protected readonly List<IConvaiCharacterAgent> CharactersInsideColliderList = new();

        protected IServiceContainer _container;
        private SphereCollider _sphereCollider;

        /// <summary>
        ///     Service for tracking visible character IDs.
        /// </summary>
        protected IVisibleCharacterService VisibilityService { get; private set; }

        private void OnEnable()
        {
            if (VisibilityService == null && _container != null)
            {
                _container.TryGet(out IVisibleCharacterService fallback);
                VisibilityService = fallback;
            }

            if (TryGetComponent(out SphereCollider sphereCollider)) return;

            _sphereCollider = gameObject.AddComponent<SphereCollider>();
            _sphereCollider.radius = 5f;
            _sphereCollider.isTrigger = true;

            RaycastHit[] hits = Physics.SphereCastAll(transform.position, _sphereCollider.radius, Vector3.up);
            foreach (RaycastHit hit in hits)
            {
                if (hit.collider.TryGetComponent(out IConvaiCharacterAgent characterAgent))
                    CharactersInsideColliderList.Add(characterAgent);
            }
        }

        private void OnDisable()
        {
            if (_sphereCollider != null) Destroy(_sphereCollider);
        }

        private void OnTriggerEnter(Collider other)
        {
            if (other.TryGetComponent(out IConvaiCharacterAgent characterAgent))
                CharactersInsideColliderList.Add(characterAgent);
        }

        private void OnTriggerExit(Collider other)
        {
            if (other.TryGetComponent(out IConvaiCharacterAgent characterAgent))
            {
                VisibilityService?.RemoveCharacter(characterAgent.CharacterId);
                CharactersInsideColliderList.Remove(characterAgent);
            }
        }

        /// <inheritdoc />
        public void InjectServices(IServiceContainer container)
        {
            _container = container;
            container.TryGet(out IVisibleCharacterService visibilityService);
            Inject(visibilityService);
        }

        /// <summary>
        ///     Injects the visibility service dependency.
        ///     Called by the ConvaiManager pipeline during scene initialization.
        /// </summary>
        /// <param name="visibilityService">The visibility service to inject.</param>
        public void Inject(IVisibleCharacterService visibilityService) => VisibilityService = visibilityService;
    }
}
