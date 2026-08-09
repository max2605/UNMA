using System;

namespace UNMA.Audio;

internal static class MechanicalSirenSynth
{
    internal const double DurationSeconds = 4.0;
    internal const double PeakFrequencyHz = 420.0;

    private const double RiseSeconds = 2.0;
    private const double FallSeconds = DurationSeconds - RiseSeconds;
    private const double RiseExponent = 2.2;
    private const double FallExponent = 1.6;
    private const double IntegratedSpeedPerCycle =
        RiseSeconds * RiseExponent / (RiseExponent + 1.0) +
        FallSeconds / (FallExponent + 1.0);
    // 1053 air pulses equal 117 complete turns of a nine-port rotor. Keeping
    // the loop on that boundary makes the tone and its motor layers seamless.
    private const double AcousticCyclesPerLoop = 1053.0;
    private const double LowFrequencyHz =
        (AcousticCyclesPerLoop -
         PeakFrequencyHz * IntegratedSpeedPerCycle) /
        (DurationSeconds - IntegratedSpeedPerCycle);
    private const double TargetPeak = 0.86;
    private const double SaturationDrive = 1.15;
    private const double TwoPi = 2.0 * Math.PI;

    internal static float[] Generate(int sampleRate)
    {
        if (sampleRate <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleRate));
        }

        var sampleCount = checked((int)Math.Round(
            DurationSeconds * sampleRate,
            MidpointRounding.AwayFromZero));
        var shapedSamples = new double[sampleCount];
        var sum = 0.0;

        for (var index = 0; index < sampleCount; index++)
        {
            var time = index / (double)sampleRate;
            var speed = SpeedAt(time);
            var phase = AcousticPhaseAt(time);
            var bodyGain = SmoothStep(0.18, 0.55, speed);

            var main =
                0.56 * Math.Sin(phase) +
                0.25 * Math.Sin(2.0 * phase - 0.10) +
                0.13 * Math.Sin(3.0 * phase - 0.28) +
                0.07 * Math.Sin(4.0 * phase - 0.45) +
                0.04 * Math.Sin(5.0 * phase - 0.65);
            var body = bodyGain *
                (0.09 * Math.Sin(phase / 3.0 + 0.20) +
                 0.05 * Math.Sin(2.0 * phase / 9.0 - 0.40));
            var flutter = 1.0 +
                0.035 * bodyGain * Math.Sin(phase / 9.0);
            var shaped = Math.Tanh(
                SaturationDrive * (main + body) * flutter);

            shapedSamples[index] = shaped;
            sum += shaped;
        }

        var mean = sum / sampleCount;
        var peak = 0.0;
        for (var index = 0; index < shapedSamples.Length; index++)
        {
            shapedSamples[index] -= mean;
            peak = Math.Max(peak, Math.Abs(shapedSamples[index]));
        }

        var samples = new float[sampleCount];
        if (peak <= double.Epsilon)
        {
            return samples;
        }

        var normalization = TargetPeak / peak;
        for (var index = 0; index < shapedSamples.Length; index++)
        {
            var sample = shapedSamples[index] * normalization;
            samples[index] = (float)Math.Max(
                -TargetPeak,
                Math.Min(TargetPeak, sample));
        }
        return samples;
    }

    internal static double FrequencyAt(double time)
    {
        return LowFrequencyHz +
               (PeakFrequencyHz - LowFrequencyHz) * SpeedAt(time);
    }

    private static double SpeedAt(double time)
    {
        var local = time % DurationSeconds;
        if (local < RiseSeconds)
        {
            var progress = local / RiseSeconds;
            return 1.0 - Math.Pow(1.0 - progress, RiseExponent);
        }

        var fallProgress = (local - RiseSeconds) /
                           FallSeconds;
        return Math.Pow(1.0 - fallProgress, FallExponent);
    }

    private static double AcousticPhaseAt(double time)
    {
        var local = time % DurationSeconds;
        var speedIntegral = IntegratedSpeedAt(local);
        var cycles = LowFrequencyHz * local +
                     (PeakFrequencyHz - LowFrequencyHz) * speedIntegral;
        return TwoPi * cycles;
    }

    private static double IntegratedSpeedAt(double local)
    {
        if (local < RiseSeconds)
        {
            var remaining = 1.0 - local / RiseSeconds;
            return local - RiseSeconds / (RiseExponent + 1.0) *
                (1.0 - Math.Pow(remaining, RiseExponent + 1.0));
        }

        var riseIntegral = RiseSeconds * RiseExponent /
                           (RiseExponent + 1.0);
        var fallProgress = (local - RiseSeconds) / FallSeconds;
        var fallIntegral = FallSeconds / (FallExponent + 1.0) *
            (1.0 - Math.Pow(
                1.0 - fallProgress,
                FallExponent + 1.0));
        return riseIntegral + fallIntegral;
    }

    private static double SmoothStep(double lower, double upper, double value)
    {
        var unit = Math.Max(
            0.0,
            Math.Min(1.0, (value - lower) / (upper - lower)));
        return unit * unit * (3.0 - 2.0 * unit);
    }
}
