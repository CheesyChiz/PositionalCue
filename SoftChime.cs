using System.Runtime.InteropServices;
using System.Text;

namespace PositionalCue;

// Small self-generated PCM chime. No external audio assets, processes or packages.
internal sealed class SoftChime : IDisposable
{
    private GCHandle pinned;
    private float currentVolume = -1;
    private bool disposed;

    [DllImport("winmm.dll", EntryPoint = "PlaySoundW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PlaySound(IntPtr sound, IntPtr module, uint flags);

    public void Play(float volume)
    {
        if (disposed || volume <= 0) return;
        volume = Math.Clamp(volume, 0, 0.4f);
        if (!pinned.IsAllocated || Math.Abs(currentVolume - volume) > 0.001f)
        {
            Release();
            pinned = GCHandle.Alloc(CreateWave(volume), GCHandleType.Pinned);
            currentVolume = volume;
        }
        // ASYNC | NODEFAULT | MEMORY. Memory stays pinned until playback stops.
        PlaySound(pinned.AddrOfPinnedObject(), IntPtr.Zero, 0x0001 | 0x0002 | 0x0004);
    }

    private static byte[] CreateWave(float volume)
    {
        const int rate = 22050;
        const int samples = 3969; // 180 ms, soft attack and decay
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.ASCII, true);
        writer.Write(Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(36 + samples * 2);
        writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(rate);
        writer.Write(rate * 2);
        writer.Write((short)2);
        writer.Write((short)16);
        writer.Write(Encoding.ASCII.GetBytes("data"));
        writer.Write(samples * 2);
        for (var i = 0; i < samples; i++)
        {
            var t = (double)i / rate;
            var envelope = Math.Sin(Math.PI * i / (samples - 1));
            envelope = envelope * envelope * Math.Exp(-10 * t);
            var tone = Math.Sin(2 * Math.PI * 740 * t) + 0.18 * Math.Sin(2 * Math.PI * 1110 * t);
            writer.Write((short)(short.MaxValue * volume * envelope * tone / 1.18));
        }
        return stream.ToArray();
    }

    private void Release()
    {
        if (!pinned.IsAllocated) return;
        PlaySound(IntPtr.Zero, IntPtr.Zero, 0);
        pinned.Free();
    }

    public void Dispose()
    {
        if (disposed) return;
        Release();
        disposed = true;
    }
}
