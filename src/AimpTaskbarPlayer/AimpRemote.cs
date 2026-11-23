using System;
using System.Runtime.InteropServices;
using System.Text;

namespace AimpTaskbarPlayer;

/// <summary>
/// Client for AIMP's built-in remote API: a shared memory block named "AIMP2_RemoteInfo"
/// (track metadata) plus a hidden window of the same class name (playback state, position, commands).
/// This is the officially supported way for an external process to talk to AIMP, no plugin install needed.
/// </summary>
public sealed class AimpRemote : IDisposable
{
    private const string MapName = "AIMP2_RemoteInfo";
    private const string WindowClass = "AIMP2_RemoteInfo";
    private const int BufferSize = 2048;
    private const uint FileMapRead = 4;
    private const uint MessageTimeoutMs = 250;

    private readonly object _gate = new();
    private readonly byte[] _buffer = new byte[BufferSize];
    private IntPtr _hwnd;
    private IntPtr _map;
    private IntPtr _view;

    private TrackInfo? _cachedTrack;
    private uint _cachedHash;
    private bool _hasCachedTrack;

    public bool IsConnected
    {
        get
        {
            lock (_gate)
            {
                return _view != IntPtr.Zero && _hwnd != IntPtr.Zero && Native.IsWindow(_hwnd);
            }
        }
    }

    public IntPtr WindowHandle
    {
        get { lock (_gate) return _hwnd; }
    }

    public bool TryConnect()
    {
        lock (_gate)
        {
            DisconnectCore();

            var hwnd = Native.FindWindow(WindowClass, null);
            if (hwnd == IntPtr.Zero) return false;

            var map = Native.OpenFileMapping(FileMapRead, false, MapName);
            if (map == IntPtr.Zero) return false;

            var view = Native.MapViewOfFile(map, FileMapRead, 0, 0, UIntPtr.Zero);
            if (view == IntPtr.Zero)
            {
                Native.CloseHandle(map);
                return false;
            }

            _hwnd = hwnd;
            _map = map;
            _view = view;
            return true;
        }
    }

    public void Disconnect()
    {
        lock (_gate) DisconnectCore();
    }

    private void DisconnectCore()
    {
        if (_view != IntPtr.Zero)
        {
            Native.UnmapViewOfFile(_view);
            _view = IntPtr.Zero;
        }
        if (_map != IntPtr.Zero)
        {
            Native.CloseHandle(_map);
            _map = IntPtr.Zero;
        }
        _hwnd = IntPtr.Zero;

        _cachedTrack = null;
        _hasCachedTrack = false;
        _cachedHash = 0;
    }

    /// <summary>Reads the metadata block. Returns null when AIMP holds no track information.</summary>
    public TrackInfo? ReadTrack()
    {
        lock (_gate)
        {
            if (_view == IntPtr.Zero) return null;

            try
            {
                Marshal.Copy(_view, _buffer, 0, BufferSize);
            }
            catch
            {
                return null;
            }

            int stringsOffset = BitConverter.ToInt32(_buffer, 0);
            // AIMP >= 5.0 stores the header size (88) at offset 0. Guard against garbage.
            if (stringsOffset < 88 || stringsOffset > 1024) return null;

            int albumLen = BitConverter.ToInt32(_buffer, 40);
            int artistLen = BitConverter.ToInt32(_buffer, 44);
            int dateLen = BitConverter.ToInt32(_buffer, 48);
            int fileLen = BitConverter.ToInt32(_buffer, 52);
            int genreLen = BitConverter.ToInt32(_buffer, 56);
            int titleLen = BitConverter.ToInt32(_buffer, 60);
            if (albumLen < 0 || artistLen < 0 || dateLen < 0 || fileLen < 0 ||
                genreLen < 0 || titleLen < 0) return null;

            int totalChars = albumLen + artistLen + dateLen + fileLen + genreLen + titleLen;
            if (totalChars <= 0 || totalChars > (BufferSize - stringsOffset) / 2) return null;

            // The panel polls four times per second. Re-parsing (and re-allocating) the same
            // strings every time creates needless GC churn, so remember the last block.
            int totalBytes = stringsOffset + totalChars * 2;
            uint hash = HashBlock(totalBytes);
            if (_hasCachedTrack && hash == _cachedHash) return _cachedTrack;

            int pos = stringsOffset;
            string album = ReadString(albumLen, ref pos);
            string artist = ReadString(artistLen, ref pos);
            string year = ReadString(dateLen, ref pos);
            string file = ReadString(fileLen, ref pos);
            string genre = ReadString(genreLen, ref pos);
            string title = ReadString(titleLen, ref pos);

            var track = new TrackInfo(
                title, artist, album, year, genre, file,
                BitConverter.ToInt32(_buffer, 16),
                BitConverter.ToInt32(_buffer, 8),
                BitConverter.ToInt32(_buffer, 12),
                BitConverter.ToInt32(_buffer, 32),
                BitConverter.ToInt32(_buffer, 36));

            _cachedTrack = track;
            _cachedHash = hash;
            _hasCachedTrack = true;
            return track;
        }
    }

