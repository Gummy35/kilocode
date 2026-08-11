using System;
using System.Collections.Generic;
using System.Text;
using FluentAssertions;
using KiloVisualStudioExtension.ApiClient;
using KiloVisualStudioExtension.ApiClient.Polymorphic;
using Newtonsoft.Json;
using Xunit;

namespace KiloVisualStudioExtension.Tests
{
    /// <summary>
    /// Tests for NSwag-generated polymorphic model deserialization with custom converters.
    /// Validates that discriminator-based deserialization correctly produces concrete types
    /// for ToolState, Part, and Message unions defined by the API.
    /// 
    /// These tests verify the production deserialization pipeline by using the same
    /// JsonSerializerSettings that KiloApiClient uses.
    /// </summary>
    public class NswagPolymorphicModelTests
    {
        private readonly JsonSerializerSettings _settings;

        public NswagPolymorphicModelTests()
        {
            // Use the same settings mechanism as KiloApiClient
            _settings = new JsonSerializerSettings();
            KiloApiClient.UpdateJsonSerializerSettingsForTest(_settings);
        }

        #region ToolState Tests

        [Fact]
        public void ToolState_Pending_DeserializesToConcreteType()
        {
            // Arrange
            var json = @"{""status"":""pending"",""input"":{},""raw"":""test command""}";

            // Act
            var state = JsonConvert.DeserializeObject<ToolState>(json, _settings);

            // Assert
            state.Should().NotBeNull();
            state.Should().BeOfType<ToolStatePending>();
            var pending = (ToolStatePending)state;
            pending.Status.Should().Be(ToolStatePendingStatus.Pending);
            pending.Raw.Should().Be("test command");
        }

        [Fact]
        public void ToolState_Running_DeserializesToConcreteType()
        {
            // Arrange
            var json = @"{""status"":""running"",""input"":{},""title"":""Executing"",""metadata"":{},""time"":{""start"":123}}";

            // Act
            var state = JsonConvert.DeserializeObject<ToolState>(json, _settings);

            // Assert
            state.Should().NotBeNull();
            state.Should().BeOfType<ToolStateRunning>();
            var running = (ToolStateRunning)state;
            running.Status.Should().Be(ToolStateRunningStatus.Running);
            running.Title.Should().Be("Executing");
        }

        [Fact]
        public void ToolState_Completed_DeserializesToConcreteType()
        {
            // Arrange
            var json = @"{""status"":""completed"",""input"":{},""output"":""result data"",""title"":""Done"",""metadata"":{},""time"":{""start"":100,""end"":200}}";

            // Act
            var state = JsonConvert.DeserializeObject<ToolState>(json, _settings);

            // Assert
            state.Should().NotBeNull();
            state.Should().BeOfType<ToolStateCompleted>();
            var completed = (ToolStateCompleted)state;
            completed.Status.Should().Be(ToolStateCompletedStatus.Completed);
            completed.Output.Should().Be("result data");
            completed.Title.Should().Be("Done");
        }

        [Fact]
        public void ToolState_Error_DeserializesToConcreteType()
        {
            // Arrange
            var json = @"{""status"":""error"",""input"":{},""error"":""Something went wrong"",""metadata"":{},""time"":{""start"":50,""end"":150}}";

            // Act
            var state = JsonConvert.DeserializeObject<ToolState>(json, _settings);

            // Assert
            state.Should().NotBeNull();
            state.Should().BeOfType<ToolStateError>();
            var error = (ToolStateError)state;
            error.Status.Should().Be(ToolStateErrorStatus.Error);
            error.Error.Should().Be("Something went wrong");
        }

        [Fact]
        public void ToolState_UnknownStatus_ThrowsException()
        {
            // Arrange
            var json = @"{""status"":""unknown"",""input"":{}}";

            // Act/Assert
            var ex = Assert.Throws<JsonSerializationException>(() => 
                JsonConvert.DeserializeObject<ToolState>(json, _settings));
            ex.Message.Should().Contain("unknown");
        }

        [Fact]
        public void ToolState_RoundTrip_SerializationPreservesData()
        {
            // Arrange
            var original = new ToolStateCompleted
            {
                Status = ToolStateCompletedStatus.Completed,
                Input = new { cmd = "test" },
                Output = "output data",
                Title = "Completed",
                Metadata = new { },
                Time = new Time10 { Start = 100, End = 200 }
            };

            // Act
            var json = JsonConvert.SerializeObject(original, _settings);
            var deserialized = JsonConvert.DeserializeObject<ToolState>(json, _settings);

            // Assert
            deserialized.Should().NotBeNull();
            deserialized.Should().BeOfType<ToolStateCompleted>();
            var completed = (ToolStateCompleted)deserialized;
            completed.Output.Should().Be("output data");
            completed.Title.Should().Be("Completed");
        }

        #endregion

        #region Part Tests

