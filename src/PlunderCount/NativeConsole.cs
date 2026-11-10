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

    // Plunder Count palette: near-black glass with muted, non-neon accents. Indexed by ConsoleColor.
    private static readonly (byte R, byte G, byte B)[] Theme =
    {
        (11, 12, 15),    // Black       (window background)
        (26, 29, 36),    // DarkBlue
        (78, 122, 94),   // DarkGreen
        (79, 99, 114),   // DarkCyan
        (138, 74, 68),   // DarkRed
        (107, 90, 134),  // DarkMagenta
        (156, 131, 85),  // DarkYellow
        (180, 184, 192), // Gray        (normal text)
        (91, 96, 105),   // DarkGray    (secondary text)
        (125, 147, 173), // Blue
        (140, 197, 160), // Green       (live / success)
        (157, 178, 198), // Cyan
        (217, 119, 111), // Red
        (182, 162, 217), // Magenta     (treasures)
        (214, 183, 124), // Yellow      (accent)
        (241, 242, 244), // White       (figures)
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
                sb.Append("\u001b]10;rgb:b4/b8/c0\u001b\\\u001b]11;rgb:0b/0c/0f\u001b\\"); // default text / background
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
