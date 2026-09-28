$ErrorActionPreference = 'Stop'
[System.Reflection.Assembly]::LoadFrom("C:\Program Files\Rhino 8\System\RhinoCommon.dll") | Out-Null
[System.Reflection.Assembly]::LoadFrom("C:\Program Files\Rhino 8\Plug-ins\Grasshopper\GH_IO.dll") | Out-Null
[System.Reflection.Assembly]::LoadFrom("C:\Program Files\Rhino 8\Plug-ins\Grasshopper\GH_Util.dll") | Out-Null

$ghPath = "C:\Program Files\Rhino 8\Plug-ins\Grasshopper\Grasshopper.dll"
$asm = [System.Reflection.Assembly]::LoadFrom($ghPath)

try {
    $allTypes = $asm.GetTypes()
} catch [System.Reflection.ReflectionTypeLoadException] {
    $allTypes = $_.Exception.Types | Where-Object { $_ -ne $null }
    Write-Output "LoaderExceptions count: $($_.Exception.LoaderExceptions.Count)"
    $_.Exception.LoaderExceptions | Select-Object -First 3 | ForEach-Object { Write-Output $_.Message }
}

Write-Output "Total types loaded: $($allTypes.Count)"
$matches = $allTypes | Where-Object { $_.Name -eq "GH_ScriptInstance" }
$matches | ForEach-Object { Write-Output $_.FullName }
$t = $matches | Select-Object -First 1

Write-Output "----TYPE----"
if ($t) {
    Write-Output $t.FullName
    Write-Output $t.BaseType.FullName
    Write-Output "----ALL MEMBERS (incl non-public, incl inherited)----"
    $flags = [System.Reflection.BindingFlags]::Public -bor [System.Reflection.BindingFlags]::NonPublic -bor [System.Reflection.BindingFlags]::Instance -bor [System.Reflection.BindingFlags]::Static
    $t.GetMembers($flags) |
        Sort-Object MemberType, Name |
        ForEach-Object { Write-Output ("{0,-10} {1}" -f $_.MemberType, $_.ToString()) }

    Write-Output "----Components namespace types----"
$compTypes = $allTypes | Where-Object { $_.Namespace -eq "Grasshopper.Kernel.Components" -and ($_.Name -match "Script|NET|CSharp|Code") }
$compTypes | ForEach-Object { Write-Output $_.FullName }
    $m = $t.GetMethod("InvokeRunScript")
    $m.GetParameters() | ForEach-Object { Write-Output ("{0} {1}" -f $_.ParameterType.FullName, $_.Name) }

    Write-Output "----CONSTRUCTORS----"
    $t.GetConstructors($flags) | ForEach-Object { Write-Output $_.ToString() }
} else {
    Write-Output "GH_ScriptInstance type not found."
}
