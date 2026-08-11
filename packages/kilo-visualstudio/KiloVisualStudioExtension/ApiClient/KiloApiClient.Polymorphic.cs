using Newtonsoft.Json;
using KiloVisualStudioExtension.ApiClient.Polymorphic;

namespace KiloVisualStudioExtension.ApiClient
{
    /// <summary>
    /// Partial class implementation that registers polymorphic JSON converters.
    /// This extends the generated KiloApiClient without modifying generated code.
    /// 
    /// The converters enable discriminator-based deserialization for:
    /// - ToolState (status discriminator)
    /// - Part (type discriminator)
    /// - Message (role discriminator)
    /// </summary>
    public partial class KiloApiClient
    {
        /// <summary>
        /// NSwag extension point for customizing JSON serializer settings.
        /// Registers polymorphic converters for anyOf/oneOf schema types.
        /// </summary>
        static partial void UpdateJsonSerializerSettings(JsonSerializerSettings settings)
        {
            // Register polymorphic converters for anyOf schema types
            settings.Converters.Add(new ToolStateConverter());
            settings.Converters.Add(new PartConverter());
            settings.Converters.Add(new MessageConverter());
        }

        /// <summary>
        /// Test helper to apply serializer settings configuration.
        /// Used by unit tests to verify polymorphic deserialization behavior.
        /// </summary>
        internal static void UpdateJsonSerializerSettingsForTest(JsonSerializerSettings settings)
        {
            UpdateJsonSerializerSettings(settings);
        }
    }
}
