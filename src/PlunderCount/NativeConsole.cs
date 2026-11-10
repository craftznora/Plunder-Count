using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace PlunderCount;

/// <summary>
/// The exe is a Windows application, so that the "start with Fortnite" standby process has no window at all.
/// The normal tool gives itself a console window here, before anything is written.
/// </summary>
[SupportedOSPlatform("windows")]
public static class NativeConsole
{
    /// <param name="attachToParent">Command line use (--once, --help): write into the terminal that started the exe.</param>
    public static void Ensure(bool attachToParent)
    {
        if (GetConsoleWindow() != IntPtr.Zero)
        {
            return; // already in a console (dotnet host, inherited)
        }
        if (IsRedirected(STD_OUTPUT_HANDLE))
        {
            return; // output goes to a pipe or a file: Console uses it as is
        }
        if (attachToParent && AttachConsole(ATTACH_PARENT_PROCESS))
        {
            return;
        }
        if (AllocConsole())
        {
            ApplyTheme(); // our own window: give it the Plunder Count palette
        }
    }

    // Plunder Count palette (design 2c): near-black, warm ink, gold accent, green / violet states. Indexed by ConsoleColor.
    private static readonly (byte R, byte G, byte B)[] Theme =
    {
        (10, 10, 11),    // Black       (window background, #0a0a0b)
        (26, 26, 29),    // DarkBlue
        (90, 140, 108),  // DarkGreen
        (90, 104, 116),  // DarkCyan
        (140, 78, 72),   // DarkRed
        (120, 108, 150), // DarkMagenta
        (168, 138, 82),  // DarkYellow  (#a88a52)
        (207, 204, 197), // Gray        (normal text, #cfccc5)
        (95, 94, 90),    // DarkGray    (secondary text, #5f5e5a)
        (127, 143, 184), // Blue
        (143, 214, 168), // Green       (live / +1, #8fd6a8)
        (140, 196, 224), // Cyan
        (224, 137, 127), // Red
        (207, 194, 245), // Magenta     (treasures, #cfc2f5)
        (230, 200, 143), // Yellow      (accent, #e6c88f)
        (242, 239, 232), // White       (figures, #f2efe8)
    };

    // ANSI palette slot of each ConsoleColor (Windows Terminal reads the ANSI slots).
    private static readonly int[] AnsiSlot = { 0, 4, 2, 6, 1, 5, 3, 7, 8, 12, 10, 14, 9, 13, 11, 15 };

    /// <summary>Recolors the console: color table for the classic console host, OSC 4 for Windows Terminal.</summary>
    private static void ApplyTheme()
    {
        try
        {
            var h = GetStdHandle(STD_OUTPUT_HANDLE);
            var info = new CONSOLE_SCREEN_BUFFER_INFO_EX { cbSize = (uint)Marshal.SizeOf<CONSOLE_SCREEN_BUFFER_INFO_EX>(), ColorTable = new uint[16] };
            if (GetConsoleScreenBufferInfoEx(h, ref info))
            {
                for (var i = 0; i < 16; i++)
                {
                    var c = Theme[i];
                    info.ColorTable[i] = (uint)(c.R | (c.G << 8) | (c.B << 16));
                }
                info.srWindow.Right++;  // SetConsoleScreenBufferInfoEx shrinks the window by one otherwise
                info.srWindow.Bottom++;
                SetConsoleScreenBufferInfoEx(h, ref info);
            }

            if (GetConsoleMode(h, out var mode) && SetConsoleMode(h, mode | ENABLE_VIRTUAL_TERMINAL_PROCESSING))
            {
                var sb = new System.Text.StringBuilder();
                for (var i = 0; i < 16; i++)
                {
                    var c = Theme[i];
                    sb.Append($"\u001b]4;{AnsiSlot[i]};rgb:{c.R:x2}/{c.G:x2}/{c.B:x2}\u001b\\");
                }
                sb.Append("\u001b]10;rgb:cf/cc/c5\u001b\\\u001b]11;rgb:0a/0a/0b\u001b\\"); // default text / background
                using var stdout = Console.OpenStandardOutput();
                var bytes = System.Text.Encoding.ASCII.GetBytes(sb.ToString());
                stdout.Write(bytes, 0, bytes.Length);
                stdout.Flush();
            }
        }
        catch
        {
            // cosmetic only
        }
    }

    /// <summary>Minimized without taking the focus (started with Fortnite: the game keeps the screen).</summary>
    public static void Minimize()
    {
        var hwnd = GetConsoleWindow();
        if (hwnd != IntPtr.Zero)
        {
            ShowWindow(hwnd, SW_SHOWMINNOACTIVE);
        }
    }

    private static bool IsRedirected(int std)
    {
        var h = GetStdHandle(std);
        if (h == IntPtr.Zero || h == new IntPtr(-1))
        {
            return false;
        }
        var type = GetFileType(h);
        return type is FILE_TYPE_DISK or FILE_TYPE_PIPE;
    }

    private const int STD_OUTPUT_HANDLE = -11;
    private const int ATTACH_PARENT_PROCESS = -1;
    private const uint FILE_TYPE_DISK = 1, FILE_TYPE_PIPE = 3;
    private const int SW_SHOWMINNOACTIVE = 7;

    private const uint ENABLE_VIRTUAL_TERMINAL_PROCESSING = 0x0004;

    [StructLayout(LayoutKind.Sequential)] private struct COORD { public short X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct SMALL_RECT { public short Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct CONSOLE_SCREEN_BUFFER_INFO_EX
    {
        public uint cbSize;
        public COORD dwSize;
        public COORD dwCursorPosition;
        public ushort wAttributes;
        public SMALL_RECT srWindow;
        public COORD dwMaximumWindowSize;
        public ushort wPopupAttributes;
        public int bFullscreenSupported;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)] public uint[] ColorTable;
    }

    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetConsoleScreenBufferInfoEx(IntPtr h, ref CONSOLE_SCREEN_BUFFER_INFO_EX info);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool SetConsoleScreenBufferInfoEx(IntPtr h, ref CONSOLE_SCREEN_BUFFER_INFO_EX info);
    [DllImport("kernel32.dll")] private static extern bool GetConsoleMode(IntPtr h, out uint mode);
    [DllImport("kernel32.dll")] private static extern bool SetConsoleMode(IntPtr h, uint mode);
    [DllImport("kernel32.dll")] private static extern IntPtr GetConsoleWindow();
    [DllImport("kernel32.dll")] private static extern bool AllocConsole();
    [DllImport("kernel32.dll")] private static extern bool AttachConsole(int processId);
    [DllImport("kernel32.dll")] private static extern IntPtr GetStdHandle(int std);
    [DllImport("kernel32.dll")] private static extern uint GetFileType(IntPtr handle);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hwnd, int cmd);
}
