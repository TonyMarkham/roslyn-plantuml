using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.Win32;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using System.Collections.ObjectModel;
using System.Text;
using System.Windows.Controls;
using Microsoft.CodeAnalysis.CSharp;

namespace RoslynPlantUml
{
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        static Solution? s_Solution = null;
        static List<AssemblyData> s_AssemblyData = new List<AssemblyData>();

        static ListView ProjectListView;
        static ListView TypeListView;

        bool SolutionFileExists = false;
        string m_SolutionPath = string.Empty;
        public event PropertyChangedEventHandler? PropertyChanged;

        public string SolutionPath
        {
            get => m_SolutionPath;
            set
            {
                m_SolutionPath = value;
                OnPropertyChanged();
                HandleSolutionExists();
            }
        }

        
        public Dictionary<Project, Compilation> CompilationsByProject { get; set; } = new();
        public Dictionary<Project, List<TypeDeclarationSyntax>> TypeDeclarationsByProject { get; set; } = new();
        public Dictionary<Project, List<SemanticModel>> SemanticModelsByProject { get; set; } = new();
        public Dictionary<SyntaxTree, SemanticModel> SemanticModelsBySyntaxTree { get; set; } = new();
        public Dictionary<TypeDeclarationSyntax, List<MemberDeclarationSyntax>> MemberDeclarationByType { get; set; } = new();

        public ObservableCollection<string> Namespaces { get; set; } = new ObservableCollection<string>();
        public ObservableCollection<string> Classes { get; set; } = new ObservableCollection<string>();
        public ObservableCollection<string> Members { get; set; } = new ObservableCollection<string>();

        public MainWindow()
        {
            Title = "Roslyn PlantUML";
            DataContext = this;
            InitializeComponent();
        }

        private void btnBrowse_Click(object sender, RoutedEventArgs e)
        {
            s_Solution = null;
            Namespaces.Clear();
            Classes.Clear();
            Members.Clear();
            
            var openFileDialog = new OpenFileDialog();
            openFileDialog.Filter = "Solution | *.sln";
            var success = openFileDialog.ShowDialog();

            switch (success)
            {
                case true:
                    SolutionPath = openFileDialog.FileName;
                    break;
            }
        }

        void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        void HandleSolutionExists()
        {
            SolutionFileExists = File.Exists(SolutionPath);
            if (!SolutionFileExists)
                return;

            btnLoad.IsEnabled = true;
        }

        private async void btnLoad_Click(object sender, RoutedEventArgs e)
        {
            if (!MSBuildLocator.IsRegistered)
                MSBuildLocator.RegisterDefaults();

            using (var w = MSBuildWorkspace.Create())
            {
                prgSolutionLoadProgress.Background = new SolidColorBrush(Colors.White);
                prgSolutionLoadProgress.IsIndeterminate = true;
                txtProgressBarText.Foreground = new SolidColorBrush(Colors.Black);
                s_Solution = await w.OpenSolutionAsync(SolutionPath);
                await CacheSolutionAsync();
                prgSolutionLoadProgress.Background = new SolidColorBrush(Colors.Transparent);
                prgSolutionLoadProgress.IsIndeterminate = false;
                txtProgressBarText.Foreground = new SolidColorBrush(Colors.Transparent);
            }
        }

