#nullable enable

namespace KiloExtensionDTOs.WebviewMessages;

/// <summary>
/// Type: ExtensionSettings
/// Source: extension-messages.ts
/// </summary>
public interface IEditorActionMessage { }

public partial class OpenFileRequest : IEditorActionMessage { }
public partial class OpenContentRequest : IEditorActionMessage { }
public partial class ValidateFilesRequest : IEditorActionMessage { }
public partial class OpenExternalRequest : IEditorActionMessage { }
public partial class OpenDiffVirtualRequest : IEditorActionMessage { }
public partial class PreviewImageRequest : IEditorActionMessage { }
