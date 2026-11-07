using System.Windows.Threading;
using Se7enVoice.Audio;
using Se7enVoice.Core.Dsp;

namespace Se7enVoice.ViewModels;

/// <summary>
/// Assistant micro : 3 s de silence, 7 s de lecture d'une phrase, puis il règle tout seul le volume du micro dans
/// Windows, le volume, l'anti-bruit, la réduction de bruit et le coupe-bas (<see cref="MicAnalysis"/>). « Annuler »
/// remet tout comme avant.
/// </summary>
public sealed class MicAssistant : Observable
{
    public const double SilenceSeconds = 3, SpeechSeconds = 7;

    private static readonly string[] Sentences =
    [
        "« Salut tout le monde, c'est moi ! Je règle mon micro pour que vous m'entendiez parfaitement, même quand je crie GG. »",
        "« Un, deux, trois, test. Le petit chat gris saute sur la table, puis il repart en courant dans le jardin. »",
        "« Ce soir on lance une partie, tout le monde est prêt ? Je compte jusqu'à trois : un, deux, trois, go ! »",
    ];

    private readonly MainViewModel _vm;
    private readonly AudioEngine _engine;
    private readonly DispatcherTimer _timer;
    private readonly object _lock = new();
    private List<float> _buffer = [];
    private float[] _silence = [];
    private DateTime _stepStart;
    private string _step = "", _title = "", _text = "", _sentence = "";
    private double _progress;
    private bool _open, _recording, _done, _failed;
    private IReadOnlyList<string> _notes = [];
    private VoiceSettings? _before;
    private (string? Id, double Db)? _gainBefore;

    public MicAssistant(MainViewModel vm, AudioEngine engine)
    {
        _vm = vm;
        _engine = engine;
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(50), DispatcherPriority.Normal, (_, _) => Tick(), Dispatcher.CurrentDispatcher);
        _timer.Stop();
    }

    public bool IsOpen { get => _open; private set => Set(ref _open, value); }
    /// <summary>« intro », « silence », « speech », « result ».</summary>
    public string Step { get => _step; private set { if (Set(ref _step, value)) { Raise(nameof(IsIntro)); Raise(nameof(IsRecording)); Raise(nameof(IsResult)); Raise(nameof(IsSpeech)); } } }
    public bool IsSpeech => Step == "speech";
    public bool IsIntro => Step == "intro";
    public bool IsRecording => Step is "silence" or "speech";
    public bool IsResult => Step == "result";
    public string Title { get => _title; private set => Set(ref _title, value); }
    public string Text { get => _text; private set => Set(ref _text, value); }
    public string Sentence { get => _sentence; private set => Set(ref _sentence, value); }
    public double Progress { get => _progress; private set => Set(ref _progress, value); }
    public IReadOnlyList<string> Notes { get => _notes; private set => Set(ref _notes, value); }
    public bool Done { get => _done; private set => Set(ref _done, value); }
    public bool Failed { get => _failed; private set => Set(ref _failed, value); }

    public void Open()
    {
        if (!_engine.IsRunning)
        {
            _vm.Toast("Le micro n'est pas actif : branchez-le ou choisissez-le dans Réglages, puis relancez l'assistant.");
            return;
        }
        Step = "intro";
        Title = "ASSISTANT MICRO";
        Text = "En 10 secondes, il règle votre micro tout seul : son volume dans Windows, le volume de votre voix, l'anti-bruit, " +
               "la réduction de bruit et le coupe-bas. La voix choisie ne change pas. Installez-vous comme d'habitude (casque, " +
               "distance), puis lancez.";
        Notes = [];
        Done = Failed = false;
        IsOpen = true;
    }

    public void Start()
    {
        Sentence = Sentences[Random.Shared.Next(Sentences.Length)];
        BeginRecording("silence");
    }

    public void Close()
    {
        StopTap();
        _timer.Stop();
        IsOpen = false;
    }

    /// <summary>Remet les réglages et le volume du micro d'avant l'assistant.</summary>
    public void Undo()
    {
        if (_before != null) _vm.ApplyMicSettings(_before, lowCutFromAssistant: false);
        if (_gainBefore is { } g) MicGain.Set(_vm.MicrophoneDevice(), g.Db);
        _before = null;
        _gainBefore = null;
        _vm.Toast("Réglages du micro remis comme avant.");
        Close();
    }

    private void BeginRecording(string step)
    {
        lock (_lock) _buffer = new List<float>(48000 * 8);
        _engine.RawTap = OnAudio;
        _recording = true;
        _stepStart = DateTime.UtcNow;
        Progress = 0;
        Step = step;
        if (step == "silence")
        {
            Title = "SILENCE · 3 S";
            Text = "Ne dites rien et ne touchez à rien : on écoute le bruit de fond de votre pièce.";
        }
        else
        {
            Title = "PARLE · 7 S";
            Text = "Lisez cette phrase à voix haute, comme quand vous parlez à vos amis :";
        }
        _timer.Start();
    }

    private void OnAudio(float[] mono, int frames)
    {
        if (!_recording) return;
        lock (_lock)
            for (int i = 0; i < frames; i++) _buffer.Add(mono[i]);
    }

    private void StopTap()
    {
        _recording = false;
        if (_engine.RawTap == OnAudio) _engine.RawTap = null;
    }

    private float[] TakeBuffer()
    {
        lock (_lock)
        {
            var a = _buffer.ToArray();
            _buffer = [];
            return a;
        }
    }

    private void Tick()
    {
        double length = Step == "silence" ? SilenceSeconds : SpeechSeconds;
        double t = (DateTime.UtcNow - _stepStart).TotalSeconds;
        Progress = Math.Min(1, t / length);
        if (t < length) return;
        _timer.Stop();
        if (Step == "silence")
        {
            _silence = TakeBuffer();
            BeginRecording("speech");
            return;
        }
        StopTap();
        Finish(TakeBuffer());
    }

    private void Finish(float[] speech)
    {
        var mic = _vm.MicrophoneDevice();
        var (level, min, max) = MicGain.Read(mic);
        var report = MicAnalysis.Analyze(_silence, speech, _engine.SampleRate, _vm.Voice, level, min, max);
        Step = "result";
        Notes = report.Notes;
        if (!report.HeardVoice)
        {
            Failed = true;
            Title = "ON RECOMMENCE ?";
            Text = "Rien n'a été changé.";
            return;
        }

        _before = _vm.Voice;
        _gainBefore = null;
        if (report.WindowsGainDb != 0 && MicGain.Set(mic, level + report.WindowsGainDb))
            _gainBefore = (mic?.ID, level);
        _vm.ApplyMicSettings(report.Settings, lowCutFromAssistant: report.Settings.LowCutOn);
        Done = true;
        Title = "MICRO RÉGLÉ";
        Text = $"Bruit de fond {report.NoiseDb:0} dB · voix {report.SpeechDb:0} dB · crête {report.PeakDb:0} dB. Voici ce qui a changé :";
    }
}
