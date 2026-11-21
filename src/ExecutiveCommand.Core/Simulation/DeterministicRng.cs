namespace ExecutiveCommand.Core.Simulation;

/// <summary>
/// SplitMix64 pseudo-random generator. The entire generator state is a single ulong that is
/// persisted inside <see cref="Model.GameState"/>, so a saved game resumes the exact same random
/// sequence. Never use System.Random or Guid.NewGuid() inside the simulation.
/// </summary>
public sealed class DeterministicRng
{
    private ulong _state;

    public DeterministicRng(ulong state) => _state = state;

    public ulong State => _state;

    public ulong NextULong()
    {
        unchecked
        {
            _state += 0x9E3779B97F4A7C15UL;
            ulong z = _state;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }
    }

    /// <summary>Uniform double in [0, 1).</summary>
    public double NextDouble() => (NextULong() >> 11) * (1.0 / (1UL << 53));

    /// <summary>Uniform integer in [minInclusive, maxExclusive).</summary>
    public int NextInt(int minInclusive, int maxExclusive)
    {
        if (maxExclusive <= minInclusive) return minInclusive;
        return minInclusive + (int)(NextULong() % (ulong)(maxExclusive - minInclusive));
    }

    public bool Chance(double probability) => NextDouble() < probability;

    /// <summary>Approximately normal value (Irwin–Hall, 4 samples) with given mean and std-dev.</summary>
    public double NextGaussian(double mean, double stdDev)
    {
        double s = NextDouble() + NextDouble() + NextDouble() + NextDouble();
        return mean + (s - 2.0) * stdDev * 1.7320508;
    }
}
