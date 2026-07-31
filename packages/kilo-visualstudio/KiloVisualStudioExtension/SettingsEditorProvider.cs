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
  public sealed class SettingsEditorProvider : IDisposable
  {
    private SettingsToolWindow? _settingsPanel;
    private string? _pendingTab;

    /// <summary>
    /// Open the settings panel with optional tab navigation.
    /// </summary>
    public void OpenPanel(string tab = "models")
    {
      _pendingTab = tab;

      // Check if panel already exists
      if (_settingsPanel != null)
      {
        ShowToolWindow(_settingsPanel);
        // The navigate message will be sent when webviewReady is received
        return;
      }

      // Create new panel
      _settingsPanel = new SettingsToolWindow();
      _settingsPanel.SetPendingTab(tab);

      ShowToolWindow(_settingsPanel);
    }

    private void ShowToolWindow(SettingsToolWindow toolWindow)
    {
      var frame = toolWindow.Frame as IVsWindowFrame;
      frame?.Show();
    }

    public void Dispose()
    {
      _settingsPanel?.Dispose();
      _settingsPanel = null;
    }
  }
}
