using NAudio.CoreAudioApi;
using NAudio.Dsp;
using NAudio.Wave;

namespace KineticWallpaper;

public sealed class AudioReactor : IDisposable
{
    private WasapiLoopbackCapture? _capture;
    private readonly Complex[] _fftBuffer = new Complex[1024];
    private int _fftPos;

    private float _peakLow = 0.002f;
    private float _peakMid = 0.002f;
    private float _peakHigh = 0.002f;
    private float _peakTotal = 0.002f;
    private float _lowAvg = 0.25f;
    private float _totalSlow = 0.25f;
    private long _lastKickMs;
    private long _lastDropMs;

    public float Low { get; private set; }
    public float Mid { get; private set; }
    public float High { get; private set; }
    public float BeatSmooth { get; private set; }
    public float KickImpulse { get; private set; }
    public float DropImpulse { get; private set; }

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
        }
    }

    public void StepFrame()
    {
        BeatSmooth += (Low - BeatSmooth) * (Low > BeatSmooth ? 0.18f : 0.05f);
        KickImpulse *= 0.84f;
        DropImpulse *= 0.90f;
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (e.BytesRecorded == 0 || _capture is null) return;
        int channels = _capture.WaveFormat.Channels;
        int bytesPerSample = _capture.WaveFormat.BitsPerSample / 8;
        int frameBytes = channels * bytesPerSample;

        for (int offset = 0; offset + frameBytes <= e.BytesRecorded; offset += frameBytes)
        {
            float mono = 0;
            for (int c = 0; c < channels; c++)
            {
                int pos = offset + c * bytesPerSample;
                mono += bytesPerSample == 4
                    ? BitConverter.ToSingle(e.Buffer, pos)
                    : BitConverter.ToInt16(e.Buffer, pos) / 32768f;
            }
            mono /= channels;

            double w = 0.5 * (1.0 - Math.Cos(2.0 * Math.PI * _fftPos / (_fftBuffer.Length - 1)));
            _fftBuffer[_fftPos].X = (float)(mono * w);
            _fftBuffer[_fftPos].Y = 0;
            _fftPos++;

            if (_fftPos >= _fftBuffer.Length)
            {
                _fftPos = 0;
                AnalyzeFft();
            }
        }
    }

    private void AnalyzeFft()
    {
        FastFourierTransform.FFT(true, 10, _fftBuffer);

        float BandEnergy(int startBin, int endBin)
        {
            float sum = 0;
            for (int i = startBin; i < endBin; i++)
                sum += MathF.Sqrt(_fftBuffer[i].X * _fftBuffer[i].X + _fftBuffer[i].Y * _fftBuffer[i].Y);
            return sum / Math.Max(1, endBin - startBin);
        }

        float rl = BandEnergy(1, 6);
        float rm = BandEnergy(7, 45);
        float rh = BandEnergy(46, 180);
        float rt = (rl + rm + rh) / 3f;

        _peakLow = Math.Max(_peakLow * 0.996f, rl);
        _peakMid = Math.Max(_peakMid * 0.996f, rm);
        _peakHigh = Math.Max(_peakHigh * 0.996f, rh);
        _peakTotal = Math.Max(_peakTotal * 0.998f, rt);

        if (_peakTotal < 0.0005f)
        {
            Low = Mid = High = 0;
            return;
        }

        Low = Math.Clamp(rl / _peakLow, 0f, 1f);
        Mid = Math.Clamp(rm / _peakMid, 0f, 1f);
        High = Math.Clamp(rh / _peakHigh, 0f, 1f);
        float tot = Math.Clamp(rt / _peakTotal, 0f, 1f);

        long now = Environment.TickCount64;
        if (Low > _lowAvg * 1.32f + 0.08f && Low > 0.48f && now - _lastKickMs > 170)
        {
            KickImpulse = Math.Clamp((Low - _lowAvg) * 1.9f, 0.25f, 1f);
            _lastKickMs = now;
        }

        if (tot > _totalSlow * 1.85f && _totalSlow < 0.42f && tot > 0.72f && now - _lastDropMs > 4000)
        {
            DropImpulse = 1f;
            _lastDropMs = now;
        }

        _lowAvg = _lowAvg * 0.90f + Low * 0.10f;
        _totalSlow = _totalSlow * 0.985f + tot * 0.015f;
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
