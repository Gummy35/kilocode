/**
 * Visual Studio WebView2 compatibility layer for VS Code API
 * Provides the same interface as acquireVsCodeApi() for the webview
 */

(function() {
  // Prevent multiple initialization
  if (window.acquireVsCodeApi) {
    return;
  }

  // Message queue for state persistence
  let currentState = undefined;
  const messageQueue = [];
  let messageListener = null;

  // Detect WebView2 environment - check for chrome.webview
  const isWebView2 = !!(window.chrome && window.chrome.webview);
  console.log('[VSCodeAPI] isWebView2:', isWebView2);
  console.log('[VSCodeAPI] window.chrome:', !!window.chrome);
  console.log('[VSCodeAPI] window.chrome.webview:', !!(window.chrome && window.chrome.webview));

  // Create the VS Code API object
  const vscodeApi = {
    /**
     * Post a message to the extension host
     * @param {any} message - The message to send
     */
    postMessage: function(message) {
      console.log('[VSCodeAPI] postMessage called:', message);
      if (isWebView2) {
        try {
          console.log('[VSCodeAPI] Sending via chrome.webview.postMessage');
          window.chrome.webview.postMessage(message);
        } catch (err) {
          console.error('[VSCodeAPI] postMessage error:', err);
        }
      } else {
        // Fallback: queue the message for debugging (development outside VS)
        console.log('[VSCodeAPI] Mock postMessage (outside VS):', message);
        messageQueue.push(message);
      }
    },

    /**
     * Get the current state from extension host
     * @returns {any} The current state
     */
    getState: function() {
      if (!currentState) {
        this.postMessage({ type: 'getState'});      
      }
      return currentState;
    },

    /**
     * Set the current state (persisted by extension host)
     * @param {any} state - The state to persist
     */
    setState: function(state) {
      currentState = state;
      // State is persisted via postMessage to extension
      this.postMessage({ type: 'setState', state: state });
    },
  };

  // Valid ExtensionMessage types that the webview expects
  const VALID_MESSAGE_TYPES = new Set([
    'ready',
    'fontSizeChanged',
    'gitStatus',
    'workspaceDirectoryChanged',
    'languageChanged',
    'connectionState',
    'error',
    'sendMessageFailed',
    'sessionCommandCompleted',
    'partUpdated',
    'partsUpdated',
    'partRemoved',
    'sessionStatus',
    'sessionTurnClosed',
    'sessionError',
    'permissionRequest',
    'permissionResolved',
    'permissionError',
    'todoUpdated',
    'sessionCreated',
    'sessionForked',
    'sessionUpdated',
    'sessionDeleted',
    'messageRemoved',
    'messagesLoaded',
    'sessionModelUsageLoaded',
    'sessionModelUsageChanged',
    'messageCreated',
    'sessionsLoaded',
    'cloudSessionsLoaded',
    'gitRemoteUrlLoaded',
    'action',
    'profileData',
    'deviceAuthStarted',
    'deviceAuthComplete',
    'deviceAuthFailed',
    'deviceAuthCancelled',
    'navigate',
    'indexingStatusLoaded',
    'indexingSettingsLoaded',
    'chatSettingsLoaded',
    'kiloEmbeddingModelsLoaded',
    'imageModelsLoaded',
    'providersLoaded',
    'agentsLoaded',
    'skillsLoaded',
    'agentRequirementsLoaded',
    'agentRequirementsInvalidated',
    'commandsLoaded',
    'autocompleteSettingsLoaded',
    'chatCompletionResult',
    'speechToTextStarted',
    'speechToTextCancelled',
    'speechToTextResult',
    'speechToTextError',
    'fileSearchResult',
    'sessionSearchResult',
    'filePickerResult',
    'terminalContextResult',
    'terminalContextError',
    'gitChangesContextResult',
    'gitChangesContextError',
    'questionRequest',
    'questionResolved',
    'questionError',
    'sessionCostAlert',
    'sessionCostAlertResolved',
    'suggestionRequest',
    'suggestionResolved',
    'suggestionError',
    'browserSettingsLoaded',
    'claudeCompatSettingLoaded',
    'configLoaded',
    'configUpdated',
    'configUpdateFailed',
    'globalConfigLoaded',
    'notificationSettingsLoaded',
    'timelineSetting',
    'throughputSetting',
    'workStyleLoaded',
    'workStyleApplied',
    'workStyleApplyFailed',
    'notificationsLoaded',
    'agentManager.sessionMeta',
    'agentManager.repoInfo',
    'agentManager.worktreeSetup',
    'agentManager.sessionAdded',
    'agentManager.sessionForked',
    'agentManager.sessionClosed',
    'agentManager.state',
    'agentManager.runStatus',
    'agentManager.keybindings',
    'autoApproveState',
    'sandboxStatus',
    'sandboxDefaultStatus',
    'sandboxStatusError',
    'agentManager.multiVersionProgress',
    'agentManager.setSessionModel',
    'agentManager.sendInitialMessage',
    'setChatBoxMessage',
    'appendChatBoxMessage',
    'appendReviewComments',
    'appendReviewCommentsToTerminal',
    'triggerTask',
    'variantsLoaded',
    'cloudSessionDataLoaded',
    'cloudSessionImported',
    'cloudSessionImportFailed',
    'openCloudSession',
    'selectKiloModel',
    'agentManager.branches',
    'agentManager.externalWorktrees',
    'agentManager.importResult',
    'agentManager.worktreeDiff',
    'agentManager.worktreeDiffFile',
    'agentManager.worktreeDiffLoading',
    'agentManager.applyWorktreeDiffResult',
    'agentManager.revertWorktreeFileResult',
    'agentManager.worktreeStats',
    'agentManager.localStats',
    'agentManager.prStatus',
    'worktreeStatsLoaded',
    'agentManager.terminal.created',
    'agentManager.terminal.fontChanged',
    'agentManager.terminal.closed',
    'agentManager.terminal.error',
    'migrationState',
    'migrationData',
    'migrationProgress',
    'migrationSessionProgress',
    'migrationComplete',
    'enhancePromptResult',
    'enhancePromptError',
    'viewSubAgentSession',
    'diffViewer.diffs',
    'diffViewer.loading',
    'diffViewer.revertFileResult',
    'diffViewer.diffFile',
    'diffViewer.markdownRender',
    'setAvailableSources',
    'diffViewer.capabilities',
    'diffViewer.notice',
    'diffViewer.branches',
    'clearPendingPrompts',
    'extensionDataReady',
    'telemetryState',
    'marketplaceData',
    'marketplaceInstallResult',
    'openInstallModal',
    'marketplaceRemoveResult',
    'providerOAuthReady',
    'providerConnected',
    'providerDisconnected',
    'providerActionError',
    'customProviderModelsFetched',
    'mcpStatusLoaded',
    'continueInWorktreeProgress',
    'remoteStatus',
    'validateFilesResult',
    'memoryLoaded',
    'memoryEvent',
    'memoryOperationResult',
    'recentsLoaded',
    'modelSelectorExpandedLoaded',
    'favoritesLoaded',
    'modelSelectionsLoaded',
    'setAvailableSources',
    'diffViewer.diffs',
    'diffViewer.loading',
    'diffViewer.revertFileResult',
    'diffViewer.diffFile',
    'diffViewer.markdownRender',
    'setAvailableSources',
    'diffViewer.capabilities',
    'diffViewer.notice',
    'diffViewer.branches',
  ]);

  // Listen for messages from extension host (for state updates and data)
  if (isWebView2) {
    console.log('[VSCodeAPI] Setting up message listener');
    messageListener = function(event) {
      console.log('[VSCodeAPI] Received raw message from chrome.webview:', event.data);
      // WebView2 delivers messages as JSON strings, need to parse them
      let parsedData;
      try {
        parsedData = typeof event.data === 'string' ? JSON.parse(event.data) : event.data;
        console.log('[VSCodeAPI] Parsed message:', parsedData);
      } catch (err) {
        console.error('[VSCodeAPI] Failed to parse message:', err);
        return;
      }
      if (parsedData && parsedData.type === 'setState') {
        currentState = parsedData.state;
      }
      // Forward parsed message to window.postMessage for webview consumption
      window.postMessage(parsedData, '*');
    };
    window.chrome.webview.addEventListener('message', messageListener);
    console.log('[VSCodeAPI] Message listener attached to chrome.webview');
  } else {
    console.log('[VSCodeAPI] Not in WebView2, skipping message listener setup');
    console.log('[VSCodeAPI] chrome:', typeof window.chrome);
    console.log('[VSCodeAPI] chrome.webview:', typeof (window.chrome && window.chrome.webview));
    if (window.chrome && window.chrome.webview) {
      console.log('[VSCodeAPI] chrome.webview methods:', Object.keys(window.chrome.webview));
    }
  }

  // Expose the API globally (same as VS Code's acquireVsCodeApi)
  window.acquireVsCodeApi = function() {
    console.log('[VSCodeAPI] acquireVsCodeApi called');
    return vscodeApi;
  };

})();
