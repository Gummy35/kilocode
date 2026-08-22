using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using KiloVisualStudioExtension.ApiClient;
using Task = System.Threading.Tasks.Task;

namespace KiloVisualStudioExtension.Services
{
  /// <summary>
  /// Represents the remote control state.
  /// Matches the RemoteState type from RemoteStatusService.ts
  /// </summary>
  public class RemoteState
  {
    /// <summary>
    /// Whether remote control is enabled.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Whether remote control is currently connected.
    /// </summary>
    public bool Connected { get; set; }

    /// <summary>
    /// Creates a new RemoteState instance.
    /// </summary>
    public RemoteState(bool enabled = false, bool connected = false)
    {
      Enabled = enabled;
      Connected = connected;
    }
  }

  /// <summary>
  /// Service that manages remote control state for the Kilo Visual Studio extension.
  /// Port of RemoteStatusService.ts from the VS Code extension.
  /// 
  /// This service:
  /// - Maintains the current remote control state (enabled/connected)
  /// - Provides methods to toggle, enable, disable, and refresh remote status
  /// - Notifies subscribers of state changes via events
  /// - Integrates with the CLI backend API for remote operations
  /// - Updates Visual Studio status bar (IVsStatusbar)
  /// - Posts status updates to the webview for tool window badge rendering
  /// </summary>
  public class RemoteStatusService : IDisposable
  {
    private RemoteState _state = new RemoteState(false, false);
    private readonly List<Action<RemoteState>> _listeners = new List<Action<RemoteState>>();
    private readonly object _lock = new object();
    private KiloApiClient? _client;
    private readonly Action<object>? _postMessageToWebView;
    private IVsStatusbar? _statusBar;
    private uint _statusBarCookie = uint.MaxValue;
    private bool _disposed;

    /// <summary>
    /// Event fired when the remote state changes.
    /// </summary>
    public event EventHandler<RemoteState>? StateChanged;

    /// <summary>
    /// Creates a new RemoteStatusService instance.
    /// </summary>
    /// <param name="postMessageToWebView">Optional action to post messages to the webview. 
    /// If provided, state changes will be pushed to the webview as "remoteStatus" messages.</param>
    public RemoteStatusService(Action<object>? postMessageToWebView = null)
    {
      _postMessageToWebView = postMessageToWebView;

      // Initialize status bar on UI thread (fire-and-forget)
      ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
      {
        await InitializeAsync();
      });
    }

    /// <summary>
    /// Initializes the service with Visual Studio shell services.
    /// Must be called on the UI thread.
    /// </summary>
    public async Task InitializeAsync()
    {
      await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
      _statusBar = (IVsStatusbar)await KiloProvider.Package.GetServiceAsync(typeof(SVsStatusbar));
    }

    /// <summary>
    /// Sets the Kilo API client used for remote operations.
    /// </summary>
    public void SetClient(KiloApiClient? client)
    {
      _client = client;
    }

    /// <summary>
    /// Gets the current remote state synchronously.
    /// </summary>
    public RemoteState GetState()
    {
      lock (_lock)
      {
        return new RemoteState(_state.Enabled, _state.Connected);
      }
    }

    /// <summary>
    /// Updates the state from an event received via SSE.
    /// </summary>
    public void UpdateFromEvent(RemoteState newState)
    {
      Update(newState);
    }

    /// <summary>
    /// Subscribes to state changes. Returns an unsubscribe action.
    /// </summary>
    public IDisposable OnChange(Action<RemoteState> callback)
    {
      lock (_lock)
      {
        _listeners.Add(callback);
        return new Unsubscriber(() => _listeners.Remove(callback));
      }
    }

    /// <summary>
    /// Clears the remote state (sets to disabled/disconnected).
    /// </summary>
    public void ClearState()
    {
      Update(new RemoteState(false, false));
    }

    /// <summary>
    /// Refreshes the remote status from the backend.
    /// </summary>
    public async Task RefreshAsync(string? directory = null, string? workspace = null)
    {
      if (_client == null)
        return;

      try
      {
        var result = await _client.Remote_statusAsync(directory ?? "", workspace ?? "").ConfigureAwait(false);
        if (result != null)
        {
          Update(new RemoteState(result.Enabled, result.Connected));
        }
      }
      catch (Exception ex)
      {
        System.Diagnostics.Debug.WriteLine($"[Kilo] RemoteStatusService: remote status refresh failed: {ex.Message}");
      }
    }

    /// <summary>
    /// Toggles the remote control on/off.
    /// </summary>
    public async Task ToggleAsync(string? directory = null, string? workspace = null)
    {
      if (_client == null)
        return;

      try
      {
        var result = await _client.Remote_statusAsync(directory ?? "", workspace ?? "").ConfigureAwait(false);
        if (result != null)
        {
          await SetEnabledAsync(!result.Enabled, directory, workspace).ConfigureAwait(false);
        }
      }
      catch (Exception ex)
      {
        System.Diagnostics.Debug.WriteLine($"[Kilo] RemoteStatusService: toggle failed: {ex.Message}");
      }
    }

