using System;
using Godot;
using VeloGenesis.Core;
using VeloGenesis.Online;

namespace VeloGenesis.UI;

/// <summary>Strava API app credentials, account connection and upload preferences.</summary>
public partial class StravaScreen : Screen
{
    public event Action Back;
    LineEdit _id, _secret;
    Label _status;
    Button _connect;
    bool _busy;

    protected override void Build()
    {
        Dim = 0.5f;
        var panel = UiTheme.Anchor(UiTheme.Panel(0.9f, 30), 0.5f, 0.5f, -680, 0, 1360, 0);
        AddChild(panel);
        var v = UiTheme.VBox(12);
        panel.AddChild(v);
        v.AddChild(UiTheme.Label("STRAVA", 48, UiTheme.Amber, bold: true));
        var intro = UiTheme.Label(
            "Optional. Without a connection, UPLOAD TO STRAVA on the ride summary opens Strava's web uploader with your ride file " +
            "ready to drag in. A direct connection uploads in one click, but Strava only lets subscribers create the API app it needs.", 20, UiTheme.Text);
        intro.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        v.AddChild(intro);

        v.AddChild(Section("1 · CREATE YOUR STRAVA API APP (one time, Strava subscribers)"));
        var help = UiTheme.Label(
            "Each rider registers their own app. On the Strava API page, create an app with any name, " +
            "category \"Training\", any website, and Authorization Callback Domain set to  localhost . " +
            "Then copy the Client ID and Client Secret below.", 19, UiTheme.Dim);
        help.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        v.AddChild(help);
        v.AddChild(UiTheme.Button("OPEN STRAVA API PAGE", () => OS.ShellOpen("https://www.strava.com/settings/api"), 20));

        var acc = Strava.Current;
        _id = Field(acc.ClientId, "e.g. 123456", false);
        v.AddChild(Row("Client ID", _id));
        _secret = Field(acc.ClientSecret, "40-character secret", true);
        v.AddChild(Row("Client Secret", _secret, "Stored only on this PC."));

        v.AddChild(Section("2 · CONNECT YOUR ACCOUNT"));
        var crow = UiTheme.HBox(16);
        _connect = UiTheme.Button("", OnConnect, 24, 300);
        crow.AddChild(_connect);
        _status = UiTheme.Label("", 21);
        _status.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _status.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        crow.AddChild(_status);
        v.AddChild(crow);

        v.AddChild(Section("3 · UPLOADS"));
        var auto = new CheckButton { Text = "Upload automatically", ButtonPressed = Settings.Current.StravaAutoUpload };
        auto.Toggled += x => Settings.Current.StravaAutoUpload = x;
        v.AddChild(Row("After each ride", auto, "Off = use the UPLOAD TO STRAVA button on the ride summary."));

        v.AddChild(new Control { CustomMinimumSize = new Vector2(0, 8) });
        var back = UiTheme.Button("◀  SAVE & BACK", Cancel, 24, 260);
        v.AddChild(back);
        FirstFocus = Strava.HasApp ? _connect : _id;

        Strava.Changed += Refresh;
        Refresh();
    }

    protected override void Cancel()
    {
        Strava.CancelConnect();
        Strava.SetApp(_id.Text, _secret.Text);
        Settings.Save();
        Back?.Invoke();
    }

    public override void _ExitTree() => Strava.Changed -= Refresh;

    static LineEdit Field(string text, string placeholder, bool secret)
    {
        var e = new LineEdit { Text = text, PlaceholderText = placeholder, Secret = secret, CustomMinimumSize = new Vector2(520, 0) };
        e.AddThemeFontSizeOverride("font_size", 22);
        return e;
    }

    void Refresh()
    {
        if (!IsInstanceValid(_status)) return;
        if (_busy)
        {
            _connect.Text = "CANCEL";
            _status.Text = "Waiting for you to approve access in the browser…";
            _status.AddThemeColorOverride("font_color", UiTheme.Cyan);
        }
        else if (Strava.Connected)
        {
            _connect.Text = "DISCONNECT";
            _status.Text = string.IsNullOrEmpty(Strava.Current.AthleteName) ? "Connected." : $"Connected as {Strava.Current.AthleteName}.";
            _status.AddThemeColorOverride("font_color", UiTheme.Lime);
        }
        else
        {
            _connect.Text = "CONNECT WITH STRAVA";
            _status.Text = "Not connected.";
            _status.AddThemeColorOverride("font_color", UiTheme.Dim);
        }
    }

    async void OnConnect()
    {
        if (_busy) { Strava.CancelConnect(); return; }
        if (Strava.Connected) { Strava.Disconnect(); return; }

        Strava.SetApp(_id.Text, _secret.Text);
        _busy = true;
        Refresh();
        var error = await Strava.Connect();
        _busy = false;
        if (!IsInstanceValid(this)) return;
        Refresh();
        if (error != null)
        {
            _status.Text = error;
            _status.AddThemeColorOverride("font_color", UiTheme.Red);
        }
        else
        {
            // Bring the game back in front of the browser.
            DisplayServer.WindowMoveToForeground();
        }
    }
}
