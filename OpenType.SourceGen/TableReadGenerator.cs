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
            var write = new Action<string, bool, bool>((string text, bool appendNewLine, bool startIndentation) =>
            {
                if (startIndentation && indentation > 0) { sb.Append((char)0x20, 4 * indentation); }
                sb.Append(text);
                if (appendNewLine) { sb.AppendLine(); }
            });

            var textInfo = new CultureInfo("en-US", false).TextInfo;
            var structNameTitleCase = structName.StartsWith("Table_")
                ? textInfo.ToTitleCase(structName).Remove(5, 1)
                : $"Read{structName}";

            write("using FontFlat.OpenType.DataTypes;", true, true);
            write("using FontFlat.OpenType.Helper;", true, true);
            sb.AppendLine();
            write("namespace FontFlat.OpenType.FontTables;", true, true);
            sb.AppendLine();
            write("public partial class Read", true, true);
            write("{", true, true); indentation++;
            write($"public static {structName} {structNameTitleCase}", false, true);

            var pass2 = false;
            switch (structName)
            {
                case "CollectionHeader":
                case "Table_name":
                case "Table_OS_2":
                    pass2 = true; break;
                default:
                    pass2 = false; break;
            }

            if (pass2)
            {
                switch (structName)
                {
                    case "Table_OS_2":
                        write($"(BigEndianBinaryReader reader, uint length) {{", true, false);
                        break;
                    default:
                        write($"(BigEndianBinaryReader reader) {{", true, false);
                        break;
                }
                indentation++;
                write($"var tbl = new {structName}();", true, true);
            }
            else
            {
                write($"(BigEndianBinaryReader reader) => new {structName}() {{", true, false);
            }

            var newBlockStart = false;
            foreach (var member in members)
            {
                if (member is FieldDeclarationSyntax field)
                {
                    foreach (var variable in field.Declaration.Variables)
                    {
                        var typeName = field.Declaration.Type.ToString();
                        var identifier = variable.Identifier.Text;
                        var nullableType = typeName.EndsWith("?");

                        var conditionPass = 0;
                        switch (structName)
                        {
                            case "Table_OS_2":
                                switch (identifier)
                                {
                                    case "sTypoAscender":
                                        conditionPass = 1;
                                        break;
                                    case "ulCodePageRange1":
                                        conditionPass = 2; newBlockStart = false;
                                        break;
                                    case "sxHeight":
                                        conditionPass = 3; newBlockStart = false;
                                        break;
                                    case "usLowerOpticalPointSize":
                                        conditionPass = 4; newBlockStart = false;
                                        break;
                                    default:
                                        break;
                                }
                                break;
                            default:
                                break;
                        }

                        if (nullableType && !newBlockStart)
                        {
                            newBlockStart = true;
                            if (!pass2)
                            {
                                write("};", true, true);
                            }
                            else { sb.AppendLine(); pass2 = false; }

                            switch (structName)
                            {
                                case "CollectionHeader":
                                    write($"if (tbl.majorVersion == 2) {{", true, true);
                                    break;
                                case "Table_name":
                                    write($"if (tbl.version == 1) {{", true, true);
                                    break;
                                case "Table_OS_2":
                                    switch (conditionPass)
                                    {
                                        case 1:
                                            write($"if (length >= 78) {{", true, true); break;
                                        case 2:
                                            write($"if (length >= 86 && tbl.version >= 1) {{", true, true); break;
                                        case 3:
                                            write($"if (length >= 96 && tbl.version >= 2) {{", true, true); break;
                                        case 4:
                                            write($"if (length >= 100 && tbl.version >= 5) {{", true, true); break;
                                    }
                                    break;
                                default:
                                    break;
                            }
                        }
                        
                        var readerMethod = GetReaderMethodName(typeName, nullableType);
                        if (pass2)
                        {
                            write($"tbl.{identifier} = reader.{readerMethod}(", false, true);

                            if (readerMethod.EndsWith("Array"))
                            {
                                switch (structName)
                                {
                                    case "CollectionHeader":
                                        write("(int)tbl.numFonts", false, false);
                                        break;
                                    case "Table_name":
                                        write("(int)tbl.count", false, false);
                                        break;
                                    default:
                                        break;
                                }
                            }
                            else if (readerMethod == "ReadBytes")
                            {
                                if (structName == "Table_OS_2" && identifier == "panose")
                                {
                                    write("10", false, false);
                                }
                            }
                            write(");", true, false);
                        }
                        else
                        {
                            sb.Append((char)0x20, nullableType ? 12 : 8);

                            if (nullableType)
                            {
                                write($"tbl.", false, false);
                            }
                            write($"{identifier} = reader.{readerMethod}(", false, false);

                            if (readerMethod.EndsWith("Array"))
                            {
                                switch (structName)
                                {
                                    case "Table_name":
                                        write("(int)tbl.langTagCount", false, false);
                                        break;
                                    default:
                                        break;
                                }
                            }
                            write(")", false, false);

                            write(nullableType ? ";" : ",", true, false);
                        }
                    }
                }
            }

            if (newBlockStart)
            {
                write("};", true, true);
                write("return tbl;", true, true);
            }

            indentation--;
            if (pass2 || newBlockStart)
            {
                newBlockStart = false;
                write("}", true, true);
            }
            else
            {
                indentation++;
                write("};", true, true);
            }
            indentation--; write("}", false, true);
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
                "Fixed" => "ReadFixed",
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
                _ => throw new NotSupportedException($"Type '{typeName}' is not supported.")
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

            sb.AppendLine();
            foreach (var tbl in tables)
            {
                string tag;
                switch (tbl)
                {
                    case "OS_2":
                        tag = "OS/2";
                        break;
                    default:
                        tag = tbl.ToLower();
                        break;
                }

                write($"private void ParseTable{tbl}() {{", true); indentation++;
                write($"var record = Records.Where(x => x.tableTag.AsSpan().SequenceEqual(\"{tag}\"u8));", true);
                write($"if (record.Count() != 1) {{ throw new Exception(\"Not have table '{tag}'\"); }}", true);
                write($"reader.BaseStream.Seek((long)record.First().offset, SeekOrigin.Begin);", true);

                switch (tbl)
                {
                    case "OS_2":
                        write($"{tbl} = FontTables.Read.Table{tbl}(reader, record.First().length);", true);
                        break;
                    default:
                        write($"{tbl} = FontTables.Read.Table{tbl}(reader);", true);
                        break;
                }

                indentation--; write("}", true);
            }

            indentation--; write("}", true);

            return sb.ToString();
        }
    }
}
