using System;
using System.Runtime.InteropServices;
using System.Text;

internal static class Probe2
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr OpenFileMapping(uint access, bool inherit, string name);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr MapViewOfFile(IntPtr h, uint access, uint hi, uint lo, UIntPtr bytes);
    [DllImport("kernel32.dll")] private static extern bool UnmapViewOfFile(IntPtr p);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr h);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindow(string c, string n);
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);

    private static void Main()
    {
        Console.OutputEncoding = Encoding.UTF8;
        IntPtr map = OpenFileMapping(4, false, "AIMP2_RemoteInfo");
        IntPtr view = MapViewOfFile(map, 4, 0, 0, UIntPtr.Zero);
        var raw = new byte[2048];
        Marshal.Copy(view, raw, 0, raw.Length);

        Console.WriteLine("first nonzero byte at offset: " + Array.FindIndex(raw, b => b != 0));

        var sb = new StringBuilder();
        for (int off = 0; off < 600; off += 2)
        {
            char ch = (char)BitConverter.ToUInt16(raw, off);
            sb.Append(ch == '\0' ? '|' : ch);
        }
        Console.WriteLine("UTF16 from 0 (| = 0x0000):");
        Console.WriteLine(sb.ToString());

        Console.WriteLine("\nu32 table:");
        for (int i = 0; i < 88; i += 4) Console.WriteLine("  @{0,3} = {1,10} (0x{1:X8})", i, BitConverter.ToUInt32(raw, i));

        IntPtr remote = FindWindow("AIMP2_RemoteInfo", null);
        const uint WM_PROP = 0x400 + 0x77;
        Console.WriteLine("\nstate={0}({1}) pos={2} dur={3} vol={4}",
            SendMessage(remote, WM_PROP, (IntPtr)0x40, IntPtr.Zero),
            SendMessage(remote, WM_PROP, (IntPtr)0x41, IntPtr.Zero).ToInt64() == 0 ? "get" : "?",
            SendMessage(remote, WM_PROP, (IntPtr)0x20, IntPtr.Zero),
            SendMessage(remote, WM_PROP, (IntPtr)0x30, IntPtr.Zero),
            SendMessage(remote, WM_PROP, (IntPtr)0x50, IntPtr.Zero));

        UnmapViewOfFile(view);
        CloseHandle(map);
    }
}
