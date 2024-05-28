using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using System;
using System.Collections.Generic;
using System.Text;
using System.Globalization;

namespace OpenType.SourceGen
{
    [Generator]
    public class TableReadGenerator : ISourceGenerator
    {
        public void Initialize(GeneratorInitializationContext context)
        {
            context.RegisterForSyntaxNotifications(() => new FontTablesSyntaxReceiver());
        }

        public void Execute(GeneratorExecutionContext context)
        {
            var token = context.CancellationToken;
            token.ThrowIfCancellationRequested();

            if (context.SyntaxReceiver is not FontTablesSyntaxReceiver receiver)
                return;

            foreach (var structDeclaration in receiver.StructDeclarations)
            {
                var structName = structDeclaration.Identifier.Text;
                var members = structDeclaration.Members;

                var readMethod = GenerateReadMethod(structName, members);
                context.AddSource($"{structName}_Reader.g.cs", SourceText.From(readMethod, Encoding.UTF8));
            }

            var getTableMethods = GenerateGetMethod(receiver.StructDeclarations);
            context.AddSource($"OTFont.g.cs", SourceText.From(getTableMethods, Encoding.UTF8));
        }

        private string GenerateReadMethod(string structName, SyntaxList<MemberDeclarationSyntax> members)
        {
            var sb = new StringBuilder();
            var indentation = 0;

            var constructors = false;
            var classOrMethodBlock = true;
            var inBlock = false;    // exclude class / struct / method…
            var assignment = false;

            var write = new Action<string, bool, bool>((string text, bool appendNewLine, bool startAppendIndentation) =>
            {
                if (startAppendIndentation && indentation > 0) { sb.Append((char)0x20, 4 * indentation); }
                sb.Append(text);
                if (appendNewLine) { sb.AppendLine(); }
            });
            var writeSyntax = new Action<SyntaxKind>((SyntaxKind kind) =>
            {
                switch (kind)
                {
                    case SyntaxKind.OpenBraceToken:
                        write("{", true, true);
                        indentation++;
                        break;
                    case SyntaxKind.CloseBraceToken:
                        indentation--;
                        if (classOrMethodBlock)
                        {
                            write("}", true, true);
                        }
                        else
                        {
                            write("};", true, true);
                        }
                        classOrMethodBlock = true;
                        break;
                    case SyntaxKind.OpenParenToken:
                        write("(", false, false);
                        break;
                    case SyntaxKind.CloseParenToken:
                        if (!assignment)
                        {
                            write(")", false, false);
                            assignment = false;
                        }
                        else if (constructors)
                        {
                            write("),", true, false);
                        }
                        else
                        {
                            write(");", true, false);
                        }
                        break;
                }
            });
            var blockStart = new Action(() => { inBlock = true; });
            var blockEnd = new Action(() => { inBlock = false; });

            // TitleCase and remove first _
            var textInfo = new CultureInfo("en-US", false).TextInfo;
            var structNameTitleCase = structName.StartsWith("Table_")
                ? textInfo.ToTitleCase(structName).Remove(5, 1)
                : $"Read{structName}";

            // File Start
            //write("using FontFlat.OpenType.DataTypes;", true, true);
            write("using FontFlat.OpenType.Helper;", true, true);
            write("using FontFlat.OpenType.DataTypes;", true, true);
            sb.AppendLine();
            write("namespace FontFlat.OpenType.FontTables;", true, true);
            sb.AppendLine();
            write("public partial class Read", true, true);
            writeSyntax(SyntaxKind.OpenBraceToken); classOrMethodBlock = true;
            write($"public static {structName} {structNameTitleCase}", false, true);

            // Set method arguments
            write(GetTableReadArguments(structName), true, false);
            writeSyntax(SyntaxKind.OpenBraceToken); classOrMethodBlock = true;
            write($"var tbl = new {structName}()", true, true);
            writeSyntax(SyntaxKind.OpenBraceToken); classOrMethodBlock = false;
            constructors = true; assignment = true; blockStart();

            // loop and parse fields
            foreach (var member in members)
            {
                if (member is FieldDeclarationSyntax field)
                {
                    foreach (var variable in field.Declaration.Variables)
                    {
                        var typeName = field.Declaration.Type.ToString();
                        var identifier = variable.Identifier.Text;
                        var nullableType = typeName.EndsWith("?");

                        if (nullableType)
                        {
                            var cond = GetTableReadConditions(structName, identifier);
                            if (cond != null)
                            {
                                if (inBlock) { writeSyntax(SyntaxKind.CloseBraceToken); blockEnd(); constructors = false; }
                                write(cond, true, true);
                                writeSyntax(SyntaxKind.OpenBraceToken); classOrMethodBlock = false; blockStart();
                            }
                        }

                        var readerMethod = GetReaderMethodName(typeName, nullableType);
                        var readerMethodArgus = GetReaderMethodArguments(structName, identifier, out var needOtherFields);

                        if (needOtherFields && constructors) {
                            writeSyntax(SyntaxKind.CloseBraceToken);
                            constructors = !constructors && constructors; blockEnd();
                        }

                        write(constructors ? $"{identifier} = reader.{readerMethod}" : $"tbl.{identifier} = reader.{readerMethod}", false, true);
                        writeSyntax(SyntaxKind.OpenParenToken);
                        assignment = true;
                        write(readerMethodArgus, false, false);
                        writeSyntax(SyntaxKind.CloseParenToken);
                    }
                }
            }

            writeSyntax(SyntaxKind.CloseBraceToken); blockEnd();
            write("return tbl;", true, true);

            while (indentation > 0) { writeSyntax(SyntaxKind.CloseBraceToken); }
            return sb.ToString();
        }
        private string GetReaderMethodName(string typeName, bool nullable)
        {
            return (nullable ? typeName.Substring(0, typeName.Length - 1) : typeName) switch
            {
                "ushort" => "ReadUInt16",
                "short" => "ReadInt16",
                "uint" => "ReadUInt32",
                "int" => "ReadInt32",
                "LONGDATETIME" => "ReadLongDateTime",
                "Offset16" => "ReadOffset16",
                "Offset24" => "ReadOffset24",
                "Offset32" => "ReadOffset32",
                "Offset32[]" => "ReadOffset32Array",
                "NameRecord" => "ReadNameRecord",
                "NameRecord[]" => "ReadNameRecordArray",
                "LangTagRecord" => "ReadLangTagRecord",
                "LangTagRecord[]" => "ReadLangTagRecordArray",
                "Tag" => "ReadTag",
                "byte[]" => "ReadBytes",
                "Fixed" => "ReadF16Dot16",
                "Version16Dot16" => "ReadF16Dot16",
                "FWORD" => "ReadInt16",
                "UFWORD" => "ReadUInt16",
                "short[]" => "ReadInt16Array",
                "ushort[]" => "ReadUInt16Array",
                "LongHorMetric[]" => "ReadLongHorMetricArray",
                _ => throw new NotSupportedException($"Type '{typeName}' is not supported.")
            };
        }
        private string GetReaderMethodArguments(string structName, string fieldName, out bool needOtherFields)
        {
            var arguments = structName switch
            {
                "CollectionHeader" => fieldName switch
                {
                    "tableDirectoryOffsets" => "(int)tbl.numFonts",
                    _ => string.Empty
                },
                "Table_name" => fieldName switch
                {
                    "nameRecords" => "(int)tbl.count",
                    "langTagRecords" => "(int)tbl.langTagCount",
                    _ => string.Empty
                },
                "Table_OS_2" => fieldName switch
                {
                    "panose" => "10",
                    _ => string.Empty,
                },
                "Table_hhea" => fieldName switch
                {
                    "reserveds" => "4",
                    _ => string.Empty,
                },
                "Table_hmtx" => fieldName switch
                {
                    "hMetrics" => "numberOfHMetrics",
                    "leftSideBearings" => "(int)(numGlyphs - numberOfHMetrics)",
                    _ => string.Empty,
                },
                "Table_post" => fieldName switch
                {
                    "glyphNameIndex" => "(int)tbl.numGlyphs",
                    "offset" => "(int)tbl.numGlyphs",
                    _ => string.Empty,
                },
                _ => string.Empty,
            };
            needOtherFields = arguments != string.Empty && (arguments.IndexOf("tbl.") > -1);
            return arguments;
        }
        private string? GetTableReadConditions(string structName, string fieldName)
        {
            return structName switch
            {
                "CollectionHeader" => fieldName switch
                {
                    "dsigTag" => "if (tbl.majorVersion == 2)",
                    _ => null,
                },
                "Table_name" => fieldName switch
                {
                    "langTagCount" => "if (tbl.version == 1)",
                    _ => null,
                },
                "Table_OS_2" => fieldName switch
                {
                    "sTypoAscender" => "if (length >= 78)",
                    "ulCodePageRange1" => "if (length >= 86 && tbl.version >= 1)",
                    "sxHeight" => "if (length >= 96 && tbl.version >= 2)",
                    "usLowerOpticalPointSize" => "if (length >= 100 && tbl.version >= 5)",
                    _ => null,
                },
                "Table_maxp" => fieldName switch
                {
                    "maxPoints" => "if (tbl.version == Const.ver10)",
                    _ => null,
                },
                "Table_post" => fieldName switch
                {
                    "numGlyphs" => "if (tbl.version == Const.ver20 || tbl.version == Const.ver25)",
                    "glyphNameIndex" => "if (tbl.version == Const.ver20)",
                    "offset" => "if (tbl.version == Const.ver25)",
                    _ => null,
                },
                _ => null,
            };
        }
        private string GetTableReadArguments(string structName)
        {
            return structName switch
            {
                "Table_OS_2" => "(BigEndianBinaryReader reader, uint length)",
                "Table_hmtx" => "(BigEndianBinaryReader reader, ushort numberOfHMetrics, ushort numGlyphs)",
                _ => "(BigEndianBinaryReader reader)"
            };
        }

