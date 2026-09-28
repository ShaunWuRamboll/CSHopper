using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Loader;
using System.Windows.Forms;
using GH_IO.Serialization;
using Grasshopper.Kernel;
using Rhino;

namespace CSHopper;

public class CSHopperComponent : GH_Component, IGH_VariableParameterComponent
{
    private const string DefaultTemplate = @"// Grasshopper Script Instance
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
";

    private string? _scriptPath;
    private string? _lastSource;
    private FileSystemWatcher? _watcher;
    private System.Threading.Timer? _debounceTimer;
    private ScriptCompileResult? _compiled;
    private AssemblyLoadContext? _previousAlc;

    public CSHopperComponent()
        : base("CS Hopper", "CSHopper", "Live C# script synced from an external .cs file edited in VS Code.", "CSHopper", "Script")
    {
    }

    public override Guid ComponentGuid => new("b2a92a5b-985a-487b-8f11-c19e85e16927");
    protected override System.Drawing.Bitmap? Icon => null;
    public override GH_Exposure Exposure => GH_Exposure.primary;

    protected override void RegisterInputParams(GH_InputParamManager pManager) { }
    protected override void RegisterOutputParams(GH_OutputParamManager pManager) { }

    protected override void SolveInstance(IGH_DataAccess DA)
    {
        if (_compiled is null || !_compiled.Success || _compiled.ScriptType is null)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "No compiled script loaded. Select or create a script file from the component menu.");
            return;
        }

        try
        {
            var instance = Activator.CreateInstance(_compiled.ScriptType);
            if (instance is not Grasshopper.Kernel.GH_ScriptInstance scriptInstance)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Compiled script instance did not derive from GH_ScriptInstance.");
                return;
            }

            scriptInstance.InvokeRunScript(this, RhinoDoc.ActiveDoc, RunCount - 1, new List<object>(), DA);
        }
        catch (Exception ex)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
        }
    }

    private void SetScriptPath(string path)
    {
        _scriptPath = path;
        AttachWatcher(path);
        RecompileFromFile();
    }

    private void AttachWatcher(string path)
    {
        _watcher?.Dispose();
        var dir = Path.GetDirectoryName(path);
        var file = Path.GetFileName(path);
        if (string.IsNullOrEmpty(dir) || string.IsNullOrEmpty(file)) return;

        _watcher = new FileSystemWatcher(dir, file)
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
            EnableRaisingEvents = true
        };
        _watcher.Changed += OnScriptFileChanged;
    }

    private void OnScriptFileChanged(object sender, FileSystemEventArgs e)
    {
        _debounceTimer?.Dispose();
        _debounceTimer = new System.Threading.Timer(_ =>
        {
            OnPingDocument()?.ScheduleSolution(250, _ => RecompileFromFile());
        }, null, 300, System.Threading.Timeout.Infinite);
    }

    private void RecompileFromFile()
    {
        if (string.IsNullOrEmpty(_scriptPath) || !File.Exists(_scriptPath)) return;

        string text;
        try
        {
            text = File.ReadAllText(_scriptPath);
        }
        catch (IOException)
        {
            return; // file briefly locked by the editor's save; next change event will retry
        }

        CompileFromSource(text);
    }

    private void CompileFromSource(string text)
    {
        _lastSource = text;
        var result = ScriptCompiler.Compile(text);
        _compiled = result;

        if (!result.Success)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, string.Join("\n", result.Errors));
            ExpireSolution(true);
            return;
        }

        SyncParameters(result);

        _previousAlc?.Unload();
        _previousAlc = result.LoadContext;

        ExpireSolution(true);
    }

    private void SyncParameters(ScriptCompileResult result)
    {
        SyncSide(Params.Input, result.Inputs, isOutput: false);
        SyncSide(Params.Output, result.Outputs, isOutput: true);
        Params.OnParametersChanged();
        VariableParameterMaintenance();
    }

    private void SyncSide(List<IGH_Param> existing, IReadOnlyList<ScriptParameterInfo> desired, bool isOutput)
    {
        while (existing.Count > desired.Count)
        {
            var p = existing[^1];
            if (isOutput) Params.UnregisterOutputParameter(p);
            else Params.UnregisterInputParameter(p);
        }

        for (int i = 0; i < desired.Count; i++)
        {
            var info = desired[i];
            bool needsNew = i >= existing.Count || !ParamTypeMap.Matches(existing[i], info);
            if (!needsNew) continue;

            var newParam = ParamTypeMap.CreateParam(info);
            if (i < existing.Count)
            {
                var old = existing[i];
                if (isOutput) Params.UnregisterOutputParameter(old);
                else Params.UnregisterInputParameter(old);
            }

            if (isOutput) Params.RegisterOutputParam(newParam, i);
            else Params.RegisterInputParam(newParam, i);
        }
    }

    public override bool Write(GH_IWriter writer)
    {
        writer.SetString("ScriptPath", _scriptPath ?? string.Empty);
        writer.SetString("ScriptSource", _lastSource ?? string.Empty);
        return base.Write(writer);
    }

    public override bool Read(GH_IReader reader)
    {
        bool ok = base.Read(reader);
        string path = string.Empty;
        reader.TryGetString("ScriptPath", ref path);
        string source = string.Empty;
        reader.TryGetString("ScriptSource", ref source);

        _scriptPath = string.IsNullOrEmpty(path) ? null : path;

        if (!string.IsNullOrEmpty(_scriptPath) && File.Exists(_scriptPath))
        {
            AttachWatcher(_scriptPath);
            RecompileFromFile();
        }
        else if (!string.IsNullOrEmpty(source))
        {
            // Original file isn't on this machine (e.g. opened by a colleague) — run from
            // the source embedded in the .gh file so the definition stays self-contained.
            CompileFromSource(source);
            if (!string.IsNullOrEmpty(_scriptPath))
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                    $"Script file not found at '{_scriptPath}'. Running from the source embedded in this document — use \"Extract Script to File...\" to resume live editing.");
        }
        return ok;
    }

    public override void AppendAdditionalMenuItems(ToolStripDropDown menu)
    {
        base.AppendAdditionalMenuItems(menu);
        Menu_AppendSeparator(menu);

        menu.Items.Add(new ToolStripMenuItem("Select Script File...", null, (s, e) => SelectExistingFile()));
        menu.Items.Add(new ToolStripMenuItem("Create New Script File...", null, (s, e) => CreateNewFile()));

        bool fileMissing = string.IsNullOrEmpty(_scriptPath) || !File.Exists(_scriptPath);
        var extractItem = new ToolStripMenuItem("Extract Script to File...", null, (s, e) => ExtractEmbeddedSource())
        {
            Enabled = fileMissing && !string.IsNullOrEmpty(_lastSource)
        };
        menu.Items.Add(extractItem);

        var openItem = new ToolStripMenuItem("Open in VS Code", null, (s, e) => OpenInVsCode())
        {
            Enabled = !string.IsNullOrEmpty(_scriptPath)
        };
        menu.Items.Add(openItem);

        var recompileItem = new ToolStripMenuItem("Recompile Now", null, (s, e) => RecompileFromFile())
        {
            Enabled = !string.IsNullOrEmpty(_scriptPath)
        };
        menu.Items.Add(recompileItem);
    }

    private void SelectExistingFile()
    {
        using var dlg = new OpenFileDialog { Filter = "C# script (*.cs)|*.cs", CheckFileExists = true };
        if (dlg.ShowDialog() == DialogResult.OK)
        {
            SetScriptPath(dlg.FileName);
        }
    }

    private void ExtractEmbeddedSource()
    {
        using var dlg = new SaveFileDialog { Filter = "C# script (*.cs)|*.cs", FileName = "Script_Instance.cs" };
        if (dlg.ShowDialog() == DialogResult.OK && !string.IsNullOrEmpty(_lastSource))
        {
            File.WriteAllText(dlg.FileName, _lastSource);
            SetScriptPath(dlg.FileName);
        }
    }

    private void CreateNewFile()
    {
        using var dlg = new SaveFileDialog { Filter = "C# script (*.cs)|*.cs", FileName = "Script_Instance.cs" };
        if (dlg.ShowDialog() == DialogResult.OK)
        {
            File.WriteAllText(dlg.FileName, DefaultTemplate);
            SetScriptPath(dlg.FileName);
        }
    }

    private void OpenInVsCode()
    {
        if (string.IsNullOrEmpty(_scriptPath)) return;
        try
        {
            Process.Start(new ProcessStartInfo("code", $"\"{_scriptPath}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"Could not launch VS Code ('code' on PATH?): {ex.Message}");
        }
    }

    public bool CanInsertParameter(GH_ParameterSide side, int index) => false;
    public bool CanRemoveParameter(GH_ParameterSide side, int index) => false;
    public IGH_Param? CreateParameter(GH_ParameterSide side, int index) => null;
    public bool DestroyParameter(GH_ParameterSide side, int index) => false;
    public void VariableParameterMaintenance() { }
}
