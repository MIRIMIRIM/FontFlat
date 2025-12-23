using BenchmarkDotNet.Running;
using OTFontFile.Benchmarks.Benchmarks;
using System;

namespace OTFontFile.Benchmarks
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("OTFontFile Performance Benchmarks");
            Console.WriteLine("====================================\n");

            if (args.Length == 0)
            {
                Console.WriteLine("Running all benchmarks...\n");
                BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
            }
            else
            {
                var command = args[0].ToLower();

                switch (command)
                {
                    case "file":
                        Console.WriteLine("Running File Loading benchmarks...\n");
                        BenchmarkRunner.Run<FileLoadingBenchmarks>();
                        break;

                    case "checksum":
                        Console.WriteLine("Running Checksum benchmarks...\n");
                        BenchmarkRunner.Run<ChecksumBenchmarks>();
                        break;

                    case "buffer":
                        Console.WriteLine("Running MBOBuffer benchmarks...\n");
                        BenchmarkRunner.Run<MBOBufferBenchmarks>();
                        break;

                    case "all":
                        Console.WriteLine("Running all benchmarks...\n");
                        BenchmarkRunner.Run<FileLoadingBenchmarks>();
                        BenchmarkRunner.Run<ChecksumBenchmarks>();
                        BenchmarkRunner.Run<MBOBufferBenchmarks>();
                        break;

                    default:
                        Console.WriteLine($"Unknown benchmark type: {command}");
                        Console.WriteLine("Available options:");
                        Console.WriteLine("  file     - File loading benchmarks");
                        Console.WriteLine("  checksum - Checksum calculation benchmarks");
                        Console.WriteLine("  buffer   - MBOBuffer operation benchmarks");
                        Console.WriteLine("  all      - Run all benchmarks");
                        break;
                }
            }
        }
    }
}
