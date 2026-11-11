using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using DiskLens.App.Services;
using DiskLens.App.ViewModels;
using DiskLens.Core;
using Microsoft.Win32;

namespace DiskLens.App;

public partial class MainWindow : Window
{
    public MainViewModel Model { get; } = new();
    private CancellationTokenSource? scanCancellation;
    public MainWindow()
    {
        InitializeComponent(); DataContext = Model;
        Map.Navigate += node => { if (!Model.IsBusy) Model.Navigate(node); };
        Closing += OnClosing;
    }
    private async Task ScanAsync(string path)
    {
        if (Model.IsBusy) return;
        scanCancellation?.Dispose(); scanCancellation = new();
        Model.BeginScan(path);
        try
        {
            var result = await new DiskScanner(Model.Policy).ScanAsync(path, new Progress<ScanProgress>(Model.AcceptProgress), scanCancellation.Token);
            Model.Load(result); MainTabs.SelectedIndex = 0;
        }
        catch (Exception ex)
        {
            Model.IsScanning = false; Model.IsBusy = false; Model.Status = "扫描未完成";
            Model.Duration = "—"; Model.Notice = "无法完成扫描，请确认目录存在且有访问权限。";
            ShowError("无法扫描", ex);
        }
    }
    private async void DriveClick(object sender, RoutedEventArgs e) { if (sender is Button { Tag: string path }) await ScanAsync(path); }
    private async void FolderClick(object sender, RoutedEventArgs e)
    {
        var picker = new OpenFolderDialog { Title = "选择要分析的本地文件夹", Multiselect = false };
        if (picker.ShowDialog(this) == true) await ScanAsync(picker.FolderName);
    }
    private void RefreshClick(object sender, RoutedEventArgs e) { try { Model.RefreshDrives(); } catch (Exception ex) { ShowError("无法刷新磁盘", ex); } }
    private void CancelClick(object sender, RoutedEventArgs e) { scanCancellation?.Cancel(); Model.Status = "正在取消扫描…"; }
    private void OverviewClick(object sender, RoutedEventArgs e) { MainTabs.SelectedIndex = 0; if (Model.Result != null) Model.Navigate(Model.Result.Root); }
    private void AnalysisClick(object sender, RoutedEventArgs e) => MainTabs.SelectedIndex = 0;
    private void CleanupClick(object sender, RoutedEventArgs e) => MainTabs.SelectedIndex = 2;
    private void HistoryClick(object sender, RoutedEventArgs e) { try { Model.RefreshHistory(); MainTabs.SelectedIndex = 3; } catch (Exception ex) { ShowError("无法读取记录", ex); } }
    private void DirectoryDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (Model.IsBusy || DirectoryGrid.SelectedItem is not DirectoryRow row) return;
        if (ItemsControl.ContainerFromElement(DirectoryGrid, e.OriginalSource as DependencyObject) is DataGridRow) Model.Navigate(row.Node);
    }
    private void OpenFileLocationClick(object sender, RoutedEventArgs e)
    {
        if (LargeFilesGrid.SelectedItem is not FileRow row) return;
        try { var start = new ProcessStartInfo("explorer.exe"); start.ArgumentList.Add("/select,"); start.ArgumentList.Add(row.Path); Process.Start(start); }
        catch (Exception ex) { ShowError("无法打开所在位置", ex); }
    }
    private void HistoryFolderClick(object sender, RoutedEventArgs e)
    {
        try { Directory.CreateDirectory(JsonJournal.Folder); Process.Start(new ProcessStartInfo(JsonJournal.Folder) { UseShellExecute = true }); }
        catch (Exception ex) { ShowError("无法打开记录", ex); }
    }
    private async void ReviewClick(object sender, RoutedEventArgs e)
    {
        if (Model.IsBusy) return;
        var chosen = Model.AllRows.Where(x => x.Selected).Select(x => new CleanupItem(x.File, x.Reason)).ToArray();
        if (chosen.Length == 0) return;
        var dialog = new CleanupWindow(chosen) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        Model.IsBusy = true; Model.SelectionChanged(); Model.Status = "正在移入回收站…";
        try
        {
            var owner = new WindowInteropHelper(this).Handle;
            var result = await RunSta(() => new CleanupService(Model.Policy, new WindowsRecycler(owner), new JsonJournal()).Execute(chosen,
                progress: (done, total) => Dispatcher.BeginInvoke(() => Model.Status = $"正在移入回收站 · {done}/{total}")));
            Model.MarkRecycled(result);
            Model.Status = $"操作完成 · {result.SuccessCount} 项移入回收站 · {result.Outcomes.Count - result.SuccessCount} 项未处理";
            var lines = result.Outcomes.Where(x => !x.Success).Take(10).Select(x => $"{x.Path}\n{x.Message}");
            MessageBox.Show(this, $"已移入回收站：{result.SuccessCount} 项，文件大小 {SizeFormatter.Format(result.RecycledBytes)}。\n\n回收站仍占用磁盘空间；需要恢复时，请打开 Windows 回收站并选择“还原”。\n\n" + string.Join("\n\n", lines) + (result.JournalWarning == null ? "" : "\n\n" + result.JournalWarning), "操作结果", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex) { Model.Status = "操作未完成，请查看记录"; ShowError("清理未完成", ex); }
        finally { Model.IsBusy = false; Model.SelectionChanged(); }
    }
    private static Task<T> RunSta<T>(Func<T> operation)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() => { try { completion.SetResult(operation()); } catch (Exception ex) { completion.SetException(ex); } }) { IsBackground = true, Name = "DiskLens.Shell" };
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); return completion.Task;
    }
    private void ScanDetailsClick(object sender, RoutedEventArgs e)
    {
        if (Model.Result is not { } result) { MessageBox.Show(this, "扫描完成后，这里会显示扫描范围和跳过原因。", "扫描详情"); return; }
        var text = $"范围：{result.Root.FullPath}\n已统计：{result.Root.FileCount:N0} 个文件，{result.Root.SizeText}\n取消：{(result.Cancelled ? "是" : "否")}；目录上限：{(result.LimitReached ? "已达到" : "未达到")}\n跳过：{result.Skipped:N0} 项（详情最多保留 500 项）\n\n逻辑大小不等于实际占用；硬链接可能重复计数。\n\n" + string.Join("\n\n", result.Issues.Select(x => $"{x.Path}\n{x.Reason}"));
        new DetailsWindow("扫描详情", text) { Owner = this }.ShowDialog();
    }
    private void ProtectionClick(object sender, RoutedEventArgs e)
    {
        new DetailsWindow("默认保护规则", "扫描仅在本机读取路径、大小和修改时间，不读取文件内容。\n\n• 不跟随目录链接，不访问云盘占位文件。\n• 系统目录、程序安装目录、ProgramData、AppData 默认不可清理。\n• 系统、隐藏、只读、离线文件默认不可清理。\n• 唯一的应用数据例外：当前用户 LocalAppData\\Temp 中至少 7 天未修改的普通文件。\n• 文件大、很久没修改，都不能证明它可以安全删除。\n• 清理前重新检查路径、大小、修改时间与属性；变化的文件跳过。\n• 仅在本地固定磁盘调用 Windows 回收站；不提供永久删除或清空回收站。\n• 移入回收站不会立即释放磁盘空间，可在回收站还原。\n• 操作记录存储在 %LOCALAPPDATA%\\DiskLens\\History，包含完整路径；不上传网络。\n\n本版本按路径和属性保护文件，不识别所有软件依赖，也不能保证消除检查与操作之间的竞态。清理个人文件前请核实用途。") { Owner = this }.ShowDialog();
    }
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (Model.IsBusy && !Model.IsScanning) { e.Cancel = true; MessageBox.Show(this, "文件操作正在进行，请等待本次操作完成后再关闭。", "操作进行中"); }
        else scanCancellation?.Cancel();
    }
    private void ShowError(string title, Exception ex) => MessageBox.Show(this, ex.Message, title, MessageBoxButton.OK, MessageBoxImage.Warning);
}