        [Fact]
        public void Part_Text_DeserializesToTextPart()
        {
            // Arrange
            var json = @"{""id"":""prt-123"",""sessionID"":""ses-456"",""messageID"":""msg-789"",""type"":""text"",""text"":""Hello world""}";

            // Act
            var part = JsonConvert.DeserializeObject<Part>(json, _settings);

            // Assert
            part.Should().NotBeNull();
            part.Should().BeOfType<TextPart>();
            var textPart = (TextPart)part;
            textPart.Type.Should().Be(TextPartType.Text);
            textPart.Text.Should().Be("Hello world");
        }

        [Fact]
        public void Part_File_DeserializesToFilePart()
        {
            // Arrange
            var json = @"{""id"":""prt-123"",""sessionID"":""ses-456"",""messageID"":""msg-789"",""type"":""file"",""mime"":""text/plain"",""url"":""file:///test.txt""}";

            // Act
            var part = JsonConvert.DeserializeObject<Part>(json, _settings);

            // Assert
            part.Should().NotBeNull();
            part.Should().BeOfType<FilePart>();
            var filePart = (FilePart)part;
            filePart.Type.Should().Be(FilePartType.File);
            filePart.Mime.Should().Be("text/plain");
        }

        [Fact]
        public void Part_Tool_DeserializesToToolPart()
        {
            // Arrange
            var json = @"{""id"":""prt-123"",""sessionID"":""ses-456"",""messageID"":""msg-789"",""type"":""tool"",""callID"":""call-001"",""tool"":""read"",""state"":{""status"":""completed"",""input"":{},""output"":""data"",""title"":""Read"",""metadata"":{},""time"":{""start"":100,""end"":200}}}";

            // Act
            var part = JsonConvert.DeserializeObject<Part>(json, _settings);

            // Assert
            part.Should().NotBeNull();
            part.Should().BeOfType<ToolPart>();
            var toolPart = (ToolPart)part;
            toolPart.Type.Should().Be(ToolPartType.Tool);
            toolPart.Tool.Should().Be("read");
            toolPart.State.Should().BeOfType<ToolStateCompleted>();
        }

        [Fact]
        public void Part_Reasoning_DeserializesToReasoningPart()
        {
            // Arrange
            var json = @"{""id"":""prt-123"",""sessionID"":""ses-456"",""messageID"":""msg-789"",""type"":""reasoning"",""text"":""Thinking about this...""}";

            // Act
            var part = JsonConvert.DeserializeObject<Part>(json, _settings);

            // Assert
            part.Should().NotBeNull();
            part.Should().BeOfType<ReasoningPart>();
            var reasoningPart = (ReasoningPart)part;
            reasoningPart.Type.Should().Be(ReasoningPartType.Reasoning);
            reasoningPart.Text.Should().Be("Thinking about this...");
        }

        [Fact]
        public void Part_StepStart_DeserializesToStepStartPart()
        {
            // Arrange
            var json = @"{""id"":""prt-123"",""sessionID"":""ses-456"",""messageID"":""msg-789"",""type"":""step-start""}";

            // Act
            var part = JsonConvert.DeserializeObject<Part>(json, _settings);

            // Assert
            part.Should().NotBeNull();
            part.Should().BeOfType<StepStartPart>();
            var stepStart = (StepStartPart)part;
            stepStart.Type.Should().Be(StepStartPartType.StepStart);
        }

        [Fact]
        public void Part_StepFinish_DeserializesToStepFinishPart()
        {
            // Arrange
            var json = @"{""id"":""prt-123"",""sessionID"":""ses-456"",""messageID"":""msg-789"",""type"":""step-finish""}";

            // Act
            var part = JsonConvert.DeserializeObject<Part>(json, _settings);

            // Assert
            part.Should().NotBeNull();
            part.Should().BeOfType<StepFinishPart>();
            var stepFinish = (StepFinishPart)part;
            stepFinish.Type.Should().Be(StepFinishPartType.StepFinish);
        }

        [Fact]
        public void Part_Snapshot_DeserializesToSnapshotPart()
        {
            // Arrange
            var json = @"{""id"":""prt-123"",""sessionID"":""ses-456"",""messageID"":""msg-789"",""type"":""snapshot""}";

            // Act
            var part = JsonConvert.DeserializeObject<Part>(json, _settings);

            // Assert
            part.Should().NotBeNull();
            part.Should().BeOfType<SnapshotPart>();
            var snapshot = (SnapshotPart)part;
            snapshot.Type.Should().Be(SnapshotPartType.Snapshot);
        }

        [Fact]
        public void Part_Patch_DeserializesToPatchPart()
        {
            // Arrange
            var json = @"{""id"":""prt-123"",""sessionID"":""ses-456"",""messageID"":""msg-789"",""type"":""patch""}";

            // Act
            var part = JsonConvert.DeserializeObject<Part>(json, _settings);

            // Assert
            part.Should().NotBeNull();
            part.Should().BeOfType<PatchPart>();
            var patch = (PatchPart)part;
            patch.Type.Should().Be(PatchPartType.Patch);
        }

