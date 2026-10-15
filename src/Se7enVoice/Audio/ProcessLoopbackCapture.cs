using System.Runtime.InteropServices;
using Se7enVoice.Core.Audio;

namespace Se7enVoice.Audio;

/// <summary>
/// Capture le son d'UNE appli (et de ses sous-processus) avec l'API « process loopback »
/// de Windows 10 2004+ / Windows 11, sans pilote. Sert à envoyer sa musique ou un jeu dans
/// le micro virtuel. Le son arrive en mono dans <see cref="Ring"/>, au rythme du moteur audio.
/// </summary>
public sealed class ProcessLoopbackCapture : IDisposable
{
    private const int CaptureRate = 48000, Channels = 2;
    private readonly Thread _thread;
    private readonly ManualResetEventSlim _started = new();
    private volatile bool _stop;
    private Exception? _error;

    public ProcessLoopbackCapture(int processId, int engineRate)
    {
        ProcessId = processId;
        Ring = new ResamplingRing(CaptureRate, engineRate, targetLatencyMs: 40);
        _thread = new Thread(Run) { IsBackground = true, Name = "app-loopback-" + processId };
        _thread.SetApartmentState(ApartmentState.MTA);
        _thread.Start();
        _started.Wait(TimeSpan.FromSeconds(5));
        if (_error != null) throw _error;
    }

    public int ProcessId { get; }
    public ResamplingRing Ring { get; }
    /// <summary>Crête (0..1) depuis le dernier appel, pour le vu-mètre.</summary>
    public float TakePeak() => Interlocked.Exchange(ref _peak, 0);
    private float _peak;

    public static bool IsSupported => Environment.OSVersion.Version.Build >= 20348;

    private void Run()
    {
        IAudioClient? client = null;
        var ready = new AutoResetEvent(false);
        try
        {
            client = Activate(ProcessId);
            var fmt = new WaveFormatEx
            {
                wFormatTag = 1, nChannels = Channels, nSamplesPerSec = CaptureRate, wBitsPerSample = 16,
                nBlockAlign = Channels * 2, nAvgBytesPerSec = CaptureRate * Channels * 2,
            };
            const uint loopback = 0x00020000, eventCallback = 0x00040000, autoConvert = 0x80000000, srcDefault = 0x08000000;
            Check(client.Initialize(0, loopback | eventCallback | autoConvert | srcDefault, 200_000, 0, ref fmt, IntPtr.Zero));
            Check(client.SetEventHandle(ready.SafeWaitHandle.DangerousGetHandle()));
            var iid = typeof(IAudioCaptureClient).GUID;
            Check(client.GetService(ref iid, out var svc));
            var capture = (IAudioCaptureClient)svc;
            Check(client.Start());
            _started.Set();

            var mono = new float[4096];
            while (!_stop)
            {
                if (!ready.WaitOne(200)) continue;
                while (!_stop && capture.GetNextPacketSize(out uint packet) == 0 && packet > 0)
                {
                    Check(capture.GetBuffer(out IntPtr data, out uint frames, out uint flags, out _, out _));
                    int n = (int)frames;
                    if (mono.Length < n) mono = new float[n * 2];
                    bool silent = (flags & 0x2) != 0;
                    float peak = 0;
                    for (int f = 0; f < n; f++)
                    {
                        float v = 0;
                        if (!silent)
                        {
                            short l = Marshal.ReadInt16(data, f * 4), r = Marshal.ReadInt16(data, f * 4 + 2);
                            v = (l + r) / 65536f;
                        }
                        mono[f] = v;
                        float a = Math.Abs(v);
                        if (a > peak) peak = a;
                    }
                    capture.ReleaseBuffer(frames);
                    Ring.Write(mono.AsSpan(0, n));
                    if (peak > _peak) _peak = peak;
                }
            }
            client.Stop();
        }
        catch (Exception ex)
        {
            _error = ex;
            _started.Set();
        }
        finally
        {
            if (client != null) Marshal.ReleaseComObject(client);
            ready.Dispose();
        }
    }

