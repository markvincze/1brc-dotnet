using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using Microsoft.Diagnostics.Tracing.Parsers;
using System.Buffers;
using System.Collections;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Pipelines;
using System.Text;

//var summary = BenchmarkRunner.Run<OneBrcChallenge>();

//await RunAndMeasure(() => new OneBrcChallenge().PrintStatsBaseline(), "BASELINE");
await RunAndMeasure(() => new OneBrcChallenge().PrintStats(), "IMPROVED");



async Task RunAndMeasure(Func<Task> action, string name)
{
    var sw = Stopwatch.StartNew();
    //var summary = BenchmarkRunner.Run<OneBrcChallenge>();
    await action();
    sw.Stop();
    Console.WriteLine("Running {0} finished in {1}", name, sw.Elapsed);
}

[WarmupCount(0)]
[IterationCount(1)]
[InvocationCount(1)]
public class OneBrcChallenge
{
    private const int TakeCount = 50_000_000;
    private const byte Newline = (byte)'\n';
    private const byte SemiColon = (byte)';';

    [Benchmark]
    public async Task PrintStats()
    {
        var sw = Stopwatch.StartNew();
        var stats = new Dictionary<string, int[]>();
        //var cstats = new ConcurrentDictionary<string, int[]>();
        //var stats = new Dictionary<byte[], int[]>(ByteArrayComparer.Default);

        using var fs = new FileStream(@"C:\Workspaces\GitHub\markvincze\1brc-dotnet\src\OneBrc.Console\measurements.txt", FileMode.Open, FileAccess.Read);
        var pr = PipeReader.Create(fs);

        var processedCount = 0;

        var readResult = await pr.ReadAsync();

        while (!readResult.IsCompleted)
        {
            var reader = new SequenceReader<byte>(readResult.Buffer);

            while (reader.TryReadTo(out ReadOnlySpan<byte> line, Newline))
            {
                var semiColon = line.IndexOf(SemiColon);
                var city = Encoding.UTF8.GetString(line[..semiColon]);

                var num = line[semiColon + 1] == '-' ?
                    int.Parse(line[(semiColon + 2)..^2]) * -10 - (line[^1] - 48) :
                    int.Parse(line[(semiColon + 1)..^2]) * 10 + (line[^1] - 48);

                if (stats.TryGetValue(city, out var values))
                //if (stats.TryGetValue(line[..semiColon].ToArray(), out var values))
                {
                    values[0] = Math.Min(values[0], num);
                    values[1] = Math.Max(values[1], num);
                    values[2] = values[2] + 1;
                    values[3] = values[3] + num;
                }
                else
                {
                    stats.Add(city, [num, num, 1, num]);
                    //stats.Add(line[..semiColon].ToArray(), [num, num, 1, num]);
                }

                if (processedCount++ >= TakeCount)
                {
                    break;
                }
            }

            if (processedCount >= TakeCount)
            {
                break;
            }

            pr.AdvanceTo(reader.Position, readResult.Buffer.End);
            readResult = await pr.ReadAsync();
        }

        Console.WriteLine("Elapsed before printing: {0}", sw.Elapsed);

        Console.Write("{");
        var first = true;

        foreach (var kvp in stats.OrderBy(kvp => kvp.Key))
        //foreach (var kvp in stats.OrderBy(kvp => Encoding.UTF8.GetString(kvp.Key)))
        {
            if (!first)
            {
                Console.Write(", ");
            }

            first = false;

            Console.Write("{0}={1}/{2}/{3}", kvp.Key, Math.Round(kvp.Value[0] / 10.0, 1), Math.Round((double)kvp.Value[3] / 10 / kvp.Value[2], 1), Math.Round(kvp.Value[1] / 10.0, 1));
            //Console.Write("{0}={1}/{2}/{3}", Encoding.UTF8.GetString(kvp.Key), Math.Round(kvp.Value[0] / 10.0, 1), Math.Round((double)kvp.Value[3] / 10 / kvp.Value[2], 1), Math.Round(kvp.Value[1] / 10.0, 1));
        }

        Console.WriteLine("}");
        sw.Stop();
    }

    [Benchmark]
    public async Task PrintStatsBaseline()
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

public readonly struct Stats(int Min, int Max, int Count, int Sum)
{}
