using System.Windows;
using System.Windows.Controls;

namespace DiskLens.App;
public sealed class DetailsWindow : Window
{
    public DetailsWindow(string title, string text)
    {
        Title = title; Width = 780; Height = 560; MinWidth = 500; MinHeight = 350;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Content = new TextBox { Text = text, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(24), BorderThickness = new Thickness(0), Padding = new Thickness(16) };
    }
}
