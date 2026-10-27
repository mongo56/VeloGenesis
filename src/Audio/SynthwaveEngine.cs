using System;
using System.Collections.Generic;
using Godot;

namespace VeloGenesis.Audio;

/// <summary>
/// Real-time procedural dark synthwave. Each generated song picks a style (Nightdrive, Outrun,
/// Darkwave, Dreamwave) which sets the tempo range, scale, groove, swing and instrument patches; then
/// a key, chord progressions, an arrangement and original melodic motifs. The rider's effort
/// (<see cref="Intensity"/>) brightens the filters and adds layers. Also renders sound effects.
/// </summary>
public partial class SynthwaveEngine : Node
{
    const int SR = 44100;
    const float Dt = 1f / SR;
    const sbyte Rest = 99, Hold = 98;

    AudioStreamPlayer _player;
    AudioStreamGeneratorPlayback _pb;
    Vector2[] _buf = new Vector2[4096];
    readonly Random _rng = new();
    uint _noiseState = (uint)System.Environment.TickCount | 1u;

    /// <summary>0..1 – how hard the rider is working.</summary>
    public float Intensity;
    /// <summary>When false the music fades out (sound effects keep playing).</summary>
    public bool MusicOn = true;
    public float MusicVolume = 0.5f;
    public float SfxVolume = 0.8f;
    public string SongTitle { get; private set; } = "";
    public event Action<string> SongChanged;

    float _int, _musicGain;

    // ------------------------------------------------------------------ arrangement

    enum Part { Intro, Verse, Build, Chorus, Break, Outro }
    enum Style { Nightdrive, Outrun, Darkwave, Dreamwave }

    sealed class Section
    {
        public Part Part;
        public int Bars;
    }

    sealed class Song
    {
        public string Name;
        public Style Style;
        public int Key;
        public float Bpm;
        public int BassTonic; // midi
        public int[] Scale;
        public int[] VerseProg, ChorusProg;
        public sbyte[] MotifA, MotifB;
        public int VerseBass, ChorusBass;
        public int[] VerseKick, ChorusKick;
        public bool HalfTimeVerse;
        public int HatMode;
        public float Swing;
        public int Lift;              // semitones added in the final chorus (80s key change)
        public int LastChorus = -1;
        // Patch
        public int BassWave, LeadWave, PadWave, ArpWave, ArpPattern, LeadOctave;
        public bool Arp16;
        public float DelaySteps, KickPitch, KickDecay, SnareGate, SnareTone, PadAttack, BassBright;
        public bool Clap;
        public readonly List<Section> Sections = new();
    }

    // Chord roots as scale degrees, one per bar.
    static readonly int[][] Progressions =
    {
        new[] { 0, 5, 2, 6 }, new[] { 0, 6, 5, 6 }, new[] { 0, 5, 3, 4 }, new[] { 5, 6, 0, 0 },
        new[] { 0, 3, 5, 4 }, new[] { 0, 0, 5, 6 }, new[] { 0, 2, 5, 6 }, new[] { 3, 5, 0, 6 },
        new[] { 0, 6, 3, 5 }, new[] { 5, 3, 0, 4 }, new[] { 0, 2, 3, 5 }, new[] { 0, 3, 0, 6 },
        new[] { 3, 4, 0, 0 }, new[] { 0, 5, 6, 4 },
    };

    static readonly (Part, int)[][] Forms =
    {
        new[] { (Part.Intro, 8), (Part.Verse, 16), (Part.Build, 8), (Part.Chorus, 16), (Part.Break, 8), (Part.Build, 4), (Part.Chorus, 16), (Part.Outro, 8) },
        new[] { (Part.Intro, 4), (Part.Verse, 8), (Part.Chorus, 8), (Part.Verse, 8), (Part.Build, 8), (Part.Chorus, 16), (Part.Break, 8), (Part.Chorus, 8), (Part.Outro, 8) },
        new[] { (Part.Intro, 8), (Part.Verse, 16), (Part.Chorus, 16), (Part.Break, 8), (Part.Build, 8), (Part.Chorus, 16), (Part.Outro, 4) },
        new[] { (Part.Intro, 8), (Part.Break, 8), (Part.Verse, 16), (Part.Build, 4), (Part.Chorus, 16), (Part.Verse, 8), (Part.Build, 8), (Part.Chorus, 16), (Part.Outro, 8) },
        new[] { (Part.Intro, 4), (Part.Chorus, 8), (Part.Verse, 16), (Part.Build, 8), (Part.Chorus, 16), (Part.Break, 4), (Part.Chorus, 8), (Part.Outro, 8) },
    };

    static readonly int[][] Rhythms =
    {
        new[] { 0, 3, 6, 8, 11, 14 }, new[] { 0, 2, 3, 6, 8, 10, 11, 14 }, new[] { 0, 4, 6, 8, 12, 14 },
        new[] { 0, 1, 2, 4, 8, 9, 10, 12 }, new[] { 2, 3, 4, 6, 10, 11, 12, 14 }, new[] { 0, 3, 4, 7, 8, 11, 12 },
        new[] { 0, 2, 4, 7, 10, 12 }, new[] { 1, 2, 4, 6, 9, 10, 12 }, new[] { 0, 2, 4, 6, 8, 12 },
        new[] { 0, 6, 8, 14 }, new[] { 0, 3, 8, 12 }, new[] { 0, 4, 8, 11 }, // sparse
    };
    const int FirstSparseRhythm = 9;