        async Task<bool> CacheSolutionAsync()
        {
            if (s_Solution == null)
                return false;

            TypeDeclarationsByProject.Clear();
            SemanticModelsByProject.Clear();
            SemanticModelsBySyntaxTree.Clear();

            using (var workspace = MSBuildWorkspace.Create())
            {
                s_AssemblyData.Clear();
                foreach (var project in s_Solution.Projects.OrderBy(a => a.Name))
                {
                    var assemblyName = project.Name;
                    Namespaces.Add(assemblyName);
                    txtProgressBarText.Text = $"Caching {assemblyName}";
                    if (string.IsNullOrEmpty(assemblyName))
                        continue;

                    var compilation = await project.GetCompilationAsync();
                    if (compilation == null)
                        continue;
                    
                    CompilationsByProject.Add(project, compilation);

                    // https://learn.microsoft.com/en-us/dotnet/api/microsoft.codeanalysis.csharp.syntax.classdeclarationsyntax?view=roslyn-dotnet-4.7.0
                    var classMethodDeclarationSyntaxes = new Dictionary<TypeDeclarationSyntax, MethodDeclarationSyntax[]>();
                    var semanticModels = new List<SemanticModel>();
                    var typeDeclarations = new List<TypeDeclarationSyntax>();
                    foreach (var document in project.Documents.OrderBy(a => a.Name))
                    {
                        if(await document.GetSemanticModelAsync() is not { } semanticModel)
                            continue;
                        
                        if (await document.GetSyntaxTreeAsync() is not { } syntaxTree)
                            continue;

                        if (await syntaxTree.GetRootAsync() is not { } root)
                            continue;

                        if (root.DescendantNodes().OfType<TypeDeclarationSyntax>().ToList() is not { } typeDeclarationSyntaxes)
                            continue;

                        semanticModels.Add(semanticModel);
                        typeDeclarations.AddRange(typeDeclarationSyntaxes);

                        // https://learn.microsoft.com/en-us/dotnet/api/microsoft.codeanalysis.csharp.syntax.memberdeclarationsyntax?view=roslyn-dotnet-4.7.0
                        foreach (var typeDeclarationSyntax in typeDeclarationSyntaxes)
                        {
                            var membersInClass = typeDeclarationSyntax
                                .Members
                                .Select(m => m)
                                .ToList();

                            if (!membersInClass.Any())
                                continue;

                            MemberDeclarationByType.Add(typeDeclarationSyntax, membersInClass);
                            SemanticModelsBySyntaxTree.TryAdd(typeDeclarationSyntax.SyntaxTree, semanticModel);
                        }
                    }

                    TypeDeclarationsByProject.Add(project, typeDeclarations);
                    SemanticModelsByProject.Add(project, semanticModels);
                }
            }

            return true;
        }

        public static bool TryGetParentSyntax<T>(SyntaxNode syntaxNode, out T result) where T : SyntaxNode
        {
            // set defaults
            result = null;

            if (syntaxNode == null)
            {
                return false;
            }

            try
            {
                syntaxNode = syntaxNode.Parent;

                if (syntaxNode == null)
                {
                    return false;
                }

                if (syntaxNode.GetType() == typeof(T))
                {
                    result = syntaxNode as T;
                    return true;
                }

                return TryGetParentSyntax<T>(syntaxNode, out result);
            }
            catch
            {
                return false;
            }
        }

        private void AssemblyListView_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            Classes.Clear();

            if (sender is not ListView listView)
                return;

            AssemblyListView = listView;
            var selectedNamespace = (string)listView.SelectedItem;
            if (string.IsNullOrEmpty(selectedNamespace))
                return;

            var classDeclarations = TypeDeclarationsByProject
                .FirstOrDefault(kvp => kvp.Key.Name.Equals(selectedNamespace, StringComparison.Ordinal))
                .Value;

            if (!classDeclarations.Any())
                return;

            var selectedClasses = classDeclarations
                .Select(a => a.Identifier.ToString())
                .OrderBy(a => a);
            if(!selectedClasses.Any())
                return;

