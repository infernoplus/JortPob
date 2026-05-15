using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace JortPob.Common;

internal static class FliteNative
{
    
    private const string DllName = "flite_wrapper.dll";
    
    // Load library
    static FliteNative()
    {
        string dllPath = Path.GetFullPath(Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "flite_wrapper.dll"));
        if (!File.Exists(dllPath))
            throw new FileNotFoundException($"flite_wrapper.dll not found at: {dllPath}");

        NativeLibrary.Load(dllPath);
    }
    
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void flite_wrapper_init();

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern int flite_wrapper_tts(string text, string voice, string outfile);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void flite_wrapper_cleanup();
}

/// <summary>
/// Wrapper for flite to allow for faster tts and parallelism using lock objects
/// </summary>
public static class FLiteWrapper
{
    private static bool _initialized = false;
    
    private static readonly Lock Lock = new();

    public static void FliteInit()
    {
        lock (Lock)
        {
            if (_initialized) return;
            FliteNative.flite_wrapper_init();
            _initialized = true;
        }
    }

    public static int Synthesize(string text, string voice, string outfile)
    {
        return FliteNative.flite_wrapper_tts(text, voice, outfile);
    }
    
    public static void Cleanup()
    {
        lock (Lock)
        {
            if(!_initialized) return;
            FliteNative.flite_wrapper_cleanup();
            _initialized = false;
        }
    }
}