    static readonly int[][] ArpPatterns =
    {
        new[] { 0, 1, 2, 3, 4, 3, 2, 1 }, new[] { 0, 2, 1, 3, 2, 4, 3, 1 }, new[] { 4, 3, 2, 1, 0, 1, 2, 3 },
        new[] { 0, 0, 2, 2, 1, 1, 3, 3 }, new[] { 0, 3, 1, 4, 2, 3, 1, 2 }, new[] { 0, 1, 2, 0, 1, 2, 3, 4 },
    };

    static readonly int[] MinorScale = { 0, 2, 3, 5, 7, 8, 10 };
    static readonly int[] DorianScale = { 0, 2, 3, 5, 7, 9, 10 };
    static readonly int[] PhrygianScale = { 0, 1, 3, 5, 7, 8, 10 };
    static readonly string[] KeyNames = { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };
    static readonly string[] TitleA = { "Midnight", "Neon", "Chrome", "Turbo", "Static", "Afterglow", "Vector", "Nocturnal", "Electric", "Phantom", "Velvet", "Crimson", "Lunar", "Hyper", "Silent", "Cobalt" };
    static readonly string[] TitleB = { "Drive", "Overdrive", "Highway", "Run", "Pursuit", "Signal", "Horizon", "Mirage", "Call", "Skyline", "Nightrider", "Heartbeat", "Boulevard", "Echoes", "Voltage", "Getaway" };

    Song _song;
    Style? _lastStyle;
    int _sec, _bar, _step = -1;
    double _stepAcc, _stepSamples = SR * 60.0 / 92.0 / 4.0;
    int[] _pendingProg;

    float _gBass, _gPad, _gLead, _gArp, _tBass, _tPad, _tLead, _tArp, _bright, _tBright;

    // ------------------------------------------------------------------ voices

    sealed class Svf
    {
        public float Low, Band;
        public void Run(float x, float cutoff, float damp)
        {
            float f = 2f * MathF.Sin(MathF.PI * Math.Clamp(cutoff, 20f, SR / 6.5f) / SR);
            Low += f * Band;
            float high = x - Low - damp * Band;
            Band += f * high;
        }
    }

    double _kP;
    float _kT = 9, _kVel, _kEnv;
    float _sT = 9, _sVel, _sGate;
    readonly Svf _sBp = new(), _sRoom = new();
    float _hT = 9, _hVel, _ohT = 9, _ohVel, _hLp;
    float _tomT = 9, _tomF, _tomVel, _tomPan;
    double _tomP;
    float _crT = 9, _crLp;
    float _riser;
    bool _riserOn;
    readonly Svf _riserF = new();

    readonly double[] _bP = new double[3];
    double _bSub;
    float _bFreq = 55, _bTarget = 55, _bEnv, _bFEnv, _lfo;
    int _bGate;
    readonly Svf _bF = new();

    readonly double[] _pP = new double[9];
    readonly float[] _pFreq = new float[9], _pTarget = new float[9];
    float _pEnv;
    readonly Svf _pL = new(), _pR = new(), _pForm = new();

    double _lP1, _lP2, _lP3;
    float _lFreq = 440, _lTarget = 440, _lEnv, _lHeld;
    bool _lOn;
    readonly Svf _lF = new();

    double _aP;
    float _aFreq = 440, _aEnv, _aLp;

    readonly float[] _dlL = new float[SR * 2], _dlR = new float[SR * 2];
    int _dlPos;

    sealed class Sfx { public float F0, F1, Dur, Vol, T, Delay; public bool Square; public double Phase; }
    readonly List<Sfx> _sfx = new();
    readonly object _sfxLock = new();

    public override void _Ready()
    {
        var gen = new AudioStreamGenerator { MixRate = SR, BufferLength = 0.25f };
        _player = new AudioStreamPlayer { Stream = gen, Bus = "Music" };
        AddChild(_player);
        _player.Play();
        _pb = (AudioStreamGeneratorPlayback)_player.GetStreamPlayback();
        NewSong();
    }

    /// <summary>Nudges the harmony when the rider enters a new kind of sector (applied next section).</summary>
    public void SetMood(int index) => _pendingProg = Progressions[((index * 5 + 1) % Progressions.Length + Progressions.Length) % Progressions.Length];

    int Pick(params int[] options) => options[_rng.Next(options.Length)];
    float Rand(float a, float b) => a + (float)_rng.NextDouble() * (b - a);
    bool Chance(double p) => _rng.NextDouble() < p;

