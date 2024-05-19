using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis;
using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using Microsoft.CodeAnalysis.CSharp;

namespace OpenType.SourceGen
{
    internal class FontTablesSyntaxReceiver : ISyntaxReceiver
    {
        public List<RecordDeclarationSyntax> StructDeclarations = [];

        public void OnVisitSyntaxNode(SyntaxNode syntaxNode)
        {
            if (syntaxNode is RecordDeclarationSyntax recordDeclaration
                && recordDeclaration.IsKind(SyntaxKind.RecordStructDeclaration)
                && recordDeclaration.Parent is FileScopedNamespaceDeclarationSyntax namespaceDeclaration
                && namespaceDeclaration.Name.ToString() == "FontFlat.OpenType.FontTables"
                )
            {
                StructDeclarations.Add(recordDeclaration);
            }
        }
    }
}
