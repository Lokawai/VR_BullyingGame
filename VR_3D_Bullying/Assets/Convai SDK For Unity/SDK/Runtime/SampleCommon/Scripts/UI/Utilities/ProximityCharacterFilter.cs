using System.Collections.Generic;
using System.Linq;
using Convai.Runtime.Presentation.Services;
using Convai.Runtime.Presentation.Services.Utilities;
using UnityEngine;

namespace Convai.Sample.UI.Utilities
{
    /// <summary>
    ///     Sample filter that tracks multiple NPCs within the player's proximity and vision cone.
    ///     All NPCs that are within the collider radius AND within the vision cone are considered visible.
    /// </summary>
    /// <remarks>
    ///     This is a reference implementation in the Samples layer.
    ///     Inherits from TranscriptFilterBase (SDK infrastructure).
    /// </remarks>
    public class ProximityCharacterFilter : TranscriptFilterBase
    {
        /// <summary>
        ///     Player input service for accessing player agent capabilities.
        /// </summary>
        private IPlayerInputService _playerInputService;

        private void Start()
        {
            if (_playerInputService == null && _container != null) _container.TryGet(out _playerInputService);
        }

        private void FixedUpdate()
        {
            List<string> insideCollider = CharactersInsideColliderList
                .Select(x => x.CharacterId)
                .ToList();

            float visionConeAngle = 90f;

            if (_playerInputService?.Player is IVisionConeProvider visionProvider)
                visionConeAngle = visionProvider.VisionConeAngle;

            Transform viewerTransform = GetViewerTransform();
            if (viewerTransform == null) return;

            List<string> insideVisionCone = CharactersInsideColliderList
                .FindAll(x => IsLookingAtTarget(viewerTransform, (x as MonoBehaviour)?.transform, visionConeAngle))
                .Select(x => x.CharacterId)
                .ToList();

            if (VisibilityService == null) return;

            insideVisionCone.ForEach(x => VisibilityService.AddCharacter(x));
            insideCollider
                .Except(insideVisionCone)
                .ToList()
                .ForEach(x => VisibilityService.RemoveCharacter(x));
        }

        /// <summary>
        ///     Injects dependencies for the filter.
        /// </summary>
        /// <param name="visibilityService">The visibility service.</param>
        /// <param name="playerInputService">The player input service.</param>
        public void Inject(IVisibleCharacterService visibilityService, IPlayerInputService playerInputService)
        {
            base.Inject(visibilityService);
            _playerInputService = playerInputService;
        }

        /// <summary>
        ///     Gets the viewer transform from the player input service.
        /// </summary>
        private Transform GetViewerTransform()
        {
            if (_playerInputService?.Player is MonoBehaviour playerMono) return playerMono.transform;

            return null;
        }

        /// <summary>
        ///     Checks if the source transform is looking at the target within the specified angle.
        /// </summary>
        private static bool IsLookingAtTarget(Transform source, Transform target, float angle)
        {
            if (source == null || target == null) return false;

            Vector3 directionToTarget = (target.position - source.position).normalized;
            float dotProduct = Vector3.Dot(source.forward, directionToTarget);
            float angleToTarget = Mathf.Acos(dotProduct) * Mathf.Rad2Deg;
            return angleToTarget <= angle / 2f;
        }
    }

    /// <summary>
    ///     Interface for player agents that provide vision cone angle.
    /// </summary>
    public interface IVisionConeProvider
    {
        public float VisionConeAngle { get; }
    }
}
