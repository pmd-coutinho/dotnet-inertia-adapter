using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace InertiaNet.Pathfinder.Analysis;

static class EnumDiscoverer
{
    public static List<EnumInfo> Discover(SyntaxTree tree)
    {
        var enums = new List<EnumInfo>();
        var root = tree.GetRoot();

        foreach (var enumDecl in root.DescendantNodes().OfType<EnumDeclarationSyntax>())
        {
            var fullName = GetFullName(enumDecl);
            var shortName = enumDecl.Identifier.Text;

            var members = new List<EnumMember>();
            foreach (var member in enumDecl.Members)
            {
                var name = member.Identifier.Text;
                string? value = null;

                if (member.EqualsValue?.Value is LiteralExpressionSyntax literal)
                    value = literal.Token.ValueText;

                members.Add(new EnumMember(name, value));
            }

            enums.Add(new EnumInfo(fullName, shortName, members));
        }

        return enums;
    }

    private static string GetFullName(EnumDeclarationSyntax enumDecl)
    {
        var parts = new List<string> { enumDecl.Identifier.Text };
        var parent = enumDecl.Parent;

        while (parent != null)
        {
            if (parent is NamespaceDeclarationSyntax ns)
                parts.Insert(0, ns.Name.ToString());
            else if (parent is FileScopedNamespaceDeclarationSyntax fileNs)
                parts.Insert(0, fileNs.Name.ToString());
            else if (parent is ClassDeclarationSyntax classDecl)
                parts.Insert(0, classDecl.Identifier.Text);
            parent = parent.Parent;
        }

        return string.Join(".", parts);
    }
}
