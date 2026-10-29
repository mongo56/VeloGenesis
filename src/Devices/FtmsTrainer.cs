using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Godot;
using Windows.Devices.Bluetooth.GenericAttributeProfile;

namespace VeloGenesis.Devices;

/// <summary>
/// Bluetooth FTMS (Fitness Machine Service) smart trainer, e.g. Cycplus R200.
/// Reads Indoor Bike Data and drives ERG (target power) or simulation (grade) via the control point.
/// </summary>
public class FtmsTrainer : BleLink, ITrainer
{
    enum Mode { None, Erg, Sim }

    GattCharacteristic _controlPoint;
    volatile float _power, _cadence, _speed;
    volatile int _hr;

    // Desired control state, written by the game thread and flushed by the sender loop.
    volatile Mode _mode = Mode.None;
    volatile int _ergTarget;
    volatile float _simGrade, _simCrr = 0.004f, _simCw = 0.51f;

    bool _senderRunning;
    volatile bool _controlGranted;
    readonly Stopwatch _clock = Stopwatch.StartNew();

    public FtmsTrainer(ulong address, string name) : base(address, name) { }

    public bool IsSimulated => false;
    public float Power => DataAge > 3 ? 0 : _power;
    public float Cadence => DataAge > 3 ? 0 : _cadence;
    public float TrainerSpeed => DataAge > 3 ? 0 : _speed;
    public int HeartRate => DataAge > 3 ? 0 : _hr;
    public string LastControlResult { get; private set; } = "";

    protected override async Task<bool> SetupAsync()
    {
        var data = await GetCharacteristicAsync(BleUuids.FitnessMachine, BleUuids.IndoorBikeData);
        if (data == null) { GD.PrintErr($"[{Name}] no FTMS Indoor Bike Data characteristic"); return false; }
        if (!await SubscribeAsync(data, false, ParseIndoorBikeData)) return false;

        _controlPoint = await GetCharacteristicAsync(BleUuids.FitnessMachine, BleUuids.FtmsControlPoint);
        if (_controlPoint != null)
        {
            // Spec requires indications to be enabled on the control point before writing to it.
            // A failure here must not drop the link: power/cadence still work, and the sender loop retries control.
            try
            {
                await SubscribeAsync(_controlPoint, true, ParseControlResponse);
                await RequestControlAsync();
            }
            catch (Exception e)
            {
                GD.PrintErr($"[{Name}] control point setup failed: {e.GetType().Name} 0x{e.HResult:X8} {e.Message}");
            }
        }
        else GD.PrintErr($"[{Name}] trainer has no FTMS control point – power data only");

        if (!_senderRunning) _ = SenderLoopAsync();
        return true;
    }

    async Task RequestControlAsync()
    {
        _controlGranted = await WriteAsync(_controlPoint, new byte[] { 0x00 });
        // Start / resume is optional; some trainers (R200) reject or throw on it.
        if (_controlGranted)
        {
            try { await WriteAsync(_controlPoint, new byte[] { 0x07 }); }
            catch (Exception e) { GD.PrintErr($"[{Name}] start/resume rejected: {e.GetType().Name} 0x{e.HResult:X8}"); }
        }
    }

    void ParseControlResponse(byte[] b)
    {
        if (b.Length < 3 || b[0] != 0x80) return;
        LastControlResult = $"op 0x{b[1]:X2} → {(b[2] == 1 ? "ok" : "result " + b[2])}";
        if (b[1] == 0x00) _controlGranted = b[2] == 1;
        else if (b[2] == 5) _controlGranted = false; // "control not permitted" – request again
    }

