using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace KiloVisualStudioExtension
{
    /// <summary>
    /// Toolbar control for the Kilo tool window with action buttons.
    /// Matches VS Code's sidebar title bar with New Task, History, Agent Manager, etc.
    /// </summary>
    public class KiloToolbar : UserControl
    {
        public static readonly DependencyProperty OnActionCommandProperty =
            DependencyProperty.Register(nameof(OnActionCommand), typeof(Action<string>), typeof(KiloToolbar));

        public Action<string>? OnActionCommand
        {
            get => (Action<string>?)GetValue(OnActionCommandProperty);
            set => SetValue(OnActionCommandProperty, value);
        }

        private readonly Button[] _buttons = new Button[7];
        private readonly string[] _actionNames = { "newTask", "history", "agentManager", "kiloClaw", "marketplace", "profile", "settings" };
        private readonly string[] _tooltips = { "New Task", "History", "Agent Manager", "KiloClaw", "Marketplace", "Profile", "Settings" };

        public KiloToolbar()
        {
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            CreateButtons();
        }

        private void CreateButtons()
        {
            var stackPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
                Margin = new Thickness(10, 5, 10, 5)
            };

            for (int i = 0; i < 7; i++)
            {
                var button = CreateIconButton(_actionNames[i], _tooltips[i]);
                _buttons[i] = button;
                stackPanel.Children.Add(button);

                // Add separator except after last button
                if (i < 6)
                {
                    var separator = new Border
                    {
                        Width = 1,
                        Margin = new Thickness(8, 0, 8, 0),
                        Background = new SolidColorBrush(Color.FromRgb(68, 68, 68))
                    };
                    stackPanel.Children.Add(separator);
                }
            }

            this.Content = stackPanel;
        }

        private Button CreateIconButton(string action, string tooltip)
        {
            var button = new Button
            {
                ToolTip = tooltip,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Width = 24,
                Height = 24,
                Padding = new Thickness(0),
                Cursor = Cursors.Hand
            };

            // Create icon path based on action
            Geometry iconPath = GetIconPath(action);
            
            var path = new Path
            {
                Data = iconPath,
                Stroke = new SolidColorBrush(Color.FromRgb(200, 200, 200)),
                StrokeThickness = 2,
                Width = 16,
                Height = 16,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = System.Windows.VerticalAlignment.Center
            };

            button.Content = path;
            button.Click += (s, e) => OnActionCommand?.Invoke(action);

            // Hover effect
            button.MouseEnter += (s, e) => path.Stroke = new SolidColorBrush(Color.FromRgb(255, 255, 255));
            button.MouseLeave += (s, e) => path.Stroke = new SolidColorBrush(Color.FromRgb(200, 200, 200));

            return button;
        }

        private Geometry GetIconPath(string action)
        {
            return action switch
            {
                "newTask" => CreatePlusIcon(),
                "history" => CreateHistoryIcon(),
                "agentManager" => CreateAgentManagerIcon(),
                "kiloClaw" => CreateKiloClawIcon(),
                "marketplace" => CreateMarketplaceIcon(),
                "profile" => CreateProfileIcon(),
                "settings" => CreateSettingsIcon(),
                _ => CreatePlusIcon()
            };
        }

        private Geometry CreatePlusIcon()
        {
            // Plus icon for New Task
            var geometryGroup = new GeometryGroup();
            geometryGroup.Children.Add(new LineGeometry(new Point(12, 4), new Point(12, 20)));
            geometryGroup.Children.Add(new LineGeometry(new Point(4, 12), new Point(20, 12)));
            return geometryGroup;
        }

        private Geometry CreateHistoryIcon()
        {
            // Clock/history icon
            var geometryGroup = new GeometryGroup();
            geometryGroup.Children.Add(new EllipseGeometry(new Rect(4, 4, 16, 16)));
            geometryGroup.Children.Add(new LineGeometry(new Point(12, 12), new Point(12, 8)));
            geometryGroup.Children.Add(new LineGeometry(new Point(12, 12), new Point(15, 10)));
            return geometryGroup;
        }

        private Geometry CreateAgentManagerIcon()
        {
            // Agent Manager icon (multiple tabs/sessions)
            var geometryGroup = new GeometryGroup();
            geometryGroup.Children.Add(new RectangleGeometry(new Rect(3, 3, 8, 10)));
            geometryGroup.Children.Add(new RectangleGeometry(new Rect(10, 6, 8, 10)));
            return geometryGroup;
        }

        private Geometry CreateKiloClawIcon()
        {
            // KiloClaw icon (chat/claw)
            var geometryGroup = new GeometryGroup();
            geometryGroup.Children.Add(new RectangleGeometry(new Rect(4, 4, 16, 12)));
            geometryGroup.Children.Add(new LineGeometry(new Point(8, 10), new Point(16, 10)));
            geometryGroup.Children.Add(new LineGeometry(new Point(8, 14), new Point(12, 14)));
            return geometryGroup;
        }

        private Geometry CreateMarketplaceIcon()
        {
            // Marketplace icon (shopping bag)
            var geometryGroup = new GeometryGroup();
            geometryGroup.Children.Add(new RectangleGeometry(new Rect(5, 8, 14, 12)));
            var figure = new PathFigure(new Point(8, 8), new[] { new BezierSegment(new Point(8, 4), new Point(16, 4), new Point(16, 8), true) }, false);
            geometryGroup.Children.Add(new PathGeometry(new[] { figure }));
            return geometryGroup;
        }

        private Geometry CreateProfileIcon()
        {
            // Profile icon (user)
            var geometryGroup = new GeometryGroup();
            geometryGroup.Children.Add(new EllipseGeometry(new Rect(8, 4, 8, 8)));
            geometryGroup.Children.Add(new EllipseGeometry(new Rect(4, 14, 16, 6)));
            return geometryGroup;
        }

        private Geometry CreateSettingsIcon()
        {
            // Settings icon (gear)
            var geometryGroup = new GeometryGroup();
            geometryGroup.Children.Add(new EllipseGeometry(new Rect(6, 6, 12, 12)));
            geometryGroup.Children.Add(new EllipseGeometry(new Rect(10, 10, 4, 4)));
            return geometryGroup;
        }
    }
}
