// Type aliases for NSwag-generated models to improve code readability
// Usage: Add "using static KiloVisualStudioExtension.ApiClient.ModelAliases;" at the top of your file

namespace KiloVisualStudioExtension.ApiClient
{
    /// <summary>
    /// Documentation for NSwag-generated request model mappings.
    /// 
    /// When you see a BodyNN type in the generated client, use this reference to find the correct type:
    /// 
    /// | Endpoint                    | Body Type | Alias/Usage                              |
    /// |-----------------------------|-----------|------------------------------------------|
    /// | session/viewed (POST)       | Body29    | ViewedRequest                            |
    /// | session/revert (POST)       | Body27    | RevertRequest                            |
    /// | session/create (POST)       | Body18    | SessionCreateRequest                     |
    /// | session/update (POST)       | Body19    | SessionUpdateRequest                     |
    /// | session/fork (POST)         | Body21    | SessionForkRequest                       |
    /// | session/prompt_async (POST) | Body24    | SessionPromptRequest                     |
    /// | permission/reply (POST)     | Body13    | PermissionReplyRequest                   |
    /// | permission/allow-everything | Body15    | PermissionAllowEverythingRequest           |
    /// | question/reply (POST)       | Body12    | QuestionReplyRequest                     |
    /// | provider/oauth/authorize    | Body16    | ProviderOAuthAuthorizeRequest              |
    /// | provider/oauth/callback     | Body17    | ProviderOAuthCallbackRequest               |
    /// | global/log (POST)           | Body      | LogRequest                               |
    /// | network/reject (POST)       | Body3     | NetworkRejectRequest                     |
    /// | sandbox/toggle (POST)       | Body8     | SandboxToggleRequest                     |
    /// 
    /// == Using Aliases in Your Code ==
    /// 
    /// Add using alias directives at the top of your file:
    /// 
    ///   using ViewedRequest = KiloVisualStudioExtension.ApiClient.Body29;
    ///   using ViewerModel = KiloVisualStudioExtension.ApiClient.Viewer;
    ///   using RevertRequest = KiloVisualStudioExtension.ApiClient.Body27;
    ///   using SessionCreateRequest = KiloVisualStudioExtension.ApiClient.Body18;
    ///   using SessionUpdateRequest = KiloVisualStudioExtension.ApiClient.Body19;
    /// 
    /// Then use the meaningful names in your code:
    /// 
    ///   // Creating a session
    ///   var createBody = new SessionCreateRequest { Title = "New Chat" };
    ///   await client.Session_createAsync(dir, "", createBody);
    /// 
    ///   // Updating a session
    ///   var updateBody = new SessionUpdateRequest { Title = "Updated Title" };
    ///   await client.Session_updateAsync(id, dir, "", updateBody);
    /// 
    ///   // Reverting a session
    ///   var revertBody = new RevertRequest { MessageID = msgId, PartID = null };
    ///   await client.Session_revertAsync(id, dir, "", revertBody);
    /// 
    ///   // Marking sessions as viewed
    ///   var viewedBody = new ViewedRequest
    ///   {
    ///       Visible = visibleList,
    ///       Attached = attachedList,
    ///       Viewer = new ViewerModel { Id = viewerId, Active = true }
    ///   };
    ///   await client.Session_viewedAsync(dir, "", viewedBody);
    /// 
    /// == Current Alias Usage in Codebase ==
    /// 
    /// The following aliases are already defined in these files:
    /// 
    /// - KiloConnectionService.cs: ViewedRequest, ViewerModel
    /// - SessionHandlerService.cs: SessionCreateRequest, SessionUpdateRequest, RevertRequest
    /// - VSProvider.cs: SessionCreateRequest
    /// 
    /// Add aliases to other files as needed when you use these types.
    /// </summary>
    public static class ApiClientDocumentation
    {
        // This class exists only for documentation purposes
    }
}
