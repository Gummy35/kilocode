using Microsoft.VisualStudio.Shell;
using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace KiloVisualStudioExtension
{
  /// <summary>
  /// Settings-specific ToolWindow with its own WebView and VSProvider.
  /// Matches the VS Code SettingsEditorProvider pattern.
  /// </summary>
  [Guid("8a8f8e8c-1234-5678-9abc-def012345681")]
  public class SettingsToolWindow : ToolWindowPane, IDisposable
  {
    private KiloWebViewControl? _webView;
    private KiloConnectionService? _connectionService;
    private VSProvider? _vsProvider;
    private bool _disposed;
    private bool _initialized;
    private string? _pendingTab;

    public KiloWebViewControl? WebView => _webView;

    // Parameterless constructor required by Visual Studio tool window infrastructure
    public SettingsToolWindow() : base(null)
    {
      Caption = "Kilo Settings";
      _webView = new KiloWebViewControl();
      Content = _webView;

      // Initialize when the window is created - the Content assignment makes it visible
      _ = InitializeWebViewAsync();
    }

    public void SetPendingTab(string tab)
    {
      _pendingTab = tab;
    }

    private async Task InitializeWebViewAsync()
    {
      if (_initialized) return;

      await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
      await _webView!.InitializeAsync();

      var backendManager = CliBackendManager.Instance;
      if (backendManager != null)
      {
        _connectionService = new KiloConnectionService(backendManager);
        _webView.SetConnectionService(_connectionService);
        _vsProvider = new SettingsEditorProvider(_webView, _connectionService);
        await _connectionService.ConnectAsync();
        //// Subscribe to webviewReady to send navigate message
        //_webView.OnMessageReceived += HandleMessageReceived;
      }

      _initialized = true;
    }

    //private void HandleMessageReceived(object? sender, WebViewMessageEventArgs e)
    //{
    //  if (e.Type == "webviewReady" && !string.IsNullOrEmpty(_pendingTab))
    //  {
    //    // Small delay to let VSProvider's own webviewReady handler finish first
    //    Task.Delay(50).ContinueWith(_ =>
    //    {
    //      ThreadHelper.JoinableTaskFactory.Run(async () =>
    //              {
    //          await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
    //          var message = System.Text.Json.JsonSerializer.Serialize(new
    //          {
    //            type = "navigate",
    //            view = "settings",
    //            tab = _pendingTab
    //          });
    //          _webView?.PostMessage(message);
    //        });
    //    });
    //  }
    //}

    protected override void Dispose(bool disposing)
    {
      if (!_disposed)
      {
        if (disposing)
        {
          //if (_webView != null)
          //{
          //  _webView.OnMessageReceived -= HandleMessageReceived;
          //}
          _connectionService?.Dispose();
          _webView?.Dispose();
        }
        _disposed = true;
      }
      base.Dispose(disposing);
    }
  }
}
