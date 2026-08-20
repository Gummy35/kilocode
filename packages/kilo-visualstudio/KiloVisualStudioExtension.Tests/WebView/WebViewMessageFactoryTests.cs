using System;
using System.Collections.Generic;
using FluentAssertions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;
using KiloVisualStudioExtension.WebView.Generated;
using KiloVisualStudioExtension.ApiClient.Json;
using KiloVisualStudioExtension.KiloExtensionDTOs.WebviewMessages;
using KiloVisualStudioExtension.KiloExtensionDTOs.ExtensionMessages;

namespace KiloVisualStudioExtension.Tests.WebView
{
    public class WebViewMessageFactoryTests
    {
        private readonly JsonSerializer _serializer;

        public WebViewMessageFactoryTests()
        {
            _serializer = KiloJsonSerializer.Create();
        }

        [Fact]
        public void Deserialize_SendMessageRequest_Success()
        {
            // Arrange
            var json = @"{
                ""type"": ""sendMessage"",
                ""text"": ""Hello, world!"",
                ""messageID"": ""msg-123"",
                ""sessionID"": ""session-456""
            }";

            // Act
            var result = WebViewMessageFactory.Deserialize<SendMessageRequest>(json);

            // Assert
            result.Should().NotBeNull();
            result.Type.Should().Be("sendMessage");
            result.Text.Should().Be("Hello, world!");
            result.MessageID.Should().Be("msg-123");
            result.SessionID.Should().Be("session-456");
        }

        [Fact]
        public void Deserialize_AbortRequest_Success()
        {
            // Arrange
            var json = @"{
                ""type"": ""abort"",
                ""sessionID"": ""session-789""
            }";

            // Act
            var result = WebViewMessageFactory.Deserialize<AbortRequest>(json);

