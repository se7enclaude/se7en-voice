using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;

namespace Se7enVoice.Audio;

/// <summary>
/// Renomme les deux entrées de VB-CABLE dans Windows en « Seven Microphone » pour qu'elles
/// soient claires dans Discord et les jeux (qui affichent « Seven Microphone (se7en Voice) »). Windows exige les droits administrateur pour ça :
/// l'app se relance une fois en mode admin avec l'argument <see cref="Argument"/>.
/// </summary>
public static class CableRenamer
{
    public const string Argument = "--rename-cable";
    public const string MicName = "Seven Microphone";
    public const string SpeakerName = "Seven Microphone - interne (ne pas utiliser)";
    /// <summary>Nom de l'« appareil » affiché entre parenthèses après l'entrée.</summary>
    public const string AdapterName = "se7en Voice";

    /// <summary>L'entrée porte déjà un nom donné par se7en Voice (actuel ou d'avant la 1.2).</summary>
    public static bool IsRenamed(string name) =>
        name.StartsWith(MicName, StringComparison.OrdinalIgnoreCase) || name.StartsWith("se7en", StringComparison.OrdinalIgnoreCase);

    /// <summary>Relance l'app en admin pour renommer. Retourne false si l'utilisateur refuse.</summary>
    public static bool RequestElevatedRename()
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo(Environment.ProcessPath!, Argument)
            {
                UseShellExecute = true,
                Verb = "runas",
            });
            p?.WaitForExit(15000);
            return p is { HasExited: true, ExitCode: 0 };
        }
        catch (Win32Exception)
        {
            return false; // L'utilisateur a cliqué « Non » sur la demande Windows.
        }
    }

    /// <summary>Appelé dans le processus admin. Retourne le code de sortie.</summary>
    public static int RunElevated()
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            int renamed = 0;
            foreach (var flow in new[] { DataFlow.Capture, DataFlow.Render })
                foreach (var d in enumerator.EnumerateAudioEndPoints(flow, DeviceState.Active))
                    if (DeviceService.IsCable(d))
                    {
                        SetName(d.ID, DeviceDesc, flow == DataFlow.Capture ? MicName : SpeakerName);
                        try { SetName(d.ID, InterfaceName, AdapterName); } catch (Exception) { /* nom d'appareil facultatif */ }
                        renamed++;
                    }
            return renamed > 0 ? 0 : 2;
        }
        catch (Exception)
        {
            return 1;
        }
    }

    // --- Interop COM minimal : IMMDevice::OpenPropertyStore en écriture ---

    private static readonly PropKey DeviceDesc = new(new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), 2);
    private static readonly PropKey InterfaceName = new(new Guid("026e516e-b814-414b-83cd-856d6fef4822"), 2);

    private static void SetName(string deviceId, PropKey key, string name)
    {
        var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorCom();
        Marshal.ThrowExceptionForHR(enumerator.GetDevice(deviceId, out var device));
        Marshal.ThrowExceptionForHR(device.OpenPropertyStore(2 /* STGM_READWRITE */, out var store));
        var value = new PropVariant { vt = 31 /* VT_LPWSTR */, pointer = Marshal.StringToCoTaskMemUni(name) };
        try
        {
            Marshal.ThrowExceptionForHR(store.SetValue(ref key, ref value));
            Marshal.ThrowExceptionForHR(store.Commit());
        }
        finally
        {
            Marshal.FreeCoTaskMem(value.pointer);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PropKey(Guid fmtid, int pid)
    {
        public Guid FormatId = fmtid;
        public int PropertyId = pid;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PropVariant
    {
        public ushort vt, r1, r2, r3;
        public IntPtr pointer, pointer2;
    }

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumeratorCom { }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
        int GetDefaultAudioEndpoint(int dataFlow, int role, out IntPtr endpoint);
        int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, out IntPtr ppInterface);
        int OpenPropertyStore(int stgmAccess, out IPropertyStore properties);
    }

    [ComImport, Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        int GetCount(out int count);
        int GetAt(int index, out PropKey key);
        int GetValue(ref PropKey key, out PropVariant value);
        int SetValue(ref PropKey key, ref PropVariant value);
        int Commit();
    }
}
