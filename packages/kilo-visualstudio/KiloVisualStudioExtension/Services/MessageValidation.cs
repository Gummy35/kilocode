namespace KiloVisualStudioExtension.Services
{
    /// <summary>
    /// Utility methods for message validation.
    /// </summary>
    public static class MessageValidation
    {
        /// <summary>
        /// Checks if a role is valid for a message.
        /// Valid roles are: user, assistant, system.
        /// </summary>
        public static bool IsValidRole(string role)
        {
            return role == "user" || role == "assistant" || role == "system";
        }
    }
}
