using System;
using System.IO;

namespace VeloGenesis.Core;

/// <summary>User-visible folders under Documents/VeloGenesis.</summary>
public static class Paths
{
    public static string Root => Ensure(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "VeloGenesis"));
    public static string Rides => Ensure(Path.Combine(Root, "Rides"));
    public static string Music => Ensure(Path.Combine(Root, "Music"));
    public static string Screenshots => Ensure(Path.Combine(Root, "Screenshots"));

    static string Ensure(string p)
    {
        Directory.CreateDirectory(p);
        return p;
    }
}
