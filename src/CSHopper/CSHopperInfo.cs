using Grasshopper.Kernel;
using System;
using System.Drawing;

namespace CSHopper;

public class CSHopperInfo : GH_AssemblyInfo
{
    public override string Name => "CSHopper";
    public override Bitmap Icon => CSHopperIcon.Create(24);
    public override string Description => "Live-sync a Grasshopper C# script component to a .cs file editable in VS Code.";
    public override Guid Id => new Guid("4ac9b0dd-2c10-4844-ace2-72eeaab2a485");
    public override string AuthorName => "";
    public override string AuthorContact => "";
    public override string Version => "0.1.0";
}
