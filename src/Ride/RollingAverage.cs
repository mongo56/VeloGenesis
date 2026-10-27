using System.Collections.Generic;

namespace VeloGenesis.Ride;

/// <summary>Time-windowed average of a signal sampled at irregular intervals.</summary>
public class RollingAverage
{
    readonly double _window;
    readonly Queue<(double t, float v, float dt)> _samples = new();
    double _sum, _weight, _now;

    public RollingAverage(double windowSeconds) => _window = windowSeconds;

    public void Add(float value, float dt)
    {
        _now += dt;
        _samples.Enqueue((_now, value, dt));
        _sum += value * dt;
        _weight += dt;
        while (_samples.Count > 0 && _samples.Peek().t < _now - _window)
        {
            var s = _samples.Dequeue();
            _sum -= s.v * s.dt;
            _weight -= s.dt;
        }
    }

    public float Value => _weight > 1e-6 ? (float)(_sum / _weight) : 0f;
}
