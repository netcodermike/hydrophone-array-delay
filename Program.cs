using System.Text.Json;

try
{
    if (args.Length != 1) throw new ArgumentException("Pass one JSON input file");
    using var data = JsonDocument.Parse(File.ReadAllText(args[0]));
    Console.WriteLine(JsonSerializer.Serialize(Analyze(data.RootElement)));
    return 0;
}
catch (Exception error) when (error is IOException or JsonException or FormatException
                               or ArgumentException or InvalidOperationException
                               or KeyNotFoundException or OverflowException)
{
    Console.Error.WriteLine(error.Message);
    return 2;
}

static object Analyze(JsonElement data)
{
    double rate = data.GetProperty("sample_rate").GetDouble();
    int maxLag = data.GetProperty("max_lag").GetInt32();
    double[] reference = Read("reference"), comparison = Read("comparison");
    double[] Read(string key)
    {
        double[] values = data.GetProperty(key).EnumerateArray().Select(item=>item.GetDouble()).ToArray();
        if (values.Any(value=>!double.IsFinite(value) || Math.Abs(value) > 1e12))
            throw new ArgumentException("Traces require finite bounded samples");
        return values;
    }
    if (!(rate > 0 && rate <= 1e9) || reference.Length != comparison.Length || maxLag < 0 ||
        maxLag >= reference.Length - 2 || (long)reference.Length * (2L * maxLag + 1) > 20_000_000)
        throw new ArgumentException("Supply equal traces, a positive sampling rate and a feasible lag range");
    double norm = Math.Sqrt(reference.Sum(value=>value*value))*Math.Sqrt(comparison.Sum(value=>value*value));
    if (norm <= 0) throw new ArgumentException("Both traces must contain nonzero energy");
    double best = double.NegativeInfinity;
    int delay = 0;
    for (int lag = -maxLag; lag <= maxLag; lag++)
    {
        int begin = Math.Max(0,-lag), end = Math.Min(reference.Length, reference.Length-lag);
        double product = 0;
        for (int i = begin; i < end; i++) product += reference[i]*comparison[i+lag];
        double score = product/norm;
        if (score > best || (score == best && Math.Abs(lag) < Math.Abs(delay)))
            (best, delay) = (score, lag);
    }
    double ms = delay * 1000.0 / rate;
    if (!double.IsFinite(ms)) throw new ArgumentException("Sample rate is too small");
    return new { lag_samples = delay, lag_ms = Math.Round(ms, 6), correlation = Math.Round(best, 6) };
}