    /// <summary>Generates a new song in a different style from the last one.</summary>
    public void NewSong()
    {
        var styles = (Style[])Enum.GetValues(typeof(Style));
        Style style;
        do style = styles[_rng.Next(styles.Length)]; while (style == _lastStyle);
        _lastStyle = style;

        var s = new Song { Style = style, Key = _rng.Next(12), Scale = MinorScale };
        switch (style)
        {
            case Style.Nightdrive:
                s.Bpm = 84 + _rng.Next(13);
                if (Chance(0.2)) s.Scale = DorianScale;
                s.VerseBass = Pick(0, 1, 3); s.ChorusBass = Pick(1, 3);
                s.HalfTimeVerse = Chance(0.5);
                s.VerseKick = Chance(0.5) ? new[] { 0, 8 } : new[] { 0, 8, 10 };
                s.ChorusKick = new[] { 0, 4, 8, 12 };
                s.HatMode = 0;
                s.BassWave = Pick(0, 3); s.LeadWave = Pick(0, 3); s.PadWave = 0; s.ArpWave = 0;
                s.Arp16 = true; s.DelaySteps = 3; s.KickPitch = 46; s.KickDecay = 6; s.SnareGate = 0.22f; s.SnareTone = 185; s.PadAttack = 0.00003f;
                break;
            case Style.Outrun:
                s.Bpm = 104 + _rng.Next(15);
                s.VerseBass = Pick(2, 4); s.ChorusBass = Pick(2, 4);
                s.VerseKick = new[] { 0, 4, 8, 12 }; s.ChorusKick = new[] { 0, 4, 8, 12 };
                s.HatMode = 1;
                s.BassWave = Pick(0, 1); s.LeadWave = Pick(1, 2); s.PadWave = Pick(0, 1); s.ArpWave = Pick(0, 1);
                s.Arp16 = true; s.DelaySteps = Pick(2, 3); s.KickPitch = 50; s.KickDecay = 8; s.SnareGate = 0.15f; s.SnareTone = 205;
                s.Clap = Chance(0.3); s.PadAttack = 0.0001f;
                break;
            case Style.Darkwave:
                s.Bpm = 78 + _rng.Next(13);
                if (Chance(0.5)) s.Scale = PhrygianScale;
                s.VerseBass = Pick(0, 3); s.ChorusBass = Pick(1, 3);
                s.HalfTimeVerse = Chance(0.6);
                s.VerseKick = new[] { 0, 6, 8 };
                s.ChorusKick = Chance(0.5) ? new[] { 0, 8, 10 } : new[] { 0, 4, 8, 12 };
                s.HatMode = 2; s.Swing = 0.06f;
                s.BassWave = Pick(2, 3); s.LeadWave = Pick(2, 3); s.PadWave = 2; s.ArpWave = 2;
                s.LeadOctave = Chance(0.5) ? -12 : 0;
                s.Arp16 = false; s.DelaySteps = Pick(4, 6); s.KickPitch = 42; s.KickDecay = 5; s.SnareGate = 0.3f; s.SnareTone = 165; s.PadAttack = 0.00002f;
                break;
            default: // Dreamwave
                s.Bpm = 92 + _rng.Next(15);
                if (Chance(0.5)) s.Scale = DorianScale;
                s.VerseBass = Pick(1, 3); s.ChorusBass = Pick(1, 2);
                s.VerseKick = new[] { 0, 8 }; s.ChorusKick = new[] { 0, 4, 8, 12 };
                s.HatMode = 0; s.Swing = Chance(0.5) ? 0.1f : 0f;
                s.BassWave = Pick(0, 1); s.LeadWave = Pick(0, 3); s.PadWave = Pick(0, 2); s.ArpWave = 2;
                s.Arp16 = Chance(0.5); s.DelaySteps = Pick(3, 4); s.KickPitch = 48; s.KickDecay = 7; s.SnareGate = 0.2f; s.SnareTone = 190;
                s.Clap = Chance(0.6); s.PadAttack = 0.00002f;
                break;
        }
        s.ArpPattern = _rng.Next(ArpPatterns.Length);
        s.BassBright = Rand(0.7f, 1.3f);
        s.BassTonic = 33 + (s.Key + 3) % 12; // A1..G#2
        s.VerseProg = Progressions[_rng.Next(Progressions.Length)];
        do s.ChorusProg = Progressions[_rng.Next(Progressions.Length)]; while (s.ChorusProg == s.VerseProg);
        s.MotifA = MakeMotif(true);
        s.MotifB = MakeMotif(false);
        foreach (var (part, bars) in Forms[_rng.Next(Forms.Length)]) s.Sections.Add(new Section { Part = part, Bars = bars });
        for (int i = 0; i < s.Sections.Count; i++) if (s.Sections[i].Part == Part.Chorus) s.LastChorus = i;
        s.Lift = Chance(0.4) ? Pick(1, 2) : 0;

        string mode = s.Scale == DorianScale ? "dorian" : s.Scale == PhrygianScale ? "phrygian" : "minor";
        s.Name = $"{TitleA[_rng.Next(TitleA.Length)]} {TitleB[_rng.Next(TitleB.Length)]} · {style} · {KeyNames[s.Key]} {mode} · {s.Bpm:0} bpm";
        _song = s;
        _sec = 0;
        _bar = 0;
        _step = -1;
        _stepAcc = 0;
        SongTitle = s.Name;
        GD.Print($"Now playing: {s.Name}");
        SongChanged?.Invoke(s.Name);
    }

    sbyte[] MakeMotif(bool sparse)
    {
        var rhythm = sparse && Chance(0.6)
            ? Rhythms[FirstSparseRhythm + _rng.Next(Rhythms.Length - FirstSparseRhythm)]
            : Rhythms[_rng.Next(FirstSparseRhythm)];
        var m = new sbyte[16];
        Array.Fill(m, Rest);
        int[] steps = { -2, -1, -1, 0, 1, 1, 2, -3, 3, -4, 4 };
        int cur = Pick(0, 2, 4, 7);
        for (int i = 0; i < rhythm.Length; i++)
        {
            int pos = rhythm[i];
            if (i > 0) cur += steps[_rng.Next(steps.Length)];
            cur = Math.Clamp(cur, -3, 10);
            if (pos % 8 == 0 || i == rhythm.Length - 1) cur = NearestChordTone(cur);
            m[pos] = (sbyte)cur;
            int end = i + 1 < rhythm.Length ? rhythm[i + 1] : 16;
            for (int p = pos + 1; p < end; p++) m[p] = Hold;
            if (end - pos > 2 && Chance(0.35)) m[end - 1] = Rest;
        }
        return m;
    }

