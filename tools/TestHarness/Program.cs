using System.Collections;
using System.Reflection;
using System.Runtime.Loader;
using CSHopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;

RegisterRhinoAssemblyResolver();

const string boilerplate = """
// Grasshopper Script Instance
#region Usings
using System;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;

using Rhino;
using Rhino.Geometry;

using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
#endregion

public class Script_Instance : GH_ScriptInstance
{
    #region Notes
    /* 
      Members:
        RhinoDoc RhinoDocument
        GH_Document GrasshopperDocument
        IGH_Component Component
        int Iteration

      Methods (Virtual & overridable):
        Print(string text)
        Print(string format, params object[] args)
        Reflect(object obj)
        Reflect(object obj, string method_name)
    */
    #endregion

    private void RunScript(object x, object y, ref object a)
    {
        // Write your logic here
        a = null;
    }
}
""";

const string typedScript = """
using System;
using System.Collections.Generic;
using Rhino.Geometry;
using Grasshopper.Kernel;

public class Script_Instance : GH_ScriptInstance
{
    private void RunScript(double x, List<double> ys, ref double sum, ref string message)
    {
        sum = x;
        foreach (var y in ys) sum += y;
        message = "hello " + x;
    }
}
""";

const string treeScript = """
using System;
using System.Collections.Generic;
using Grasshopper;
using Grasshopper.Kernel;

public class Script_Instance : GH_ScriptInstance
{
    private void RunScript(DataTree<double> values, ref DataTree<double> doubled)
    {
        var result = new DataTree<double>();
        foreach (var path in values.Paths)
        {
            var branch = new List<double>();
            foreach (var v in values.Branch(path)) branch.Add(v * 2);
            result.AddRange(branch, path);
        }
        doubled = result;
    }
}
""";

Console.WriteLine("=== Test 1: exact GH boilerplate (object x, object y, ref object a) ===");
RunTest(boilerplate, new Dictionary<int, object?> { [0] = 3.0, [1] = 4.0 });

Console.WriteLine();
Console.WriteLine("=== Test 2: typed + list script (double x, List<double> ys, ref double sum, ref string message) ===");
RunTest(typedScript, new Dictionary<int, object?> { [0] = 10.0, [1] = new List<double> { 1.0, 2.0, 3.0 } });

Console.WriteLine();
Console.WriteLine("=== Test 3: data tree access (DataTree<double> values, ref DataTree<double> doubled) ===");
RunTest(treeScript, new Dictionary<int, object?> { [0] = BuildTestTree() });

// Kept in its own method (not inline in Main) so the Grasshopper.DataTree<T>/GH_Path type
// references are only JITted/resolved when this runs — after the resolver above is registered.
static object BuildTestTree()
{
    var inputTree = new Grasshopper.DataTree<double>();
    inputTree.Add(1.0, new GH_Path(0));
    inputTree.Add(2.0, new GH_Path(0));
    inputTree.Add(10.0, new GH_Path(1));
    return inputTree;
}

static void RunTest(string source, Dictionary<int, object?> inputValues)
{
    var result = ScriptCompiler.Compile(source);
    Console.WriteLine($"Success: {result.Success}");
    if (!result.Success)
    {
        foreach (var err in result.Errors) Console.WriteLine("  ERROR: " + err);
        return;
    }

    Console.WriteLine($"Inputs:  {string.Join(", ", result.Inputs.Select(i => $"{i.TypeText} {i.Name}"))}");
    Console.WriteLine($"Outputs: {string.Join(", ", result.Outputs.Select(o => $"{o.TypeText} {o.Name}"))}");

    var instance = (Grasshopper.Kernel.GH_ScriptInstance)Activator.CreateInstance(result.ScriptType!)!;
    var da = new FakeDataAccess(inputValues);
    instance.InvokeRunScript(owner: null!, rhinoDocument: null, iteration: 0, inputs: new List<object>(), DA: da);

    foreach (var kv in da.Outputs)
        Console.WriteLine($"  out[{kv.Key}] = {Describe(kv.Value)}");
}

static string Describe(object? o) => o switch
{
    null => "null",
    Grasshopper.DataTree<double> tree => string.Join("; ", tree.Paths.Select(p => $"{p}:[{string.Join(",", tree.Branch(p))}]")),
    IEnumerable e and not string => "[" + string.Join(", ", e.Cast<object>()) + "]",
    _ => o.ToString() ?? "null"
};

