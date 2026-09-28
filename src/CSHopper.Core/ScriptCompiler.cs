using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CSHopper;

/// Compiles a GH_ScriptInstance-derived .cs source into a loadable, runnable type.
public static class ScriptCompiler
{
    private static readonly Regex ListPattern = new(
        @"^(?:global::)?(?:System\.Collections\.Generic\.)?List<\s*(.+)\s*>$",
        RegexOptions.Compiled);

    private static readonly Regex TreePattern = new(
        @"^(?:global::)?(?:Grasshopper\.)?DataTree<\s*(.+)\s*>$",
        RegexOptions.Compiled);

    private static readonly string[] ReservedInjectedNames =
    {
        "RhinoDocument", "GrasshopperDocument", "Component", "Iteration",
        "Print", "Reflect", "InvokeRunScript"
    };

    public static ScriptCompileResult Compile(string sourceCode)
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.Latest);
        var tree = CSharpSyntaxTree.ParseText(sourceCode, parseOptions);
        var root = (CompilationUnitSyntax)tree.GetRoot();

        var classDecl = root.DescendantNodes().OfType<ClassDeclarationSyntax>()
            .FirstOrDefault(c => c.BaseList?.Types.Any(bt => ShortName(bt.Type.ToString()) == "GH_ScriptInstance") == true);

        if (classDecl is null)
        {
            return ScriptCompileResult.Fail("No class inheriting GH_ScriptInstance was found (expected e.g. `class Script_Instance : GH_ScriptInstance`).");
        }

        var runScript = classDecl.Members.OfType<MethodDeclarationSyntax>()
            .FirstOrDefault(m => m.Identifier.Text == "RunScript");

        if (runScript is null)
        {
            return ScriptCompileResult.Fail("No `RunScript` method was found on the script class.");
        }

        var existingNames = new HashSet<string>(classDecl.Members.Select(GetMemberName).Where(n => n is not null)!, StringComparer.Ordinal);
        bool userDefinesInvoke = existingNames.Contains("InvokeRunScript");

        var inputs = new List<ScriptParameterInfo>();
        var outputs = new List<ScriptParameterInfo>();
        var callArgs = new List<string>();

        foreach (var p in runScript.ParameterList.Parameters)
        {
            var typeText = p.Type!.ToString();
            var name = p.Identifier.Text;
            bool isOut = p.Modifiers.Any(SyntaxKind.RefKeyword) || p.Modifiers.Any(SyntaxKind.OutKeyword);
            string modifier = p.Modifiers.Any(SyntaxKind.OutKeyword) ? "out" : p.Modifiers.Any(SyntaxKind.RefKeyword) ? "ref" : "";

            var listMatch = ListPattern.Match(typeText);
            var treeMatch = TreePattern.Match(typeText);
            ScriptParamAccess access;
            string innerType;
            if (treeMatch.Success)
            {
                access = ScriptParamAccess.Tree;
                innerType = treeMatch.Groups[1].Value.Trim();
            }
            else if (listMatch.Success)
            {
                access = ScriptParamAccess.List;
                innerType = listMatch.Groups[1].Value.Trim();
            }
            else
            {
                access = ScriptParamAccess.Item;
                innerType = typeText;
            }

            var info = new ScriptParameterInfo(name, typeText, access, innerType);
            callArgs.Add(string.IsNullOrEmpty(modifier) ? name : $"{modifier} {name}");

            if (isOut) outputs.Add(info);
            else inputs.Add(info);
        }

        var injected = userDefinesInvoke
            ? string.Empty
            : BuildInjectedMembers(inputs, outputs, callArgs, existingNames);

        CompilationUnitSyntax finalRoot = root;
        if (!string.IsNullOrEmpty(injected))
        {
            var wrapper = CSharpSyntaxTree.ParseText("class __Injected__ {\n" + injected + "\n}", parseOptions);
            var wrapperClass = ((CompilationUnitSyntax)wrapper.GetRoot()).Members.OfType<ClassDeclarationSyntax>().First();
            var newClassDecl = classDecl.AddMembers(wrapperClass.Members.ToArray());
            finalRoot = root.ReplaceNode(classDecl, newClassDecl);
        }

        var finalTree = CSharpSyntaxTree.Create(finalRoot, parseOptions);
        var references = GetReferences();
        var assemblyName = "CSHopperScript_" + Guid.NewGuid().ToString("N");

        var compilation = CSharpCompilation.Create(
            assemblyName,
            new[] { finalTree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using var peStream = new System.IO.MemoryStream();
        var emitResult = compilation.Emit(peStream);

        if (!emitResult.Success)
        {
            var errors = emitResult.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(FormatDiagnostic)
                .ToArray();
            return ScriptCompileResult.Fail(errors.Length > 0 ? errors : new[] { "Unknown compilation error." });
        }

        peStream.Seek(0, System.IO.SeekOrigin.Begin);
        var alc = new AssemblyLoadContext(assemblyName, isCollectible: true);
        var assembly = alc.LoadFromStream(peStream);

        var className = classDecl.Identifier.Text;
        var scriptType = assembly.GetTypes().FirstOrDefault(t => t.Name == className);

        if (scriptType is null)
        {
            alc.Unload();
            return ScriptCompileResult.Fail($"Compiled assembly did not contain a type named '{className}'.");
        }

        return new ScriptCompileResult
        {
            Success = true,
            ScriptType = scriptType,
            Assembly = assembly,
            LoadContext = alc,
            Inputs = inputs,
            Outputs = outputs,
        };
    }

    private static string? GetMemberName(MemberDeclarationSyntax member) => member switch
    {
        MethodDeclarationSyntax m => m.Identifier.Text,
        FieldDeclarationSyntax f => f.Declaration.Variables.FirstOrDefault()?.Identifier.Text,
        PropertyDeclarationSyntax p => p.Identifier.Text,
        _ => null
    };

    private static string ShortName(string typeText)
    {
        var t = typeText.Trim().TrimEnd('?');
        int idx = t.LastIndexOf('.');
        return idx >= 0 ? t[(idx + 1)..] : t;
    }

    private static string BuildInjectedMembers(
        List<ScriptParameterInfo> inputs,
        List<ScriptParameterInfo> outputs,
        List<string> callArgs,
        HashSet<string> existingNames)
    {
        var sb = new StringBuilder();

        if (!existingNames.Contains("RhinoDocument"))
            sb.AppendLine("private global::Rhino.RhinoDoc RhinoDocument;");
        if (!existingNames.Contains("GrasshopperDocument"))
            sb.AppendLine("private global::Grasshopper.Kernel.GH_Document GrasshopperDocument;");
        if (!existingNames.Contains("Component"))
            sb.AppendLine("private global::Grasshopper.Kernel.IGH_Component Component;");
        if (!existingNames.Contains("Iteration"))
            sb.AppendLine("private int Iteration;");

        if (!existingNames.Contains("Print"))
        {
            sb.AppendLine("private void Print(string text) { Component?.AddRuntimeMessage(global::Grasshopper.Kernel.GH_RuntimeMessageLevel.Remark, text); }");
            sb.AppendLine("private void Print(string format, params object[] args) { Print(string.Format(format, args)); }");
        }
        if (!existingNames.Contains("Reflect"))
        {
            sb.AppendLine("private void Reflect(object obj) { Print(global::Grasshopper.Kernel.GH_ScriptComponentUtilities.ReflectType_CS(obj)); }");
            sb.AppendLine("private void Reflect(object obj, string method_name) { Print(global::Grasshopper.Kernel.GH_ScriptComponentUtilities.ReflectType_CS(obj, method_name)); }");
        }

        sb.AppendLine("public override void InvokeRunScript(global::Grasshopper.Kernel.IGH_Component owner, object rhinoDocument, int iteration, global::System.Collections.Generic.List<object> inputs, global::Grasshopper.Kernel.IGH_DataAccess DA)");
        sb.AppendLine("{");
        sb.AppendLine("    Component = owner;");
        sb.AppendLine("    RhinoDocument = rhinoDocument as global::Rhino.RhinoDoc;");
        sb.AppendLine("    GrasshopperDocument = owner?.OnPingDocument();");
        sb.AppendLine("    Iteration = iteration;");
        sb.AppendLine();

        for (int i = 0; i < inputs.Count; i++)
        {
            var p = inputs[i];
            switch (p.Access)
            {
                case ScriptParamAccess.Tree:
                    sb.AppendLine($"    global::Grasshopper.Kernel.Data.GH_Structure<global::Grasshopper.Kernel.Types.IGH_Goo> {p.Name}__tree;");
                    sb.AppendLine($"    DA.GetDataTree({i}, out {p.Name}__tree);");
                    sb.AppendLine($"    global::Grasshopper.DataTree<{p.InnerTypeText}> {p.Name} = global::CSHopper.TreeAccess.ToDataTree<{p.InnerTypeText}>({p.Name}__tree);");
                    break;
                case ScriptParamAccess.List:
                    sb.AppendLine($"    global::System.Collections.Generic.List<{p.InnerTypeText}> {p.Name} = new global::System.Collections.Generic.List<{p.InnerTypeText}>();");
                    break;
                default:
                    sb.AppendLine($"    {p.TypeText} {p.Name} = default({p.TypeText});");
                    break;
            }
        }
        for (int i = 0; i < outputs.Count; i++)
        {
            var p = outputs[i];
            if (p.Access == ScriptParamAccess.Tree)
                sb.AppendLine($"    global::Grasshopper.DataTree<{p.InnerTypeText}> {p.Name} = new global::Grasshopper.DataTree<{p.InnerTypeText}>();");
            else if (p.Access == ScriptParamAccess.List)
                sb.AppendLine($"    global::System.Collections.Generic.List<{p.InnerTypeText}> {p.Name} = new global::System.Collections.Generic.List<{p.InnerTypeText}>();");
            else
                sb.AppendLine($"    {p.TypeText} {p.Name} = default({p.TypeText});");
        }

        sb.AppendLine();
        for (int i = 0; i < inputs.Count; i++)
        {
            var p = inputs[i];
            if (p.Access == ScriptParamAccess.List)
                sb.AppendLine($"    DA.GetDataList({i}, {p.Name});");
            else if (p.Access == ScriptParamAccess.Item)
                sb.AppendLine($"    DA.GetData({i}, ref {p.Name});");
            // Tree inputs are already read above, right where they're declared.
        }

        sb.AppendLine();
        sb.AppendLine($"    RunScript({string.Join(", ", callArgs)});");
        sb.AppendLine();

        for (int i = 0; i < outputs.Count; i++)
        {
            var p = outputs[i];
            if (p.Access == ScriptParamAccess.Tree)
                sb.AppendLine($"    DA.SetDataTree({i}, {p.Name});");
            else if (p.Access == ScriptParamAccess.List)
                sb.AppendLine($"    DA.SetDataList({i}, {p.Name});");
            else
                sb.AppendLine($"    DA.SetData({i}, {p.Name});");
        }

        sb.AppendLine("}");

        return sb.ToString();
    }

    private static string FormatDiagnostic(Diagnostic d)
    {
        var pos = d.Location.GetLineSpan();
        return $"({pos.StartLinePosition.Line + 1},{pos.StartLinePosition.Character + 1}): {d.GetMessage()}";
    }

    private static List<MetadataReference> GetReferences()
    {
        var refs = new List<MetadataReference>();
        var trusted = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES");
        if (trusted is not null)
        {
            foreach (var path in trusted.Split(System.IO.Path.PathSeparator))
            {
                if (System.IO.File.Exists(path))
                    refs.Add(MetadataReference.CreateFromFile(path));
            }
        }

        AddIfMissing(refs, typeof(Rhino.RhinoDoc).Assembly.Location);
        AddIfMissing(refs, typeof(Grasshopper.Kernel.GH_Component).Assembly.Location);
        AddIfMissing(refs, typeof(GH_IO.Serialization.GH_IWriter).Assembly.Location);
        AddIfMissing(refs, typeof(ScriptCompiler).Assembly.Location); // for CSHopper.TreeAccess

        return refs;
    }

    private static void AddIfMissing(List<MetadataReference> refs, string location)
    {
        if (!string.IsNullOrEmpty(location) &&
            !refs.Any(r => string.Equals(((PortableExecutableReference)r).FilePath, location, StringComparison.OrdinalIgnoreCase)))
        {
            refs.Add(MetadataReference.CreateFromFile(location));
        }
    }
}
