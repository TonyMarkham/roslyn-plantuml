using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace RoslynPlantUml
{
    public class AssemblyData
    {
        public AssemblyData(string assemblyName, Compilation compilation, Dictionary<ClassDeclarationSyntax, MethodDeclarationSyntax[]> classMethodDeclarationSyntaxes)
        {
            AssemblyName = assemblyName;
            Compilation = compilation;
            ClassMethodDeclarationSyntaxes = classMethodDeclarationSyntaxes;
        }

        public string AssemblyName { get; set; }
        public Compilation Compilation { get; set; }
        public Dictionary<ClassDeclarationSyntax, MethodDeclarationSyntax[]> ClassMethodDeclarationSyntaxes { get; set; }
    }
}
