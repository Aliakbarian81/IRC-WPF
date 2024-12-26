using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Metadata;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Media.Media3D;
using System.Windows.Media;
using System.Windows;

namespace IRC_WPF
{
    public class InputDialog : Window
    {
        private TextBox inputBox;
        public string ResponseText { get; private set; }

        public InputDialog(string title, string prompt)
        {
            Title = title;
            Width = 300;
            Height = 150;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E1E1E"));

            var grid = new Grid { Margin = new Thickness(10) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var promptText = new TextBlock
            {
                Text = prompt,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E0E0E0")),
                Margin = new Thickness(0, 0, 0, 5)
            };
            Grid.SetRow(promptText, 0);

            inputBox = new TextBox
            {
                Margin = new Thickness(0, 0, 0, 10),
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2D2D2D")),
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E0E0E0"))
            };
            Grid.SetRow(inputBox, 1);

            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            var okButton = new Button
            {
                Content = "OK",
                Width = 70,
                Margin = new Thickness(0, 0, 5, 0),
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#007ACC")),
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E0E0E0"))
            };
            okButton.Click += (s, e) => { ResponseText = inputBox.Text; DialogResult = true; };

            var cancelButton = new Button
            {
                Content = "Cancel",
                Width = 70,
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2D2D2D")),
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E0E0E0"))
            };
            cancelButton.Click += (s, e) => DialogResult = false;

            buttonPanel.Children.Add(okButton);
            buttonPanel.Children.Add(cancelButton);
            Grid.SetRow(buttonPanel, 2);

            grid.Children.Add(promptText);
            grid.Children.Add(inputBox);
            grid.Children.Add(buttonPanel);

            Content = grid;
        }
    }
}
