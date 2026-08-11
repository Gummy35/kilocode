using Newtonsoft.Json;

namespace KiloVisualStudioExtension.ApiClient.Json
{
    /// <summary>
    /// Shared JSON serializer settings for the entire project.
    /// Both NSwag HTTP API and SSE deserialization use this single configuration.
    /// </summary>
    public static class KiloJsonSerializer
    {
        private static readonly JsonSerializerSettings _sharedSettings = CreateSharedSettings();

        /// <summary>
        /// Gets the shared JsonSerializerSettings instance used throughout the project.
        /// </summary>
        public static JsonSerializerSettings SharedSettings => _sharedSettings;

        /// <summary>
        /// Creates a new JsonSerializerSettings instance with Kilo-specific configuration.
        /// This is called once during static initialization and the result is reused.
        /// </summary>
        private static JsonSerializerSettings CreateSharedSettings()
        {
            var settings = new JsonSerializerSettings
            {
                NullValueHandling = NullValueHandling.Ignore,
                MissingMemberHandling = MissingMemberHandling.Ignore,
                ReferenceLoopHandling = ReferenceLoopHandling.Ignore
            };

            // Apply NSwag client customizations (via partial method in KiloApiClient)
            // This ensures SSE uses the same settings as HTTP API deserialization
            KiloApiClient.UpdateJsonSerializerSettingsForShared(settings);

            return settings;
        }

        /// <summary>
        /// Creates a JsonSerializer instance with shared settings.
        /// </summary>
        public static JsonSerializer Create()
        {
            return JsonSerializer.Create(_sharedSettings);
        }
    }
}
