namespace CSHopper;

public enum ScriptParamAccess { Item, List, Tree }

public sealed record ScriptParameterInfo(string Name, string TypeText, ScriptParamAccess Access, string InnerTypeText);

public sealed class ScriptCompileResult
{
    public bool Success { get; init; }
    public System.Type? ScriptType { get; init; }
    public System.Reflection.Assembly? Assembly { get; init; }
    public System.Runtime.Loader.AssemblyLoadContext? LoadContext { get; init; }
    public System.Collections.Generic.IReadOnlyList<ScriptParameterInfo> Inputs { get; init; }
        = System.Array.Empty<ScriptParameterInfo>();
    public System.Collections.Generic.IReadOnlyList<ScriptParameterInfo> Outputs { get; init; }
        = System.Array.Empty<ScriptParameterInfo>();
    public System.Collections.Generic.IReadOnlyList<string> Errors { get; init; }
        = System.Array.Empty<string>();

    public static ScriptCompileResult Fail(params string[] errors) => new() { Success = false, Errors = errors };
}
