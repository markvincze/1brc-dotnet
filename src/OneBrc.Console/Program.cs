using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using Microsoft.Diagnostics.Tracing.Parsers;
using System.Buffers;
using System.Collections;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Drawing;
using System.IO.MemoryMappedFiles;
using System.IO.Pipelines;
using System.Runtime.InteropServices;
using System.Text;
using OneBrc.Console;

//using var fsOrig = new FileStream(@"C:\Workspaces\GitHub\markvincze\1brc-dotnet\src\OneBrc.Console\measurements.txt", FileMode.Open, FileAccess.Read);
//using var fsSmall = new FileStream(@"C:\Workspaces\GitHub\markvincze\1brc-dotnet\src\OneBrc.Console\measurements-100m.txt", FileMode.Create, FileAccess.ReadWrite);
//using var sr = new StreamReader(fsOrig);
//using var sw = new StreamWriter(fsSmall);

//for (int i = 0; i < 100_000_000; i++)
//{
//    sw.Write(sr.ReadLine());
//    sw.Write("\n");
//}
//return;

//await RunAndMeasure(() => new OneBrcChallenge().PrintStatsBaseline(), "BASELINE");
RunAndMeasure(() => new OneBrcChallenge().PrintStats(), "IMPROVED");

void RunAndMeasure(Action action, string name)
{
    var sw = Stopwatch.StartNew();
    action();
    sw.Stop();
    Console.WriteLine("Running {0} finished in {1}", name, sw.Elapsed);
}

[WarmupCount(0)]
[IterationCount(1)]
[InvocationCount(1)]
public class OneBrcChallenge
{
    //private const string filePath = @"C:\Workspaces\GitHub\markvincze\1brc-dotnet\src\OneBrc.Console\measurements-5.txt";
    //private const string filePath = @"C:\Workspaces\GitHub\markvincze\1brc-dotnet\src\OneBrc.Console\measurements-996.txt";
    //private const string filePath = @"C:\Workspaces\GitHub\markvincze\1brc-dotnet\src\OneBrc.Console\measurements-1000.txt";
    //private const string filePath = @"C:\Workspaces\GitHub\markvincze\1brc-dotnet\src\OneBrc.Console\measurements-small.txt";
    //private const string filePath = @"C:\Workspaces\GitHub\markvincze\1brc-dotnet\src\OneBrc.Console\measurements-100m.txt";
    private const string filePath = @"C:\Workspaces\GitHub\markvincze\1brc-dotnet\src\OneBrc.Console\measurements.txt";
    private const byte Newline = (byte)'\n';
    private const byte SemiColon = (byte)';';
    private long totalLinesProcessed = 0;

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
        var coreCount = Environment.ProcessorCount;
        //var coreCount = 1;

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

    private void ProcessBatchRandomAccess(long from, long to, Dictionary<string, Stats> stats)
    {
        Console.WriteLine("Starting batch from {0} to {1}", from, to);

        var handle = File.OpenHandle(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, FileOptions.None);
        Span<byte> buffer = stackalloc byte[256 << 10];

        var linesProcessed = new HashSet<string>();

        while (from < to)
        {
            var bytesRead = RandomAccess.Read(handle, buffer, from);
            var remainingBuffer = buffer;

            while (from < to)
            {
                var lineEnd = remainingBuffer.IndexOf(Newline);

                if (lineEnd == -1)
                {
                    break;
                }

                //Interlocked.Increment(ref totalLinesProcessed);

                var line = remainingBuffer[..lineEnd];

                var semiColon = line.IndexOf(SemiColon);
                var city = Encoding.UTF8.GetString(line[..semiColon]);

                var num = line[semiColon + 1] == '-' ?
                    int.Parse(line[(semiColon + 2)..^2]) * -10 - (line[^1] - 48) :
                    int.Parse(line[(semiColon + 1)..^2]) * 10 + (line[^1] - 48);

                if (stats.TryGetValue(city, out var s))
                {
                    stats[city] = new Stats(Math.Min(s.Min, num), Math.Max(s.Max, num), s.Count + 1, s.Sum + num);
                }
                else
                {
                    stats.Add(city, new Stats(num, num, 1, num));
                }

                remainingBuffer = remainingBuffer[(lineEnd + 1)..];
                from += lineEnd + 1;
            }
        }

        Console.WriteLine("Finishing batch from {0} to {1}", from, to);
    }

