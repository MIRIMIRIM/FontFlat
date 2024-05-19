using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using System.Globalization;
using System.Drawing;
using System.IO;
using System.Reflection.PortableExecutable;

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
            var textInfo = new CultureInfo("en-US", false).TextInfo;
            var structNameTitleCase = structName.StartsWith("Table_")
                ? textInfo.ToTitleCase(structName).Remove(5, 1)
                : $"Read{structName}";
            var sb = new StringBuilder();
            sb.AppendLine("using FontFlat.OpenType.DataTypes;");
            sb.AppendLine("using FontFlat.OpenType.Helper;");
            sb.AppendLine();
            sb.AppendLine("namespace FontFlat.OpenType.FontTables;");
            sb.AppendLine();
            sb.AppendLine("public partial class Read");
            sb.AppendLine("{");
            sb.Append((char)0x20, 4);
            sb.Append($"public static {structName} {structNameTitleCase}");

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
                        sb.AppendLine($"(BigEndianBinaryReader reader, uint length) {{");
                        break;
                    default:
                        sb.AppendLine($"(BigEndianBinaryReader reader) {{");
                        break;
                }
                sb.Append((char)0x20, 8);
                sb.AppendLine($"var tbl = new {structName}();");
            }
            else
            {
                sb.AppendLine($"(BigEndianBinaryReader reader) => new {structName}() {{");
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
                                sb.Append((char)0x20, 8); sb.AppendLine("};");
                            }
                            else { sb.AppendLine(); pass2 = false; }
                            
                            sb.Append((char)0x20, 8);

                            switch (structName)
                            {
                                case "CollectionHeader":
                                    sb.AppendLine($"if (tbl.majorVersion == 2) {{");
                                    break;
                                case "Table_name":
                                    sb.AppendLine($"if (tbl.version == 1) {{");
                                    break;
                                case "Table_OS_2":
                                    switch (conditionPass)
                                    {
                                        case 1:
                                            sb.AppendLine($"if (length >= 78) {{"); break;
                                        case 2:
                                            sb.AppendLine($"if (length >= 86 && tbl.version >= 1) {{"); break;
                                        case 3:
                                            sb.AppendLine($"if (length >= 96 && tbl.version >= 2) {{"); break;
                                        case 4:
                                            sb.AppendLine($"if (length >= 100 && tbl.version >= 5) {{"); break;
                                    }
                                    break;
                                default:
                                    break;
                            }
                        }
                        
                        var readerMethod = GetReaderMethodName(typeName, nullableType);
                        if (pass2)
                        {
                            sb.Append((char)0x20, 8);
                            sb.Append($"tbl.{identifier} = reader.{readerMethod}(");

                            if (readerMethod.EndsWith("Array"))
                            {
                                switch (structName)
                                {
                                    case "CollectionHeader":
                                        sb.Append("(int)tbl.numFonts");
                                        break;
                                    case "Table_name":
                                        sb.Append("(int)tbl.count");
                                        break;
                                    default:
                                        break;
                                }
                            }
                            else if (readerMethod == "ReadBytes")
                            {
                                if (structName == "Table_OS_2" && identifier == "panose")
                                {
                                    sb.Append("10");
                                }
                            }
                            sb.AppendLine(");");
                        }
                        else
                        {
                            sb.Append((char)0x20, nullableType ? 12 : 8);

                            if (nullableType)
                            {
                                sb.Append($"tbl.");
                            }
                            sb.Append($"{identifier} = reader.{readerMethod}(");

                            if (readerMethod.EndsWith("Array"))
                            {
                                switch (structName)
                                {
                                    case "Table_name":
                                        sb.Append("(int)tbl.langTagCount");
                                        break;
                                    default:
                                        break;
                                }
                            }
                            sb.Append(")");

                            sb.AppendLine(nullableType ? ";" : ",");
                        }
                    }
                }
            }

            if (newBlockStart)
            {
                sb.Append((char)0x20, 8); sb.AppendLine("};");
                sb.Append((char)0x20, 8); sb.AppendLine("return tbl;");
            }

            sb.Append((char)0x20, 4);
            if (pass2 || newBlockStart)
            {
                newBlockStart = false;
                sb.AppendLine("}");
            }
            else
            {
                sb.AppendLine("};");
            }
            sb.Append("}");
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
            var textInfo = new CultureInfo("en-US", false).TextInfo;
            var sb = new StringBuilder(
                $"using FontFlat.OpenType.DataTypes;\nusing FontFlat.OpenType.Helper;\nusing FontFlat.OpenType.FontTables;\n\n" +
                $"namespace FontFlat.OpenType;\n\n" +
                $"public partial class OTFont\n{{\n");

            List<string> tables = [];

            foreach (var structDeclaration in structDeclarations)
            {
                var structName = structDeclaration.Identifier.Text;
                if (!structName.StartsWith("Table_")) { continue; }
                var structNameTitleCase = textInfo.ToTitleCase(structName.Substring(6));
                tables.Add(structNameTitleCase);

                sb.Append((char)0x20, 4);
                sb.AppendLine($"public {structName}? GetTable{structNameTitleCase}() {{");
                sb.Append((char)0x20, 8);
                sb.AppendLine($"if ({structNameTitleCase} is null) {{ ParseTable{structNameTitleCase}(); }}");
                sb.Append((char)0x20, 8);
                sb.AppendLine($"return {structNameTitleCase};");
                sb.Append((char)0x20, 4);
                sb.AppendLine($"}}");
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

                sb.Append((char)0x20, 4); sb.AppendLine($"private void ParseTable{tbl}() {{");
                sb.Append((char)0x20, 8); sb.AppendLine($"var record = Records.Where(x => x.tableTag.AsSpan().SequenceEqual(\"{tag}\"u8));");
                sb.Append((char)0x20, 8); sb.AppendLine($"if (record.Count() != 1) {{ throw new Exception(\"Not have table '{tag}'\"); }}");
                sb.Append((char)0x20, 8); sb.AppendLine($"reader.BaseStream.Seek((long)record.First().offset, SeekOrigin.Begin);");

                switch (tbl)
                {
                    case "OS_2":
                        sb.Append((char)0x20, 8); sb.AppendLine($"{tbl} = FontTables.Read.Table{tbl}(reader, record.First().length);");
                        break;
                    default:
                        sb.Append((char)0x20, 8); sb.AppendLine($"{tbl} = FontTables.Read.Table{tbl}(reader);");
                        break;
                }

                sb.Append((char)0x20, 4); sb.AppendLine("}");
            }

            sb.AppendLine("}");

            return sb.ToString();
        }
    }
}
