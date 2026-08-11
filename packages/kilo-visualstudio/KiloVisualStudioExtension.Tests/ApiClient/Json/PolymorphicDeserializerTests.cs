using System;
using FluentAssertions;
using KiloVisualStudioExtension.ApiClient;
using KiloVisualStudioExtension.ApiClient.Json;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace KiloVisualStudioExtension.Tests.ApiClient.Json
{
    /// <summary>
    /// Tests for polymorphic deserialization of NSwag-generated models.
    /// Verifies that discriminator-based deserialization produces correct concrete types.
    /// </summary>
    public class PolymorphicDeserializerTests
    {
        private readonly JsonSerializer _serializer;

        public PolymorphicDeserializerTests()
        {
            _serializer = KiloJsonSerializer.Create();
        }

        #region ToolState Tests

        [Fact]
        public void DeserializeToolState_Pending_ReturnsToolStatePending()
        {
            // Arrange
            var json = @"{""status"":""pending"",""input"":{},""raw"":""test command""}";
            var token = JToken.Parse(json);

            // Act
            var result = PolymorphicDeserializer.DeserializeToolState(token, _serializer);

            // Assert
            result.Should().BeOfType<ToolStatePending>();
            var pending = (ToolStatePending)result;
            pending.Status.Should().Be(ToolStatePendingStatus.Pending);
            pending.Raw.Should().Be("test command");
        }

        [Fact]
        public void DeserializeToolState_Running_ReturnsToolStateRunning()
        {
            // Arrange
            var json = @"{""status"":""running"",""input"":{},""title"":""Executing"",""time"":{""start"":123}}";
            var token = JToken.Parse(json);

            // Act
            var result = PolymorphicDeserializer.DeserializeToolState(token, _serializer);

            // Assert
            result.Should().BeOfType<ToolStateRunning>();
            var running = (ToolStateRunning)result;
            running.Status.Should().Be(ToolStateRunningStatus.Running);
            running.Title.Should().Be("Executing");
        }

        [Fact]
        public void DeserializeToolState_Completed_ReturnsToolStateCompleted()
        {
            // Arrange
            var json = @"{""status"":""completed"",""input"":{},""output"":""result"",""title"":""Done"",""metadata"":{},""time"":{""start"":100,""end"":200}}";
            var token = JToken.Parse(json);

            // Act
            var result = PolymorphicDeserializer.DeserializeToolState(token, _serializer);

            // Assert
            result.Should().BeOfType<ToolStateCompleted>();
            var completed = (ToolStateCompleted)result;
            completed.Status.Should().Be(ToolStateCompletedStatus.Completed);
            completed.Output.Should().Be("result");
            completed.Title.Should().Be("Done");
        }

        [Fact]
        public void DeserializeToolState_Error_ReturnsToolStateError()
        {
            // Arrange
            var json = @"{""status"":""error"",""input"":{},""error"":""Something went wrong"",""time"":{""start"":50,""end"":150}}";
            var token = JToken.Parse(json);

            // Act
            var result = PolymorphicDeserializer.DeserializeToolState(token, _serializer);

            // Assert
            result.Should().BeOfType<ToolStateError>();
            var error = (ToolStateError)result;
            error.Status.Should().Be(ToolStateErrorStatus.Error);
            error.Error.Should().Be("Something went wrong");
        }

        [Fact]
        public void DeserializeToolState_UnknownStatus_ThrowsException()
        {
            // Arrange
            var json = @"{""status"":""unknown"",""input"":{}}";
            var token = JToken.Parse(json);

            // Act/Assert
            var ex = Assert.Throws<JsonSerializationException>(() => 
                PolymorphicDeserializer.DeserializeToolState(token, _serializer));
            ex.Message.Should().Contain("unknown");
        }

        #endregion

        #region Part Tests

        [Fact]
        public void DeserializePart_Text_ReturnsTextPart()
        {
            // Arrange
            var json = @"{""id"":""prt-123"",""sessionID"":""ses-456"",""messageID"":""msg-789"",""type"":""text"",""text"":""Hello world""}";
            var token = JToken.Parse(json);

            // Act
            var result = PolymorphicDeserializer.DeserializePart(token, _serializer);

            // Assert
            result.Should().BeOfType<TextPart>();
            var textPart = (TextPart)result;
            textPart.Id.Should().Be("prt-123");
            textPart.Text.Should().Be("Hello world");
        }

        [Fact]
        public void DeserializePart_Reasoning_ReturnsReasoningPart()
        {
            // Arrange
            var json = @"{""id"":""prt-123"",""sessionID"":""ses-456"",""messageID"":""msg-789"",""type"":""reasoning"",""text"":""Thinking..."",""time"":{""start"":100}}";
            var token = JToken.Parse(json);

            // Act
            var result = PolymorphicDeserializer.DeserializePart(token, _serializer);

            // Assert
            result.Should().BeOfType<ReasoningPart>();
            var reasoningPart = (ReasoningPart)result;
            reasoningPart.Text.Should().Be("Thinking...");
        }

        [Fact]
        public void DeserializePart_File_ReturnsFilePart()
        {
            // Arrange
            var json = @"{""id"":""prt-123"",""sessionID"":""ses-456"",""messageID"":""msg-789"",""type"":""file"",""mime"":""text/plain"",""url"":""http://example.com/file.txt""}";
            var token = JToken.Parse(json);

            // Act
            var result = PolymorphicDeserializer.DeserializePart(token, _serializer);

            // Assert
            result.Should().BeOfType<FilePart>();
            var filePart = (FilePart)result;
            filePart.Mime.Should().Be("text/plain");
            filePart.Url.Should().Be("http://example.com/file.txt");
        }

        [Fact]
        public void DeserializePart_Tool_ReturnsToolPart()
        {
            // Arrange
            var json = @"{""id"":""prt-123"",""sessionID"":""ses-456"",""messageID"":""msg-789"",""type"":""tool"",""callID"":""call-123"",""tool"":""task"",""state"":{""status"":""running"",""input"":{},""time"":{""start"":100}}}";
            var token = JToken.Parse(json);

            // Act
            var result = PolymorphicDeserializer.DeserializePart(token, _serializer);

            // Assert
            result.Should().BeOfType<ToolPart>();
            var toolPart = (ToolPart)result;
            toolPart.Tool.Should().Be("task");
            // State property is typed as ToolState but contains ToolStateRunning at runtime
            // Use separate deserialization to get the concrete type
            var stateToken = token["state"];
            var state = PolymorphicDeserializer.DeserializeToolState(stateToken!, _serializer);
            state.Should().BeOfType<ToolStateRunning>();
        }

        [Fact]
        public void DeserializePart_Subtask_ReturnsSubtaskPart()
        {
            // Arrange
            var json = @"{""id"":""prt-123"",""sessionID"":""ses-456"",""messageID"":""msg-789"",""type"":""subtask"",""prompt"":""Do something"",""description"":""Description"",""agent"":""primary""}";
            var token = JToken.Parse(json);

            // Act
            var result = PolymorphicDeserializer.DeserializePart(token, _serializer);

            // Assert
            result.Should().BeOfType<SubtaskPart>();
            var subtaskPart = (SubtaskPart)result;
            subtaskPart.Prompt.Should().Be("Do something");
            subtaskPart.Description.Should().Be("Description");
        }

        [Fact]
        public void DeserializePart_UnknownType_ThrowsException()
        {
            // Arrange
            var json = @"{""id"":""prt-123"",""type"":""unknown""}";
            var token = JToken.Parse(json);

            // Act/Assert
            var ex = Assert.Throws<JsonSerializationException>(() => 
                PolymorphicDeserializer.DeserializePart(token, _serializer));
            ex.Message.Should().Contain("unknown");
        }

        #endregion

        #region Message Tests

        [Fact]
        public void DeserializeMessage_User_ReturnsUserMessage()
        {
            // Arrange
            var json = @"{""id"":""msg-123"",""sessionID"":""ses-456"",""role"":""user"",""time"":{""created"":123},""agent"":""primary"",""model"":{""providerID"":""test"",""modelID"":""test""}}";
            var token = JToken.Parse(json);

            // Act
            var result = PolymorphicDeserializer.DeserializeMessage(token, _serializer);

            // Assert
            result.Should().BeOfType<UserMessage>();
            var userMessage = (UserMessage)result;
            userMessage.Role.Should().Be(UserMessageRole.User);
            userMessage.Agent.Should().Be("primary");
        }

        [Fact]
        public void DeserializeMessage_Assistant_ReturnsAssistantMessage()
        {
            // Arrange
            var json = @"{""id"":""msg-123"",""sessionID"":""ses-456"",""role"":""assistant"",""time"":{""created"":123,""completed"":200},""parentID"":""msg-000"",""modelID"":""model-1"",""providerID"":""test"",""mode"":""chat"",""agent"":""primary"",""path"":{""cwd"":""/test"",""root"":""/test""},""cost"":0.01,""tokens"":{""input"":100,""output"":50,""reasoning"":0,""cache"":{""read"":0,""write"":0}}}";
            var token = JToken.Parse(json);

            // Act
            var result = PolymorphicDeserializer.DeserializeMessage(token, _serializer);

            // Assert
            result.Should().BeOfType<AssistantMessage>();
            var assistantMessage = (AssistantMessage)result;
            assistantMessage.Role.Should().Be(AssistantMessageRole.Assistant);
            assistantMessage.Agent.Should().Be("primary");
        }

        [Fact]
        public void DeserializeMessage_UnknownRole_ThrowsException()
        {
            // Arrange
            var json = @"{""id"":""msg-123"",""role"":""unknown""}";
            var token = JToken.Parse(json);

            // Act/Assert
            var ex = Assert.Throws<JsonSerializationException>(() => 
                PolymorphicDeserializer.DeserializeMessage(token, _serializer));
            ex.Message.Should().Contain("unknown");
        }

        #endregion

        #region Nested Polymorphism Tests

        [Fact]
        public void DeserializePart_Tool_WithNestedToolStateCompleted()
        {
            // Arrange
            var json = @"{
                ""id"":""prt-123"",
                ""sessionID"":""ses-456"",
                ""messageID"":""msg-789"",
                ""type"":""tool"",
                ""callID"":""call-123"",
                ""tool"":""task"",
                ""state"":{
                    ""status"":""completed"",
                    ""input"":{},
                    ""output"":""done"",
                    ""title"":""Finished"",
                    ""metadata"":{},
                    ""time"":{""start"":100,""end"":200}
                }
            }";
            var token = JToken.Parse(json);

            // Act
            var result = PolymorphicDeserializer.DeserializePart(token, _serializer);

            // Assert
            result.Should().BeOfType<ToolPart>();
            var toolPart = (ToolPart)result;
            // State property is typed as ToolState but contains ToolStateCompleted at runtime
            // Use separate deserialization to get the concrete type
            var stateToken = token["state"];
            var state = PolymorphicDeserializer.DeserializeToolState(stateToken!, _serializer);
            state.Should().BeOfType<ToolStateCompleted>();
            var completedState = (ToolStateCompleted)state;
            completedState.Output.Should().Be("done");
        }

        #endregion
    }
}
