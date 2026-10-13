using System.Buffers.Binary;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using FontWeave;
using FontWeave.Containers;
using FontWeave.Core;
using FontWeave.Inspection;
using CompiledFont = FontWeave.Operations.CompiledFont;
using HeadMetadataEdit = FontWeave.Tables.HeadMetadataEdit;
using MetadataEditOperation = FontWeave.Operations.MetadataEditOperation;
using MetadataEditOptions = FontWeave.Operations.MetadataEditOptions;
using NameEditPlan = FontWeave.Tables.NameEditPlan;
using NameReplacement = FontWeave.Tables.NameReplacement;
using TypedHeadView = FontWeave.Tables.HeadView;
using TypedUnknownTablePolicy = FontWeave.Operations.UnknownTablePolicy;

namespace FontWeave.Cli;

internal static class Program
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private static readonly UnicodeEncoding Utf16Be = new(true, false, true);

    public static int Main(string[] args)
    {
        if (args.Length == 0 || args is ["--help"])
        {
            Console.WriteLine("FontWeave (experimental)\n" +
                "validate INPUT [--face N] [--table TAG]\n" +
                "inspect INPUT [--face N] [--table TAG] [--show-raw]\n" +
                "edit-rebuild INPUT --record N --text TEXT --output OUTPUT [--head-revision INT32] [--head-modified INT64]\n" +
                "names INPUT [--face N] [--show-raw]\n" +
                "name-replace INPUT --record N --text TEXT --encoding utf16be --output OUTPUT\n" +
                "name-add INPUT --platform N --encoding-id N --language N --name-id N --text TEXT --encoding utf16be --output OUTPUT\n" +
                "name-delete INPUT --record N --output OUTPUT\n" +
                "copy-original INPUT --output OUTPUT (byte copy, no format validation)\n" +
                "Edits also accept --face N, --drop-signature, --unknown-tables fail|drop|preserve.\n" +
                "Legacy name-* commands accept --hex HEX instead of --text/--encoding.\n" +
                "edit-rebuild uses strict UTF-16BE; head revision is raw signed 16.16, modified is OpenType seconds.\n" +
                "TAG is four ASCII characters or 0xXXXXXXXX. Typed commands accept --max-temporary-bytes and --max-work-units.\n" +
                "Selected raw inspection accepts --max-raw-bytes (default 8 MiB); oversized payloads are refused.\n" +
                "All results are JSON. Output must be a new path; inputs are never overwritten.");
            return args.Length == 0 ? 2 : 0;
        }
        try
        {
            long allocated = GC.GetTotalAllocatedBytes(true);
            var watch = Stopwatch.StartNew();
            Dictionary<string, object?> result = Execute(args, out int exitCode);
            result["metrics"] = new
            {
                elapsedMilliseconds = watch.Elapsed.TotalMilliseconds,
                allocatedBytes = GC.GetTotalAllocatedBytes(true) - allocated,
                peakWorkingSetBytes = Process.GetCurrentProcess().PeakWorkingSet64,
                scope = "cold CLI operation before JSON serialization; includes file input and output, not a throughput benchmark"
            };
            Console.WriteLine(JsonSerializer.Serialize(result, Json));
            return exitCode;
        }
        catch (FontDataException error) { return Fail(error.Error.ToString(), error.Message, 3); }
        catch (CliException error) { return Fail(error.Code, error.Message, 2); }
        catch (EncoderFallbackException error) { return Fail("InvalidEncoding", error.Message, 3); }
        catch (IOException error) { return Fail("IoFailure", error.Message, 4); }
        catch (UnauthorizedAccessException error) { return Fail("IoFailure", error.Message, 4); }
        catch (ArgumentException error) { return Fail("Usage", error.Message, 2); }
        catch (Exception error) { return Fail("InternalError", error.ToString(), 5); }
    }

    private static Dictionary<string, object?> Execute(string[] args, out int exitCode)
    {
        exitCode = 0;
        if (args.Length < 2) throw new CliException("Usage", "A command and input path are required.");
        string command = args[0];
        bool edit = command is "name-replace" or "name-add" or "name-delete" or "edit-rebuild";
        bool copy = command == "copy-original";
        if (!edit && !copy && command is not ("inspect" or "names" or "validate"))
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
        if (command is "validate" or "edit-rebuild" || command == "inspect" && options.ContainsKey("table"))
            return ExecuteTyped(command, input, output, faceIndex, options, limits, out exitCode);
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

    private static Dictionary<string, object?> ExecuteTyped(string command, string input,
        string? output, int faceIndex, Dictionary<string, string?> options, FontLimits fileLimits,
        out int exitCode)
    {
        var limits = new BuildLimits
        {
            MaxInputBytes = fileLimits.MaxInputBytes,
            MaxOutputBytes = fileLimits.MaxOutputBytes,
            MaxTemporaryBytes = LongInteger(options, "max-temporary-bytes", 128L * 1024 * 1024),
            MaxWorkUnits = LongInteger(options, "max-work-units", 512L * 1024 * 1024)
        };
        if (limits.MaxTemporaryBytes < 0 || limits.MaxWorkUnits < 0)
            throw new CliException("Usage", "Build budgets must be nonnegative.");
        int rawLimit = Integer(options, "max-raw-bytes", 8 * 1024 * 1024);
        if (rawLimit < 0) throw new CliException("Usage", "--max-raw-bytes must be nonnegative.");
        uint? requestedTag = options.ContainsKey("table") ? ParseTag(Required(options, "table")) : null;
        var result = new Dictionary<string, object?>
        {
            ["operation"] = command, ["input"] = input, ["faceIndex"] = faceIndex,
            ["sourceId"] = null, ["inputLength"] = null, ["requestedTag"] = requestedTag,
            ["limits"] = LimitsReport(limits), ["report"] = null, ["raw"] = null
        };
        if (command == "edit-rebuild")
        {
            result["output"] = output;
            result["outputLength"] = null;
            result["written"] = 0;
            result["outputWritten"] = false;
            result["usage"] = null;
            result["decisions"] = null;
        }

        ReadStatus status = TryReadTypedInput(input, limits.MaxInputBytes, out byte[]? inputBytes,
            out FontDiagnostic diagnostic);
        if (status != ReadStatus.Done) return SetOutcome(result, status, diagnostic, out exitCode);
        result["inputLength"] = inputBytes!.Length;
        status = FontSource.TryCopy(inputBytes, out FontSource? source, out diagnostic, limits.MaxInputBytes);
        if (status != ReadStatus.Done) return SetOutcome(result, status, diagnostic, out exitCode);
        FontSource actualSource = source!;
        result["sourceId"] = actualSource.SourceId;
        status = actualSource.TryGetFace(faceIndex, out FontFaceRef face, out diagnostic);
        if (status != ReadStatus.Done) return SetOutcome(result, status, diagnostic, out exitCode);

        if (command == "edit-rebuild")
        {
            int ordinal = Integer(options, "record");
            string text = Required(options, "text");
            var editOptions = new MetadataEditOptions
            {
                DropDigitalSignature = options.ContainsKey("drop-signature"),
                UnknownTables = (options.GetValueOrDefault("unknown-tables") ?? "fail") switch
                {
                    "fail" => TypedUnknownTablePolicy.Reject,
                    "drop" => TypedUnknownTablePolicy.Drop,
                    "preserve" => TypedUnknownTablePolicy.Preserve,
                    _ => throw new CliException("Usage", "--unknown-tables must be fail, drop or preserve.")
                }
            };
            var replacements = new[] { new NameReplacement(ordinal, text) };
            CompiledFont? compiled;
            if (options.ContainsKey("head-revision") || options.ContainsKey("head-modified"))
            {
                status = ReadHeadEdit(face, options, limits, out HeadMetadataEdit headEdit, out diagnostic);
                if (status != ReadStatus.Done) return SetOutcome(result, status, diagnostic, out exitCode);
                status = MetadataEditOperation.TryPrepare(face, headEdit, replacements, out compiled,
                    out diagnostic, editOptions, limits);
            }
            else
            {
                var plan = new NameEditPlan { Replacements = replacements };
                status = MetadataEditOperation.TryPrepare(face, plan, out compiled,
                    out diagnostic, editOptions, limits);
            }
            if (status != ReadStatus.Done) return SetOutcome(result, status, diagnostic, out exitCode);
            CompiledFont prepared = compiled!;
            byte[] bytes = GC.AllocateUninitializedArray<byte>(prepared.OutputLength);
            status = prepared.TryCopyTo(bytes, out int written, out diagnostic);
            if (status != ReadStatus.Done) return SetOutcome(result, status, diagnostic, out exitCode);
            WriteNewFile(output!, bytes);
            result["outputLength"] = prepared.OutputLength;
            result["written"] = written;
            result["outputWritten"] = true;
            result["usage"] = UsageReport(prepared.Usage);
            result["decisions"] = DecisionReport(prepared.Decisions);
            result["recordOrdinal"] = ordinal;
            result["textEncoding"] = "utf16be";
            return SetOutcome(result, status, diagnostic, out exitCode);
        }

        FontInspectionReport? report;
        status = FontInspection.TryValidate(face, out report, out diagnostic, requestedTag, limits);
        if (report is not null) result["report"] = InspectionReport(report);
        if (command == "inspect" && options.ContainsKey("show-raw") && report is not null)
        {
            ReadStatus rawStatus = ReadRawTable(face, requestedTag!.Value, rawLimit,
                out object raw, out FontDiagnostic rawDiagnostic);
            result["raw"] = raw;
            if (status == ReadStatus.Done && rawStatus != ReadStatus.Done)
            {
                status = rawStatus;
                diagnostic = rawDiagnostic;
            }
        }
        return SetOutcome(result, status, diagnostic, out exitCode);
    }

    private static ReadStatus ReadHeadEdit(FontFaceRef face, Dictionary<string, string?> options,
        BuildLimits limits, out HeadMetadataEdit edit, out FontDiagnostic diagnostic)
    {
        edit = default;
        diagnostic = default;
        int? revision = options.ContainsKey("head-revision") ? Integer(options, "head-revision") : null;
        long? modified = options.ContainsKey("head-modified") ? LongInteger(options, "head-modified") : null;
        if (revision.HasValue && modified.HasValue)
        {
            edit = new(revision.Value, modified.Value);
            return ReadStatus.Done;
        }
        ReadStatus status = face.TryOpenDirectory(out var directory, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = directory.Validate(out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = directory.TryFindUniqueTable(0x68656164, out int ordinal, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = directory.TryGetTableBytes(ordinal, out var bytes, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = TypedHeadView.TryOpen(bytes, out var view, out diagnostic, directory.TableLocation(ordinal));
        if (status != ReadStatus.Done) return status;
        status = view.TryRead(out var model, out diagnostic, limits);
        if (status != ReadStatus.Done) return status;
        edit = new(revision ?? model!.Values.FontRevisionRaw, modified ?? model!.Values.ModifiedSeconds);
        return ReadStatus.Done;
    }

    private static ReadStatus TryReadTypedInput(string input, int maximum, out byte[]? bytes,
        out FontDiagnostic diagnostic)
    {
        bytes = null;
        diagnostic = default;
        using var stream = new FileStream(input, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > maximum)
        {
            diagnostic = new(DiagnosticCode.BudgetExceeded,
                DataLocation.Anonymous().At(DataField.Length, 0) with { Basis = OffsetBasis.File },
                (ulong)stream.Length, (ulong)maximum);
            return ReadStatus.ResourceLimit;
        }
        bytes = GC.AllocateUninitializedArray<byte>((int)stream.Length);
        stream.ReadExactly(bytes);
        return ReadStatus.Done;
    }

    private static ReadStatus ReadRawTable(FontFaceRef face, uint tag, int maximum,
        out object raw, out FontDiagnostic diagnostic)
    {
        int? ordinal = null;
        uint? length = null;
        DataLocation? location = null;
        string? payloadHex = null;
        ReadStatus status = face.TryOpenDirectory(out var directory, out diagnostic);
        if (status == ReadStatus.Done)
        {
            status = directory.TryFindUniqueTable(tag, out int found, out diagnostic);
            if (status == ReadStatus.Done)
            {
                ordinal = found;
                status = directory.TryGetRecord(found, out var record, out diagnostic);
                if (status == ReadStatus.Done)
                {
                    length = record.Length;
                    location = directory.TableLocation(found);
                    status = directory.TryGetTableBytes(found, out var bytes, out diagnostic);
                    if (status == ReadStatus.Done && bytes.Length > maximum)
                    {
                        diagnostic = new(DiagnosticCode.BudgetExceeded,
                            location.Value.At(DataField.Length, 0), (ulong)bytes.Length, (ulong)maximum);
                        status = ReadStatus.ResourceLimit;
                    }
                    if (status == ReadStatus.Done) payloadHex = Convert.ToHexString(bytes);
                }
            }
        }
        raw = new
        {
            tag, tagText = TagText(tag), ordinal, length,
            location = location.HasValue ? LocationReport(location.Value) : null,
            status = (byte)status, statusName = status.ToString(),
            diagnostic = DiagnosticReport(diagnostic), payloadHex, maxRawBytes = maximum
        };
        return status;
    }

    private static Dictionary<string, object?> SetOutcome(Dictionary<string, object?> result,
        ReadStatus status, FontDiagnostic diagnostic, out int exitCode)
    {
        result["status"] = (byte)status;
        result["statusName"] = status.ToString();
        result["diagnostic"] = DiagnosticReport(diagnostic);
        exitCode = status == ReadStatus.Done ? 0 : 1;
        return result;
    }

    private static object DiagnosticReport(FontDiagnostic diagnostic) => new
    {
        code = (ushort)diagnostic.Code, codeName = diagnostic.Code.ToString(),
        location = LocationReport(diagnostic.Location), detail = diagnostic.Detail, expected = diagnostic.Expected
    };

    private static object InspectionReport(FontInspectionReport report) => new
    {
        sourceId = report.SourceId, faceIndex = report.FaceIndex, isCollection = report.IsCollection,
        containerVersion = report.ContainerVersion, declaredTableCount = report.DeclaredTableCount,
        requestedTableTag = report.RequestedTableTag,
        containerValidationStatus = (byte)report.ContainerValidationStatus,
        containerValidationStatusName = report.ContainerValidationStatus.ToString(),
        containerDiagnostic = DiagnosticReport(report.ContainerDiagnostic),
        admissionUsage = UsageReport(report.AdmissionUsage), perTableLimits = LimitsReport(report.PerTableLimits),
        tables = report.Tables.Select(table => new
        {
            ordinal = table.Ordinal, tag = table.Tag, tagText = table.Tag.HasValue ? TagText(table.Tag.Value) : null,
            record = table.Record.HasValue ? new
            {
                ordinal = table.Record.Value.Ordinal, tag = table.Record.Value.Tag,
                checksum = table.Record.Value.Checksum, fileOffset = table.Record.Value.FileOffset,
                length = table.Record.Value.Length
            } : null,
            location = LocationReport(table.Location),
            inspectionStatus = (byte)table.InspectionStatus, inspectionStatusName = table.InspectionStatus.ToString(),
            inspectionDiagnostic = DiagnosticReport(table.InspectionDiagnostic),
            validationStatus = table.ValidationStatus.HasValue ? (byte?)table.ValidationStatus.Value : null,
            validationStatusName = table.ValidationStatus?.ToString(),
            validationDiagnostic = table.ValidationDiagnostic.HasValue
                ? DiagnosticReport(table.ValidationDiagnostic.Value) : null,
            rawVersion = table.RawVersion, profile = table.Profile,
            dependencies = table.Dependencies.Select(dependency => new
            {
                tag = dependency.Tag, tagText = TagText(dependency.Tag), required = dependency.Required,
                status = (byte)dependency.Status, statusName = dependency.Status.ToString(),
                location = LocationReport(dependency.Location), diagnostic = DiagnosticReport(dependency.Diagnostic)
            }).ToArray(),
            metadataOnlyLookupCount = table.MetadataOnlyLookupCount, validationScope = table.ValidationScope
        }).ToArray()
    };

    private static object LocationReport(DataLocation location) => new
    {
        sourceId = location.SourceId, faceIndex = location.FaceIndex, tableTag = location.TableTag,
        recordOrdinal = location.RecordOrdinal, field = (ushort)location.Field,
        fieldName = location.Field.ToString(), basis = (byte)location.Basis,
        basisName = location.Basis.ToString(), offset = location.Offset, tableOrdinal = location.TableOrdinal,
        tableFileOffset = location.TableFileOffset, absoluteOffset = location.AbsoluteOffset
    };

    private static object LimitsReport(BuildLimits limits) => new
    {
        maxInputBytes = limits.MaxInputBytes, maxOutputBytes = limits.MaxOutputBytes,
        maxTemporaryBytes = limits.MaxTemporaryBytes, maxWorkUnits = limits.MaxWorkUnits
    };

    private static object UsageReport(BuildUsage usage) => new
    {
        workUnits = usage.WorkUnits, temporaryBytes = usage.TemporaryBytes, inputBytes = usage.InputBytes,
        copiedTableBytes = usage.CopiedTableBytes, tableCopyCount = usage.TableCopyCount,
        compiledTableBytes = usage.CompiledTableBytes, compiledTableCount = usage.CompiledTableCount,
        outputBytes = usage.OutputBytes
    };

    private static object[] DecisionReport(ReadOnlySpan<FontWeave.Operations.TableDecision> decisions)
    {
        var result = new object[decisions.Length];
        for (int i = 0; i < decisions.Length; i++)
            result[i] = new { tag = decisions[i].Tag, tagText = TagText(decisions[i].Tag),
                action = (int)decisions[i].Action, actionName = decisions[i].Action.ToString() };
        return result;
    }

    private static uint ParseTag(string value)
    {
        if (value.Length == 10 && value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) &&
            uint.TryParse(value.AsSpan(2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out uint tag))
            return tag;
        if (value.Length != 4 || value.Any(character => character > 0x7F))
            throw new CliException("Usage", "--table must be four ASCII characters or 0xXXXXXXXX.");
        return (uint)value[0] << 24 | (uint)value[1] << 16 | (uint)value[2] << 8 | value[3];
    }

    private static string TagText(uint tag) => new(new[] { (char)(tag >> 24), (char)((tag >> 16) & 0xFF),
        (char)((tag >> 8) & 0xFF), (char)(tag & 0xFF) });

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
        else if (command == "validate" || command == "inspect" && options.ContainsKey("table"))
        {
            allowed.UnionWith(["table", "max-temporary-bytes", "max-work-units"]);
            if (command == "inspect") allowed.UnionWith(["show-raw", "max-raw-bytes"]);
        }
        else if (command == "edit-rebuild")
            allowed.UnionWith(["output", "record", "text", "head-revision", "head-modified",
                "drop-signature", "unknown-tables", "max-temporary-bytes", "max-work-units"]);
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

    private static long LongInteger(Dictionary<string, string?> options, string key, long? fallback = null)
    {
        if (!options.ContainsKey(key) && fallback is long value) return value;
        return long.TryParse(Required(options, key), NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsed)
            ? parsed : throw new CliException("Usage", $"--{key} must be an Int64.");
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