    /// <summary>
    /// Enables or disables remote control.
    /// </summary>
    public async Task SetEnabledAsync(bool enabled, string? directory = null, string? workspace = null)
    {
      if (_client == null)
        return;

      try
      {
        if (enabled)
        {
          await _client.Remote_enableAsync(directory ?? "", workspace ?? "").ConfigureAwait(false);
        }
        else
        {
          await _client.Remote_disableAsync(directory ?? "", workspace ?? "").ConfigureAwait(false);
        }
        Update(new RemoteState(enabled, false));
      }
      catch (Exception ex)
      {
        System.Diagnostics.Debug.WriteLine($"[Kilo] RemoteStatusService: setEnabled failed: {ex.Message}");
      }
    }

    /// <summary>
    /// Handles remote-related messages from the webview.
    /// </summary>
    public async Task<RemoteState?> HandleMessageAsync(string type, bool? enabled = null)
    {
      switch (type)
      {
        case "toggleRemote":
          await ToggleAsync().ConfigureAwait(false);
          return null;

        case "setRemoteEnabled":
          if (enabled.HasValue)
          {
            await SetEnabledAsync(enabled.Value).ConfigureAwait(false);
          }
          return null;

        case "requestRemoteStatus":
          await RefreshAsync().ConfigureAwait(false);
          lock (_lock)
          {
            return new RemoteState(_state.Enabled, _state.Connected);
          }

        default:
          return null;
      }
    }

    /// <summary>
    /// Disposes of the service and unsubscribes all listeners.
    /// Clears the status bar element.
    /// </summary>
    public void Dispose()
    {
      if (_disposed)
        return;

      _disposed = true;

      // Clear status bar on UI thread
      ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
      {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        try
        {
          if (_statusBar != null)
          {
            _statusBar.SetText("");
          }
          _statusBar = null;
        }
        catch { }
      });

      lock (_lock)
      {
        _listeners.Clear();
      }

      StateChanged = null;
    }

    /// <summary>
    /// Updates the internal state and notifies all listeners.
    /// Also updates the status bar and posts to the webview.
    /// </summary>
    private void Update(RemoteState newState)
    {
      lock (_lock)
      {
        if (_state.Enabled == newState.Enabled && _state.Connected == newState.Connected)
          return;

        _state = newState;
      }

      StateChanged?.Invoke(this, newState);

      // Update status bar on UI thread
      ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
      {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        await UpdateStatusBarAsync(newState);
      });

      // Post to webview if callback is provided
      if (_postMessageToWebView != null)
      {
        try
        {
          _postMessageToWebView(new
          {
            type = "remoteStatus",
            enabled = newState.Enabled,
            connected = newState.Connected
          });
        }
        catch (Exception ex)
        {
          System.Diagnostics.Debug.WriteLine($"[Kilo] RemoteStatusService: failed to post to webview: {ex.Message}");
        }
      }

      List<Action<RemoteState>> listenersToNotify;
      lock (_lock)
      {
        listenersToNotify = new List<Action<RemoteState>>(_listeners);
      }

      foreach (var listener in listenersToNotify)
      {
        try
        {
          listener(newState);
        }
        catch (Exception ex)
        {
          System.Diagnostics.Debug.WriteLine($"[Kilo] RemoteStatusService: listener error: {ex.Message}");
        }
      }
    }

    /// <summary>
    /// Updates the Visual Studio status bar with the current remote state.
    /// Must be called on the UI thread.
    /// </summary>
    private async Task UpdateStatusBarAsync(RemoteState state)
    {
      if (_statusBar == null)
        return;

      try
      {
        // Freeze the status bar for updates
        _statusBar.FreezeOutput(1);

        if (!state.Enabled)
        {
          // Hide the status bar element when remote is disabled
          _statusBar.SetText("");
          _statusBar.FreezeOutput(0);
          return;
        }

        // Set the status bar text
        string text = state.Connected
            ? "Kilo Remote: Connected"
            : "Kilo Remote: Connecting...";
        _statusBar.SetText(text);

        _statusBar.FreezeOutput(0);
      }
      catch (Exception ex)
      {
        System.Diagnostics.Debug.WriteLine($"[Kilo] RemoteStatusService: status bar update failed: {ex.Message}");
        _statusBar?.FreezeOutput(0);
      }
    }

    /// <summary>
    /// Unsubscriber for the OnChange subscription.
    /// </summary>
    private class Unsubscriber : IDisposable
    {
      private readonly System.Action _dispose;

      public Unsubscriber(System.Action dispose)
      {
        _dispose = dispose;
      }

      public void Dispose()
      {
        _dispose();
      }
    }
  }
}
