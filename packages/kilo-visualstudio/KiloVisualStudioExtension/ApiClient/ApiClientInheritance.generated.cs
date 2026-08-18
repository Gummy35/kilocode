namespace KiloVisualStudioExtension.ApiClient
{
  /// <summary>
  /// Inheritance declarations for polymorphic types defined in the OpenAPI spec.
  /// NSwag generates independent classes for polymorphic schemas (anyOf/oneOf),
  /// but the OpenAPI spec defines inheritance relationships that should be restored
  /// here for proper polymorphic behavior.
  /// </summary>

  // FilePartSource hierarchy
  // Defined in OpenAPI spec as: "FilePartSource": { "anyOf": [FileSource, ResourceSource, SymbolSource] }
  public partial class FileSource : FilePartSource { }
  public partial class ResourceSource : FilePartSource { }
  public partial class SymbolSource : FilePartSource { }

  // OutputFormat hierarchy
  // Defined in OpenAPI spec as: "OutputFormat": { "anyOf": [OutputFormatJsonSchema, OutputFormatText] }
  public partial class OutputFormatJsonSchema : OutputFormat { }
  public partial class OutputFormatText : OutputFormat { }

  // Part hierarchy
  // Defined in OpenAPI spec as: "Part": { "anyOf": [AgentPart, CompactionPart, FilePart, PatchPart, ReasoningPart, RetryPart, SnapshotPart, StepFinishPart, StepStartPart, SubtaskPart, TextPart, ToolPart] }
  public partial class AgentPart : Part { }
  public partial class CompactionPart : Part { }
  public partial class FilePart : Part { }
  public partial class PatchPart : Part { }
  public partial class ReasoningPart : Part { }
  public partial class RetryPart : Part { }
  public partial class SnapshotPart : Part { }
  public partial class StepFinishPart : Part { }
  public partial class StepStartPart : Part { }
  public partial class SubtaskPart : Part { }
  public partial class TextPart : Part { }
  public partial class ToolPart : Part { }

  // SessionMessage hierarchy
  // Defined in OpenAPI spec as: "SessionMessage": { "anyOf": [SessionMessageAgentSwitched, SessionMessageAssistant, SessionMessageCompaction, SessionMessageModelSwitched, SessionMessageShell, SessionMessageSynthetic, SessionMessageSystem, SessionMessageUser] }
  public partial class SessionMessageAgentSwitched : SessionMessage { }
  public partial class SessionMessageAssistant : SessionMessage { }
  public partial class SessionMessageCompaction : SessionMessage { }
  public partial class SessionMessageModelSwitched : SessionMessage { }
  public partial class SessionMessageShell : SessionMessage { }
  public partial class SessionMessageSynthetic : SessionMessage { }
  public partial class SessionMessageSystem : SessionMessage { }
  public partial class SessionMessageUser : SessionMessage { }

  // ToolState hierarchy
  // Defined in OpenAPI spec as: "ToolState": { "anyOf": [ToolStateCompleted, ToolStateError, ToolStatePending, ToolStateRunning] }
  public partial class ToolStateCompleted : ToolState { }
  public partial class ToolStateError : ToolState { }
  public partial class ToolStatePending : ToolState { }
  public partial class ToolStateRunning : ToolState { }

 
}
