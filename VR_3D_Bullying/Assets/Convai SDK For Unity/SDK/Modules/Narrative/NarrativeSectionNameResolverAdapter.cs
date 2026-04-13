using System.Collections.Generic;
using Convai.Domain.Abstractions;
using UnityEngine;

namespace Convai.Modules.Narrative
{
    /// <summary>
    ///     Adapter that implements <see cref="INarrativeSectionNameResolver" /> by querying
    ///     all <see cref="ConvaiNarrativeDesignManager" /> instances in the scene.
    /// </summary>
    /// <remarks>
    ///     This adapter is used by RTVIHandler to resolve human-readable section names
    ///     for debug logging purposes. It searches through all NarrativeDesignManagers
    ///     to find a matching section by ID.
    /// </remarks>
    public class NarrativeSectionNameResolverAdapter : INarrativeSectionNameResolver
    {
        private readonly List<ConvaiNarrativeDesignManager> _managers = new();
        private bool _initialized;

        /// <inheritdoc />
        public bool TryGetSectionName(string sectionId, out string sectionName)
        {
            sectionName = null;

            if (string.IsNullOrEmpty(sectionId)) return false;

            if (!_initialized) Refresh();

            foreach (ConvaiNarrativeDesignManager manager in _managers)
            {
                if (manager == null) continue;

                UnitySectionEventConfig config = manager.FindSectionConfig(sectionId);
                if (config != null && !string.IsNullOrEmpty(config.SectionName))
                {
                    sectionName = config.SectionName;
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        ///     Refreshes the list of narrative design managers from the scene.
        ///     Call this if managers are added or removed at runtime.
        /// </summary>
        public void Refresh()
        {
            _managers.Clear();
            ConvaiNarrativeDesignManager[] managers = Object.FindObjectsByType<ConvaiNarrativeDesignManager>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);

            if (managers != null) _managers.AddRange(managers);
            _initialized = true;
        }
    }
}
