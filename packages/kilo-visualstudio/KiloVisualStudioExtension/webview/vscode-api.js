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

  // Message handler registry for onMessage - supports multiple handlers
  const messageHandlers = [];

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
    },

    /**
     * Register a message handler to receive messages from the extension host
     * @param {function} handler - The message handler function
     * @returns {function} Unsubscribe function to remove the handler
     */
    onMessage: function(handler) {
      console.log('[VSCodeAPI] onMessage called');
      messageHandlers.push(handler);
      
      // Return unsubscribe function
      return function() {
        const index = messageHandlers.indexOf(handler);
        if (index > -1) {
          messageHandlers.splice(index, 1);
          console.log('[VSCodeAPI] Handler unsubscribed');
        }
      };
    }
  };

  // Listen for messages from extension host (for state updates and data)
  if (isWebView2) {
    console.log('[VSCodeAPI] Setting up message listener');
    messageListener = function(event) {
      console.log('[VSCodeAPI] Received message:', event.data);
      if (event.data && event.data.type === 'setState') {
        currentState = event.data.state;
      }
      // Forward all messages to all registered handlers
      for (let i = 0; i < messageHandlers.length; i++) {
        try {
          messageHandlers[i](event.data);
        } catch (err) {
          console.error('[VSCodeAPI] Message handler error:', err);
        }
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
