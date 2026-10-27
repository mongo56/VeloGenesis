using System;
using System.Threading.Tasks;
using Godot;
using Windows.Media.Control;
using VeloGenesis.Core;

namespace VeloGenesis.Audio;

/// <summary>
/// Follows and controls whatever is playing in Windows' media controls (SMTC): YouTube Music in
/// Firefox / Chrome / Edge, Spotify, etc. Used for the "External player" music source.
/// WinRT events arrive on worker threads and are marshalled to the main thread.
/// </summary>
public static class ExternalPlayer
{
    public const string LikedMusicUrl = "https://music.youtube.com/playlist?list=LM";
    public const string YouTubeMusicUrl = "https://music.youtube.com/";

    static GlobalSystemMediaTransportControlsSessionManager _manager;
    static GlobalSystemMediaTransportControlsSession _session;
    static Task _starting;

    /// <summary>"Title – Artist" of the current media, or "" when nothing is playing.</summary>
    public static string NowPlaying { get; private set; } = "";
    public static bool IsPlaying { get; private set; }
    public static event Action Changed;

    public static Task Start() => _starting ??= StartAsync();

    static async Task StartAsync()
    {
        try
        {
            _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            _manager.CurrentSessionChanged += (_, _) => MainThread.Post(Attach);
            Attach();
        }
        catch (Exception e)
        {
            GD.PrintErr($"Windows media controls unavailable: {e.Message}");
        }
    }

    static void Attach()
    {
        var s = _manager?.GetCurrentSession();
        if (s == _session) return;
        if (_session != null)
        {
            _session.MediaPropertiesChanged -= OnMedia;
            _session.PlaybackInfoChanged -= OnPlayback;
        }
        _session = s;
        if (_session != null)
        {
            _session.MediaPropertiesChanged += OnMedia;
            _session.PlaybackInfoChanged += OnPlayback;
        }
        _ = Refresh();
    }

    static void OnMedia(GlobalSystemMediaTransportControlsSession s, MediaPropertiesChangedEventArgs e) => MainThread.Post(() => _ = Refresh());
    static void OnPlayback(GlobalSystemMediaTransportControlsSession s, PlaybackInfoChangedEventArgs e) => MainThread.Post(() => _ = Refresh());

    static async Task Refresh()
    {
        string now = "";
        bool playing = false;
        var s = _session;
        if (s != null)
        {
            try
            {
                var p = await s.TryGetMediaPropertiesAsync();
                if (p != null && !string.IsNullOrWhiteSpace(p.Title))
                    now = string.IsNullOrWhiteSpace(p.Artist) ? p.Title : $"{p.Title} – {p.Artist}";
                playing = s.GetPlaybackInfo()?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            }
            catch (Exception e)
            {
                GD.PrintErr($"Media info read failed: {e.Message}");
            }
        }
        if (s != _session || (now == NowPlaying && playing == IsPlaying)) return;
        NowPlaying = now;
        IsPlaying = playing;
        Changed?.Invoke();
    }

    public static void Next() => Run(s => s.TrySkipNextAsync().AsTask());
    public static void Pause() => Run(s => s.TryPauseAsync().AsTask());
    public static void Play() => Run(s => s.TryPlayAsync().AsTask());

    static void Run(Func<GlobalSystemMediaTransportControlsSession, Task<bool>> command)
    {
        var s = _session;
        if (s == null) return;
        command(s).ContinueWith(t =>
        {
            if (t.IsFaulted) GD.PrintErr($"Media control failed: {t.Exception?.GetBaseException().Message}");
        });
    }
}
