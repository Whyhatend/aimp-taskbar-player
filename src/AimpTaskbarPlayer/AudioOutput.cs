using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace AimpTaskbarPlayer;

public sealed record AudioDevice(string Id, string Name, bool IsDefault);

/// <summary>
/// Reads the Windows render endpoints and switches the system default device.
/// AIMP is configured to "Windows Default" out of the box, so it follows this
/// switch immediately - the same list Windows shows in Settings -> Sound.
/// </summary>
public static class AudioOutput
{
    private const int ERender = 0;
    private const int EConsole = 0;
    private const int EMultimedia = 1;
    private const int ECommunications = 2;
    private const int DeviceStateActive = 1;

    public static IReadOnlyList<AudioDevice> GetRenderDevices()
    {
        var result = new List<AudioDevice>();
        IMMDeviceCollection? collection = null;

        try
        {
            var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
            try
            {
                var defaultId = TryGetDefaultId(enumerator);

                if (enumerator.EnumAudioEndpoints(ERender, DeviceStateActive, out collection) != 0 ||
                    collection == null)
                    return result;

                collection.GetCount(out var count);
                for (var i = 0; i < count; i++)
                {
                    IMMDevice? device = null;
                    try
                    {
                        if (collection.Item(i, out device) != 0 || device == null) continue;
                        if (device.GetId(out var id) != 0 || string.IsNullOrEmpty(id)) continue;

                        var name = ReadFriendlyName(device);
                        result.Add(new AudioDevice(id, name, id == defaultId));
                    }
                    catch
                    {
                        // skip a device that disappeared while we were enumerating
                    }
                    finally
                    {
                        Release(device);
                    }
                }
            }
            finally
            {
                Release(enumerator);
            }
        }
        catch
        {
            // audio service unavailable - the caller shows an empty list
        }
        finally
        {
            Release(collection);
        }

        return result;
    }

    public static string? GetDefaultDeviceId()
    {
        try
        {
            var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
            try { return TryGetDefaultId(enumerator); }
            finally { Release(enumerator); }
        }
        catch
        {
            return null;
        }
    }

    public static bool SetDefaultDevice(string deviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceId)) return false;

        try
        {
            var policy = (IPolicyConfig)new CPolicyConfigClient();
            try
            {
                var console = policy.SetDefaultEndpoint(deviceId, EConsole) == 0;
                policy.SetDefaultEndpoint(deviceId, EMultimedia);
                policy.SetDefaultEndpoint(deviceId, ECommunications);
                return console;
            }
            finally
            {
                Release(policy);
            }
        }
        catch
        {
            return false;
        }
    }

    private static string? TryGetDefaultId(IMMDeviceEnumerator enumerator)
    {
        IMMDevice? device = null;
        try
        {
            if (enumerator.GetDefaultAudioEndpoint(ERender, EConsole, out device) != 0 || device == null)
                return null;
            return device.GetId(out var id) == 0 ? id : null;
        }
        catch
        {
            return null;
        }
        finally
        {
            Release(device);
        }
    }

    private static string ReadFriendlyName(IMMDevice device)
    {
        IPropertyStore? store = null;
        try
        {
            if (device.OpenPropertyStore(0, out store) != 0 || store == null) return "Устройство вывода";

            var key = PKeyDeviceFriendlyName;
            if (store.GetValue(ref key, out var value) != 0) return "Устройство вывода";

            try
            {
                var name = Marshal.PtrToStringUni(value.pointerValue);
                return string.IsNullOrWhiteSpace(name) ? "Устройство вывода" : name!;
            }
            finally
            {
                PropVariantClear(ref value);
            }
        }
        catch
        {
            return "Устройство вывода";
        }
        finally
        {
            Release(store);
        }
    }

    private static void Release(object? instance)
    {
        if (instance != null && Marshal.IsComObject(instance))
        {
            try { Marshal.ReleaseComObject(instance); } catch { }
        }
    }

    // ------------------------------------------------------------ Core Audio interop

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumeratorComObject { }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        int EnumAudioEndpoints(int dataFlow, int stateMask, out IMMDeviceCollection? devices);
        int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice? device);
        int GetDevice(string id, out IMMDevice? device);
        int RegisterEndpointNotificationCallback(IntPtr client);
        int UnregisterEndpointNotificationCallback(IntPtr client);
    }

    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceCollection
    {
        int GetCount(out int count);
        int Item(int index, out IMMDevice? device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object? instance);
        int OpenPropertyStore(int access, out IPropertyStore? store);
        int GetId([MarshalAs(UnmanagedType.LPWStr)] out string? id);
        int GetState(out int state);
    }

    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        int GetCount(out int count);
        int GetAt(int index, out PROPERTYKEY key);
        int GetValue(ref PROPERTYKEY key, out PROPVARIANT value);
        int SetValue(ref PROPERTYKEY key, ref PROPVARIANT value);
        int Commit();
    }

    [ComImport, Guid("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9")]
    private class CPolicyConfigClient { }

    [ComImport, Guid("F8679F50-850A-41CF-9C72-430F290290C8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPolicyConfig
    {
        int GetMixFormat(string deviceId, out IntPtr format);
        int GetDeviceFormat(string deviceId, int isDefault, out IntPtr format);
        int ResetDeviceFormat(string deviceId);
        int SetDeviceFormat(string deviceId, IntPtr endpointFormat, IntPtr mixFormat);
        int GetProcessingPeriod(string deviceId, int isDefault, out long defaultPeriod, out long minimumPeriod);
        int SetProcessingPeriod(string deviceId, ref long period);
        int GetShareMode(string deviceId, out IntPtr mode);
        int SetShareMode(string deviceId, IntPtr mode);
        int GetPropertyValue(string deviceId, ref PROPERTYKEY key, out PROPVARIANT value);
        int SetPropertyValue(string deviceId, ref PROPERTYKEY key, ref PROPVARIANT value);
        int SetDefaultEndpoint(string deviceId, int role);
        int SetEndpointVisibility(string deviceId, int visible);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROPERTYKEY
    {
        public Guid fmtid;
        public int pid;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct PROPVARIANT
    {
        [FieldOffset(0)] public short vt;
        [FieldOffset(8)] public IntPtr pointerValue;
    }

    private static readonly PROPERTYKEY PKeyDeviceFriendlyName = new()
    {
        fmtid = new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"),
        pid = 14,
    };

    [DllImport("ole32.dll")]
    private static extern int PropVariantClear(ref PROPVARIANT value);
}