    private string ReadString(int chars, ref int offset)
    {
        if (chars <= 0) return string.Empty;
        var s = Encoding.Unicode.GetString(_buffer, offset, chars * 2).TrimEnd('\0').Trim();
        offset += chars * 2;
        return s;
    }

    private uint HashBlock(int length)
    {
        // FNV-1a over the populated part of the shared block. 2 KB at 4 Hz is nothing,
        // and it lets us skip all string allocations while the track does not change.
        uint hash = 2166136261;
        var span = _buffer.AsSpan(0, length);
        foreach (var value in span)
        {
            hash ^= value;
            hash *= 16777619;
        }
        return hash;
    }

    public PlaybackState GetState()
    {
        int value = GetProperty(Native.RA_PROPERTY_PLAYER_STATE);
        return value switch
        {
            2 => PlaybackState.Playing,
            1 => PlaybackState.Paused,
            _ => PlaybackState.Stopped,
        };
    }

    public int GetPositionMs() => Math.Max(0, GetProperty(Native.RA_PROPERTY_PLAYER_POSITION));

    public int GetDurationMs() => Math.Max(0, GetProperty(Native.RA_PROPERTY_PLAYER_DURATION));

    public int GetVolume() => GetProperty(Native.RA_PROPERTY_VOLUME);

    public void SetVolume(int volume) => SetProperty(Native.RA_PROPERTY_VOLUME, Math.Clamp(volume, 0, 100));

    public void SetPositionMs(int positionMs) => SetProperty(Native.RA_PROPERTY_PLAYER_POSITION, Math.Max(0, positionMs));

    public void PlayPause() => SendCommand(Native.CMD_PLAYPAUSE);

    public void Play() => SendCommand(Native.CMD_PLAY);

    public void Pause() => SendCommand(Native.CMD_PAUSE);

    public void Stop() => SendCommand(Native.CMD_STOP);

    public void Next() => SendCommand(Native.CMD_NEXT);

    public void Previous() => SendCommand(Native.CMD_PREV);

    public int GetVersionRaw() => GetProperty(Native.RA_PROPERTY_VERSION);

    public void SendCommand(int command) => Send(Native.WM_AIMP_COMMAND, command, 0);

    private int GetProperty(int property) => (int)Send(Native.WM_AIMP_PROPERTY, property | Native.PROP_GET, 0);

    private void SetProperty(int property, int value) => Send(Native.WM_AIMP_PROPERTY, property | Native.PROP_SET, value);

    private IntPtr Send(uint message, int wParam, int lParam)
    {
        IntPtr hwnd;
        lock (_gate) hwnd = _hwnd;
        if (hwnd == IntPtr.Zero) return IntPtr.Zero;

        // Never block the UI thread for long if AIMP is stuck.
        Native.SendMessageTimeout(hwnd, message, new IntPtr(wParam), new IntPtr(lParam),
            Native.SMTO_ABORTIFHUNG | Native.SMTO_BLOCK, MessageTimeoutMs, out var result);
        return result;
    }

    public void Dispose() => Disconnect();
}