// This harness runs standalone (outside Rhino), but CSHopper.Core is compiled against
// the "Grasshopper" NuGet package's placeholder version (8.0.23304.9001), while the
// real installed Rhino 8 assemblies report a newer version (e.g. 8.32.x). Inside Rhino
// this is a non-issue because Rhino's own already-loaded assemblies satisfy the plugin's
// simple-name reference; here we must replicate that by resolving to the real files.
static void RegisterRhinoAssemblyResolver()
{
    var knownPaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["RhinoCommon"] = @"C:\Program Files\Rhino 8\System\RhinoCommon.dll",
        ["Grasshopper"] = @"C:\Program Files\Rhino 8\Plug-ins\Grasshopper\Grasshopper.dll",
        ["GH_IO"] = @"C:\Program Files\Rhino 8\Plug-ins\Grasshopper\GH_IO.dll",
        ["GH_Util"] = @"C:\Program Files\Rhino 8\Plug-ins\Grasshopper\GH_Util.dll",
    };

    AssemblyLoadContext.Default.Resolving += (ctx, name) =>
        name.Name is not null && knownPaths.TryGetValue(name.Name, out var path)
            ? ctx.LoadFromAssemblyPath(path)
            : null;
}

sealed class FakeDataAccess : IGH_DataAccess
{
    private readonly Dictionary<int, object?> _inputs;
    public readonly Dictionary<int, object?> Outputs = new();

    public FakeDataAccess(Dictionary<int, object?> inputs) => _inputs = inputs;

    public int Iteration => 0;
    public void IncrementIteration() { }
    public void DisableGapLogic() { }
    public void DisableGapLogic(int paramIndex) { }
    public GH_Path ParameterTargetPath(int paramIndex) => new GH_Path(0);
    public int ParameterTargetIndex(int paramIndex) => 0;
    public void AbortComponentSolution() { }
    public List<T> Util_RemoveNullRefs<T>(List<T> L) => L;
    public int Util_CountNullRefs<T>(List<T> L) => 0;
    public int Util_CountNonNullRefs<T>(List<T> L) => L.Count;
    public bool Util_EnsureNonNullCount<T>(List<T> L, int N) => L.Count >= N;
    public int Util_FirstNonNullItem<T>(List<T> L) => 0;

    public bool SetData(int paramIndex, object data) { Outputs[paramIndex] = data; return true; }
    public bool SetData(int paramIndex, object data, int itemIndexOverride) => SetData(paramIndex, data);
    public bool SetData(string paramName, object data) => true;
    public bool SetDataList(int paramIndex, IEnumerable data) { Outputs[paramIndex] = data.Cast<object>().ToList(); return true; }
    public bool SetDataList(int paramIndex, IEnumerable data, int listIndexOverride) => SetDataList(paramIndex, data);
    public bool SetDataList(string paramName, IEnumerable data) => true;
    public bool SetDataTree(int paramIndex, IGH_DataTree tree) { Outputs[paramIndex] = tree; return true; }
    public bool SetDataTree(int paramIndex, IGH_Structure tree) { Outputs[paramIndex] = tree; return true; }
    public bool BlitData<Q>(int paramIndex, GH_Structure<Q> tree, bool overwrite) where Q : IGH_Goo => true;

    public bool GetData<T>(int index, ref T destination)
    {
        if (_inputs.TryGetValue(index, out var v) && v is T typed) { destination = typed; return true; }
        return false;
    }
    public bool GetData<T>(string name, ref T destination) => false;

    public bool GetDataList<T>(int index, List<T> list)
    {
        if (_inputs.TryGetValue(index, out var v) && v is IEnumerable<T> typed) { list.AddRange(typed); return true; }
        return false;
    }
    public bool GetDataList<T>(string name, List<T> list) => false;

    public bool GetDataTree<T>(int index, out GH_Structure<T> tree) where T : IGH_Goo
    {
        tree = new GH_Structure<T>();
        if (_inputs.TryGetValue(index, out var v) && v is Grasshopper.DataTree<double> source)
        {
            foreach (var path in source.Paths)
                tree.AppendRange(source.Branch(path).Select(d => (T)(object)new GH_Number(d)), path);
            return true;
        }
        return false;
    }
    public bool GetDataTree<T>(string name, out GH_Structure<T> tree) where T : IGH_Goo { tree = new GH_Structure<T>(); return false; }
}
