using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace KiloVisualStudioExtension.Tests
{
    public class TestHttpClient : HttpClient
    {
        private readonly Dictionary<string, Func<HttpRequestMessage, Task<HttpResponseMessage>>> _handlers = new();
        private readonly List<RequestRecord> _requests = new();

        public List<RequestRecord> Requests => _requests;

        public void RegisterHandler(string method, string path, Func<HttpRequestMessage, Task<HttpResponseMessage>> handler)
        {
            _handlers[$"{method}:{path}"] = handler;
        }

        public void RegisterJsonHandler(string method, string path, Func<HttpRequestMessage, JsonElement?> responseFactory)
        {
            _handlers[$"{method}:{path}"] = async _ =>
            {
                var response = responseFactory(null);
                var json = response.HasValue ? response.Value.GetRawText() : "{}";
                return new HttpResponseMessage
                {
                    StatusCode = System.Net.HttpStatusCode.OK,
                    Content = new StringContent(json, Encoding.UTF8, "application/json")
                };
            };
        }

        public override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            _requests.Add(new RequestRecord(
                request.Method.Method,
                request.RequestUri?.ToString(),
                request.Content
            ));

            var path = request.RequestUri?.PathAndQuery ?? "";
            var key = $"{request.Method.Method}:{path}";

            if (_handlers.TryGetValue(key, out var handler))
            {
                return handler(request);
            }

            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.NotFound));
        }
    }

    public class RequestRecord
    {
        public string Method { get; }
        public string? Url { get; }
        public HttpContent? Content { get; }

        public RequestRecord(string method, string? url, HttpContent? content)
        {
            Method = method;
            Url = url;
            Content = content;
        }
    }

    public class TestWebView
    {
        private readonly List<string> _postedMessages = new();

        public List<string> PostedMessages => _postedMessages;

        public void PostMessage(string message)
        {
            _postedMessages.Add(message);
        }

        public T? GetLastMessage<T>() where T : class
        {
            if (_postedMessages.Count == 0) return null;
            var json = _postedMessages[_postedMessages.Count - 1];
            return JsonSerializer.Deserialize<T>(json);
        }

        public List<T> GetAllMessages<T>() where T : class
        {
            var result = new List<T>();
            foreach (var msg in _postedMessages)
            {
                var deserialized = JsonSerializer.Deserialize<T>(msg);
                if (deserialized != null) result.Add(deserialized);
            }
            return result;
        }

        public void ClearMessages() => _postedMessages.Clear();
    }

    public class TestConnectionService
    {
        private readonly TestHttpClient _httpClient;

        public TestHttpClient HttpClient => _httpClient;

        public TestConnectionService(TestHttpClient? httpClient = null)
        {
            _httpClient = httpClient ?? new TestHttpClient();
        }

        public HttpClient GetHttpClient() => _httpClient;

        public bool IsConnected() => true;
    }

    public static class TestHelpers
    {
        public static SSEHelper CreateSSEHelper(Action<string>? postMessage = null)
        {
            var action = postMessage ?? (message => { });
            return new SSEHelper(action);
        }

        public static JsonElement CreateMessage(string id, string role, long time)
        {
            var json = $$"""
            {
                "info": {
                    "id": "{{id}}",
                    "sessionID": "s1",
                    "role": "{{role}}",
                    "time": { "created": {{time}} }
                },
                "parts": []
            }
            """;
            return JsonDocument.Parse(json).RootElement;
        }

        public static JsonElement CreateSession(string id, string directory, string title = "Session")
        {
            var json = $$"""
            {
                "id": "{{id}}",
                "slug": "session",
                "version": "1",
                "projectID": "project",
                "directory": "{{directory}}",
                "title": "{{title}}",
                "cost": 0,
                "tokens": { "input": 0, "output": 0, "reasoning": 0, "cache": { "read": 0, "write": 0 } },
                "time": { "created": 1, "updated": 1 }
            }
            """;
            return JsonDocument.Parse(json).RootElement;
        }

        public static JsonElement CreateMessagesResponse(params JsonElement[] messages)
        {
            var arrayBuilder = new StringBuilder("[");
            for (int i = 0; i < messages.Length; i++)
            {
                if (i > 0) arrayBuilder.Append(",");
                arrayBuilder.Append(messages[i].GetRawText());
            }
            arrayBuilder.Append("]");
            return JsonDocument.Parse(arrayBuilder.ToString()).RootElement;
        }

        public static void SetupCreateSessionHandler(TestHttpClient httpClient, string sessionID = "created-session")
        {
            httpClient.RegisterJsonHandler("POST", "/session", _ =>
            {
                var json = $$"""
                {
                    "id": "{{sessionID}}",
                    "title": "New Chat",
                    "time": { "created": 1, "updated": 1 }
                }
                """;
                return JsonDocument.Parse(json).RootElement;
            });
        }

        public static void SetupDeleteSessionHandler(TestHttpClient httpClient)
        {
            httpClient.RegisterJsonHandler("POST", "/session/delete", _ =>
            {
                return JsonDocument.Parse("{}").RootElement;
            });
        }

        public static void SetupMessagesHandler(TestHttpClient httpClient, params JsonElement[] messages)
        {
            httpClient.RegisterJsonHandler("GET", "/session", _ =>
            {
                return CreateMessagesResponse(messages);
            });
        }

        public static void SetupAbortHandler(TestHttpClient httpClient)
        {
            httpClient.RegisterJsonHandler("POST", "/session/abort", _ =>
            {
                return JsonDocument.Parse("{}").RootElement;
            });
        }
    }
}
