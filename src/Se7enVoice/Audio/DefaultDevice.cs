using System.Runtime.InteropServices;

namespace Se7enVoice.Audio;

/// <summary>
/// Change le périphérique de sortie par défaut de Windows (l'installateur de VB-CABLE le met souvent sur
/// le câble : plus aucun son dans le casque). Passe par IPolicyConfig, l'interface du panneau Son de Windows,
/// sans droits administrateur.
/// </summary>
public static class DefaultDevice
{
    /// <summary>Met <paramref name="deviceId"/> par défaut pour les trois rôles (console, multimédia, communications).</summary>
    public static bool SetDefaultOutput(string deviceId)
    {
        try
        {
            var config = (IPolicyConfig)Activator.CreateInstance(Type.GetTypeFromCLSID(PolicyConfigClsid)!)!;
            for (int role = 0; role < 3; role++)
                Marshal.ThrowExceptionForHR(config.SetDefaultEndpoint(deviceId, role));
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static readonly Guid PolicyConfigClsid = new("870af99c-171d-4f9e-af0d-e63df40c2bc9");

    [ComImport, Guid("f8679f50-850a-41cf-9c72-430f290290c8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPolicyConfig
    {
        int GetMixFormat(IntPtr a, IntPtr b);
        int GetDeviceFormat(IntPtr a, int b, IntPtr c);
        int ResetDeviceFormat(IntPtr a);
        int SetDeviceFormat(IntPtr a, IntPtr b, IntPtr c);
        int GetProcessingPeriod(IntPtr a, int b, IntPtr c, IntPtr d);
        int SetProcessingPeriod(IntPtr a, IntPtr b);
        int GetShareMode(IntPtr a, IntPtr b);
        int SetShareMode(IntPtr a, IntPtr b);
        int GetPropertyValue(IntPtr a, IntPtr b, IntPtr c);
        int SetPropertyValue(IntPtr a, IntPtr b, IntPtr c);
        int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string deviceId, int role);
        int SetEndpointVisibility(IntPtr a, int b);
    }
}