        private string GenerateGetMethod(List<RecordDeclarationSyntax> structDeclarations)
        {
            var sb = new StringBuilder();
            var indentation = 0;
            var write = new Action<string, bool>((string text, bool newLine) =>
                {
                    if (indentation > 0) { sb.Append((char)0x20, 4 * indentation); }
                    sb.Append(text);
                    if (newLine) { sb.AppendLine(); }
                });

            var textInfo = new CultureInfo("en-US", false).TextInfo;

            write("using FontFlat.OpenType.DataTypes;", true);
            write("using FontFlat.OpenType.Helper;", true);
            write("using FontFlat.OpenType.FontTables;", true);
            sb.AppendLine();
            write("namespace FontFlat.OpenType;", true);
            sb.AppendLine();
            write("public partial class OTFont", true);
            write("{", true); indentation++;

            List<string> tables = [];

            foreach (var structDeclaration in structDeclarations)
            {
                var structName = structDeclaration.Identifier.Text;
                if (!structName.StartsWith("Table_")) { continue; }
                var structNameTitleCase = textInfo.ToTitleCase(structName.Substring(6));
                tables.Add(structNameTitleCase);

                write($"public {structName}? GetTable{structNameTitleCase}() {{", true); indentation++;
                write($"if ({structNameTitleCase} is null) {{ ParseTable{structNameTitleCase}(); }}", true);
                write($"return {structNameTitleCase};", true);
                indentation--; write("}", true);
            }

            var parseTablesOverload = new Action<string?>((string? ovrType) =>
            {
                foreach (var tbl in tables)
                {
                    var tag = tbl switch
                    {
                        "OS_2" => "OS/2",
                        _ => tbl.ToLower(),
                    };

                    switch (ovrType)
                    {
                        case "returnRecord":
                            write($"private void ParseTable{tbl}(out TableRecord record) {{", true); indentation++;
                            break;
                        default:
                            write($"private void ParseTable{tbl}() {{", true); indentation++;
                            break;
                    }

                    switch (tbl)
                    {
                        case "Hmtx":
                            write($"if (Hhea is null) {{ ParseTableHhea(); }}", true);
                            write($"if (Maxp is null) {{ ParseTableMaxp(); }}", true);
                            break;
                    }

                    switch (ovrType)
                    {
                        case "returnRecord":
                            write($"record = GetTableRecord(\"{tag}\"u8);", true);
                            break;
                        default:
                            write($"var record = GetTableRecord(\"{tag}\"u8);", true);
                            break;
                    }
                    write($"reader.BaseStream.Seek((long)record.offset, SeekOrigin.Begin);", true);

                    switch (tbl)
                    {
                        case "OS_2":
                            write($"{tbl} = FontTables.Read.Table{tbl}(reader, record.length);", true);
                            break;
                        case "Hmtx":
                            write($"{tbl} = FontTables.Read.Table{tbl}(reader, ((Table_hhea)Hhea).numberOfHMetrics, ((Table_maxp)Maxp).numGlyphs);", true);
                            break;
                        default:
                            write($"{tbl} = FontTables.Read.Table{tbl}(reader);", true);
                            break;
                    }

                    indentation--; write("}", true);
                }
            });

            sb.AppendLine();
            parseTablesOverload(null);
            sb.AppendLine();
            parseTablesOverload("returnRecord");
            indentation--; write("}", true);

            return sb.ToString();
        }
    }
}
