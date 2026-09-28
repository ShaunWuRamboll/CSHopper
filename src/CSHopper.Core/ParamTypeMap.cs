using System;
using System.Collections.Generic;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;

namespace CSHopper;

/// Maps a script parameter's declared C# type to a matching Grasshopper IGH_Param.
public static class ParamTypeMap
{
    private static readonly Dictionary<string, Func<IGH_Param>> Map = new(StringComparer.Ordinal)
    {
        ["object"] = () => new Param_GenericObject(),
        ["string"] = () => new Param_String(),
        ["String"] = () => new Param_String(),
        ["bool"] = () => new Param_Boolean(),
        ["Boolean"] = () => new Param_Boolean(),
        ["int"] = () => new Param_Integer(),
        ["Int32"] = () => new Param_Integer(),
        ["double"] = () => new Param_Number(),
        ["Double"] = () => new Param_Number(),
        ["Point3d"] = () => new Param_Point(),
        ["Vector3d"] = () => new Param_Vector(),
        ["Plane"] = () => new Param_Plane(),
        ["Line"] = () => new Param_Line(),
        ["Circle"] = () => new Param_Circle(),
        ["Rectangle3d"] = () => new Param_Rectangle(),
        ["Curve"] = () => new Param_Curve(),
        ["Brep"] = () => new Param_Brep(),
        ["Mesh"] = () => new Param_Mesh(),
        ["Surface"] = () => new Param_Surface(),
        ["GeometryBase"] = () => new Param_Geometry(),
        ["Guid"] = () => new Param_Guid(),
        ["Color"] = () => new Param_Colour(),
    };

    /// Strips namespace qualifiers and a trailing '?' so lookups match short type names.
    private static string ShortName(string typeText)
    {
        var t = typeText.Trim().TrimEnd('?');
        int dot = t.LastIndexOf('.');
        return dot >= 0 ? t[(dot + 1)..] : t;
    }

    public static IGH_Param CreateParam(ScriptParameterInfo info)
    {
        var lookupType = info.IsList ? info.InnerTypeText : info.TypeText;
        var key = ShortName(lookupType);
        var factory = Map.TryGetValue(key, out var f) ? f : () => new Param_GenericObject();
        var param = factory();
        param.Name = info.Name;
        param.NickName = info.Name;
        param.Description = info.Name;
        param.Access = info.IsList ? GH_ParamAccess.list : GH_ParamAccess.item;
        param.Optional = true;
        return param;
    }

    /// True if an existing param already matches the desired signature slot (name/type/access).
    public static bool Matches(IGH_Param existing, ScriptParameterInfo info)
    {
        if (existing.Name != info.Name) return false;
        var desiredAccess = info.IsList ? GH_ParamAccess.list : GH_ParamAccess.item;
        if (existing.Access != desiredAccess) return false;
        var lookupType = info.IsList ? info.InnerTypeText : info.TypeText;
        var key = ShortName(lookupType);
        var factory = Map.TryGetValue(key, out var f) ? f : () => new Param_GenericObject();
        var probe = factory();
        return existing.GetType() == probe.GetType();
    }
}
