using System;
using System.Collections.Concurrent;
using Godot;

namespace VeloGenesis.Core;

/// <summary>Marshals callbacks from Bluetooth / worker threads onto Godot's main thread.</summary>
public static class MainThread
{
    static readonly ConcurrentQueue<Action> Queue = new();

    public static void Post(Action a) => Queue.Enqueue(a);

    public static void Pump()
    {
        while (Queue.TryDequeue(out var a))
        {
            try { a(); }
            catch (Exception e) { GD.PrintErr(e.ToString()); }
        }
    }
}
