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
