using Microsoft.VisualStudio.Shell;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Data;
using System.Linq;
using System.Windows.Interop;

namespace KiloVisualStudioExtension.Utils
{
  public class QuickPickEntry
  {
    public string Label { get; set; } = "";
    public string? Description { get; set; }
    public string? Detail { get; set; }
    public object? Item { get; set; }
  }

  public class QuickPickOptions
  {
    public string? Title { get; set; }
    public string? PlaceHolder { get; set; }
    public bool MatchOnDescription { get; set; } = true;
    public bool MatchOnDetail { get; set; } = true;
  }

  public class QuickPickWindow : Window
  {
    private TextBox _searchBox;
    private ListBox _entriesList;
    private List<QuickPickEntry> _allEntries;
    private List<QuickPickEntry> _filteredEntries;
    private QuickPickOptions _options;

    public QuickPickEntry? SelectedEntry { get; private set; }

    public QuickPickWindow(List<QuickPickEntry> entries, QuickPickOptions options)
    {
      _allEntries = new List<QuickPickEntry>(entries);
      _filteredEntries = new List<QuickPickEntry>(entries);
      _options = options;
      SelectedEntry = null;

      InitializeComponents();
      ApplyOptions();
    }

    private void InitializeComponents()
    {
      var grid = new Grid { Margin = new Thickness(10) };
      grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
      grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
      grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

      _searchBox = new TextBox { Height = 30, Margin = new Thickness(0, 0, 0, 10) };
      _searchBox.TextChanged += OnSearchTextChanged;
      Grid.SetRow(_searchBox, 0);
      grid.Children.Add(_searchBox);

      _entriesList = new ListBox { Height = 300, Margin = new Thickness(0, 0, 0, 10) };
      _entriesList.SelectionChanged += OnSelectionChanged;
      
      var template = new DataTemplate();
      var factory = new FrameworkElementFactory(typeof(StackPanel));
      factory.SetValue(StackPanel.OrientationProperty, Orientation.Vertical);
      
      var label = new FrameworkElementFactory(typeof(TextBlock));
      label.SetValue(TextBlock.TextProperty, new Binding("Label"));
      label.SetValue(TextBlock.FontWeightProperty, FontWeights.Bold);
      label.SetValue(TextBlock.MarginProperty, new Thickness(0, 0, 0, 2));
      factory.AppendChild(label);

      var desc = new FrameworkElementFactory(typeof(TextBlock));
      desc.SetValue(TextBlock.TextProperty, new Binding("Description"));
      desc.SetValue(TextBlock.FontSizeProperty, 10d);
      desc.SetValue(TextBlock.ForegroundProperty, System.Windows.Media.Brushes.Gray);
      factory.AppendChild(desc);

      var detail = new FrameworkElementFactory(typeof(TextBlock));
      detail.SetValue(TextBlock.TextProperty, new Binding("Detail"));
      detail.SetValue(TextBlock.FontSizeProperty, 10d);
      detail.SetValue(TextBlock.ForegroundProperty, System.Windows.Media.Brushes.Gray);
      factory.AppendChild(detail);

      template.VisualTree = factory;
      _entriesList.ItemTemplate = template;
      _entriesList.ItemsSource = _filteredEntries;
      
      Grid.SetRow(_entriesList, 1);
      grid.Children.Add(_entriesList);

      var buttonPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
      
      var okButton = new Button { Content = "OK", Width = 80, Margin = new Thickness(0, 0, 10, 0) };
      okButton.Click += (s, e) => { SelectedEntry = _entriesList.SelectedItem as QuickPickEntry; DialogResult = true; Close(); };
      
      var cancelButton = new Button { Content = "Cancel", Width = 80 };
      cancelButton.Click += (s, e) => { DialogResult = false; Close(); };
      
      buttonPanel.Children.Add(okButton);
      buttonPanel.Children.Add(cancelButton);
      Grid.SetRow(buttonPanel, 2);
      grid.Children.Add(buttonPanel);

      Content = grid;
      Width = 500;
      MinHeight = 400;
      WindowStartupLocation = WindowStartupLocation.CenterOwner;
      KeyDown += OnKeyDown;
    }

    private void ApplyOptions()
    {
      if (!string.IsNullOrEmpty(_options.Title))
        Title = _options.Title;
      
      if (!string.IsNullOrEmpty(_options.PlaceHolder))
        _searchBox.Hint = _options.PlaceHolder;
    }

    private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
    {
      var query = _searchBox.Text.ToLower();
      
      if (string.IsNullOrEmpty(query))
      {
        _filteredEntries = new List<QuickPickEntry>(_allEntries);
      }
      else
      {
        _filteredEntries = _allEntries.Where(entry =>
        {
          var matches = entry.Label.ToLower().Contains(query);
          if (_options.MatchOnDescription && entry.Description != null)
            matches |= entry.Description.ToLower().Contains(query);
          if (_options.MatchOnDetail && entry.Detail != null)
            matches |= entry.Detail.ToLower().Contains(query);
          return matches;
        }).ToList();
      }
      
      _entriesList.ItemsSource = _filteredEntries;
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
      if (_entriesList.SelectedItem is QuickPickEntry entry)
        SelectedEntry = entry;
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
      if (e.Key == Key.Enter && SelectedEntry != null)
      {
        DialogResult = true;
        Close();
      }
      else if (e.Key == Key.Escape)
      {
        DialogResult = false;
        Close();
      }
    }
  }

  public static class QuickPickHelper
  {
    public static async Task<QuickPickEntry?> ShowQuickPickAsync(
      List<QuickPickEntry> entries,
      QuickPickOptions options,
      IWin32Window? owner = null)
    {
      await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
      
      var window = new QuickPickWindow(entries, options);
      
      //if (owner != null)
      //  window.Owner = System.Windows.Interop.WindowInteropHelper.ApplicationWindow;
      
      var result = window.ShowDialog();
      
      return result == true ? window.SelectedEntry : null;
    }
  }
}