            foreach ( var classDeclaration in selectedClasses)
            {
                Classes.Add(classDeclaration);
            }

        }

        private void ClassListView_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            Members.Clear();

            if (sender is not ListView listView)
                return;

            TypeListView = listView;
            var selectedClass = (string)listView.SelectedItem;
            if (string.IsNullOrEmpty(selectedClass))
                return;
            
            var plantUmlContent = new StringBuilder();
            var note = new StringBuilder();

            var typeType = "class";
            var typeDeclarationSyntax = MemberDeclarationByType
                .FirstOrDefault(kvp => kvp.Key.Identifier
                    .ToString().Equals(selectedClass, StringComparison.Ordinal)).Key;
            
            switch (typeDeclarationSyntax)
            {
                case InterfaceDeclarationSyntax _:
                    typeType = "interface";
                    break;
                case ClassDeclarationSyntax classDeclaration:
                    var isAbstract = classDeclaration.Modifiers.Any(x => x.IsKind(SyntaxKind.AbstractKeyword));
                    typeType = isAbstract ? "abstract class" : "class";
                    break;
                case StructDeclarationSyntax _:
                    typeType = "struct";
                    break;
                case RecordDeclarationSyntax _:
                    typeType = "record";
                    break;
            }
            
            /* ====================================================================================================== */
            
            var assemblyName = string.Empty;
            List<string> pumlReferences = new();
            var fullyQuallifiedName = string.Empty;
            if (typeDeclarationSyntax is ClassDeclarationSyntax classDeclarationSyntax)
            {
                if (AssemblyListView.SelectedItem is string assemblyString)
                {
                    assemblyName = assemblyString;
                    fullyQuallifiedName = string.IsNullOrEmpty(assemblyName) ? selectedClass : $"{assemblyName}.{selectedClass}";
                }
                
                note.AppendLine($"note top of {fullyQuallifiedName}");
                
                if (classDeclarationSyntax.BaseList is { } baseList)
                {
                    var x = baseList.Types;
                    foreach (var baseTypeSyntax in baseList.Types)
                    {
                        SyntaxReference baseTypeSyntaxReference;
                        var baseTypeName = string.Empty;
                        var baseTypeSyntaxReferencePath = string.Empty;
                        List<string?> interfaceNames = new();
                        List<SyntaxReference> interfaceTypeSyntaxReference = new();
                        if (SemanticModelsBySyntaxTree.TryGetValue(baseTypeSyntax.Type.SyntaxTree, out var semanticModel))
                        {
                            if (semanticModel.GetDeclaredSymbol(classDeclarationSyntax) is ITypeSymbol typeSymbol)
                            {
                                if (typeSymbol.BaseType is { } baseType)
                                {
                                    if (baseType.ConstructedFrom is { } baseConstructedFrom)
                                    {
                                        if(!string.IsNullOrEmpty(baseConstructedFrom.Name))
                                        {
                                            baseTypeName = baseType.ConstructedFrom.ToString();
                                        }
                                    }
                                    var baseTypeSyntaxReferences = baseType.DeclaringSyntaxReferences;
                                    if (baseTypeSyntaxReferences.Any())
                                    {
                                        baseTypeSyntaxReference = baseTypeSyntaxReferences[0];
                                        
                                        if (baseTypeSyntaxReference.SyntaxTree is { } syntaxTree)
                                        {
                                            if(!string.IsNullOrEmpty(syntaxTree.FilePath))
                                            {
                                                baseTypeSyntaxReferencePath = syntaxTree.FilePath;
                                            }
                                        }
                                    }
                                }
                                
                                var interfaces = typeSymbol.Interfaces;
                                if (interfaces.Any())
                                {
                                    interfaceNames = interfaces.Select(a => a.ConstructedFrom.ToString()).Distinct().ToList();
                                    interfaceTypeSyntaxReference = interfaces.SelectMany(a => a.DeclaringSyntaxReferences).ToList();
                                }
                            }
                        }

                        var baseTypeNameString = string.IsNullOrEmpty(baseTypeName)
                            ? "none"
                            : baseTypeName.Split('<')[0];
                        if (!string.IsNullOrEmpty(baseTypeName))
                        {
                            note.AppendLine($"Base: {fullyQuallifiedName} --|> {baseTypeNameString}");
                            pumlReferences.Add($"{fullyQuallifiedName} --|> {baseTypeNameString}");
                        }
                        
                        if(interfaceNames.Any())
                        {
                            foreach (var reference in interfaceNames)
                            {
                                var interfaceTypeNameString = string.IsNullOrEmpty(reference)
                                    ? "none"
                                    : reference.Split('<')[0];
                                if (!string.IsNullOrEmpty(reference))
                                {
                                    note.AppendLine($"Interface: {fullyQuallifiedName} ..|> {interfaceTypeNameString}");
                                    pumlReferences.Add($"interface {interfaceTypeNameString}");
                                    pumlReferences.Add($"{fullyQuallifiedName} ..|> {interfaceTypeNameString}");
                                }
                                
                            }
                        }

                        break;
                    }
                }
                
                if (typeDeclarationSyntax.Parent is NamespaceDeclarationSyntax namespaceDeclarationSyntax)
                    note.AppendLine($"Namespace: [{namespaceDeclarationSyntax.Name.ToString()}]");
            }

            /* ====================================================================================================== */

            var members = MemberDeclarationByType
                .Where(kvp => kvp.Key.Identifier.ToString().Equals(selectedClass, StringComparison.Ordinal))
                .SelectMany(kvp => kvp.Value)
                .ToList();

            if (members.Count == 0)
                return;
            
            plantUmlContent.AppendLine("@startuml");
            plantUmlContent.AppendLine();
            foreach (var pumlReference in pumlReferences)
            {
                plantUmlContent.AppendLine(pumlReference);
            }
            plantUmlContent.AppendLine();
            plantUmlContent.AppendLine($"{typeType} {fullyQuallifiedName} {{");
            
            /* ------------------------------------------------------------------------------------------------------ */
            var fieldDeclarations = members
                .Where(m => m is FieldDeclarationSyntax)
                .Select(m => m)
                .Cast<FieldDeclarationSyntax>()
                .OrderBy(f => f.Declaration.Variables.FirstOrDefault()?.Identifier.ToString())
                .ToArray();
            if (fieldDeclarations.Length > 0)
            {
                var fields =fieldDeclarations.Distinct().ToArray();

                foreach (var field in fields)
                {
                    var variable = field.Declaration;
                    var type = variable.Type.ToString();
                    var name = variable.Variables.FirstOrDefault()?.Identifier.ToString();
                    plantUmlContent.AppendLine($"    {{field}} -{name} : {type}");
                }
            }
            
            var propertyDeclarations = members
                .Where(m => m is PropertyDeclarationSyntax)
                .Select(m => m)
                .Cast<PropertyDeclarationSyntax>()
                .OrderBy(p => p.Identifier.ToString())
                .ToArray();

            if (propertyDeclarations.Length > 0)
            {
                var properties =propertyDeclarations.Distinct().ToArray();

                foreach (var property in properties)
                {
                    plantUmlContent.AppendLine($"    {{field}} +{property.Identifier.ToString()} : {property.Type.ToString()}");
                }
            }

            var methods = members
                .Where(m => m is MethodDeclarationSyntax)
                .Select(m => m)
                .Distinct()
                .Cast<MethodDeclarationSyntax>()
                .OrderBy(m => m.Identifier.ToString())
                .ToList();

            foreach (var method in methods)
            {
                var modifier = string.Empty;
                switch (method.Modifiers.FirstOrDefault().ToString())
                {
                    case "private":
                        modifier = "-";
                        break;
                    case "protected":
                        modifier = "#";
                        break;
                    case "internal":
                        modifier = "~";
                        break;
                    case "public":
                        modifier = "+";
                        break;
                    default:
                        modifier = typeDeclarationSyntax is InterfaceDeclarationSyntax ? string.Empty : "-";
                        break;
                }
                
                plantUmlContent.Append($"    {{method}} {modifier}{method.Identifier.ToString()}(");
                for(var index = 0; index < method.ParameterList.Parameters.Count; index++)
                {
                    var parameter = method.ParameterList.Parameters[index];
                    if(parameter.Type == null)
                        continue;
            
                    plantUmlContent.Append($"{parameter.Identifier.ToString()} : {parameter.Type.ToString()}");
                    if(index < method.ParameterList.Parameters.Count - 1)
                        plantUmlContent.Append(", ");
                }
                plantUmlContent.AppendLine($") : {method.ReturnType.ToString()}");
            }
            
            plantUmlContent.AppendLine("}");
            plantUmlContent.AppendLine();
            note.AppendLine("end note");
            //plantUmlContent.AppendLine(note.ToString());
            plantUmlContent.AppendLine("@enduml");
            
            Members.Add(plantUmlContent.ToString());
            Clipboard.Clear();
            Clipboard.SetDataObject(plantUmlContent.ToString());
        }
    }
}
