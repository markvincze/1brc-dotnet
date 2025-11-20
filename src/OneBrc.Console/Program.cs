using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using System.Diagnostics;
using System.IO.Pipelines;

//var summary = BenchmarkRunner.Run<OneBrcChallenge>();

RunAndMeasure(() => new OneBrcChallenge().PrintStatsBaseline(), "BASELINE");
RunAndMeasure(() => new OneBrcChallenge().PrintStats(), "IMPROVED");

void RunAndMeasure(Action action, string name)
{
    var sw = Stopwatch.StartNew();
    //var summary = BenchmarkRunner.Run<OneBrcChallenge>();
    action();
    sw.Stop();
    Console.WriteLine("Running {0} finished in {1}", name, sw.Elapsed);
}

[WarmupCount(0)]
[IterationCount(1)]
[InvocationCount(1)]
public class OneBrcChallenge
{
    private const int TakeCount = 50_000_000;

    [Benchmark]
    public void PrintStats()
    {
        var sw = Stopwatch.StartNew();
        var stats = new Dictionary<string, int[]>();

        //using (var fs = new FileStream(""))
        //var pr = PipeReader.Create()
        foreach (var line in File.ReadLines(@"C:\Workspaces\GitHub\markvincze\1brc-dotnet\src\OneBrc.Console\measurements.txt").Take(TakeCount))
        {
            var semiColon = line.IndexOf(';');
            var city = new string(line.AsSpan(0, semiColon));
            var numSpan = line.AsSpan(semiColon + 1);
            var num = numSpan[0] == '-' ?
                int.Parse(numSpan[1..^2]) * -10 - (numSpan[^1] - 48) :
                int.Parse(numSpan[0..^2]) * 10 + (numSpan[^1] - 48);

            if (stats.TryGetValue(city, out var values))
            {
                values[0] = Math.Min(values[0], num);
                values[1] = Math.Max(values[1], num);
                values[2] = values[2] + 1;
                values[3] = values[3] + num;
            }
            else
            {
                stats.Add(city, [num, num, 1, num]);
            }
        }

        Console.WriteLine("Elapsed before printing: {0}", sw.Elapsed);

        Console.Write("{");
        var first = true;

        foreach (var kvp in stats.OrderBy(kvp => kvp.Key))
        {
            if (!first)
            {
                Console.Write(", ");
            }

            first = false;

            Console.Write("{0}={1}/{2}/{3}", kvp.Key, Math.Round(kvp.Value[0] / 10.0, 1), Math.Round((double)kvp.Value[3] / 10 / kvp.Value[2], 1), Math.Round(kvp.Value[1] / 10.0, 1));
        }

        Console.WriteLine("}");
        sw.Stop();
    }

    [Benchmark]
    public void PrintStatsBaseline()
    {
        var stats = new Dictionary<string, double[]>();

        foreach (var l in File.ReadLines(@"C:\Workspaces\GitHub\markvincze\1brc-dotnet\src\OneBrc.Console\measurements.txt").Take(TakeCount))
        {
            var segments = l.Split(';');
            var num = double.Parse(segments[1]);

            if (stats.TryGetValue(segments[0], out var values))
            {
                stats[segments[0]] = [
                    Math.Min(values[0], num),
                    Math.Max(values[1], num),
                    values[2] + 1,
                    values[3] + num
                ];
            }
            else
            {
                stats.Add(segments[0], [num, num, 1, num]);
            }
        }

        Console.Write("{");
        var first = true;

        foreach (var kvp in stats.OrderBy(kvp => kvp.Key))
        {
            if (!first)
            {
                Console.Write(", ");
            }

            first = false;

            Console.Write("{0}={1}/{2}/{3}", kvp.Key, Math.Round(kvp.Value[0], 1), Math.Round(kvp.Value[3] / kvp.Value[2], 1), Math.Round(kvp.Value[1], 1));
        }

        Console.WriteLine("}");
    }
}
