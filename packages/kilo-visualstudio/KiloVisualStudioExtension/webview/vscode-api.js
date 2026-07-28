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
    }
  };

  // Listen for messages from extension host (for state updates)
  if (isWebView2) {
    console.log('[VSCodeAPI] Setting up message listener');
    messageListener = function(event) {
      console.log('[VSCodeAPI] Received message:', event.data);
      if (event.data && event.data.type === 'setState') {
        currentState = event.data.state;
      }
    };
    window.chrome.webview.addEventListener('message', messageListener);
  } else {
    console.log('[VSCodeAPI] Not in WebView2, skipping message listener setup');
  }

  // Expose the API globally (same as VS Code's acquireVsCodeApi)
  window.acquireVsCodeApi = function() {
    console.log('[VSCodeAPI] acquireVsCodeApi called');
    return vscodeApi;
  };
})();
