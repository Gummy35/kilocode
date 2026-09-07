using KiloVisualStudioExtension.Utils;
using Microsoft.VisualStudio.Shell;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace KiloVisualStudioExtension.Utils
{
  public sealed class QuickPickEntry
  {
    public string Label { get; set; } = string.Empty;

    public string? Description { get; set; }

    public string? Detail { get; set; }

    public object? Item { get; set; }
  }

  public sealed class QuickPickOptions
  {
    public string? Title { get; set; }

    public string? PlaceHolder { get; set; }

    public bool MatchOnDescription { get; set; } = true;

    public bool MatchOnDetail { get; set; } = true;
  }

  public sealed class QuickPickWindow : Window
  {
    private readonly List<QuickPickEntry> _allEntries;
    private readonly QuickPickOptions _options;

    private readonly TextBox _searchBox;
    private readonly ListBox _entriesList;

    private List<QuickPickEntry> _filteredEntries;

    public QuickPickEntry? SelectedEntry
    {
      get;
      private set;
    }

    public QuickPickWindow(
        IEnumerable<QuickPickEntry> entries,
        QuickPickOptions? options = null)
    {
      _allEntries = entries?.ToList()
          ?? new List<QuickPickEntry>();

      _filteredEntries = new List<QuickPickEntry>(_allEntries);
      _options = options ?? new QuickPickOptions();

      _searchBox = new TextBox
      {
        Height = 30,
        Margin = new Thickness(0, 0, 0, 10)
      };

      _entriesList = new ListBox
      {
        MinHeight = 250,
        Margin = new Thickness(0, 0, 0, 10),
        DisplayMemberPath = nameof(QuickPickEntry.Label)
      };

      InitializeWindow();
      InitializeControls();
      ApplyOptions();
      RefreshEntries();

      Loaded += OnWindowLoaded;
    }

    private void InitializeWindow()
    {
      Title = "Select an item";
      Width = 700;
      MinWidth = 450;
      Height = 450;
      MinHeight = 300;
      ResizeMode = ResizeMode.CanResize;
      WindowStartupLocation = WindowStartupLocation.CenterOwner;
      ShowInTaskbar = false;
    }

    private void InitializeControls()
    {
      var grid = new Grid
      {
        Margin = new Thickness(12)
      };

      grid.RowDefinitions.Add(
          new RowDefinition
          {
            Height = GridLength.Auto
          });

      grid.RowDefinitions.Add(
          new RowDefinition
          {
            Height = new GridLength(1, GridUnitType.Star)
          });

      grid.RowDefinitions.Add(
          new RowDefinition
          {
            Height = GridLength.Auto
          });

      _searchBox.TextChanged += OnSearchTextChanged;
      _searchBox.KeyDown += OnSearchBoxKeyDown;

      Grid.SetRow(_searchBox, 0);
      grid.Children.Add(_searchBox);

      _entriesList.SelectionChanged += OnSelectionChanged;
      _entriesList.MouseDoubleClick += OnEntriesListDoubleClick;
      _entriesList.KeyDown += OnEntriesListKeyDown;

      _entriesList.ItemTemplate = CreateEntryTemplate();

      Grid.SetRow(_entriesList, 1);
      grid.Children.Add(_entriesList);

      var buttonPanel = new StackPanel
      {
        Orientation = Orientation.Horizontal,
        HorizontalAlignment = HorizontalAlignment.Right
      };

      var openButton = new Button
      {
        Content = "Open",
        Width = 85,
        Margin = new Thickness(0, 0, 8, 0),
        IsDefault = true
      };

      openButton.Click += OnOpenButtonClick;

      var cancelButton = new Button
      {
        Content = "Cancel",
        Width = 85,
        IsCancel = true
      };

      cancelButton.Click += OnCancelButtonClick;

      buttonPanel.Children.Add(openButton);
      buttonPanel.Children.Add(cancelButton);

      Grid.SetRow(buttonPanel, 2);
      grid.Children.Add(buttonPanel);

      Content = grid;

      KeyDown += OnWindowKeyDown;
    }

    private DataTemplate CreateEntryTemplate()
    {
      var template = new DataTemplate();

      var panel = new FrameworkElementFactory(
          typeof(StackPanel));

      panel.SetValue(
          StackPanel.OrientationProperty,
          Orientation.Vertical);

      var label = new FrameworkElementFactory(
          typeof(TextBlock));

      label.SetBinding(
          TextBlock.TextProperty,
          new Binding(nameof(QuickPickEntry.Label)));

      label.SetValue(
          TextBlock.FontWeightProperty,
          FontWeights.Bold);

      label.SetValue(
          TextBlock.MarginProperty,
          new Thickness(0, 0, 0, 2));

      panel.AppendChild(label);

      var description = new FrameworkElementFactory(
          typeof(TextBlock));

      description.SetBinding(
          TextBlock.TextProperty,
          new Binding(nameof(QuickPickEntry.Description)));

      description.SetValue(
          TextBlock.FontSizeProperty,
          11.0);

      description.SetValue(
          TextBlock.ForegroundProperty,
          Brushes.Gray);

      description.SetValue(
          TextBlock.TextTrimmingProperty,
          TextTrimming.CharacterEllipsis);

      panel.AppendChild(description);

      var detail = new FrameworkElementFactory(
          typeof(TextBlock));

      detail.SetBinding(
          TextBlock.TextProperty,
          new Binding(nameof(QuickPickEntry.Detail)));

      detail.SetValue(
          TextBlock.FontSizeProperty,
          11.0);

      detail.SetValue(
          TextBlock.ForegroundProperty,
          Brushes.Gray);

      detail.SetValue(
          TextBlock.TextTrimmingProperty,
          TextTrimming.CharacterEllipsis);

      panel.AppendChild(detail);

      template.VisualTree = panel;

      return template;
    }

    private void ApplyOptions()
    {
      if (!string.IsNullOrWhiteSpace(_options.Title))
      {
        Title = _options.Title;
      }

      if (!string.IsNullOrWhiteSpace(_options.PlaceHolder))
      {
        _searchBox.ToolTip = _options.PlaceHolder;
      }
    }

    private void OnWindowLoaded(
        object sender,
        RoutedEventArgs e)
    {
      _searchBox.Focus();

      if (_filteredEntries.Count > 0)
      {
        _entriesList.SelectedIndex = 0;
        _entriesList.ScrollIntoView(
            _entriesList.SelectedItem);
      }
    }

    private void OnSearchTextChanged(
        object sender,
        TextChangedEventArgs e)
    {
      RefreshEntries();
    }

    private void RefreshEntries()
    {
      var query = _searchBox.Text.Trim();

      if (string.IsNullOrEmpty(query))
      {
        _filteredEntries = new List<QuickPickEntry>(
            _allEntries);
      }
      else
      {
        _filteredEntries = _allEntries
            .Where(entry => Matches(entry, query))
            .ToList();
      }

      _entriesList.ItemsSource = _filteredEntries;

      if (_filteredEntries.Count > 0)
      {
        _entriesList.SelectedIndex = 0;
        SelectedEntry = _filteredEntries[0];
      }
      else
      {
        _entriesList.SelectedIndex = -1;
        SelectedEntry = null;
      }
    }

    private bool Matches(
        QuickPickEntry entry,
        string query)
    {
      if (ContainsIgnoreCase(entry.Label, query))
        return true;

      if (_options.MatchOnDescription &&
          !string.IsNullOrEmpty(entry.Description) &&
          ContainsIgnoreCase(entry.Description, query))
      {
        return true;
      }

      if (_options.MatchOnDetail &&
          !string.IsNullOrEmpty(entry.Detail) &&
          ContainsIgnoreCase(entry.Detail, query))
      {
        return true;
      }

      return false;
    }

    private static bool ContainsIgnoreCase(
        string? value,
        string query)
    {
      return !string.IsNullOrEmpty(value) &&
             value.IndexOf(
                 query,
                 StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private void OnSelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
      SelectedEntry =
          _entriesList.SelectedItem as QuickPickEntry;
    }

    private void OnOpenButtonClick(
        object sender,
        RoutedEventArgs e)
    {
      AcceptSelection();
    }

    private void OnCancelButtonClick(
        object sender,
        RoutedEventArgs e)
    {
      CancelSelection();
    }

    private void OnEntriesListDoubleClick(
        object sender,
        MouseButtonEventArgs e)
    {
      if (_entriesList.SelectedItem != null)
      {
        AcceptSelection();
      }
    }

    private void OnSearchBoxKeyDown(
        object sender,
        KeyEventArgs e)
    {
      if (e.Key == Key.Down)
      {
        _entriesList.Focus();
        e.Handled = true;
      }
      else if (e.Key == Key.Enter)
      {
        AcceptSelection();
        e.Handled = true;
      }
      else if (e.Key == Key.Escape)
      {
        CancelSelection();
        e.Handled = true;
      }
    }

    private void OnEntriesListKeyDown(
        object sender,
        KeyEventArgs e)
    {
      if (e.Key == Key.Enter)
      {
        AcceptSelection();
        e.Handled = true;
      }
      else if (e.Key == Key.Escape)
      {
        CancelSelection();
        e.Handled = true;
      }
    }

    private void OnWindowKeyDown(
        object sender,
        KeyEventArgs e)
    {
      if (e.Key == Key.Escape)
      {
        CancelSelection();
        e.Handled = true;
      }
    }

    private void AcceptSelection()
    {
      if (_entriesList.SelectedItem is not QuickPickEntry entry)
        return;

      SelectedEntry = entry;
      DialogResult = true;
    }

    private void CancelSelection()
    {
      SelectedEntry = null;
      DialogResult = false;
    }
  }

  public static class QuickPickHelper
  {
    public static async Task<QuickPickEntry?> ShowQuickPickAsync(
        IEnumerable<QuickPickEntry> entries,
        QuickPickOptions? options = null,
        Window? owner = null)
    {
      await ThreadHelper.JoinableTaskFactory
          .SwitchToMainThreadAsync();

      var window = new QuickPickWindow(
          entries,
          options);

      if (owner != null &&
          owner != window &&
          owner.IsVisible)
      {
        window.Owner = owner;
      }

      var result = window.ShowDialog();

      return result == true
          ? window.SelectedEntry
          : null;
    }
  }
}


//Sample Usage

//  var selected = await QuickPickHelper.ShowQuickPickAsync(
//    entries,
//    new QuickPickOptions
//    {
//      Title = $"Multiple matches for \"{fileName}\"",
//      PlaceHolder = "Select a file",
//      MatchOnDescription = true,
//      MatchOnDetail = false
//    });

//if (selected?.Item is string selectedPath)
//{
//  await ShowDocumentAsync(selectedPath, line, column);
//}
