using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Task = System.Threading.Tasks.Task;

namespace KiloVisualStudioExtension
{
  /// <summary>
  /// Opens Settings as a separate tool window, keeping the sidebar chat undisturbed.
  /// Matches the VS Code SettingsEditorProvider pattern.
  /// </summary>
  public class SettingsEditorProvider : VSProvider
  {
    private SettingsToolWindow? _settingsPanel;
    private string? _pendingTab;

    public SettingsEditorProvider(KiloWebViewControl webView, KiloConnectionService connectionService, string pendingTab = "models"): base(webView, connectionService) 
    {
      _pendingTab = pendingTab;
    }


    ///// <summary>
    ///// Open the settings panel with optional tab navigation.
    ///// </summary>
    //public void OpenPanel(string tab = "models")
    //{
    //  _pendingTab = tab;

    //  // Check if panel already exists
    //  if (_settingsPanel != null)
    //  {
    //    ShowToolWindow(_settingsPanel);
    //    // The navigate message will be sent when webviewReady is received
    //    return;
    //  }

    //  // Create new panel
    //  _settingsPanel = new SettingsToolWindow();
    //  _settingsPanel.SetPendingTab(tab);

    //  ShowToolWindow(_settingsPanel);
    //}

    //private void ShowToolWindow(SettingsToolWindow toolWindow)
    //{
    //  var frame = toolWindow.Frame as IVsWindowFrame;
    //  frame?.Show();
    //}

    //public void Dispose()
    //{
    //  _settingsPanel?.Dispose();
    //  _settingsPanel = null;
    //}

    protected override async Task HandleWebviewReadyAsync()
    {
      await base.HandleWebviewReadyAsync();
      // Small delay to let VSProvider's own webviewReady handler finish first
      Task.Delay(50).ContinueWith(_ =>
      {
        ThreadHelper.JoinableTaskFactory.Run(async () =>
        {
          await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
          var message = System.Text.Json.JsonSerializer.Serialize(new
          {
            type = "navigate",
            view = "settings",
            tab = _pendingTab
          });
          _webView?.PostMessage(message);
        });
      });
    }

  }
}
