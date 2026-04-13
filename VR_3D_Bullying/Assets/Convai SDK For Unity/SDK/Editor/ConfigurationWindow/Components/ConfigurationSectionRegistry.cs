using System.Collections.Generic;
using System.Linq;
using Convai.Editor.ConfigurationWindow.Components.Sections;
using Convai.Editor.ConfigurationWindow.Components.Sections.LoggerSettings;
using Convai.Editor.ConfigurationWindow.Components.Sections.LongTermMemory;
#if CONVAI_ENABLE_SERVER_ANIMATION
using Convai.Editor.ConfigurationWindow.Components.Sections.ServerAnimation;
#endif

namespace Convai.Editor.ConfigurationWindow.Components
{
    /// <summary>
    ///     Central registry for configuration window sections.
    /// </summary>
    public static class ConfigurationSectionRegistry
    {
        private static readonly List<ConfigurationSectionDescriptor> _sections = BuildSectionDescriptors();

        /// <summary>
        ///     Gets all descriptors (enabled and disabled).
        /// </summary>
        public static IReadOnlyList<ConfigurationSectionDescriptor> AllSections => _sections;

        /// <summary>
        ///     Gets enabled descriptors in navigation order.
        /// </summary>
        public static IReadOnlyList<ConfigurationSectionDescriptor> GetEnabledSections() =>
            _sections.Where(section => section.IsEnabled).ToArray();

        private static List<ConfigurationSectionDescriptor> BuildSectionDescriptors()
        {
            List<ConfigurationSectionDescriptor> sections = new()
            {
                new ConfigurationSectionDescriptor(
                    ConvaiWelcomeSection.SECTION_NAME,
                    "Welcome",
                    context => new ConvaiWelcomeSection(context)),
                new ConfigurationSectionDescriptor(
                    ConvaiAccountSection.SECTION_NAME,
                    "Account",
                    context => new ConvaiAccountSection(context)),
                new ConfigurationSectionDescriptor(
                    ConvaiLoggerSettingSection.SECTION_NAME,
                    "Logger Settings",
                    context => new ConvaiLoggerSettingSection()),
                new ConfigurationSectionDescriptor(
                    ConvaiLongTermMemorySection.SECTION_NAME,
                    "Long Term Memory",
                    context => new ConvaiLongTermMemorySection(context),
                    true),

#if CONVAI_ENABLE_SERVER_ANIMATION
                new ConfigurationSectionDescriptor(
                    ConvaiServerAnimationSection.SECTION_NAME,
                    "Server Animation",
                    context => new ConvaiServerAnimationSection(context),
                    requiresApiKey: true,
                    isEnabled: true),
#else
                new ConfigurationSectionDescriptor(
                    "server-animation",
                    "Server Animation",
                    _ => null,
                    true,
                    false),
#endif

#if CONVAI_ENABLE_UPDATES_SECTION
                new ConfigurationSectionDescriptor(
                    ConvaiUpdatesSection.SECTION_NAME,
                    "Updates",
                    _ => new ConvaiUpdatesSection(),
                    requiresApiKey: false,
                    isEnabled: true),
#else
                new ConfigurationSectionDescriptor(
                    "updates",
                    "Updates",
                    _ => null,
                    isEnabled: false),
#endif

                new ConfigurationSectionDescriptor(
                    ConvaiContactSection.SECTION_NAME,
                    "Contact Us",
                    _ => new ConvaiContactSection())
            };

            return sections;
        }
    }
}
