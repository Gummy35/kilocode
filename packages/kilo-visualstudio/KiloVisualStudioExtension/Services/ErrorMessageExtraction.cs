using System;
using System.Collections;

namespace KiloVisualStudioExtension.Services
{
    /// <summary>
    /// Utility methods for extracting error messages from various error types.
    /// Handles Exception instances, strings, dictionaries, and SDK error shapes.
    /// </summary>
    public static class ErrorMessageExtraction
    {
        /// <summary>
        /// Extracts a human-readable error message from various error types.
        /// </summary>
        /// <param name="error">The error object to extract message from</param>
        /// <returns>Human-readable error message</returns>
        public static string GetErrorMessage(object? error)
        {
            if (error == null)
                return "null";

            if (error is Exception ex)
                return ex.Message;

            if (error is string str)
                return str;

            var dict = error as IDictionary;
            if (dict != null)
            {
                if (dict.Contains("message"))
                    return dict["message"]?.ToString() ?? "[object Object]";
                if (dict.Contains("error"))
                    return dict["error"]?.ToString() ?? "[object Object]";
            }

            return error.ToString() ?? "[object Object]";
        }
    }
}