    static int NearestChordTone(int v)
    {
        int best = 0;
        foreach (int c in new[] { -3, 0, 2, 4, 7, 9 })
            if (Math.Abs(v - c) < Math.Abs(v - best)) best = c;
        return best;
    }

    static int FloorDiv(int a, int b) => (int)MathF.Floor((float)a / b);

    /// <summary>Scale degree → midi. Raises the 7th on a V chord (harmonic minor) for that dark pull home.</summary>
    int DegreeMidi(int degree, int tonic, bool raiseLeading)
    {
        int oct = FloorDiv(degree, 7);
        int idx = degree - oct * 7;
        int lift = _sec == _song.LastChorus ? _song.Lift : 0;
        return tonic + lift + oct * 12 + _song.Scale[idx] + (raiseLeading && idx == 6 && _song.Scale == MinorScale ? 1 : 0);
    }

    static float Hz(int midi) => 440f * MathF.Pow(2f, (midi - 69) / 12f);

    Section Current => _song.Sections[_sec];
    Part NextPart => _sec + 1 < _song.Sections.Count ? _song.Sections[_sec + 1].Part : Part.Intro;
    int[] Prog => Current.Part is Part.Chorus or Part.Break ? _song.ChorusProg : _song.VerseProg;
    int ChordRoot => Prog[_bar % 4];

    // ------------------------------------------------------------------ sequencing

    void AdvanceStep()
    {
        _step++;
        if (_step >= 16)
        {
            _step = 0;
            _bar++;
            if (_bar >= Current.Bars)
            {
                _bar = 0;
                _sec++;
                if (_sec >= _song.Sections.Count) { NewSong(); _step = 0; }
                if (_pendingProg != null && Current.Part is Part.Verse or Part.Chorus)
                {
                    if (Current.Part == Part.Chorus) _song.ChorusProg = _pendingProg; else _song.VerseProg = _pendingProg;
                    _pendingProg = null;
                }
            }
        }
        double baseLen = SR * 60.0 / _song.Bpm / 4.0;
        _stepSamples = baseLen * (_step % 2 == 0 ? 1 + _song.Swing : 1 - _song.Swing);
        OnStep();
    }

