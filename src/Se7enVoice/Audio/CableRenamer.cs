using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;

namespace Se7enVoice.Audio;

/// <summary>
/// Renomme les entrées de VB-CABLE dans Windows pour qu'on sache quoi choisir dans Discord et les jeux :
/// le côté enregistrement devient « Seven Microphone - Entrée » (le micro à choisir), le côté lecture
/// « Seven Microphone - Sortie (réservée à se7en Voice) » (celui où se7en Voice envoie votre voix).
/// Windows l'accepte sans droits administrateur ; l'app ne se relance en admin (argument <see cref="Argument"/>)
/// que si l'écriture est refusée.
/// </summary>
public static class CableRenamer
{
    public const string Argument = "--rename-cable";
    public const string BaseName = "Seven Microphone";
    public const string MicName = BaseName + " - Entrée";
    public const string SpeakerName = BaseName + " - Sortie (réservée à se7en Voice)";
    public const string Speaker16Name = BaseName + " - Sortie 16 canaux (réservée à se7en Voice)";
    /// <summary>Nom de l'« appareil » affiché entre parenthèses après l'entrée.</summary>
    public const string AdapterName = "se7en Voice";

    /// <summary>Le micro porte le nom actuel (les noms d'avant la 1.3.2 sont refaits).</summary>
    public static bool IsRenamed(string name) => name.StartsWith(MicName, StringComparison.OrdinalIgnoreCase);

    /// <summary>Dernière erreur de renommage (pour le message à l'utilisateur et le diagnostic).</summary>
    public static string LastError { get; private set; } = "";

    /// <summary>Renomme tout ce qui n'a pas encore le bon nom. Retourne le nombre d'entrées renommées, -1 si Windows refuse.</summary>
    public static int Rename()
    {
        LastError = "";
        int renamed = 0;
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            foreach (var flow in new[] { DataFlow.Capture, DataFlow.Render })
                foreach (var d in enumerator.EnumerateAudioEndPoints(flow, DeviceState.Active))
                {
                    if (!DeviceService.IsCable(d)) continue;
                    string current = Safe(() => d.FriendlyName);
                    string target = flow == DataFlow.Capture ? MicName
                        : current.Contains("16ch", StringComparison.OrdinalIgnoreCase) || current.Contains("16 canaux", StringComparison.OrdinalIgnoreCase) ? Speaker16Name
                        : SpeakerName;
                    if (current.StartsWith(target, StringComparison.OrdinalIgnoreCase)) continue;
                    SetName(d.ID, DeviceDesc, target);
                    try { SetName(d.ID, InterfaceName, AdapterName); } catch (Exception) { /* nom d'appareil facultatif */ }
                    renamed++;
                }
            return renamed;
        }
        catch (UnauthorizedAccessException ex)
        {
            LastError = ex.ToString();
            return -1;
        }
        catch (Exception ex)
        {
            LastError = ex.ToString();
            return renamed > 0 ? renamed : -1;
        }
    }

    /// <summary>Renomme dans ce processus, et ne demande les droits admin que si Windows refuse. True si tout porte le bon nom.</summary>
    public static bool RenameAll() => Rename() >= 0 || RequestElevatedRename();

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

    /// <summary>Appelé avec <see cref="Argument"/> (processus admin ou installateur). Retourne le code de sortie.</summary>
    public static int RunElevated()
    {
        int n = Rename();
        if (n < 0) try { File.WriteAllText(Path.Combine(Path.GetTempPath(), "se7en-voice-rename.log"), LastError); } catch (IOException) { }
        return n >= 0 ? 0 : 1;
    }

    private static string Safe(Func<string> get)
    {
        try { return get() ?? ""; } catch (Exception) { return ""; }
    }

    // --- Interop COM minimal : IMMDevice::OpenPropertyStore en écriture ---

    private static readonly PropKey DeviceDesc = new(new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), 2);
    private static readonly PropKey InterfaceName = new(new Guid("026e516e-b814-414b-83cd-856d6fef4822"), 2);

    private static void SetName(string deviceId, PropKey key, string name)
    {
        // Par le CLSID et non « new » : NAudio a déjà enveloppé ce même objet COM sous son propre type,
        // et le cast vers une classe COM locale échoue (InvalidCastException).
        var enumerator = (IMMDeviceEnumerator)Activator.CreateInstance(Type.GetTypeFromCLSID(EnumeratorClsid)!)!;
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

    private static readonly Guid EnumeratorClsid = new("BCDE0395-E52F-467C-8E3D-C4579291692E");

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
