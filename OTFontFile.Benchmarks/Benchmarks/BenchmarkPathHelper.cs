using System;
using System.IO;
using System.Linq;

namespace OTFontFile.Benchmarks.Benchmarks
{
    internal static class BenchmarkPathHelper
    {
        public static string ResolveSampleFontsPath()
        {
            string repoRoot = FindRepoRoot();
            string candidate = Path.Combine(repoRoot, "OTFontFile.Benchmarks", "BenchmarkResources", "SampleFonts");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            return Path.Combine(AppContext.BaseDirectory, "BenchmarkResources", "SampleFonts");
        }

        public static string ResolvePerformanceTestFontsPath()
        {
            string repoRoot = FindRepoRoot();
            return Path.Combine(repoRoot, "OTFontFile.Performance.Tests", "TestResources", "SampleFonts");
        }

        public static string? FindLargestTtf(string directoryPath)
        {
            if (!Directory.Exists(directoryPath))
            {
                return null;
            }

            return Directory.GetFiles(directoryPath, "*.ttf")
                .OrderByDescending(path => new FileInfo(path).Length)
                .FirstOrDefault();
        }

        public static string? FindLargestTtc(string directoryPath)
        {
            if (!Directory.Exists(directoryPath))
            {
                return null;
            }

            return Directory.GetFiles(directoryPath, "*.ttc")
                .OrderByDescending(path => new FileInfo(path).Length)
                .FirstOrDefault();
        }

        private static string FindRepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "FontFlat.slnx")))
                {
                    return dir.FullName;
                }

                dir = dir.Parent;
            }

            return AppContext.BaseDirectory;
        }
    }
}
