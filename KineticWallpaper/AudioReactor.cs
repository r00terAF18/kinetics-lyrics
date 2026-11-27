using NAudio.Dsp;
using NAudio.Wave;

namespace KineticWallpaper;

public sealed class AudioReactor : IDisposable
{
    private WasapiLoopbackCapture? _capture;
    private readonly Complex[] _fftLeft = new Complex[1024];
    private readonly Complex[] _fftRight = new Complex[1024];
    private int _fftPos;
    private readonly float[] _bins = new float[128];
    private readonly object _lock = new();

    public void Start()
    {
        try
        {
            _capture = new WasapiLoopbackCapture();
            _capture.DataAvailable += OnDataAvailable;
            _capture.StartRecording();
        }
        catch
        {
            // No active audio output device
        }
    }

    public float[] GetFftBinsCopy()
    {
        lock (_lock)
        {
            var copy = new float[128];
            Array.Copy(_bins, copy, 128);
            return copy;
        }
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (e.BytesRecorded == 0 || _capture is null) return;
        int channels = _capture.WaveFormat.Channels;
        int bytesPerSample = _capture.WaveFormat.BitsPerSample / 8;
        int frameBytes = channels * bytesPerSample;

        for (int offset = 0; offset + frameBytes <= e.BytesRecorded; offset += frameBytes)
        {
            float left = 0;
            float right = 0;
            for (int c = 0; c < channels; c++)
            {
                int pos = offset + c * bytesPerSample;
                float sample = bytesPerSample == 4
                    ? BitConverter.ToSingle(e.Buffer, pos)
                    : BitConverter.ToInt16(e.Buffer, pos) / 32768f;
                if (c == 0) left = sample;
                if (c == 1 || channels == 1) right = sample;
            }

            double w = 0.5 * (1.0 - Math.Cos(2.0 * Math.PI * _fftPos / 1023.0));
            _fftLeft[_fftPos].X = (float)(left * w);
            _fftLeft[_fftPos].Y = 0;
            _fftRight[_fftPos].X = (float)(right * w);
            _fftRight[_fftPos].Y = 0;
            _fftPos++;

            if (_fftPos >= 1024)
            {
                _fftPos = 0;
                ComputeBins();
            }
        }
    }

    private void ComputeBins()
    {
        FastFourierTransform.FFT(true, 10, _fftLeft);
        FastFourierTransform.FFT(true, 10, _fftRight);

        lock (_lock)
        {
            // Map 512 positive FFT bins into 64 logarithmically spaced bands per channel (matching Wallpaper Engine)
            for (int b = 0; b < 64; b++)
            {
                double t0 = b / 64.0;
                double t1 = (b + 1) / 64.0;
                int start = Math.Max(1, (int)Math.Floor(Math.Pow(512.0, t0)));
                int end = Math.Max(start + 1, Math.Min(512, (int)Math.Ceiling(Math.Pow(512.0, t1))));

                float sumL = 0, sumR = 0;
                for (int k = start; k < end; k++)
                {
                    sumL += MathF.Sqrt(_fftLeft[k].X * _fftLeft[k].X + _fftLeft[k].Y * _fftLeft[k].Y);
                    sumR += MathF.Sqrt(_fftRight[k].X * _fftRight[k].X + _fftRight[k].Y * _fftRight[k].Y);
                }

                float scale = (1.0f + b * 0.14f) * 3.5f / (end - start);
                _bins[b] = Math.Clamp(sumL * scale, 0f, 1.5f);
                _bins[64 + b] = Math.Clamp(sumR * scale, 0f, 1.5f);
            }
        }
    }

    public void Dispose()
    {
        if (_capture is not null)
        {
            _capture.DataAvailable -= OnDataAvailable;
            _capture.StopRecording();
            _capture.Dispose();
        }
    }
}
