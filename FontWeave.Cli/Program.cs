using System.Buffers.Binary;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using FontWeave;

namespace FontWeave.Cli;

internal static class Program
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private static readonly UnicodeEncoding Utf16Be = new(true, false, true);

    public static int Main(string[] args)
    {
        if (args.Length == 0 || args is ["--help"])
        {
            Console.WriteLine("FontWeave M1 (experimental)\n" +
                "inspect INPUT [--face N]\nnames INPUT [--face N] [--show-raw]\n" +
                "name-replace INPUT --record N --text TEXT --encoding utf16be --output OUTPUT\n" +
                "name-add INPUT --platform N --encoding-id N --language N --name-id N --text TEXT --encoding utf16be --output OUTPUT\n" +
                "name-delete INPUT --record N --output OUTPUT\n" +
                "copy-original INPUT --output OUTPUT (byte copy, no format validation)\n" +
                "Edits also accept --face N, --drop-signature, --unknown-tables fail|drop|preserve.\n" +
                "Use --hex HEX instead of --text/--encoding for explicit raw payloads.\n" +
                "All results are JSON. Output must be a new path; inputs are never overwritten.");
            return args.Length == 0 ? 2 : 0;
        }
        try
        {
            long allocated = GC.GetTotalAllocatedBytes(true);
            var watch = Stopwatch.StartNew();
            Dictionary<string, object?> result = Execute(args);
            result["metrics"] = new
            {
                elapsedMilliseconds = watch.Elapsed.TotalMilliseconds,
                allocatedBytes = GC.GetTotalAllocatedBytes(true) - allocated,
                peakWorkingSetBytes = Process.GetCurrentProcess().PeakWorkingSet64,
                scope = "cold CLI operation before JSON serialization; includes file input and output, not a throughput benchmark"
            };
            Console.WriteLine(JsonSerializer.Serialize(result, Json));
            return 0;
        }
        catch (FontDataException error) { return Fail(error.Error.ToString(), error.Message, 3); }
        catch (CliException error) { return Fail(error.Code, error.Message, 2); }
        catch (EncoderFallbackException error) { return Fail("InvalidEncoding", error.Message, 3); }
        catch (IOException error) { return Fail("IoFailure", error.Message, 4); }
        catch (UnauthorizedAccessException error) { return Fail("IoFailure", error.Message, 4); }
        catch (ArgumentException error) { return Fail("Usage", error.Message, 2); }
        catch (Exception error) { return Fail("InternalError", error.ToString(), 5); }
    }

    private static Dictionary<string, object?> Execute(string[] args)
    {
        if (args.Length < 2) throw new CliException("Usage", "A command and input path are required.");
        string command = args[0];
        bool edit = command is "name-replace" or "name-add" or "name-delete";
        bool copy = command == "copy-original";
        if (!edit && !copy && command is not ("inspect" or "names"))
            throw new CliException("Usage", $"Unknown command '{command}'.");
        var options = ParseOptions(args.AsSpan(2));
        ValidateOptions(command, options);
        int faceIndex = Integer(options, "face", 0);
        var limits = new FontLimits
        {
            MaxInputBytes = Integer(options, "max-input-bytes", 64 * 1024 * 1024),
            MaxOutputBytes = Integer(options, "max-output-bytes", 64 * 1024 * 1024)
        };
        limits.Validate();
        string input = Path.GetFullPath(args[1]);
        string? output = edit || copy ? Path.GetFullPath(Required(options, "output")) : null;
        if (output is not null) CheckOutputPath(input, output);
        byte[] inputBytes = ReadInput(input, limits.MaxInputBytes);
        if (copy)
        {
            if (inputBytes.Length > limits.MaxOutputBytes)
                throw new FontDataException(FontError.ResourceLimit, "Raw copy exceeds MaxOutputBytes.");
            WriteNewFile(output!, inputBytes);
            return new() { ["operation"] = command, ["output"] = output,
                ["outputLength"] = inputBytes.Length,
                ["diagnostics"] = new[] { "Raw original bytes copied without format or signature validation." } };
        }
        FontFile file = FontFile.Parse(inputBytes, limits);
        FontFace face = file.GetFace(faceIndex);
        if (command == "inspect")
        {
            return new()
            {
                ["container"] = file.IsCollection ? "ttc" : "sfnt",
                ["faceCount"] = file.FaceCount, ["faceIndex"] = faceIndex,
                ["sfntVersion"] = face.SfntVersion,
                ["flavor"] = face.SfntVersion == 0x4F54544F ? "CFF" : "TrueType",
                ["tables"] = face.Tables.Select(TableReport).ToArray(),
                ["head"] = Capture(() => HeadReport(face)),
                ["maxp"] = Capture(() => MaxpReport(face)),
                ["names"] = Capture(() => NameReport(NameTable.Parse(face.GetTable("name").Data, limits)))
            };
        }
        NameTable names = NameTable.Parse(face.GetTable("name").Data, limits);
        if (command == "names")
        {
            var result = NameReport(names);
            result["faceIndex"] = faceIndex;
            result["faceCount"] = file.FaceCount;
            return result;
        }

        byte[] newName;
        int ordinal = command == "name-add" ? -1 : Integer(options, "record");
        switch (command)
        {
            case "name-replace":
                if (ordinal < 0 || ordinal >= names.Records.Count)
                    throw new FontDataException(FontError.InvalidIndex, "name record ordinal is out of range.");
                var record = names.Records[ordinal];
                newName = NameEditor.Replace(names, ordinal, Payload(options, record.PlatformId, record.EncodingId));
                break;
            case "name-add":
                ushort platform = Word(options, "platform");
                ushort encoding = Word(options, "encoding-id");
                newName = NameEditor.Add(names, platform, encoding, Word(options, "language"),
                    Word(options, "name-id"), Payload(options, platform, encoding));
                break;
            default:
                newName = NameEditor.Delete(names, ordinal);
                break;
        }
        string unknown = options.GetValueOrDefault("unknown-tables") ?? "fail";
        var writeOptions = new FontWriteOptions
        {
            DropSignature = options.ContainsKey("drop-signature"),
            UnknownTablePolicy = unknown switch
            {
                "fail" => UnknownTablePolicy.Fail,
                "drop" => UnknownTablePolicy.Drop,
                "preserve" => UnknownTablePolicy.PreserveExplicit,
                _ => throw new CliException("Usage", "--unknown-tables must be fail, drop or preserve.")
            }
        };
        FontWritePlan plan = SfntWriter.Prepare(face, newName, writeOptions);
        WriteNewFile(output!, plan.ToArray());
        return new()
        {
            ["operation"] = command, ["faceIndex"] = faceIndex, ["output"] = output,
            ["outputLength"] = plan.OutputLength, ["nameFormat"] = names.Format,
            ["recordCount"] = names.Records.Count + (command == "name-add" ? 1 : command == "name-delete" ? -1 : 0),
            ["diagnostics"] = plan.Diagnostics, ["preservedTables"] = plan.PreservedTables
        };
    }

    private static Dictionary<string, object?> NameReport(NameTable table)
    {
        // Repeated or overlapping input ranges must not amplify into an unbounded
        // hex/text diagnostic. Reject before constructing strings; never truncate.
        const long maxRenderedPayloadBytes = 8 * 1024 * 1024;
        long total = 0;
        foreach (var record in table.Records) total += record.Payload.Length;
        foreach (var tag in table.LanguageTags) total += tag.Payload.Length;
        if (total > maxRenderedPayloadBytes)
            throw new FontDataException(FontError.ResourceLimit, "Aggregate name payloads exceed the 8 MiB inspection budget.");
        return new()
        {
        ["format"] = table.Format, ["recordCount"] = table.Records.Count,
        ["diagnostics"] = table.Diagnostics,
        ["records"] = table.Records.Select(record => new
        {
            ordinal = record.Ordinal, platformId = record.PlatformId, encodingId = record.EncodingId,
            languageId = record.LanguageId, nameId = record.NameId, length = record.Length,
            payloadHex = Convert.ToHexString(record.Payload), text = record.Text,
            payloadError = record.PayloadError?.ToString(), decodeError = record.DecodeError?.ToString()
        }).ToArray(),
        ["languageTags"] = table.LanguageTags.Select(tag => new
        {
            index = tag.Index, length = tag.Length, payloadHex = Convert.ToHexString(tag.Payload),
            text = tag.Text, payloadError = tag.PayloadError?.ToString(), decodeError = tag.DecodeError?.ToString()
        }).ToArray()
        };
    }

    private static object TableReport(SfntTable table)
    {
        uint checksum = Checksum(table.Data, table.Tag == "head");
        return new { tag = table.Tag, offset = table.Offset, length = table.Length,
            checksum = table.Checksum, calculatedChecksum = checksum, checksumMatches = checksum == table.Checksum };
    }

    private static uint Checksum(ReadOnlySpan<byte> data, bool head)
    {
        uint sum = 0;
        for (int i = 0; i < data.Length; i += 4)
        {
            uint word = 0;
            for (int j = 0; j < 4; j++)
            {
                int at = i + j;
                byte value = at < data.Length && !(head && at >= 8 && at < 12) ? data[at] : (byte)0;
                word = (word << 8) | value;
            }
            sum = unchecked(sum + word);
        }
        return sum;
    }

    private static object HeadReport(FontFace face)
    {
        ReadOnlySpan<byte> bytes = face.GetTable("head").Data;
        if (bytes.Length < 54) throw new FontDataException(FontError.Truncated, "head requires 54 bytes for M1 inspection.");
        return new { unitsPerEm = BinaryPrimitives.ReadUInt16BigEndian(bytes[18..]),
            created = BinaryPrimitives.ReadInt64BigEndian(bytes[20..]),
            modified = BinaryPrimitives.ReadInt64BigEndian(bytes[28..]),
            indexToLocFormat = BinaryPrimitives.ReadInt16BigEndian(bytes[50..]) };
    }

    private static object MaxpReport(FontFace face)
    {
        ReadOnlySpan<byte> bytes = face.GetTable("maxp").Data;
        if (bytes.Length < 6) throw new FontDataException(FontError.Truncated, "maxp requires at least 6 bytes.");
        return new { version = BinaryPrimitives.ReadUInt32BigEndian(bytes),
            numGlyphs = BinaryPrimitives.ReadUInt16BigEndian(bytes[4..]) };
    }

    private static object Capture(Func<object> read)
    {
        try { return read(); }
        catch (FontDataException error) { return new { error = error.Error.ToString(), message = error.Message }; }
    }

    private static byte[] Payload(Dictionary<string, string?> options, ushort platform, ushort encoding)
    {
        if (options.TryGetValue("hex", out string? hex))
        {
            if (options.ContainsKey("text") || options.ContainsKey("encoding"))
                throw new CliException("Usage", "--hex cannot be combined with --text or --encoding.");
            try { return Convert.FromHexString(hex!); }
            catch (FormatException) { throw new CliException("Usage", "--hex must contain an even number of hexadecimal digits."); }
        }
        string text = Required(options, "text");
        if (Required(options, "encoding") != "utf16be")
            throw new CliException("Usage", "The M1 text encoder is utf16be; use --hex for explicit raw payloads.");
        if (!(platform == 0 || platform == 3 && encoding is 0 or 1 or 10))
            throw new FontDataException(FontError.UnsupportedFormat, "Record metadata does not select a supported Unicode encoding; use explicit raw --hex.");
        return Utf16Be.GetBytes(text);
    }

    private static byte[] ReadInput(string path, int maximum)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > maximum)
            throw new FontDataException(FontError.ResourceLimit, "Input exceeds MaxInputBytes before allocation.");
        byte[] bytes = GC.AllocateUninitializedArray<byte>(checked((int)stream.Length));
        stream.ReadExactly(bytes);
        return bytes;
    }

    private static void CheckOutputPath(string input, string output)
    {
        var comparer = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (string.Equals(input, output, comparer))
            throw new CliException("SourceOverwrite", "Output must not be the input path.");
        if (File.Exists(output) || Directory.Exists(output))
            throw new CliException("DestinationExists", "Output already exists; M1 does not overwrite files.");
    }

    private static void WriteNewFile(string output, byte[] bytes)
    {
        string directory = Path.GetDirectoryName(output)!;
        string temporary = Path.Combine(directory, $".fontweave-{Guid.NewGuid():N}.tmp");
        bool created = false;
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                created = true;
                stream.Write(bytes);
                stream.Flush(true);
            }
            File.Move(temporary, output, overwrite: false);
        }
        finally
        {
            if (created && File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static Dictionary<string, string?> ParseOptions(ReadOnlySpan<string> args)
    {
        var result = new Dictionary<string, string?>(StringComparer.Ordinal);
        for (int i = 0; i < args.Length; i++)
        {
            if (!args[i].StartsWith("--", StringComparison.Ordinal))
                throw new CliException("Usage", $"Unexpected argument '{args[i]}'.");
            string name = args[i][2..];
            string? value = null;
            if (name is not ("drop-signature" or "show-raw" or "json"))
            {
                if (++i == args.Length || args[i].StartsWith("--", StringComparison.Ordinal))
                    throw new CliException("Usage", $"--{name} requires a value.");
                value = args[i];
            }
            if (!result.TryAdd(name, value)) throw new CliException("Usage", $"Duplicate option --{name}.");
        }
        return result;
    }

    private static void ValidateOptions(string command, Dictionary<string, string?> options)
    {
        var allowed = new HashSet<string>(StringComparer.Ordinal) { "face", "json", "max-input-bytes", "max-output-bytes" };
        if (command == "copy-original")
        {
            allowed.Remove("face");
            allowed.Add("output");
        }
        else if (command is "inspect" or "names") allowed.Add("show-raw");
        else
        {
            allowed.UnionWith(["output", "drop-signature", "unknown-tables"]);
            if (command != "name-add") allowed.Add("record");
            if (command != "name-delete") allowed.UnionWith(["text", "encoding", "hex"]);
            if (command == "name-add") allowed.UnionWith(["platform", "encoding-id", "language", "name-id"]);
        }
        foreach (string key in options.Keys)
            if (!allowed.Contains(key)) throw new CliException("Usage", $"--{key} is not accepted by {command}.");
    }

    private static string Required(Dictionary<string, string?> options, string key) =>
        options.TryGetValue(key, out string? value) && value is not null ? value
            : throw new CliException("Usage", $"--{key} is required.");

    private static int Integer(Dictionary<string, string?> options, string key, int? fallback = null)
    {
        if (!options.ContainsKey(key) && fallback is int value) return value;
        return int.TryParse(Required(options, key), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
            ? parsed : throw new CliException("Usage", $"--{key} must be an Int32.");
    }

    private static ushort Word(Dictionary<string, string?> options, string key)
    {
        int value = Integer(options, key);
        return value >= 0 && value <= ushort.MaxValue ? (ushort)value
            : throw new CliException("Usage", $"--{key} must be 0..65535.");
    }

    private static int Fail(string error, string message, int code)
    {
        Console.Error.WriteLine(JsonSerializer.Serialize(new { error, message }));
        return code;
    }

    private sealed class CliException(string code, string message) : Exception(message)
    {
        public string Code { get; } = code;
    }
}
