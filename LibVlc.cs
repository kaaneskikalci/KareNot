using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace KareNot;

// Minimal libvlc 3.x bindings, loaded from the installed VLC (no NuGet packages needed).
static class LibVlc
{
    const string Lib = "libvlc";

    public const int StateOpening = 1, StateBuffering = 2, StatePlaying = 3, StatePaused = 4,
        StateStopped = 5, StateEnded = 6, StateError = 7;

    public static string InstallDir { get; private set; }

    public static bool TryInit(out string error)
    {
        error = null;
        var candidates = new List<string>();
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            try
            {
                using var key = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view).OpenSubKey(@"SOFTWARE\VideoLAN\VLC");
                if (key?.GetValue("InstallDir") is string dir) candidates.Add(dir);
            }
            catch { }
        }
        candidates.Add(@"C:\Program Files\VideoLAN\VLC");
        InstallDir = candidates.FirstOrDefault(d => File.Exists(Path.Combine(d, "libvlc.dll")));
        if (InstallDir == null)
        {
            error = "VLC (64 bit) bulunamadı. Uygulama, kurulu VLC'nin oynatıcı motorunu kullanır.";
            return false;
        }

        SetDllDirectory(InstallDir);
        NativeLibrary.SetDllImportResolver(typeof(LibVlc).Assembly, (name, asm, path) =>
            name == Lib ? NativeLibrary.Load(Path.Combine(InstallDir, "libvlc.dll")) : IntPtr.Zero);
        return true;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool SetDllDirectory(string path);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr libvlc_new(int argc, IntPtr[] argv);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern void libvlc_release(IntPtr instance);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr libvlc_errmsg();

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr libvlc_media_new_path(IntPtr instance, [MarshalAs(UnmanagedType.LPUTF8Str)] string path);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern void libvlc_media_release(IntPtr media);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr libvlc_media_new_location(IntPtr instance, [MarshalAs(UnmanagedType.LPUTF8Str)] string mrl);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr libvlc_media_player_new(IntPtr instance);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern void libvlc_media_player_release(IntPtr mp);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern void libvlc_media_player_set_media(IntPtr mp, IntPtr media);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern void libvlc_media_player_set_hwnd(IntPtr mp, IntPtr hwnd);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern int libvlc_media_player_play(IntPtr mp);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern void libvlc_media_player_set_pause(IntPtr mp, int pause);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern void libvlc_media_player_stop(IntPtr mp);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern long libvlc_media_player_get_time(IntPtr mp);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern void libvlc_media_player_set_time(IntPtr mp, long ms);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern long libvlc_media_player_get_length(IntPtr mp);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern int libvlc_media_player_get_state(IntPtr mp);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern int libvlc_media_player_set_rate(IntPtr mp, float rate);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern float libvlc_media_player_get_fps(IntPtr mp);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern void libvlc_media_player_next_frame(IntPtr mp);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern void libvlc_video_set_mouse_input(IntPtr mp, uint on);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern void libvlc_video_set_key_input(IntPtr mp, uint on);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern int libvlc_video_take_snapshot(IntPtr mp, uint num, [MarshalAs(UnmanagedType.LPUTF8Str)] string path, uint width, uint height);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern int libvlc_video_get_size(IntPtr mp, uint num, out uint width, out uint height);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern int libvlc_audio_get_volume(IntPtr mp);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern int libvlc_audio_set_volume(IntPtr mp, int volume);

    public static string LastError()
    {
        var p = libvlc_errmsg();
        return p == IntPtr.Zero ? "" : Marshal.PtrToStringUTF8(p);
    }
}

// One media player bound to a window handle; all calls happen on the UI thread.
sealed class VlcPlayer : IDisposable
{
    IntPtr instance, player;
    long pendingSeekMs = -1;
    bool pendingPause;

    public string FilePath { get; private set; }
    public bool HasMedia => FilePath != null;

    public VlcPlayer(IntPtr hwnd)
    {
        var args = new[] { "--no-video-title-show", "--no-snapshot-preview", "--no-osd", "--quiet" };
        var argv = args.Select(a => Marshal.StringToCoTaskMemUTF8(a)).ToArray();
        try { instance = LibVlc.libvlc_new(argv.Length, argv); }
        finally { foreach (var p in argv) Marshal.FreeCoTaskMem(p); }
        if (instance == IntPtr.Zero) throw new InvalidOperationException("libvlc başlatılamadı: " + LibVlc.LastError());
        player = LibVlc.libvlc_media_player_new(instance);
        // Mouse/keys go to our window (drag-to-select, shortcuts) instead of VLC's video window.
        LibVlc.libvlc_video_set_mouse_input(player, 0);
        LibVlc.libvlc_video_set_key_input(player, 0);
        LibVlc.libvlc_media_player_set_hwnd(player, hwnd);
    }

