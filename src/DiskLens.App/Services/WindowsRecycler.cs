using System.IO;
using System.Runtime.InteropServices;
using DiskLens.Core;

namespace DiskLens.App.Services;

public sealed class WindowsRecycler(nint owner) : IFileRecycler
{
    private const uint RecycleOnDelete = 0x00080000;
    private const uint AddUndoRecord = 0x20000000;
    private const uint NoConnectedElements = 0x00002000;
    public void Recycle(string fullPath)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10)) throw new PlatformNotSupportedException("需要 Windows 10 或更新版本。");
        var root = Path.GetPathRoot(Path.GetFullPath(fullPath)) ?? throw new IOException("无效磁盘路径。");
        if (new DriveInfo(root).DriveType != DriveType.Fixed) throw new IOException("仅支持本地固定磁盘的回收站操作。");
        IFileOperation? operation = null; IShellItem? item = null;
        try
        {
            operation = (IFileOperation)new FileOperation();
            // RECYCLEONDELETE explicitly requests recycling. No delete API or permanent-delete fallback exists here.
            // Do not set NOCONFIRMATION: keep any shell warning visible to the user.
            Marshal.ThrowExceptionForHR(operation.SetOperationFlags(RecycleOnDelete | AddUndoRecord | NoConnectedElements));
            Marshal.ThrowExceptionForHR(operation.SetOwnerWindow(owner));
            var iid = typeof(IShellItem).GUID;
            Marshal.ThrowExceptionForHR(SHCreateItemFromParsingName(fullPath, 0, ref iid, out item));
            var sink = new RecycleSink();
            Marshal.ThrowExceptionForHR(operation.DeleteItem(item, sink));
            Marshal.ThrowExceptionForHR(operation.PerformOperations());
            Marshal.ThrowExceptionForHR(operation.GetAnyOperationsAborted(out var aborted));
            if (aborted || !sink.Completed) throw new IOException("操作被取消，或 Windows 未确认移入回收站。");
            Marshal.ThrowExceptionForHR(sink.Result);
            if (sink.Result != 0 || File.Exists(fullPath)) throw new IOException("Windows 未返回已回收的成功状态，请核实文件和回收站。");
        }
        finally
        {
            if (item != null) Marshal.ReleaseComObject(item);
            if (operation != null) Marshal.ReleaseComObject(operation);
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int SHCreateItemFromParsingName(string path, nint bindContext, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out IShellItem item);

    [ComImport, Guid("3AD05575-8857-4850-9277-11B85BDB8E09"), ClassInterface(ClassInterfaceType.None)]
    private class FileOperation { }

    [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IShellItem { }

    [ComImport, Guid("947AAB5F-0A5C-4C13-B4D6-4BF7836FC9F8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFileOperation
    {
        [PreserveSig] int Advise(IFileOperationProgressSink sink, out uint cookie);
        [PreserveSig] int Unadvise(uint cookie);
        [PreserveSig] int SetOperationFlags(uint flags);
        [PreserveSig] int SetProgressMessage([MarshalAs(UnmanagedType.LPWStr)] string message);
        [PreserveSig] int SetProgressDialog(nint dialog);
        [PreserveSig] int SetProperties(nint properties);
        [PreserveSig] int SetOwnerWindow(nint hwnd);
        [PreserveSig] int ApplyPropertiesToItem(IShellItem item);
        [PreserveSig] int ApplyPropertiesToItems(nint items);
        [PreserveSig] int RenameItem(IShellItem item, [MarshalAs(UnmanagedType.LPWStr)] string name, IFileOperationProgressSink? sink);
        [PreserveSig] int RenameItems(nint items, [MarshalAs(UnmanagedType.LPWStr)] string name);
        [PreserveSig] int MoveItem(IShellItem item, IShellItem destination, [MarshalAs(UnmanagedType.LPWStr)] string? name, IFileOperationProgressSink? sink);
        [PreserveSig] int MoveItems(nint items, IShellItem destination);
        [PreserveSig] int CopyItem(IShellItem item, IShellItem destination, [MarshalAs(UnmanagedType.LPWStr)] string? name, IFileOperationProgressSink? sink);
        [PreserveSig] int CopyItems(nint items, IShellItem destination);
        [PreserveSig] int DeleteItem(IShellItem item, IFileOperationProgressSink? sink);
        [PreserveSig] int DeleteItems(nint items);
        [PreserveSig] int NewItem(IShellItem destination, uint attributes, [MarshalAs(UnmanagedType.LPWStr)] string name, [MarshalAs(UnmanagedType.LPWStr)] string? templateName, IFileOperationProgressSink? sink);
        [PreserveSig] int PerformOperations();
        [PreserveSig] int GetAnyOperationsAborted([MarshalAs(UnmanagedType.Bool)] out bool aborted);
    }

    [ComVisible(true), Guid("04B0F1A7-9490-44BC-96E1-4296A31252E2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IFileOperationProgressSink
    {
        [PreserveSig] int StartOperations();
        [PreserveSig] int FinishOperations(int result);
        [PreserveSig] int PreRenameItem(uint flags, IShellItem item, [MarshalAs(UnmanagedType.LPWStr)] string name);
        [PreserveSig] int PostRenameItem(uint flags, IShellItem item, [MarshalAs(UnmanagedType.LPWStr)] string name, int result, IShellItem? newItem);
        [PreserveSig] int PreMoveItem(uint flags, IShellItem item, IShellItem destination, [MarshalAs(UnmanagedType.LPWStr)] string? name);
        [PreserveSig] int PostMoveItem(uint flags, IShellItem item, IShellItem destination, [MarshalAs(UnmanagedType.LPWStr)] string? name, int result, IShellItem? newItem);
        [PreserveSig] int PreCopyItem(uint flags, IShellItem item, IShellItem destination, [MarshalAs(UnmanagedType.LPWStr)] string? name);
        [PreserveSig] int PostCopyItem(uint flags, IShellItem item, IShellItem destination, [MarshalAs(UnmanagedType.LPWStr)] string? name, int result, IShellItem? newItem);
        [PreserveSig] int PreDeleteItem(uint flags, IShellItem item);
        [PreserveSig] int PostDeleteItem(uint flags, IShellItem item, int result, IShellItem? newItem);
        [PreserveSig] int PreNewItem(uint flags, IShellItem destination, [MarshalAs(UnmanagedType.LPWStr)] string name);
        [PreserveSig] int PostNewItem(uint flags, IShellItem destination, [MarshalAs(UnmanagedType.LPWStr)] string name, [MarshalAs(UnmanagedType.LPWStr)] string? templateName, uint attributes, int result, IShellItem? newItem);
        [PreserveSig] int UpdateProgress(uint total, uint completed);
        [PreserveSig] int ResetTimer();
        [PreserveSig] int PauseTimer();
        [PreserveSig] int ResumeTimer();
    }

    [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
    public sealed class RecycleSink : IFileOperationProgressSink
    {
        public bool Completed { get; private set; }
        public int Result { get; private set; }
        public int StartOperations() => 0;
        public int FinishOperations(int result) => 0;
        public int PreDeleteItem(uint flags, IShellItem item) => (flags & 0x80) != 0 ? 0 : unchecked((int)0x80004004); // refuse non-recycle transfers
        public int PostDeleteItem(uint flags, IShellItem item, int result, IShellItem? newItem) { Completed = true; Result = result; return 0; }
        public int PreRenameItem(uint flags, IShellItem item, string name) => 0;
        public int PostRenameItem(uint flags, IShellItem item, string name, int result, IShellItem? newItem) => 0;
        public int PreMoveItem(uint flags, IShellItem item, IShellItem destination, string? name) => 0;
        public int PostMoveItem(uint flags, IShellItem item, IShellItem destination, string? name, int result, IShellItem? newItem) => 0;
        public int PreCopyItem(uint flags, IShellItem item, IShellItem destination, string? name) => 0;
        public int PostCopyItem(uint flags, IShellItem item, IShellItem destination, string? name, int result, IShellItem? newItem) => 0;
        public int PreNewItem(uint flags, IShellItem destination, string name) => 0;
        public int PostNewItem(uint flags, IShellItem destination, string name, string? templateName, uint attributes, int result, IShellItem? newItem) => 0;
        public int UpdateProgress(uint total, uint completed) => 0;
        public int ResetTimer() => 0;
        public int PauseTimer() => 0;
        public int ResumeTimer() => 0;
    }
}
