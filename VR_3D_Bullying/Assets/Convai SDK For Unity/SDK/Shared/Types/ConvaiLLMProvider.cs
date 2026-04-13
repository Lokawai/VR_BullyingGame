namespace Convai.Shared.Types
{
    /// <summary>
    ///     Defines the LLM provider options for Convai character sessions.
    /// </summary>
    /// <remarks>
    ///     This enum is serializable for Unity Inspector and provides type-safe
    ///     LLM provider selection. These are the client-facing provider options
    ///     that map to backend API values.
    /// </remarks>
    public enum ConvaiLLMProvider
    {
        /// <summary>
        ///     Dynamic provider selection based on character configuration.
        ///     The backend resolves the actual provider based on the character's response_model.
        ///     Maps to API value: "dynamic"
        /// </summary>
        Dynamic = 0,

        /// <summary>
        ///     Gemini Live realtime mode for low-latency conversations.
        ///     Maps to API value: "gemini-live"
        /// </summary>
        GeminiLive = 1,

        /// <summary>
        ///     BAML-enhanced Gemini service for structured outputs.
        ///     Maps to API value: "gemini-baml"
        /// </summary>
        GeminiBaml = 2
    }

    /// <summary>
    ///     Extension methods for <see cref="ConvaiLLMProvider" /> enum.
    /// </summary>
    public static class ConvaiLLMProviderExtensions
    {
        /// <summary>
        ///     Converts the enum value to its corresponding API string value.
        /// </summary>
        /// <param name="provider">The LLM provider enum value.</param>
        /// <returns>The API string representation.</returns>
        public static string ToApiString(this ConvaiLLMProvider provider)
        {
            return provider switch
            {
                ConvaiLLMProvider.Dynamic => "dynamic",
                ConvaiLLMProvider.GeminiLive => "gemini-live",
                ConvaiLLMProvider.GeminiBaml => "gemini-baml",
                _ => "dynamic"
            };
        }

        /// <summary>
        ///     Parses an API string value to the corresponding enum value.
        /// </summary>
        /// <param name="apiString">The API string value.</param>
        /// <returns>The corresponding enum value, defaulting to Dynamic if not recognized.</returns>
        public static ConvaiLLMProvider FromApiString(string apiString)
        {
            return apiString?.ToLowerInvariant() switch
            {
                "dynamic" => ConvaiLLMProvider.Dynamic,
                "gemini-live" => ConvaiLLMProvider.GeminiLive,
                "gemini-baml" => ConvaiLLMProvider.GeminiBaml,
                "gemini" => ConvaiLLMProvider.Dynamic,
                _ => ConvaiLLMProvider.Dynamic
            };
        }

        /// <summary>
        ///     Gets a user-friendly display name for the provider.
        /// </summary>
        /// <param name="provider">The LLM provider enum value.</param>
        /// <returns>A human-readable display name.</returns>
        public static string GetDisplayName(this ConvaiLLMProvider provider)
        {
            return provider switch
            {
                ConvaiLLMProvider.Dynamic => "Dynamic (Auto-select)",
                ConvaiLLMProvider.GeminiLive => "Gemini Live (Realtime)",
                ConvaiLLMProvider.GeminiBaml => "Gemini BAML (Structured)",
                _ => "Dynamic (Auto-select)"
            };
        }
    }
}