    void OnStep()
    {
        var sec = Current;
        var part = sec.Part;
        int st = _step;
        int root = ChordRoot;
        bool dominant = root == 4;
        bool lastBar = _bar == sec.Bars - 1;
        float I = _int;
        float sectionPos = (_bar + st / 16f) / sec.Bars;
        var song = _song;

        float fade = part == Part.Outro ? 1f - sectionPos : 1f;
        (_tBass, _tPad, _tLead, _tArp) = part switch
        {
            Part.Intro => (0.75f, 1f, 0f, song.Style == Style.Dreamwave ? 0.5f : 0f),
            Part.Verse => (1f, 0.75f, _bar >= 4 ? 0.85f : 0f, I > 0.7f || song.Style == Style.Outrun ? 0.55f : 0f),
            Part.Build => (1f, 0.7f, 0f, 0.9f),
            Part.Chorus => (1f, 1f, 1f, 0.6f + 0.3f * I),
            Part.Break => (0.45f, 1f, 0.7f, song.Style == Style.Dreamwave ? 0.6f : 0f),
            _ => (0.85f * fade, fade, 0f, 0f),
        };
        float baseBright = part switch
        {
            Part.Intro => 0.15f + 0.25f * sectionPos,
            Part.Verse => 0.45f,
            Part.Build => 0.45f + 0.5f * sectionPos,
            Part.Chorus => 0.85f,
            Part.Break => 0.3f,
            _ => 0.35f * fade,
        };
        if (song.Style == Style.Darkwave) baseBright *= 0.75f;
        _tBright = Math.Clamp(baseBright * 0.7f + I * 0.4f, 0f, 1f);

        if (st == 0)
        {
            int padTonic = song.BassTonic + 24;
            int[] chord = { root, root + 2, root + 4 };
            for (int v = 0; v < 3; v++)
            {
                float f = Hz(DegreeMidi(chord[v], padTonic, dominant));
                _pTarget[v * 3] = f * 0.994f;
                _pTarget[v * 3 + 1] = f;
                _pTarget[v * 3 + 2] = f * 1.006f;
            }
            _pEnv = Math.Min(_pEnv, 0.75f);
            if (_bar == 0 && (part == Part.Chorus || (part == Part.Verse && _sec > 0 && song.Sections[_sec - 1].Part == Part.Build))) _crT = 0;
        }

        // ---- drums
        bool fill = lastBar && st >= 12 && part is not (Part.Intro or Part.Break) && NextPart != Part.Break;
        float hatVel = 0.55f + 0.6f * I;
        if (fill)
        {
            float[] toms = { 196f, 165f, 131f, 98f };
            Tom(toms[st - 12] * (song.Style == Style.Darkwave ? 0.85f : 1f), 0.9f, st % 2 == 0 ? -0.4f : 0.4f);
            if (st == 12) Kick(1f);
        }
        else switch (part)
        {
            case Part.Intro:
                if (_bar >= sec.Bars / 2 && st % 4 == 2) Hat(0.35f);
                if (lastBar && st >= 8) Snare(0.25f + (st - 8) * 0.08f, 0.04f);
                break;
            case Part.Verse:
                if (Array.IndexOf(song.VerseKick, st) >= 0 || (_bar % 4 == 3 && st == 14)) Kick(1f);
                if (song.HalfTimeVerse ? st == 8 : st is 4 or 12) Snare(1f, song.SnareGate);
                Hats(st, hatVel * 0.85f, false);
                break;
            case Part.Build:
                if (st % 4 == 0) Kick(1f);
                if (lastBar) Snare(0.3f + st * 0.045f, 0.05f);
                else if (_bar >= sec.Bars - 2) { if (st % 2 == 0) Snare(0.45f + st * 0.02f, 0.06f); }
                else if (st is 4 or 12) Snare(1f, song.SnareGate);
                Hat((st % 2 == 0 ? 0.55f : 0.3f) * hatVel);
                break;
            case Part.Chorus:
                if (Array.IndexOf(song.ChorusKick, st) >= 0) Kick(1f);
                if (st is 4 or 12) Snare(1f, song.SnareGate + 0.02f);
                Hats(st, hatVel, true);
                break;
            case Part.Outro:
                if (_bar < sec.Bars - 2)
                {
                    if (Array.IndexOf(song.VerseKick, st) >= 0) Kick(0.9f * fade + 0.1f);
                    if (st is 4 or 12) Snare(0.9f * fade + 0.1f, song.SnareGate);
                }
                break;
        }

        bool riserPart = (part == Part.Build && _bar >= sec.Bars - 4) || (part == Part.Break && _bar >= sec.Bars - 2 && NextPart == Part.Chorus);
        if (riserPart && !_riserOn) { _riserOn = true; _riser = 0; }
        if (!riserPart) _riserOn = false;

        // ---- bass
        int bassMidi = DegreeMidi(root, song.BassTonic, false);
        if (root >= 5) bassMidi -= 12;
        int style = part switch { Part.Intro or Part.Break or Part.Outro => 0, Part.Build => song.Style == Style.Outrun ? 4 : 2, Part.Chorus => song.ChorusBass, _ => song.VerseBass };
        int stepLen = (int)(SR * 60.0 / song.Bpm / 4.0);
        switch (style)
        {
            case 0: // sustained, glide into each chord
                if (st == 0) BassNote(bassMidi, stepLen * 16, true);
                break;
            case 1: // driving quarters
                if (st % 4 == 0) BassNote(bassMidi, (int)(stepLen * 3.4f), st != 0);
                break;
            case 2: // outrun eighths with octave jumps
                if (st % 2 == 0) BassNote(bassMidi + (st % 4 == 2 && I > 0.4f ? 12 : 0), (int)(stepLen * 1.6f), false);
                break;
            case 3: // syncopated
                if (st is 0 or 3 or 6 or 8 or 11 or 14) BassNote(bassMidi + (st == 14 ? 12 : 0), (int)(stepLen * 2.4f), false);
                break;
            case 4: // galloping 8th + two 16ths
                if (st % 4 != 1) BassNote(bassMidi + (st % 8 == 6 ? 12 : 0), (int)(stepLen * (st % 4 == 0 ? 1.6f : 0.8f)), false);
                break;
        }

        // ---- lead (motif developed over 8-bar phrases)
        if (st % 2 == 0 && _tLead > 0)
        {
            var motif = part == Part.Chorus ? song.MotifB : song.MotifA;
            int pos = (_bar % 2) * 8 + st / 2;
            int phrase = (_bar / 2) % 4;
            sbyte v = motif[pos];
            if (v == Rest) _lOn = false;
            else if (v != Hold)
            {
                int deg = v;
                if (phrase == 2 && pos >= 12) deg = NearestChordTone(deg + 2);
                if (phrase == 3) deg += 2;
                int tonic = song.BassTonic + 36 + song.LeadOctave + (part == Part.Break ? 12 : 0);
                LeadNote(DegreeMidi(root + deg, tonic, dominant));
            }
        }

        // ---- arpeggio
        if (_tArp > 0 && (song.Arp16 || st % 2 == 0))
        {
            int[] pattern = ArpPatterns[song.ArpPattern];
            int[] tones = { 0, 2, 4, 7, 9 };
            int idx = song.Arp16 ? st : st / 2;
            int deg = root + tones[pattern[(idx + _bar * 3) % 8]];
            _aFreq = Hz(DegreeMidi(deg, song.BassTonic + 36, dominant));
            _aEnv = 1f;
        }
    }

    void Hats(int st, float vel, bool chorus)
    {
        switch (_song.HatMode)
        {
            case 1: // 16ths with open hats on the off-beat
                Hat((st % 2 == 0 ? 0.4f : 0.22f) * vel);
                if (st % 4 == 2) OpenHat(0.45f * vel);
                break;
            case 2: // sparse off-beats
                if (st % 4 == 2) Hat(0.55f * vel);
                if (chorus && st % 8 == 6) OpenHat(0.35f * vel);
                break;
            default: // eighths
                if (st % 2 == 0) Hat(st % 4 == 2 ? 0.6f * vel : 0.35f * vel);
                if (chorus && st % 4 == 2) OpenHat(0.4f * vel);
                break;
        }
    }

    void Kick(float v) { _kT = 0; _kP = 0; _kVel = v; }
    void Snare(float v, float gate) { _sT = 0; _sVel = Math.Clamp(v, 0, 1.2f); _sGate = gate; }
    void Hat(float v) { _hT = 0; _hVel = v; }
    void OpenHat(float v) { _ohT = 0; _ohVel = v; }
    void Tom(float f, float v, float pan) { _tomT = 0; _tomF = f; _tomVel = v; _tomPan = pan; _tomP = 0; }

    void BassNote(int midi, int gateSamples, bool glide)
    {
        _bTarget = Hz(midi);
        if (!glide) _bFreq = _bTarget;
        _bEnv = 1f;
        _bFEnv = 1f;
        _bGate = gateSamples;
    }