            // Assert
            result.Should().NotBeNull();
            result.Type.Should().Be("abort");
            result.SessionID.Should().Be("session-789");
        }

        [Fact]
        public void Deserialize_SessionCreatedMessage_Success()
        {
            // Arrange
            var json = @"{
                ""type"": ""sessionCreated"",
                ""session"": {
                    ""id"": ""session-abc"",
                    ""createdAt"": ""2026-08-11T12:00:00Z"",
                    ""updatedAt"": ""2026-08-11T12:00:00Z""
                }
            }";

            // Act
            var result = WebViewMessageFactory.Deserialize<SessionCreatedMessage>(json);

            // Assert
            result.Should().NotBeNull();
            result.Type.Should().Be("sessionCreated");
            result.Session.Should().NotBeNull();
        }

        [Fact]
        public void Deserialize_UnknownMessageType_ThrowsException()
        {
            // Arrange
            var json = @"{
                ""type"": ""unknownMessageType"",
                ""data"": ""test""
            }";

            // Act
            var act = () => WebViewMessageFactory.Deserialize<object>(json);

            // Assert
            act.Should().Throw<JsonSerializationException>()
                .WithMessage("*Unknown message type*");
        }

        [Fact]
        public void Deserialize_MissingTypeProperty_ThrowsException()
        {
            // Arrange
            var json = @"{
                ""data"": ""test""
            }";

            // Act
            var act = () => WebViewMessageFactory.Deserialize<object>(json);

            // Assert
            act.Should().Throw<JsonSerializationException>()
                .WithMessage("*Unknown message type*");
        }

        [Fact]
        public void Deserialize_NullableProperties_HandlesNull()
        {
            // Arrange
            var json = @"{
                ""type"": ""sendMessage"",
                ""text"": ""Test"",
                ""messageID"": null,
                ""sessionID"": null
            }";

            // Act
            var result = WebViewMessageFactory.Deserialize<SendMessageRequest>(json);

            // Assert
            result.Should().NotBeNull();
            result.Type.Should().Be("sendMessage");
            result.Text.Should().Be("Test");
            result.MessageID.Should().BeNull();
            result.SessionID.Should().BeNull();
        }

        [Fact]
        public void Deserialize_ArrayProperties_Success()
        {
            // Arrange
            var json = @"{
                ""type"": ""agentManager.setTabOrder"",
                ""key"": ""local"",
                ""order"": [""session-1"", ""session-2"", ""session-3""]
            }";

            // Act
            var result = WebViewMessageFactory.Deserialize<SetTabOrderRequest>(json);

            // Assert
            result.Should().NotBeNull();
            result.Type.Should().Be("agentManager.setTabOrder");
            result.Order.Should().NotBeNull();
        }

        [Fact]
        public void Deserialize_ObjectProperties_Success()
        {
            // Arrange
            var json = @"{
                ""type"": ""updateConfig"",
                ""config"": {
                    ""key1"": ""value1"",
                    ""key2"": 123
                }
            }";

            // Act
            var result = WebViewMessageFactory.Deserialize<UpdateConfigMessage>(json);

            // Assert
            result.Should().NotBeNull();
            result.Type.Should().Be("updateConfig");
            result.Config.Should().NotBeNull();
        }

        [Fact]
        public void RoundTrip_SerializeThenDeserialize_PreservesData()
        {
            // Arrange
            var original = new SendMessageRequest
            {
                Type = "sendMessage",
                Text = "Test message",
                MessageID = "msg-123",
                SessionID = "session-456"
            };

            // Act
            var json = JsonConvert.SerializeObject(original);
            var result = WebViewMessageFactory.Deserialize<SendMessageRequest>(json);

            // Assert
            result.Should().NotBeNull();
            result.Type.Should().Be(original.Type);
            result.Text.Should().Be(original.Text);
            result.MessageID.Should().Be(original.MessageID);
            result.SessionID.Should().Be(original.SessionID);
        }

        [Fact]
        public void Deserialize_JToken_ViaOverload_Success()
        {
            // Arrange
            var json = @"{
                ""type"": ""abort"",
                ""sessionID"": ""session-test""
            }";
            var token = JToken.Parse(json);

            // Act
            var result = WebViewMessageFactory.Deserialize<AbortRequest>(token);

            // Assert
            result.Should().NotBeNull();
            result.Type.Should().Be("abort");
            result.SessionID.Should().Be("session-test");
        }
    }

    public class GeneratedDtoSerializationTests
    {
        private readonly JsonSerializer _serializer;

        public GeneratedDtoSerializationTests()
        {
            _serializer = KiloJsonSerializer.Create();
        }

        [Fact]
        public void Serialize_SendMessageRequest_ProducesCorrectJson()
        {
            // Arrange
            var message = new SendMessageRequest
            {
                Type = "sendMessage",
                Text = "Hello",
                MessageID = "msg-1"
            };

            // Act
            var json = JsonConvert.SerializeObject(message);
            var deserialized = JsonConvert.DeserializeObject<SendMessageRequest>(json);

            // Assert
            deserialized.Should().NotBeNull();
            deserialized.Type.Should().Be(message.Type);
            deserialized.Text.Should().Be(message.Text);
            deserialized.MessageID.Should().Be(message.MessageID);
        }

        [Fact]
        public void Serialize_SessionCreatedMessage_ProducesCorrectJson()
        {
            // Arrange
            var message = new SessionCreatedMessage
            {
                Type = "sessionCreated"
            };

            // Act
            var json = JsonConvert.SerializeObject(message);
            var deserialized = JsonConvert.DeserializeObject<SessionCreatedMessage>(json);

            // Assert
            deserialized.Should().NotBeNull();
            deserialized.Type.Should().Be(message.Type);
        }

        [Fact]
        public void PropertyNames_MatchWireFormat()
        {
            // Arrange
            var message = new SendMessageRequest
            {
                Type = "sendMessage",
                Text = "Test",
                MessageID = "msg-123",
                SessionID = "session-456"
            };

            // Act
            var json = JsonConvert.SerializeObject(message);

            // Assert - Verify camelCase property names are preserved
            json.Should().Contain("\"type\"");
            json.Should().Contain("\"text\"");
            json.Should().Contain("\"messageID\"");
            json.Should().Contain("\"sessionID\"");
        }
    }
}
