using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using Microsoft.Diagnostics.Tracing.Parsers;
using System.Buffers;
using System.Collections;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Pipelines;
using System.Text;

//using var fsOrig = new FileStream(@"C:\Workspaces\GitHub\markvincze\1brc-dotnet\src\OneBrc.Console\measurements.txt", FileMode.Open, FileAccess.Read);
//using var fsSmall = new FileStream(@"C:\Workspaces\GitHub\markvincze\1brc-dotnet\src\OneBrc.Console\measurements-small.txt", FileMode.Create, FileAccess.ReadWrite);
//using var sr = new StreamReader(fsOrig);
//using var sw = new StreamWriter(fsSmall);

//for (int i = 0; i < 50_000_000; i++)
//{
//    sw.WriteLine(sr.ReadLine());
//}


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
    private const string filePath = @"C:\Workspaces\GitHub\markvincze\1brc-dotnet\src\OneBrc.Console\measurements-small.txt";
    private const byte Newline = (byte)'\n';
    private const byte SemiColon = (byte)';';
    private int totalLinesProcessed = 0;

    private FileStream OpenFile() => new FileStream(filePath, FileMode.Open, FileAccess.Read);

    private long FindNextNewlinePos(FileStream fs)
    {
        while (fs.ReadByte() != '\n')
        {
        }

        return fs.Position - 1;
    }

    private long[] GetBatchPositions()
    {
        using var fs = OpenFile();
        //var coreCount = Environment.ProcessorCount;
        var coreCount = 1;

        var batchSize = fs.Length / coreCount;

        var batchPositions = new long[coreCount + 1];
        batchPositions[0] = 0;
        batchPositions[coreCount] = fs.Length;

        for (int i = 1; i < coreCount; i++)
        {
            fs.Seek(batchSize * i, SeekOrigin.Begin);

            batchPositions[i] = FindNextNewlinePos(fs) + 1;
        }

        return batchPositions;
    }

    private void ProcessBatch(long from, long to, ConcurrentDictionary<string, Stats> stats)
    {
        using var fs = OpenFile();
        fs.Seek(from, SeekOrigin.Begin);
        var bytesRead = from;

        using var sr = new StreamReader(fs);

        //while (fs.Position < to)
        while (bytesRead < to)
        {
            var line = sr.ReadLine();
            bytesRead += (line.Length + 2);
            Interlocked.Increment(ref totalLinesProcessed);

            var semiColon = line.IndexOf(';');
            var city = line[..semiColon];

            var num = line[semiColon + 1] == '-' ?
                int.Parse(line[(semiColon + 2)..^2]) * -10 - (line[^1] - 48) :
                int.Parse(line[(semiColon + 1)..^2]) * 10 + (line[^1] - 48);

            stats.AddOrUpdate(city,
                new Stats(num, num, 1, num),
                (c, s) => new Stats(Math.Min(s.Min, num), Math.Max(s.Max, num), s.Count + 1, s.Sum + num));
        }
    }

    [Benchmark]
    public async Task PrintStats()
    {
        //using var f = OpenFile();
        //var s = FindNextNewlinePos(f);
        //foreach ( var l in File.ReadLines(filePath).Take(10))
        //{
        //    Console.WriteLine(l);
        //}
        //return;
        var sw = Stopwatch.StartNew();
        var stats = new ConcurrentDictionary<string, Stats>();

        var batchPositions = GetBatchPositions();

        Console.WriteLine("Determining batch positions took: {0}", sw.Elapsed);

        var tasks = new List<Task>();

        for (int i = 0; i < batchPositions.Length - 1; i++)
        {
            long from = batchPositions[i];
            long to = batchPositions[i + 1];
            //tasks.Add(Task.Run(() => ProcessBatch(from, to, stats)));
            ProcessBatch(from, to, stats);
        }

        await Task.WhenAll(tasks);

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

            //Console.Write("{0}={1}/{2}/{3}", kvp.Key, Math.Round(kvp.Value[0] / 10.0, 1), Math.Round((double)kvp.Value[3] / 10 / kvp.Value[2], 1), Math.Round(kvp.Value[1] / 10.0, 1));
            Console.Write("{0}={1}/{2}/{3}", kvp.Key, Math.Round(kvp.Value.Min / 10.0, 1), Math.Round((double)kvp.Value.Sum / 10 / kvp.Value.Count, 1), Math.Round(kvp.Value.Max / 10.0, 1));
            //Console.Write("{0}={1}/{2}/{3}", Encoding.UTF8.GetString(kvp.Key), Math.Round(kvp.Value[0] / 10.0, 1), Math.Round((double)kvp.Value[3] / 10 / kvp.Value[2], 1), Math.Round(kvp.Value[1] / 10.0, 1));
        }

        Console.WriteLine("}");
        Console.WriteLine("Total lines processed: {0}", totalLinesProcessed);
        sw.Stop();
    }

    [Benchmark]
    public async Task PrintStatsBaseline()
    {
        var stats = new Dictionary<string, double[]>();

        //using var fs = OpenFile();
        //using 

        foreach (var l in File.ReadLines(filePath))
        {
            Interlocked.Increment(ref totalLinesProcessed);
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
        Console.WriteLine("Total lines processed: {0}", totalLinesProcessed);
    }
}

public readonly record struct Stats(int Min, int Max, int Count, int Sum)
{ }