    public void Open(string path)
    {
        path = Path.GetFullPath(path);
        var media = LibVlc.libvlc_media_new_path(instance, path);
        if (media == IntPtr.Zero) media = LibVlc.libvlc_media_new_location(instance, new Uri(path).AbsoluteUri);
        if (media == IntPtr.Zero) throw new InvalidOperationException("Video açılamadı: " + path);
        LibVlc.libvlc_media_player_set_media(player, media);
        LibVlc.libvlc_media_release(media);
        FilePath = path;
        pendingSeekMs = -1;
        LibVlc.libvlc_media_player_play(player);
    }

    public int State => LibVlc.libvlc_media_player_get_state(player);
    public bool IsPlaying => State == LibVlc.StatePlaying || State == LibVlc.StateOpening || State == LibVlc.StateBuffering;
    bool IsFinished => State == LibVlc.StateEnded || State == LibVlc.StateStopped || State == LibVlc.StateError;

    public long LengthMs => HasMedia ? Math.Max(0, LibVlc.libvlc_media_player_get_length(player)) : 0;

    public long TimeMs
    {
        get
        {
            if (!HasMedia) return 0;
            if (pendingSeekMs >= 0) return pendingSeekMs;
            if (State == LibVlc.StateEnded) return LengthMs;
            return Math.Max(0, LibVlc.libvlc_media_player_get_time(player));
        }
    }

    public void Play()
    {
        if (!HasMedia) return;
        if (IsFinished)
        {
            // Restart from the beginning after the end of the video.
            LibVlc.libvlc_media_player_stop(player);
            LibVlc.libvlc_media_player_play(player);
            return;
        }
        LibVlc.libvlc_media_player_set_pause(player, 0);
    }

    public void Pause()
    {
        if (HasMedia && IsPlaying) LibVlc.libvlc_media_player_set_pause(player, 1);
    }

    public void TogglePause()
    {
        if (IsPlaying) Pause(); else Play();
    }

    public void Seek(long ms)
    {
        if (!HasMedia) return;
        var length = LengthMs;
        if (length > 0) ms = Math.Min(ms, length - 200);
        ms = Math.Max(0, ms);
        if (IsFinished)
        {
            // The player has to run again before it accepts a position; Tick() applies it.
            pendingSeekMs = ms;
            pendingPause = true;
            LibVlc.libvlc_media_player_stop(player);
            LibVlc.libvlc_media_player_play(player);
            return;
        }
        LibVlc.libvlc_media_player_set_time(player, ms);
    }

    // Called by the UI timer.
    public void Tick()
    {
        if (pendingSeekMs >= 0 && State == LibVlc.StatePlaying)
        {
            LibVlc.libvlc_media_player_set_time(player, pendingSeekMs);
            if (pendingPause) LibVlc.libvlc_media_player_set_pause(player, 1);
            pendingSeekMs = -1;
        }
    }

    public void SetRate(float rate) => LibVlc.libvlc_media_player_set_rate(player, rate);

    // Steps by one frame through a seek, so the shown time (and note time) follows the picture.
    public void StepFrame(int direction)
    {
        if (!HasMedia) return;
        Pause();
        var fps = LibVlc.libvlc_media_player_get_fps(player);
        if (fps < 1 || fps > 240) fps = 30;
        Seek(TimeMs + direction * (long)Math.Ceiling(1000 / fps));
    }

    public int Volume
    {
        get => LibVlc.libvlc_audio_get_volume(player);
        set => LibVlc.libvlc_audio_set_volume(player, Math.Clamp(value, 0, 200));
    }

    public bool HasVideo => HasMedia && LibVlc.libvlc_video_get_size(player, 0, out var w, out var h) == 0 && w > 0 && h > 0;

    // Writes the current frame (original resolution) as PNG; waits until the file is on disk.
    public bool TakeSnapshot(string path, out string error)
    {
        error = null;
        if (!HasVideo)
        {
            error = "Görüntü yok (video oynamıyor).";
            return false;
        }
        try { if (File.Exists(path)) File.Delete(path); } catch { }
        if (LibVlc.libvlc_video_take_snapshot(player, 0, path, 0, 0) != 0)
        {
            error = "VLC kare alamadı. " + LibVlc.LastError();
            return false;
        }
        for (int i = 0; i < 100; i++)
        {
            try
            {
                if (File.Exists(path) && new FileInfo(path).Length > 0)
                {
                    using var s = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
                    return true;
                }
            }
            catch (IOException) { }
            Thread.Sleep(30);
        }
        error = "Kare dosyası oluşmadı.";
        return false;
    }

    public void Dispose()
    {
        if (player != IntPtr.Zero)
        {
            LibVlc.libvlc_media_player_stop(player);
            LibVlc.libvlc_media_player_release(player);
            player = IntPtr.Zero;
        }
        if (instance != IntPtr.Zero)
        {
            LibVlc.libvlc_release(instance);
            instance = IntPtr.Zero;
        }
    }
}
