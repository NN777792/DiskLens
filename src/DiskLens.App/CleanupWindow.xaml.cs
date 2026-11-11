using System.Windows;
using DiskLens.Core;

namespace DiskLens.App;
public partial class CleanupWindow : Window
{
    public CleanupWindow(IReadOnlyList<CleanupItem> items)
    {
        InitializeComponent(); ReviewGrid.ItemsSource = items;
        Summary.Text = $"共 {items.Count:N0} 个文件，逻辑大小 {SizeFormatter.Format(items.Sum(x => x.File.Length))}。执行前会逐项重新检查。";
    }
    private void ConfirmClick(object sender, RoutedEventArgs e) => DialogResult = true;
}