    void ParseIndoorBikeData(byte[] b)
    {
        if (b.Length < 2) return;
        int flags = b[0] | (b[1] << 8);
        int i = 2;
        // Bit 0 is "More Data": when CLEAR, instantaneous speed is present.
        if ((flags & 0x0001) == 0 && i + 2 <= b.Length) { _speed = U16(b, i) * 0.01f / 3.6f; i += 2; }
        if ((flags & 0x0002) != 0) i += 2; // average speed
        if ((flags & 0x0004) != 0 && i + 2 <= b.Length) { _cadence = U16(b, i) * 0.5f; i += 2; }
        if ((flags & 0x0008) != 0) i += 2; // average cadence
        if ((flags & 0x0010) != 0) i += 3; // total distance (uint24)
        if ((flags & 0x0020) != 0) i += 2; // resistance level
        if ((flags & 0x0040) != 0 && i + 2 <= b.Length) { _power = Math.Max(0, (int)(short)U16(b, i)); i += 2; }
        if ((flags & 0x0080) != 0) i += 2; // average power
        if ((flags & 0x0100) != 0) i += 5; // expended energy
        if ((flags & 0x0200) != 0 && i + 1 <= b.Length) { _hr = b[i]; i += 1; }
        MarkData();
    }

    static int U16(byte[] b, int i) => b[i] | (b[i + 1] << 8);

    public void SetErgTarget(int watts)
    {
        _ergTarget = Math.Clamp(watts, 0, 2000);
        _mode = Mode.Erg;
    }

    public void SetSimulation(float gradePercent, float crr, float cwKgPerM)
    {
        _simGrade = Math.Clamp(gradePercent, -20f, 20f);
        _simCrr = crr;
        _simCw = cwKgPerM;
        _mode = Mode.Sim;
    }

    public void Tick(double dt) { }

    async Task SenderLoopAsync()
    {
        _senderRunning = true;
        int lastErg = -1;
        float lastGrade = float.NaN;
        Mode lastMode = Mode.None;
        double lastSend = -100;
        try
        {
            while (Device != null)
            {
                await Task.Delay(200);
                if (_controlPoint == null || State != ConnState.Connected) continue;
                double now = _clock.Elapsed.TotalSeconds;
                if (!_controlGranted && now - lastSend > 5)
                {
                    lastSend = now;
                    await RequestControlAsync();
                    lastMode = Mode.None;
                    continue;
                }

                var mode = _mode;
                if (mode == Mode.Erg)
                {
                    int target = _ergTarget;
                    if (target != lastErg || lastMode != Mode.Erg || now - lastSend > 15)
                    {
                        var cmd = new byte[] { 0x05, (byte)(target & 0xFF), (byte)((target >> 8) & 0xFF) };
                        if (await WriteAsync(_controlPoint, cmd)) { lastErg = target; lastMode = mode; lastSend = now; }
                    }
                }
                else if (mode == Mode.Sim)
                {
                    float grade = _simGrade;
                    bool changed = float.IsNaN(lastGrade) || Math.Abs(grade - lastGrade) >= 0.1f || lastMode != Mode.Sim;
                    if ((changed && now - lastSend > 0.6) || now - lastSend > 10)
                    {
                        short g = (short)Math.Round(grade * 100f);          // 0.01 %
                        byte crr = (byte)Math.Clamp(Math.Round(_simCrr * 10000f), 0, 255); // 0.0001
                        byte cw = (byte)Math.Clamp(Math.Round(_simCw * 100f), 0, 255);     // 0.01 kg/m
                        var cmd = new byte[] { 0x11, 0, 0, (byte)(g & 0xFF), (byte)((g >> 8) & 0xFF), crr, cw };
                        if (await WriteAsync(_controlPoint, cmd)) { lastGrade = grade; lastMode = mode; lastSend = now; }
                    }
                }
            }
        }
        catch (Exception e) { GD.PrintErr($"[{Name}] control loop stopped: {e.Message}"); }
        finally { _senderRunning = false; }
    }

    public override void Disconnect()
    {
        try
        {
            if (_controlPoint != null && State == ConnState.Connected)
                _ = WriteAsync(_controlPoint, new byte[] { 0x11, 0, 0, 0, 0, 40, 51 }); // leave it flat
        }
        catch { /* best effort */ }
        base.Disconnect();
    }
}