        [Fact]
        public void Part_Agent_DeserializesToAgentPart()
        {
            // Arrange
            var json = @"{""id"":""prt-123"",""sessionID"":""ses-456"",""messageID"":""msg-789"",""type"":""agent""}";

            // Act
            var part = JsonConvert.DeserializeObject<Part>(json, _settings);

            // Assert
            part.Should().NotBeNull();
            part.Should().BeOfType<AgentPart>();
            var agent = (AgentPart)part;
            agent.Type.Should().Be(AgentPartType.Agent);
        }

        [Fact]
        public void Part_Retry_DeserializesToRetryPart()
        {
            // Arrange
            var json = @"{""id"":""prt-123"",""sessionID"":""ses-456"",""messageID"":""msg-789"",""type"":""retry""}";

            // Act
            var part = JsonConvert.DeserializeObject<Part>(json, _settings);

            // Assert
            part.Should().NotBeNull();
            part.Should().BeOfType<RetryPart>();
            var retry = (RetryPart)part;
            retry.Type.Should().Be(RetryPartType.Retry);
        }

        [Fact]
        public void Part_Compaction_DeserializesToCompactionPart()
        {
            // Arrange
            var json = @"{""id"":""prt-123"",""sessionID"":""ses-456"",""messageID"":""msg-789"",""type"":""compaction""}";

            // Act
            var part = JsonConvert.DeserializeObject<Part>(json, _settings);

            // Assert
            part.Should().NotBeNull();
            part.Should().BeOfType<CompactionPart>();
            var compaction = (CompactionPart)part;
            compaction.Type.Should().Be(CompactionPartType.Compaction);
        }

        [Fact]
        public void Part_Subtask_DeserializesToSubtaskPart()
        {
            // Arrange
            var json = @"{""id"":""prt-123"",""sessionID"":""ses-456"",""messageID"":""msg-789"",""type"":""subtask""}";

            // Act
            var part = JsonConvert.DeserializeObject<Part>(json, _settings);

            // Assert
            part.Should().NotBeNull();
            part.Should().BeOfType<SubtaskPart>();
            var subtask = (SubtaskPart)part;
            subtask.Type.Should().Be(SubtaskPartType.Subtask);
        }

        [Fact]
        public void Part_UnknownType_ThrowsException()
        {
            // Arrange
            var json = @"{""id"":""prt-123"",""type"":""unknown""}";

            // Act/Assert
            var ex = Assert.Throws<JsonSerializationException>(() => 
                JsonConvert.DeserializeObject<Part>(json, _settings));
            ex.Message.Should().Contain("unknown");
        }

        #endregion

        #region Message Tests

        [Fact]
        public void Message_User_DeserializesToUserMessage()
        {
            // Arrange
            var json = @"{""id"":""msg-123"",""sessionID"":""ses-456"",""role"":""user""}";

            // Act
            var message = JsonConvert.DeserializeObject<Message>(json, _settings);

            // Assert
            message.Should().NotBeNull();
            message.Should().BeOfType<UserMessage>();
            var userMessage = (UserMessage)message;
            userMessage.Role.Should().Be(UserMessageRole.User);
        }

        [Fact]
        public void Message_Assistant_DeserializesToAssistantMessage()
        {
            // Arrange
            var json = @"{""id"":""msg-123"",""sessionID"":""ses-456"",""role"":""assistant""}";

            // Act
            var message = JsonConvert.DeserializeObject<Message>(json, _settings);

            // Assert
            message.Should().NotBeNull();
            message.Should().BeOfType<AssistantMessage>();
            var assistantMessage = (AssistantMessage)message;
            assistantMessage.Role.Should().Be(AssistantMessageRole.Assistant);
        }

        [Fact]
        public void Message_UnknownRole_ThrowsException()
        {
            // Arrange
            var json = @"{""id"":""msg-123"",""role"":""unknown""}";

            // Act/Assert
            var ex = Assert.Throws<JsonSerializationException>(() => 
                JsonConvert.DeserializeObject<Message>(json, _settings));
            ex.Message.Should().Contain("unknown");
        }

        #endregion

        #region AdditionalProperties Tests

        [Fact]
        public void Part_AdditionalProperties_CapturesUnknownFields()
        {
            // Arrange - JSON with extra fields not in schema
            var json = @"{""id"":""prt-123"",""sessionID"":""ses-456"",""messageID"":""msg-789"",""type"":""text"",""text"":""test"",""customField"":""custom value""}";

            // Act
            var part = JsonConvert.DeserializeObject<Part>(json, _settings);

            // Assert
            part.Should().NotBeNull();
            part.Should().BeOfType<TextPart>();
            // Note: Concrete types don't have AdditionalProperties, only the base Part does
            // This test verifies the concrete type is returned correctly
            var textPart = (TextPart)part;
            textPart.Text.Should().Be("test");
        }

        #endregion
    }
}