    void LeadNote(int midi)
    {
        _lTarget = Hz(midi);
        if (!_lOn) { _lFreq = _lTarget; _lHeld = 0; }
        else _lHeld = Math.Min(_lHeld, 0.1f);
        _lOn = true;
    }

    // ------------------------------------------------------------------ rendering

    public override void _Process(double delta)
    {
        if (_pb == null) return;
        int frames = _pb.GetFramesAvailable();
        if (frames <= 0) return;
        if (_buf.Length < frames) _buf = new Vector2[frames];
        for (int i = 0; i < frames; i++) _buf[i] = Render();
        if (frames == _buf.Length) _pb.PushBuffer(_buf);
        else _pb.PushBuffer(_buf[..frames]);
    }

    float Noise()
    {
        _noiseState ^= _noiseState << 13;
        _noiseState ^= _noiseState >> 17;
        _noiseState ^= _noiseState << 5;
        return (_noiseState & 0xFFFF) / 32767.5f - 1f;
    }

    static float Blep(double t, double dt)
    {
        if (t < dt) { t /= dt; return (float)(t + t - t * t - 1); }
        if (t > 1 - dt) { t = (t - 1) / dt; return (float)(t * t + t + t + 1); }
        return 0f;
    }

    static float Saw(ref double phase, float freq)
    {
        double dt = freq * Dt;
        phase += dt;
        if (phase >= 1) phase -= 1;
        return (float)(2 * phase - 1) - Blep(phase, dt);
    }

    static float Pulse(ref double phase, float freq, float width = 0.5f)
    {
        double dt = freq * Dt;
        phase += dt;
        if (phase >= 1) phase -= 1;
        float v = phase < width ? 1f : -1f;
        return v + Blep(phase, dt) - Blep((phase + 1.0 - width) % 1.0, dt);
    }

    static float Tri(ref double phase, float freq)
    {
        phase += freq * Dt;
        if (phase >= 1) phase -= 1;
        return (float)(4.0 * Math.Abs(phase - 0.5) - 1.0);
    }

    static float Approach(float cur, float target, float rate) => cur + (target - cur) * rate;

