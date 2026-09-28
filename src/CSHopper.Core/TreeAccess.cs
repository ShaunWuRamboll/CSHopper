using System.Collections.Generic;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;

namespace CSHopper;

/// Converts the low-level tree structure read via IGH_DataAccess.GetDataTree into the
/// user-facing Grasshopper.DataTree&lt;T&gt; type used in RunScript tree parameters.
public static class TreeAccess
{
    public static Grasshopper.DataTree<T> ToDataTree<T>(GH_Structure<IGH_Goo> structure)
    {
        var tree = new Grasshopper.DataTree<T>();
        if (structure is null) return tree;

        foreach (var path in structure.Paths)
        {
            var branch = structure[path];
            var converted = new List<T>(branch.Count);
            foreach (var goo in branch)
                converted.Add(ConvertItem<T>(goo));
            tree.AddRange(converted, path);
        }

        return tree;
    }

    private static T ConvertItem<T>(IGH_Goo? goo)
    {
        if (goo is null) return default!;
        if (typeof(T) == typeof(object)) return (T)goo.ScriptVariable();
        if (goo.CastTo(out T result)) return result;
        if (goo.ScriptVariable() is T direct) return direct;
        return default!;
    }
}
