using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;

namespace KiloVisualStudioExtension.ApiClient
{
    /// <summary>
    /// Partial class extension for KiloApiClient that implements Basic Authentication.
    /// This file contains authentication logic separate from the generated client code.
    /// 
    /// Authentication mechanism:
    /// - Uses HTTP Basic Authentication with credentials "kilo:&lt;password&gt;"
    /// - Password is injected via constructor parameter (not hardcoded)
    /// - Applied to every API request via the PrepareRequest partial method
    /// - Works with dynamically discovered CLI BaseUrl from CliBackendManager
    /// </summary>
    public partial class KiloApiClient
    {
        private readonly string? _password;

        /// <summary>
        /// Creates a new instance of KiloApiClient with Basic Authentication.
        /// </summary>
        /// <param name="baseUrl">The base URL of the CLI backend (e.g., "http://127.0.0.1:9999").</param>
        /// <param name="password">The password for authentication (extracted from CLI startup).</param>
        public KiloApiClient(string baseUrl, string password)
        {
            BaseUrl = baseUrl;
            _password = password;
        }

        /// <summary>
        /// Prepares the HTTP request by adding Basic Authentication header.
        /// This partial method is called by the generated code before each request.
        /// </summary>
        /// <param name="client">The HttpClient being used.</param>
        /// <param name="request">The HttpRequestMessage being prepared.</param>
        /// <param name="url">The request URL.</param>
        partial void PrepareRequest(HttpClient client, HttpRequestMessage request, string url)
        {
            if (!string.IsNullOrEmpty(_password))
            {
                var credentials = Convert.ToBase64String(
                    Encoding.ASCII.GetBytes($"kilo:{_password}"));
                request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            }
        }

        /// <summary>
        /// Prepares the HTTP request by adding Basic Authentication header.
        /// This overload is also called by the generated code.
        /// </summary>
        /// <param name="client">The HttpClient being used.</param>
        /// <param name="request">The HttpRequestMessage being prepared.</param>
        /// <param name="urlBuilder">The StringBuilder containing the URL.</param>
        partial void PrepareRequest(HttpClient client, HttpRequestMessage request, System.Text.StringBuilder urlBuilder)
        {
            if (!string.IsNullOrEmpty(_password))
            {
                var credentials = Convert.ToBase64String(
                    Encoding.ASCII.GetBytes($"kilo:{_password}"));
                request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            }
        }
    }
}