    Vector2 Render()
    {
        _int = Approach(_int, Math.Clamp(Intensity, 0f, 1f), 0.00002f);
        _musicGain = Approach(_musicGain, MusicOn ? 1f : 0f, 0.00003f);

        if (_musicGain > 0.001f)
        {
            _stepAcc += 1;
            if (_step < 0 || _stepAcc >= _stepSamples)
            {
                if (_step >= 0) _stepAcc -= _stepSamples;
                AdvanceStep();
            }
        }

        var song = _song;
        _gBass = Approach(_gBass, _tBass, 0.00008f);
        _gPad = Approach(_gPad, _tPad, 0.00004f);
        _gLead = Approach(_gLead, _tLead, 0.00008f);
        _gArp = Approach(_gArp, _tArp, 0.00008f);
        _bright = Approach(_bright, _tBright, 0.00003f);
        _lfo += Dt;
        float B = _bright;
        float L = 0, R = 0;

        // Kick
        if (_kT < 0.6f)
        {
            float f = song.KickPitch + 140f * MathF.Exp(-_kT * 32f);
            _kP += f * Dt;
            _kEnv = MathF.Exp(-_kT * song.KickDecay) * _kVel;
            float k = MathF.Tanh(MathF.Sin((float)(_kP * Math.Tau)) * 1.6f) * _kEnv + (_kT < 0.0025f ? Noise() * 0.25f * _kVel : 0);
            L += k * 0.8f;
            R += k * 0.8f;
            _kT += Dt;
        }
        else _kEnv = 0;
        float duck = 1f - 0.4f * _kEnv;

        // Gated 80s snare (or clap): tone + band-passed crack + gated "room" that cuts off sharply.
        if (_sT < _sGate + 0.08f)
        {
            float n = Noise();
            _sBp.Run(n, song.Clap ? 1400f : 1900f, 0.9f);
            _sRoom.Run(n, 3200f, 1.3f);
            float gate = _sT < _sGate ? 1f : MathF.Exp(-(_sT - _sGate) * 90f);
            float crackEnv = song.Clap && _sT < 0.03f ? MathF.Exp(-(_sT % 0.01f) * 280f) : MathF.Exp(-_sT * 22f);
            float crack = _sBp.Band * crackEnv * (song.Clap ? 1.9f : 1.4f);
            float room = _sRoom.Low * (1f - 0.45f * Math.Min(_sT / 0.03f, 1f)) * gate * 0.55f;
            float body = song.Clap ? 0f : MathF.Sin(_sT * song.SnareTone * MathF.Tau) * MathF.Exp(-_sT * 24f) * 0.5f;
            float sn = (crack + room + body) * _sVel * 0.75f;
            L += sn;
            R += sn * 0.95f;
            _sT += Dt;
        }

        // Hats
        float hats = 0;
        if (_hT < 0.12f || _ohT < 0.6f)
        {
            float n = Noise();
            _hLp = Approach(_hLp, n, 0.45f);
            float hp = n - _hLp;
            if (_hT < 0.12f) { hats += hp * MathF.Exp(-_hT * 70f) * _hVel; _hT += Dt; }
            if (_ohT < 0.6f) { hats += hp * MathF.Exp(-_ohT * 8f) * _ohVel * 0.6f; _ohT += Dt; }
        }
        L += hats * 0.16f;
        R += hats * 0.21f;

        // Toms
        if (_tomT < 0.5f)
        {
            float f = _tomF * (1f + 0.5f * MathF.Exp(-_tomT * 22f));
            _tomP += f * Dt;
            float t = (MathF.Sin((float)(_tomP * Math.Tau)) + Noise() * 0.08f) * MathF.Exp(-_tomT * 7f) * _tomVel * 0.55f;
            L += t * (1f - _tomPan) * 0.6f;
            R += t * (1f + _tomPan) * 0.6f;
            _tomT += Dt;
        }

        // Crash
        if (_crT < 2.5f)
        {
            float n = Noise();
            _crLp = Approach(_crLp, n, 0.3f);
            float c = (n - _crLp) * MathF.Exp(-_crT * 1.6f) * 0.16f;
            L += c;
            R += c;
            _crT += Dt;
        }

        // Riser
        if (_riserOn)
        {
            float barsLen = (float)(SR * 60.0 / song.Bpm * 16);
            _riser = Math.Min(1f, _riser + 1f / barsLen);
            _riserF.Run(Noise(), 300f + 7000f * _riser * _riser, 0.7f);
            float r = _riserF.Band * _riser * 0.22f;
            L += r * (0.8f + 0.2f * MathF.Sin(_riser * 40f));
            R += r * (0.8f - 0.2f * MathF.Sin(_riser * 40f));
        }

        // Bass (patch per song), driven into a resonant low-pass, gliding between notes.
        _bFreq = Approach(_bFreq, _bTarget, 0.0006f);
        if (_bGate-- > 0) _bEnv = Approach(_bEnv, 0.82f, 0.00002f);
        else _bEnv *= 0.9994f;
        _bFEnv *= 0.99992f;
        float bf = _bFreq;
        float braw = song.BassWave switch
        {
            1 => Pulse(ref _bP[0], bf) * 0.6f + Saw(ref _bP[1], bf * 1.003f) * 0.4f,
            2 => Pulse(ref _bP[0], bf, 0.5f + 0.35f * MathF.Sin(_lfo * 0.7f * MathF.Tau)) * 0.8f,
            3 => (Saw(ref _bP[0], bf * 0.985f) + Saw(ref _bP[1], bf * 1.015f) + Saw(ref _bP[2], bf) * 0.5f) * 0.4f,
            _ => (Saw(ref _bP[0], bf * 0.996f) + Saw(ref _bP[1], bf) + Saw(ref _bP[2], bf * 1.005f)) * 0.33f,
        };
        _bSub += bf * 0.5 * Dt;
        if (_bSub >= 1) _bSub -= 1;
        _bF.Run(MathF.Tanh(braw * 1.8f), 90f + (220f + 1100f * B) * song.BassBright * (0.45f + 0.55f * _bFEnv), 0.45f);
        float bass = (_bF.Low * 0.9f + MathF.Sin((float)(_bSub * Math.Tau)) * 0.55f) * _bEnv * _gBass * duck;
        L += bass * 0.42f;
        R += bass * 0.42f;

        // Pad (patch per song) with a vowel-like band for a choir colour.
        _pEnv = Approach(_pEnv, 1f, song.PadAttack);
        float padL = 0, padR = 0;
        for (int v = 0; v < 9; v++)
        {
            _pFreq[v] = Approach(_pFreq[v], _pTarget[v], 0.0004f);
            float s = song.PadWave switch
            {
                1 => Pulse(ref _pP[v], _pFreq[v], 0.5f) * 0.8f,
                2 => Tri(ref _pP[v], _pFreq[v]) * 1.6f,
                _ => Saw(ref _pP[v], _pFreq[v]),
            };
            switch (v % 3) { case 0: padL += s; break; case 2: padR += s; break; default: padL += s * 0.5f; padR += s * 0.5f; break; }
        }
        float padCut = 500f + 1600f * B;
        _pL.Run(padL, padCut, 1.1f);
        _pR.Run(padR, padCut, 1.1f);
        _pForm.Run((padL + padR) * 0.5f, song.PadWave == 2 ? 1100f : 820f, 0.35f);
        float pg = _pEnv * _gPad * (0.75f + 0.25f * duck) * 0.04f;
        L += (_pL.Low + _pForm.Band * 0.6f) * pg;
        R += (_pR.Low + _pForm.Band * 0.6f) * pg;

        // Lead (patch per song): kept dark and soft so it sits behind the rest.
        float lead = 0;
        _lEnv = Approach(_lEnv, _lOn ? 1f : 0f, _lOn ? 0.0006f : 0.00025f);
        if (_lEnv > 0.0005f)
        {
            _lFreq = Approach(_lFreq, _lTarget, 0.0011f);
            _lHeld += Dt;
            float vib = 1f + 0.004f * Math.Clamp((_lHeld - 0.25f) * 3f, 0f, 1f) * MathF.Sin(_lHeld * 5.6f * MathF.Tau);
            float f = _lFreq * vib;
            float raw = song.LeadWave switch
            {
                1 => Pulse(ref _lP1, f) * 0.5f + Pulse(ref _lP2, f * 1.004f) * 0.3f,
                2 => Pulse(ref _lP1, f, 0.5f + 0.3f * MathF.Sin(_lfo * 1.3f * MathF.Tau)) * 0.55f + Saw(ref _lP2, f * 0.996f) * 0.3f,
                3 => Tri(ref _lP1, f) * 0.9f + Saw(ref _lP2, f * 1.002f) * 0.15f,
                _ => Saw(ref _lP1, f * 0.997f) * 0.5f + Saw(ref _lP2, f * 1.003f) * 0.5f + Pulse(ref _lP3, f * 0.5f) * 0.25f,
            };
            _lF.Run(raw, (650f + 1000f * B) * (0.75f + 0.25f * _lEnv), 1.05f);
            lead = _lF.Low * _lEnv * _gLead * (song.LeadWave == 3 ? 1.4f : 1f);
        }
        L += lead * 0.1f;
        R += lead * 0.1f;

        // Arp (patch per song)
        float araw;
        if (song.ArpWave == 2)
        {
            _aEnv *= 0.99965f; // pluck
            araw = Tri(ref _aP, _aFreq) * 1.3f;
        }
        else
        {
            _aEnv *= 0.99985f;
            araw = song.ArpWave == 1 ? Saw(ref _aP, _aFreq) : Pulse(ref _aP, _aFreq);
        }
        _aLp = Approach(_aLp, araw, 0.06f + 0.3f * B);
        float arp = _aLp * _aEnv * _gArp * duck;
        L += arp * 0.07f;
        R += arp * 0.07f;

        // Ping-pong delay on lead + arp; length set per song.
        int dlLen = Math.Clamp((int)(SR * 60.0 / song.Bpm / 4.0 * song.DelaySteps), 1, _dlL.Length - 1);
        int rp = (_dlPos - dlLen + _dlL.Length) % _dlL.Length;
        float dl = _dlL[rp], dr = _dlR[rp];
        float send = lead * 0.07f + arp * 0.07f;
        _dlL[_dlPos] = send + dr * 0.45f;
        _dlR[_dlPos] = dl * 0.45f;
        _dlPos = (_dlPos + 1) % _dlL.Length;
        L += dl * 0.55f;
        R += dr * 0.55f;

        float mg = _musicGain * MusicVolume * 0.8f;
        L *= mg;
        R *= mg;

        float fx = RenderSfx();
        L += fx * SfxVolume;
        R += fx * SfxVolume;
        return new Vector2(MathF.Tanh(L), MathF.Tanh(R));
    }

