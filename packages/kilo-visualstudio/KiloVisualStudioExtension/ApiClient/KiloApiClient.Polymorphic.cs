using Newtonsoft.Json;

namespace KiloVisualStudioExtension.ApiClient
{
    /// <summary>
    /// Partial class implementation for NSwag serializer settings customization.
    /// Provides a shared settings mechanism that both HTTP API and SSE can use.
    /// </summary>
    public partial class KiloApiClient
    {
        /// <summary>
        /// NSwag extension point for customizing JSON serializer settings.
        /// Called during client initialization to apply Kilo-specific configuration.
        /// </summary>
        static partial void UpdateJsonSerializerSettings(JsonSerializerSettings settings)
        {
            // Apply any NSwag-specific customizations here
            // Currently empty - NSwag uses default settings
        }

        /// <summary>
        /// Internal method for creating shared serializer settings used by both
        /// NSwag HTTP API and SSE deserialization.
        /// </summary>
        internal static void UpdateJsonSerializerSettingsForShared(JsonSerializerSettings settings)
        {
            // Apply the same customizations as UpdateJsonSerializerSettings
            UpdateJsonSerializerSettings(settings);
        }
    }
}