    private static IAudioClient Activate(int pid)
    {
        // AUDIOCLIENT_ACTIVATION_PARAMS { type = PROCESS_LOOPBACK, { pid, INCLUDE_TARGET_PROCESS_TREE } }
        IntPtr parms = Marshal.AllocHGlobal(12);
        IntPtr variant = Marshal.AllocHGlobal(24);
        try
        {
            Marshal.WriteInt32(parms, 0, 1);
            Marshal.WriteInt32(parms, 4, pid);
            Marshal.WriteInt32(parms, 8, 0);
            // PROPVARIANT VT_BLOB
            for (int i = 0; i < 24; i += 4) Marshal.WriteInt32(variant, i, 0);
            Marshal.WriteInt16(variant, 0, 65);
            Marshal.WriteInt32(variant, 8, 12);
            Marshal.WriteIntPtr(variant, 16, parms);

            var handler = new CompletionHandler();
            var iid = typeof(IAudioClient).GUID;
            Check(ActivateAudioInterfaceAsync("VAD\\Process_Loopback", ref iid, variant, handler, out _));
            if (!handler.Done.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("Windows n'a pas répondu à la capture de l'appli.");
            Check(handler.Result);
            return (IAudioClient)handler.Interface!;
        }
        finally
        {
            Marshal.FreeHGlobal(variant);
            Marshal.FreeHGlobal(parms);
        }
    }

    private static void Check(int hr)
    {
        if (hr < 0) Marshal.ThrowExceptionForHR(hr);
    }

    public void Dispose()
    {
        _stop = true;
        _thread.Join(1000);
        _started.Dispose();
    }

    // ---------- Interop ----------

    [DllImport("Mmdevapi.dll", ExactSpelling = true)]
    private static extern int ActivateAudioInterfaceAsync([MarshalAs(UnmanagedType.LPWStr)] string deviceInterfacePath, ref Guid riid,
        IntPtr activationParams, IActivateAudioInterfaceCompletionHandler completionHandler, out IActivateAudioInterfaceAsyncOperation operation);

    [StructLayout(LayoutKind.Sequential, Pack = 2)]
    private struct WaveFormatEx
    {
        public short wFormatTag;
        public short nChannels;
        public int nSamplesPerSec;
        public int nAvgBytesPerSec;
        public short nBlockAlign;
        public short wBitsPerSample;
        public short cbSize;
    }

    [ComImport, Guid("72A22D78-CDE4-431D-B8CC-843A71199B6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IActivateAudioInterfaceAsyncOperation
    {
        [PreserveSig] int GetActivateResult(out int activateResult, [MarshalAs(UnmanagedType.IUnknown)] out object activatedInterface);
    }

    [ComImport, Guid("41D949AB-9862-444A-80F6-C261334DA5EB"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IActivateAudioInterfaceCompletionHandler
    {
        [PreserveSig] int ActivateCompleted(IActivateAudioInterfaceAsyncOperation operation);
    }

    [ComImport, Guid("94ea2b94-e9cc-49e0-c0ff-ee64ca8f5b90"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAgileObject { }

    [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
    private sealed class CompletionHandler : IActivateAudioInterfaceCompletionHandler, IAgileObject
    {
        public readonly ManualResetEventSlim Done = new();
        public int Result;
        public object? Interface;

        public int ActivateCompleted(IActivateAudioInterfaceAsyncOperation operation)
        {
            int hr = operation.GetActivateResult(out Result, out Interface);
            if (hr < 0) Result = hr;
            Done.Set();
            return 0;
        }
    }

    [ComImport, Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioClient
    {
        [PreserveSig] int Initialize(int shareMode, uint streamFlags, long bufferDuration, long periodicity, ref WaveFormatEx format, IntPtr sessionGuid);
        [PreserveSig] int GetBufferSize(out uint frames);
        [PreserveSig] int GetStreamLatency(out long latency);
        [PreserveSig] int GetCurrentPadding(out uint frames);
        [PreserveSig] int IsFormatSupported(int shareMode, IntPtr format, out IntPtr closest);
        [PreserveSig] int GetMixFormat(out IntPtr format);
        [PreserveSig] int GetDevicePeriod(out long defaultPeriod, out long minimumPeriod);
        [PreserveSig] int Start();
        [PreserveSig] int Stop();
        [PreserveSig] int Reset();
        [PreserveSig] int SetEventHandle(IntPtr handle);
        [PreserveSig] int GetService(ref Guid iid, [MarshalAs(UnmanagedType.IUnknown)] out object service);
    }

    [ComImport, Guid("C8ADBD64-E71E-48a0-A4DE-185C395CD317"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioCaptureClient
    {
        [PreserveSig] int GetBuffer(out IntPtr data, out uint frames, out uint flags, out ulong devicePosition, out ulong qpcPosition);
        [PreserveSig] int ReleaseBuffer(uint frames);
        [PreserveSig] int GetNextPacketSize(out uint frames);
    }
}
