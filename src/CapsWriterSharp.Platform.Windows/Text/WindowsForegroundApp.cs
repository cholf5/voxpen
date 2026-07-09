using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using CapsWriterSharp.Core.Abstractions;

namespace CapsWriterSharp.Platform.Windows.Text;

/// <summary>
/// 通过 Win32 API 探测前台窗口所属进程的可执行文件名。
/// </summary>
public sealed class WindowsForegroundApp : IForegroundApp
{
    public string? GetForegroundExeName()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return null;

        GetWindowThreadProcessId(hwnd, out uint pid);
        if (pid == 0) return null;

        // 优先使用 QueryFullProcessImageName（比 Process.GetProcessById 稳、无异常）
        var handle = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (handle != IntPtr.Zero)
        {
            try
            {
                var buf = new StringBuilder(1024);
                int size = buf.Capacity;
                if (QueryFullProcessImageName(handle, 0, buf, ref size))
                {
                    return Path.GetFileName(buf.ToString());
                }
            }
            finally
            {
                CloseHandle(handle);
            }
        }

        // 兜底
        try
        {
            using var p = Process.GetProcessById((int)pid);
            return Path.GetFileName(p.MainModule?.FileName ?? p.ProcessName);
        }
        catch
        {
            return null;
        }
    }

    private const int PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(int desiredAccess, bool inheritHandle, uint processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool QueryFullProcessImageName(IntPtr hProcess, uint dwFlags, StringBuilder lpExeName, ref int lpdwSize);
}
