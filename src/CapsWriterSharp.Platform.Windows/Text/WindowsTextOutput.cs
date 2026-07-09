using System.Diagnostics;
using System.Runtime.InteropServices;
using CapsWriterSharp.Core.Abstractions;

namespace CapsWriterSharp.Platform.Windows.Text;

/// <summary>
/// 通过 <c>SendInput</c> 向前台窗口注入文字，或经剪贴板模拟 Ctrl+V 粘贴。
///
/// - 打字模式：<c>KEYEVENTF_UNICODE</c>，逐字符发送，避开输入法（IME）拦截，
///   代理对（surrogate pair，例如 emoji）需两个 INPUT。
/// - 粘贴模式：写 CF_UNICODETEXT → 模拟 Ctrl+V → 可选恢复原剪贴板。
///
/// 注：向以管理员权限运行的窗口输入需要本进程也以管理员运行（UIPI 隔离）。
/// </summary>
public sealed class WindowsTextOutput : ITextOutput
{
    private const int TypeIntervalMs = 1;  // 每字符间隔，避免快速输入被应用丢弃

    public async Task TypeAsync(string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(text)) return;

        foreach (var rune in text.EnumerateRunes())
        {
            cancellationToken.ThrowIfCancellationRequested();
            SendUnicodeChar(rune.Value);
            if (TypeIntervalMs > 0)
            {
                await Task.Delay(TypeIntervalMs, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    public async Task PasteAsync(string text, bool restoreClipboard)
    {
        if (string.IsNullOrEmpty(text)) return;

        string? backup = null;
        if (restoreClipboard)
        {
            backup = ClipboardInterop.TryGetUnicodeText();
        }

        ClipboardInterop.SetUnicodeText(text);

        // 模拟 Ctrl+V
        SendKey(VK_CONTROL, keyUp: false);
        SendKey(VK_V, keyUp: false);
        SendKey(VK_V, keyUp: true);
        SendKey(VK_CONTROL, keyUp: true);

        if (restoreClipboard && backup is not null)
        {
            // 给目标应用一点时间读取剪贴板，再恢复
            await Task.Delay(150).ConfigureAwait(false);
            ClipboardInterop.SetUnicodeText(backup);
        }
    }

    // ---------- SendInput ----------

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public int type;
        public INPUTUNION u;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct INPUTUNION
    {
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    private const int INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint KEYEVENTF_UNICODE = 0x0004;
    private const ushort VK_CONTROL = 0x11;
    private const ushort VK_V = 0x56;
    private const ushort VK_CAPITAL = 0x14;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    private static void SendUnicodeChar(int codepoint)
    {
        if (codepoint <= 0xFFFF)
        {
            SendUnicodeUnit((ushort)codepoint);
            return;
        }
        // 代理对
        int adjusted = codepoint - 0x10000;
        ushort high = (ushort)(0xD800 + (adjusted >> 10));
        ushort low = (ushort)(0xDC00 + (adjusted & 0x3FF));
        SendUnicodeUnit(high);
        SendUnicodeUnit(low);
    }

    private static void SendUnicodeUnit(ushort unit)
    {
        var inputs = new INPUT[2];
        inputs[0].type = INPUT_KEYBOARD;
        inputs[0].u.ki = new KEYBDINPUT
        {
            wVk = 0,
            wScan = unit,
            dwFlags = KEYEVENTF_UNICODE,
            time = 0,
            dwExtraInfo = IntPtr.Zero,
        };
        inputs[1].type = INPUT_KEYBOARD;
        inputs[1].u.ki = new KEYBDINPUT
        {
            wVk = 0,
            wScan = unit,
            dwFlags = KEYEVENTF_UNICODE | KEYEVENTF_KEYUP,
            time = 0,
            dwExtraInfo = IntPtr.Zero,
        };
        if (SendInput(2, inputs, Marshal.SizeOf<INPUT>()) != 2)
        {
            Debug.WriteLine($"SendInput failed with LastError={Marshal.GetLastWin32Error()}");
        }
    }

    private static void SendKey(ushort vk, bool keyUp)
    {
        var input = new INPUT
        {
            type = INPUT_KEYBOARD,
            u = new INPUTUNION
            {
                ki = new KEYBDINPUT
                {
                    wVk = vk,
                    wScan = 0,
                    dwFlags = keyUp ? KEYEVENTF_KEYUP : 0,
                    time = 0,
                    dwExtraInfo = IntPtr.Zero,
                }
            }
        };
        SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
    }

    /// <summary>
    /// 合成一次 CapsLock 按下再松开。用于短按补发场景：
    /// 若快捷键抑制期间用户短按 CapsLock (&lt; 阈值)，我们撤销录音并补发该键，
    /// 让用户仍然能像平常一样切换大小写状态。
    /// </summary>
    public void ResendCapsLock()
    {
        SendKey(VK_CAPITAL, keyUp: false);
        SendKey(VK_CAPITAL, keyUp: true);
    }
}

/// <summary>Windows 剪贴板互操作，只处理 Unicode 文本。</summary>
internal static class ClipboardInterop
{
    private const uint CF_UNICODETEXT = 13;
    private const uint GMEM_MOVEABLE = 0x0002;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool OpenClipboard(IntPtr hWndNewOwner);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool EmptyClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetClipboardData(uint uFormat);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalAlloc(uint uFlags, UIntPtr dwBytes);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GlobalLock(IntPtr hMem);

    [DllImport("kernel32.dll")]
    private static extern bool GlobalUnlock(IntPtr hMem);

    public static void SetUnicodeText(string text)
    {
        if (!OpenClipboardWithRetry()) return;
        try
        {
            EmptyClipboard();

            var bytes = (text.Length + 1) * 2;
            var hMem = GlobalAlloc(GMEM_MOVEABLE, (UIntPtr)bytes);
            if (hMem == IntPtr.Zero) return;

            var ptr = GlobalLock(hMem);
            try
            {
                Marshal.Copy(text.ToCharArray(), 0, ptr, text.Length);
                Marshal.WriteInt16(ptr, text.Length * 2, 0);
            }
            finally
            {
                GlobalUnlock(hMem);
            }

            SetClipboardData(CF_UNICODETEXT, hMem);
            // Ownership transferred to clipboard; do NOT GlobalFree
        }
        finally
        {
            CloseClipboard();
        }
    }

    public static string? TryGetUnicodeText()
    {
        if (!OpenClipboardWithRetry()) return null;
        try
        {
            var hMem = GetClipboardData(CF_UNICODETEXT);
            if (hMem == IntPtr.Zero) return null;
            var ptr = GlobalLock(hMem);
            try
            {
                return ptr == IntPtr.Zero ? null : Marshal.PtrToStringUni(ptr);
            }
            finally
            {
                GlobalUnlock(hMem);
            }
        }
        finally
        {
            CloseClipboard();
        }
    }

    private static bool OpenClipboardWithRetry(int attempts = 5, int delayMs = 20)
    {
        for (int i = 0; i < attempts; i++)
        {
            if (OpenClipboard(IntPtr.Zero)) return true;
            Thread.Sleep(delayMs);
        }
        return false;
    }
}