    /// <summary>Debug: render the procedural music offline to a 16-bit stereo WAV (several songs back to back).</summary>
    public void RenderToWav(string path, float seconds, float intensity)
    {
        Intensity = intensity;
        _int = intensity;
        _musicGain = 1f;
        int n = (int)(seconds * SR);
        using var f = System.IO.File.Create(path);
        using var w = new System.IO.BinaryWriter(f);
        w.Write("RIFF"u8); w.Write(36 + n * 4); w.Write("WAVE"u8);
        w.Write("fmt "u8); w.Write(16); w.Write((short)1); w.Write((short)2); w.Write(SR); w.Write(SR * 4); w.Write((short)4); w.Write((short)16);
        w.Write("data"u8); w.Write(n * 4);
        for (int i = 0; i < n; i++)
        {
            var s = Render();
            w.Write((short)(Math.Clamp(s.X, -1f, 1f) * 32767));
            w.Write((short)(Math.Clamp(s.Y, -1f, 1f) * 32767));
        }
    }

    float RenderSfx()
    {
        float sum = 0;
        lock (_sfxLock)
        {
            for (int k = _sfx.Count - 1; k >= 0; k--)
            {
                var s = _sfx[k];
                if (s.Delay > 0) { s.Delay -= Dt; continue; }
                float p = s.T / s.Dur;
                if (p >= 1) { _sfx.RemoveAt(k); continue; }
                float f = s.F0 + (s.F1 - s.F0) * p;
                float env = MathF.Min(1f, s.T * 400f) * (1f - p) * (1f - p);
                float w;
                if (s.Square) w = Pulse(ref s.Phase, f) * 0.5f;
                else
                {
                    w = MathF.Sin((float)(s.Phase * Math.Tau));
                    s.Phase += f * Dt;
                    if (s.Phase > 1) s.Phase -= 1;
                }
                sum += w * env * s.Vol;
                s.T += Dt;
            }
        }
        return sum;
    }

    void Play(float f0, float f1, float dur, float vol, bool square = false, float delay = 0)
    {
        lock (_sfxLock) _sfx.Add(new Sfx { F0 = f0, F1 = f1, Dur = dur, Vol = vol, Square = square, Delay = delay });
    }

    public void SfxShiftUp() => Play(700, 1100, 0.07f, 0.25f, true);
    public void SfxShiftDown() => Play(1000, 600, 0.07f, 0.25f, true);
    public void SfxUi() => Play(1200, 1500, 0.05f, 0.18f);
    public void SfxForkSelect() { Play(660, 660, 0.09f, 0.3f, true); Play(990, 990, 0.12f, 0.3f, true, 0.09f); }
    public void SfxCountdown(bool final) => Play(final ? 1320 : 880, final ? 1320 : 880, final ? 0.35f : 0.12f, 0.35f);
    public void SfxGel() { Play(1046, 1046, 0.12f, 0.3f); Play(1318, 1318, 0.12f, 0.3f, false, 0.07f); Play(1568, 1568, 0.25f, 0.3f, false, 0.14f); }
    public void SfxSector() { Play(220, 440, 0.5f, 0.25f, true); Play(330, 660, 0.5f, 0.2f, true, 0.12f); }

    public void SfxSlipstream(bool on)
    {
        if (on) { Play(180, 1400, 0.7f, 0.22f, true); Play(360, 2200, 0.7f, 0.12f, false, 0.05f); }
        else Play(900, 300, 0.4f, 0.15f, true);
    }

    public void SfxKm() => Play(1760, 1760, 0.2f, 0.18f);
}
