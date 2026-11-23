using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

internal static class Probe
{
    private delegate bool EnumProc(IntPtr h, IntPtr l);

    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc cb, IntPtr l);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindow(string c, string n);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr OpenFileMapping(uint access, bool inherit, string name);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr MapViewOfFile(IntPtr h, uint access, uint hi, uint lo, UIntPtr bytes);
    [DllImport("kernel32.dll")] private static extern bool UnmapViewOfFile(IntPtr p);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr h);

    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);

    private static void Main()
    {
        Console.OutputEncoding = Encoding.UTF8;
        Console.WriteLine("=== AIMP processes ===");
        foreach (var p in Process.GetProcesses())
        {
            if (p.ProcessName.IndexOf("AIMP", StringComparison.OrdinalIgnoreCase) >= 0)
                Console.WriteLine("pid={0} name={1} title={2}", p.Id, p.ProcessName, p.MainWindowTitle);
        }

        uint aimpPid = 0;
        foreach (var p in Process.GetProcessesByName("AIMP")) aimpPid = (uint)p.Id;

        Console.WriteLine("\n=== top-level windows of AIMP pid {0} ===", aimpPid);
        EnumWindows((h, l) =>
        {
            uint pid; GetWindowThreadProcessId(h, out pid);
            if (pid != aimpPid) return true;
            var c = new StringBuilder(256); GetClassName(h, c, 256);
            var t = new StringBuilder(256); GetWindowText(h, t, 256);
            Console.WriteLine("  hwnd={0} class='{1}' title='{2}' visible={3}", h.ToInt64(), c, t, IsWindowVisible(h));
            return true;
        }, IntPtr.Zero);

        Console.WriteLine("\nFindWindow('AIMP2_RemoteInfo') = {0}", FindWindow("AIMP2_RemoteInfo", null).ToInt64());
        Console.WriteLine("FindWindow(null,'AIMP')          = {0}", FindWindow(null, "AIMP").ToInt64());

        Console.WriteLine("\n=== shared memory AIMP2_RemoteInfo ===");
        IntPtr map = OpenFileMapping(4 /*FILE_MAP_READ*/, false, "AIMP2_RemoteInfo");
        Console.WriteLine("OpenFileMapping={0} err={1}", map.ToInt64(), Marshal.GetLastWin32Error());
        if (map == IntPtr.Zero) return;

        IntPtr view = MapViewOfFile(map, 4, 0, 0, UIntPtr.Zero);
        Console.WriteLine("MapViewOfFile(whole)={0} err={1}", view.ToInt64(), Marshal.GetLastWin32Error());
        if (view == IntPtr.Zero) { CloseHandle(map); return; }

        var raw = new byte[2048];
        Marshal.Copy(view, raw, 0, raw.Length);

        for (int i = 0; i < 96; i += 4)
            Console.Write("u32@{0}={1} ", i, BitConverter.ToUInt32(raw, i));
        Console.WriteLine();

        // Layout with natural alignment (matches Delphi record / python native struct)
        int duration = (int)BitConverter.ToUInt32(raw, 16);
        int albumLen = (int)BitConverter.ToUInt32(raw, 60);
        int artistLen = (int)BitConverter.ToUInt32(raw, 64);
        int dateLen = (int)BitConverter.ToUInt32(raw, 68);
        int fileLen = (int)BitConverter.ToUInt32(raw, 72);
        int genreLen = (int)BitConverter.ToUInt32(raw, 76);
        int titleLen = (int)BitConverter.ToUInt32(raw, 80);
        Console.WriteLine("active(byte4)={0} bitrate={1} channels={2} duration={3}",
            raw[4], BitConverter.ToUInt32(raw, 8), BitConverter.ToUInt32(raw, 12), duration);
        Console.WriteLine("lens album={0} artist={1} date={2} file={3} genre={4} title={5}",
            albumLen, artistLen, dateLen, fileLen, genreLen, titleLen);

        const int stringsStart = 84;
        if (albumLen + artistLen + dateLen + fileLen + genreLen + titleLen > 0 &&
            stringsStart + (albumLen + artistLen + dateLen + fileLen + genreLen + titleLen) * 2 <= raw.Length)
        {
            string all = Encoding.Unicode.GetString(raw, stringsStart,
                (albumLen + artistLen + dateLen + fileLen + genreLen + titleLen) * 2);
            int pos = 0;
            Console.WriteLine("album  = '{0}'", all.Substring(pos, albumLen)); pos += albumLen;
            Console.WriteLine("artist = '{0}'", all.Substring(pos, artistLen)); pos += artistLen;
            Console.WriteLine("date   = '{0}'", all.Substring(pos, dateLen)); pos += dateLen;
            Console.WriteLine("file   = '{0}'", all.Substring(pos, fileLen)); pos += fileLen;
            Console.WriteLine("genre  = '{0}'", all.Substring(pos, genreLen)); pos += genreLen;
            Console.WriteLine("title  = '{0}'", all.Substring(pos, titleLen));
        }
        else
        {
            Console.WriteLine("raw utf16 head: '{0}'", Encoding.Unicode.GetString(raw, 0, 200));
        }

        IntPtr remote = FindWindow("AIMP2_RemoteInfo", null);
        if (remote != IntPtr.Zero)
        {
            const uint WM_AIMP_PROPERTY = 0x400 + 0x77;
            const uint WM_AIMP_COMMAND = 0x400 + 0x75;
            Console.WriteLine("state(0x40)  = {0}", SendMessage(remote, WM_AIMP_PROPERTY, (IntPtr)0x40, IntPtr.Zero).ToInt64());
            Console.WriteLine("position(0x20)={0}", SendMessage(remote, WM_AIMP_PROPERTY, (IntPtr)0x20, IntPtr.Zero).ToInt64());
            Console.WriteLine("duration(0x30)={0}", SendMessage(remote, WM_AIMP_PROPERTY, (IntPtr)0x30, IntPtr.Zero).ToInt64());
            Console.WriteLine("version(0x10) = {0}", SendMessage(remote, WM_AIMP_PROPERTY, (IntPtr)0x10, IntPtr.Zero).ToInt64());
        }

        UnmapViewOfFile(view);
        CloseHandle(map);
    }
}
