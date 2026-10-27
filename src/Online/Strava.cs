using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using HttpClient = System.Net.Http.HttpClient;

namespace VeloGenesis.Online;

/// <summary>
/// Strava OAuth (browser + local loopback redirect) and FIT upload.
/// The rider registers their own API app at strava.com/settings/api (callback domain "localhost")
/// and pastes its client id / secret into Settings. Tokens live in user://strava.json.
/// Methods are awaited from the Godot main thread; its SynchronizationContext brings continuations back there.
/// </summary>
public static class Strava
{
    public class Account
    {
        public string ClientId = "";
        public string ClientSecret = "";
        public string AccessToken = "";
        public string RefreshToken = "";
        public long ExpiresAt;
        public string AthleteName = "";
    }

    public class UploadResult
    {
        public long ActivityId;
        public bool Duplicate;
        public string Error;
        public bool Ok => Error == null && ActivityId > 0;
        public string Url => ActivityId > 0 ? $"https://www.strava.com/activities/{ActivityId}" : null;
    }

    public static Account Current { get; private set; } = new();
    public static bool HasApp => Current.ClientId.Length > 0 && Current.ClientSecret.Length > 0;
    public static bool Connected => HasApp && Current.RefreshToken.Length > 0;
    public static event Action Changed;

    const string AuthUrl = "https://www.strava.com/oauth/authorize";
    const string TokenUrl = "https://www.strava.com/oauth/token";
    const string ApiUrl = "https://www.strava.com/api/v3";
    const string Scope = "activity:write,read";

    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(60) };
    static readonly JsonSerializerOptions JsonOpts = new() { IncludeFields = true, WriteIndented = true };
    static string FilePath => ProjectSettings.GlobalizePath("user://strava.json");
    static CancellationTokenSource _connectCts;

    public static void Load()
    {
        try
        {
            if (File.Exists(FilePath))
                Current = JsonSerializer.Deserialize<Account>(File.ReadAllText(FilePath), JsonOpts) ?? new Account();
        }
        catch (Exception e)
        {
            GD.PrintErr($"Strava account load failed: {e.Message}");
            Current = new Account();
        }
    }

    public static void Save()
    {
        try { File.WriteAllText(FilePath, JsonSerializer.Serialize(Current, JsonOpts)); }
        catch (Exception e) { GD.PrintErr($"Strava account save failed: {e.Message}"); }
        Changed?.Invoke();
    }

    public static void SetApp(string clientId, string clientSecret)
    {
        clientId = clientId.Trim();
        clientSecret = clientSecret.Trim();
        if (clientId == Current.ClientId && clientSecret == Current.ClientSecret) return;
        Current.ClientId = clientId;
        Current.ClientSecret = clientSecret;
        ClearTokens();
    }

    public static void Disconnect()
    {
        var token = Current.AccessToken;
        ClearTokens();
        // Revoke on Strava's side too; best effort.
        if (token.Length > 0)
            _ = Http.PostAsync("https://www.strava.com/oauth/deauthorize",
                new FormUrlEncodedContent(new[] { KeyValuePair("access_token", token) })).ContinueWith(_ => { });
    }

    static void ClearTokens()
    {
        Current.AccessToken = Current.RefreshToken = Current.AthleteName = "";
        Current.ExpiresAt = 0;
        Save();
    }

    public static void CancelConnect() => _connectCts?.Cancel();

    /// <summary>
    /// Manual path for riders without an API app: opens Strava's web uploader, then an Explorer window
    /// with the ride file selected so it can be dragged straight onto the page.
    /// </summary>
    public static void OpenManualUpload(string fitPath)
    {
        OS.ShellOpen("https://www.strava.com/upload/select");
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"/select,\"{Path.GetFullPath(fitPath)}\""));
        }
        catch (Exception e)
        {
            GD.PrintErr($"Couldn't open Explorer: {e.Message}");
            OS.ShellOpen(Path.GetDirectoryName(fitPath));
        }
    }

    /// <summary>
    /// Opens the Strava consent page in the browser and waits for the redirect on a loopback port.
    /// Returns null on success, otherwise a message for the rider.
    /// </summary>
    public static async Task<string> Connect()
    {
        if (!HasApp) return "Enter your Strava app's client ID and secret first.";
        _connectCts?.Cancel();
        var cts = _connectCts = new CancellationTokenSource(TimeSpan.FromMinutes(4));

        // Listen on both loopback stacks: browsers may resolve "localhost" to ::1 or 127.0.0.1.
        // Loopback-only binding avoids a Windows Firewall prompt.
        var v4 = new TcpListener(IPAddress.Loopback, 0);
        TcpListener v6 = null;
        try
        {
            v4.Start();
            int port = ((IPEndPoint)v4.LocalEndpoint).Port;
            try { v6 = new TcpListener(IPAddress.IPv6Loopback, port); v6.Start(); }
            catch { v6 = null; }

            string state = Guid.NewGuid().ToString("N");
            string redirect = $"http://localhost:{port}/strava";
            string url = $"{AuthUrl}?client_id={Uri.EscapeDataString(Current.ClientId)}&response_type=code" +
                         $"&redirect_uri={Uri.EscapeDataString(redirect)}&approval_prompt=auto" +
                         $"&scope={Uri.EscapeDataString(Scope)}&state={state}";
            OS.ShellOpen(url);

            var accept4 = v4.AcceptTcpClientAsync(cts.Token).AsTask();
            var accept6 = v6?.AcceptTcpClientAsync(cts.Token).AsTask();
            while (true)
            {
                var done = accept6 == null ? accept4 : await Task.WhenAny(accept4, accept6);
                using var client = await done;
                if (done == accept4) accept4 = v4.AcceptTcpClientAsync(cts.Token).AsTask();
                else accept6 = v6.AcceptTcpClientAsync(cts.Token).AsTask();
                var query = await ReadRequestQuery(client, cts.Token);
                if (query == null) continue; // favicon or junk request

                string error = null;
                if (query.Get("state") != state) error = "Login response didn't match. Try again.";
                else if (query.Get("error") != null) error = "Strava access was denied.";
                else if (!(query.Get("scope") ?? "").Contains("activity:write"))
                    error = "Please leave \"Upload your activities\" ticked on the Strava page.";

                if (error == null)
                {
                    (error, _) = await RequestToken(new[]
                    {
                        KeyValuePair("code", query.Get("code") ?? ""),
                        KeyValuePair("grant_type", "authorization_code"),
                    });
                }

                await Respond(client, error == null
                    ? "<h1 style='color:#1ef0ff'>VeloGenesis is connected to Strava</h1><p>You can close this tab and return to the game.</p>"
                    : $"<h1 style='color:#ff2ed1'>Strava connection failed</h1><p>{WebUtility.HtmlEncode(error)}</p>");
                return error;
            }
        }
        catch (OperationCanceledException)
        {
            return "Connection cancelled or timed out.";
        }
        catch (Exception e)
        {
            GD.PrintErr($"Strava connect failed: {e}");
            return $"Connection failed: {e.Message}";
        }
        finally
        {
            v4.Stop();
            v6?.Stop();
            if (_connectCts == cts) _connectCts = null;
            cts.Dispose();
        }
    }

    /// <summary>Reads one HTTP request line; returns its query string if it's the OAuth callback.</summary>
    static async Task<System.Collections.Specialized.NameValueCollection> ReadRequestQuery(TcpClient client, CancellationToken ct)
    {
        var stream = client.GetStream();
        var buf = new byte[8192];
        var sb = new StringBuilder();
        while (!sb.ToString().Contains("\r\n\r\n") && sb.Length < 16384)
        {
            int n = await stream.ReadAsync(buf, ct);
            if (n == 0) break;
            sb.Append(Encoding.ASCII.GetString(buf, 0, n));
        }
        var line = sb.ToString().Split("\r\n")[0].Split(' ');
        if (line.Length < 2 || !line[1].StartsWith("/strava"))
        {
            await Respond(client, "", 404);
            return null;
        }
        int q = line[1].IndexOf('?');
        return System.Web.HttpUtility.ParseQueryString(q >= 0 ? line[1][(q + 1)..] : "");
    }

    static async Task Respond(TcpClient client, string body, int status = 200)
    {
        var html = $"<!doctype html><html><head><meta charset='utf-8'><title>VeloGenesis</title></head>" +
                   $"<body style='background:#06021a;color:#e8f0ff;font-family:Segoe UI,sans-serif;text-align:center;padding-top:15vh'>{body}</body></html>";
        var bytes = Encoding.UTF8.GetBytes(html);
        var head = Encoding.ASCII.GetBytes($"HTTP/1.1 {status} {(status == 200 ? "OK" : "Not Found")}\r\n" +
                                           $"Content-Type: text/html; charset=utf-8\r\nContent-Length: {bytes.Length}\r\nConnection: close\r\n\r\n");
        var s = client.GetStream();
        await s.WriteAsync(head);
        await s.WriteAsync(bytes);
    }

    static System.Collections.Generic.KeyValuePair<string, string> KeyValuePair(string k, string v) => new(k, v);

    /// <summary>
    /// Exchanges a code or refresh token. Error is null on success; Rejected means Strava refused the
    /// credentials (as opposed to a network or server hiccup).
    /// </summary>
    static async Task<(string Error, bool Rejected)> RequestToken(System.Collections.Generic.KeyValuePair<string, string>[] grant)
    {
        var form = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string, string>>(grant)
        {
            KeyValuePair("client_id", Current.ClientId),
            KeyValuePair("client_secret", Current.ClientSecret),
        };
        using var resp = await Http.PostAsync(TokenUrl, new FormUrlEncodedContent(form));
        var text = await resp.Content.ReadAsStringAsync();
        if (!resp.IsSuccessStatusCode)
        {
            GD.PrintErr($"Strava token request failed ({(int)resp.StatusCode}): {text}");
            return resp.StatusCode == HttpStatusCode.Unauthorized || resp.StatusCode == HttpStatusCode.BadRequest
                ? ("Strava rejected the login. Check the client ID and secret.", true)
                : ($"Strava token request failed ({(int)resp.StatusCode}).", false);
        }
        var j = JsonNode.Parse(text)!;
        Current.AccessToken = (string)j["access_token"] ?? "";
        Current.RefreshToken = (string)j["refresh_token"] ?? Current.RefreshToken;
        Current.ExpiresAt = (long?)j["expires_at"] ?? 0;
        if (j["athlete"] is JsonObject a)
            Current.AthleteName = $"{(string)a["firstname"]} {(string)a["lastname"]}".Trim();
        Save();
        return (null, false);
    }

    /// <summary>Returns a valid access token (refreshing if it expires within a minute), or an error.</summary>
    static async Task<(string Token, string Error)> AccessToken()
    {
        if (!Connected) return (null, "Not connected to Strava. Connect in Settings.");
        if (Current.AccessToken.Length > 0 && Current.ExpiresAt > DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 60)
            return (Current.AccessToken, null);
        var (err, rejected) = await RequestToken(new[]
        {
            KeyValuePair("refresh_token", Current.RefreshToken),
            KeyValuePair("grant_type", "refresh_token"),
        });
        if (err == null) return (Current.AccessToken, null);
        if (!rejected) return (null, err);
        ClearTokens(); // access was revoked on strava.com: force a reconnect
        return (null, "Strava access expired or was revoked. Reconnect in Settings.");
    }

    /// <summary>Uploads a FIT file and waits until Strava has processed it into an activity.</summary>
    public static async Task<UploadResult> Upload(string fitPath, string name, string description, Action<string> progress = null)
    {
        try
        {
            progress?.Invoke("Connecting…");
            var (token, tokenError) = await AccessToken();
            if (token == null) return new UploadResult { Error = tokenError };

            progress?.Invoke("Uploading…");
            using var form = new MultipartFormDataContent();
            var file = new ByteArrayContent(await File.ReadAllBytesAsync(fitPath));
            file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            form.Add(file, "file", Path.GetFileName(fitPath));
            form.Add(new StringContent("fit"), "data_type");
            form.Add(new StringContent(name), "name");
            form.Add(new StringContent(description), "description");
            form.Add(new StringContent("1"), "trainer");
            form.Add(new StringContent(Path.GetFileNameWithoutExtension(fitPath)), "external_id");

            using var req = new HttpRequestMessage(HttpMethod.Post, $"{ApiUrl}/uploads") { Content = form };
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var resp = await Http.SendAsync(req);
            var j = await ParseUploadResponse(resp);
            if (j.Error != null) return j.Result;

            // Strava processes uploads asynchronously; poll until the activity exists.
            progress?.Invoke("Strava is processing…");
            for (int i = 0; i < 30 && j.Result.ActivityId == 0 && j.Result.Error == null; i++)
            {
                await Task.Delay(i < 5 ? 1500 : 3000);
                using var poll = new HttpRequestMessage(HttpMethod.Get, $"{ApiUrl}/uploads/{j.UploadId}");
                poll.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                using var pr = await Http.SendAsync(poll);
                j = await ParseUploadResponse(pr);
            }
            if (j.Result.ActivityId == 0 && j.Result.Error == null)
                j.Result.Error = "Strava is still processing the ride. Check your Strava feed in a minute.";
            return j.Result;
        }
        catch (Exception e)
        {
            GD.PrintErr($"Strava upload failed: {e}");
            return new UploadResult { Error = $"Upload failed: {e.Message}" };
        }
    }

    static readonly Regex DuplicateRe = new(@"duplicate of.*?/activities/(\d+)", RegexOptions.IgnoreCase);

    static async Task<(UploadResult Result, long UploadId, string Error)> ParseUploadResponse(HttpResponseMessage resp)
    {
        var text = await resp.Content.ReadAsStringAsync();
        if (resp.StatusCode == HttpStatusCode.TooManyRequests)
            return Fail("Strava rate limit reached. Try again in 15 minutes.");
        if (resp.StatusCode == HttpStatusCode.Unauthorized)
            return Fail("Strava refused access. Reconnect in Settings.");
        JsonNode j;
        try { j = JsonNode.Parse(text); }
        catch { j = null; }
        if (!resp.IsSuccessStatusCode || j == null)
        {
            GD.PrintErr($"Strava upload error ({(int)resp.StatusCode}): {text}");
            return Fail($"Strava upload error ({(int)resp.StatusCode}).");
        }

        var r = new UploadResult { ActivityId = (long?)j["activity_id"] ?? 0 };
        long uploadId = (long?)j["id"] ?? 0;
        var error = (string)j["error"];
        if (!string.IsNullOrEmpty(error))
        {
            var dup = DuplicateRe.Match(error);
            if (dup.Success)
            {
                r.Duplicate = true;
                r.ActivityId = long.Parse(dup.Groups[1].Value);
            }
            else
            {
                GD.PrintErr($"Strava rejected upload: {error}");
                r.Error = $"Strava rejected the file: {error}";
            }
        }
        return (r, uploadId, null);

        static (UploadResult, long, string) Fail(string msg) => (new UploadResult { Error = msg }, 0, msg);
    }
}