    [Benchmark]
    public void PrintStats()
    {
        var sw = Stopwatch.StartNew();

        var batchPositions = GetBatchPositions();

        Console.WriteLine("Determining batch positions took: {0}", sw.Elapsed);

        var threads = new Thread[batchPositions.Length - 1];
        var dicts = Array.Create(batchPositions.Length - 1, _ => new Dictionary<string, Stats>());

        for (int i = 0; i < batchPositions.Length - 1; i++)
        {
            long from = batchPositions[i];
            long to = batchPositions[i + 1];
            var dict = dicts[i];


            threads[i] = new Thread(() =>
            {
                //ProcessBatchPipeline(from, to, stats).Wait();
                ProcessBatchRandomAccess(from, to, dict);
            });
            threads[i].Start();
        }

        foreach (var t in threads)
        {
            if (t != null) t.Join();
        }

        var aggregateStats = dicts
            .SelectMany(d => d.Keys)
            .Distinct()
            .ToDictionary(
                city => city,
                city => dicts.Aggregate(
                    new Stats(int.MaxValue, int.MinValue, 0, 0),
                    (acc, dict) =>
                    {
                        if (dict.TryGetValue(city, out var s))
                        {
                            return new Stats(Math.Min(s.Min, acc.Min), Math.Max(s.Max, acc.Max), s.Count + acc.Count, s.Sum + acc.Sum);
                        }
                        else
                        {
                            return acc;
                        }
                    }));

        Console.WriteLine("Elapsed before printing: {0}", sw.Elapsed);

        Console.Write("{");
        var first = true;

        foreach (var kvp in aggregateStats.OrderBy(kvp => kvp.Key))
        {
            if (!first)
            {
                Console.Write(", ");
            }

            first = false;

            Console.Write("{0}={1}/{2}/{3}", kvp.Key, Math.Round(kvp.Value.Min / 10.0, 1), Math.Round((double)kvp.Value.Sum / 10 / kvp.Value.Count, 1), Math.Round(kvp.Value.Max / 10.0, 1));
        }

        Console.WriteLine("}");
        Console.WriteLine("Total lines processed: {0}", totalLinesProcessed);
        sw.Stop();
    }

    //public async Task PrintStatsMM()
    //{
    //    using (var mmf = MemoryMappedFile.CreateFromFile(filePath, FileMode.Open, "measurements"))
    //    {
    //        // Create a random access view, from the 256th megabyte (the offset)
    //        // to the 768th megabyte (the offset plus length).
    //        //using (var accessor = mmf.CreateViewAccessor(offset, length))
    //        using (var accessor = mmf.CreateViewAccessor())
    //        {
    //            accessor.Read
    //            int colorSize = Marshal.SizeOf(typeof(MyColor));
    //            MyColor color;

    //            // Make changes to the view.
    //            for (long i = 0; i < length; i += colorSize)
    //            {
    //                accessor.Read(i, out color);
    //                color.Brighten(10);
    //                accessor.Write(i, ref color);
    //            }
    //        }
    //    }
    //}

    private async Task ProcessBatchPipeline(long from, long to, ConcurrentDictionary<string, Stats> stats)
    {
        Console.WriteLine("Starting batch from {0} to {1}", from, to);

        using var fs = OpenFile();
        fs.Seek(from, SeekOrigin.Begin);

        var pr = PipeReader.Create(fs);
        var bytesRead = from;

        var readResult = await pr.ReadAsync();

        while (!readResult.IsCompleted)
        {
            var reader = new SequenceReader<byte>(readResult.Buffer);

            while (bytesRead < to && reader.TryReadTo(out ReadOnlySpan<byte> line, Newline))
            {
                bytesRead += (line.Length + 1);
                Interlocked.Increment(ref totalLinesProcessed);

                var semiColon = line.IndexOf(SemiColon);
                var city = Encoding.UTF8.GetString(line[..semiColon]);

                var num = line[semiColon + 1] == '-' ?
                    int.Parse(line[(semiColon + 2)..^2]) * -10 - (line[^1] - 48) :
                    int.Parse(line[(semiColon + 1)..^2]) * 10 + (line[^1] - 48);

                stats.AddOrUpdate(city,
                    new Stats(num, num, 1, num),
                    (c, s) => new Stats(Math.Min(s.Min, num), Math.Max(s.Max, num), s.Count + 1, s.Sum + num));
            }

            pr.AdvanceTo(reader.Position, readResult.Buffer.End);
            readResult = await pr.ReadAsync();
        }

        Console.WriteLine("Finishing batch from {0} to {1}", from, to);
    }

    [Benchmark]
    public async Task PrintStatsBaseline()
    {
        var stats = new Dictionary<string, double[]>();

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

