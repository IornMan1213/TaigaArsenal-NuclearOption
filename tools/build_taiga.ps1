# Headless build of Taiga Arsenal (Windows PowerShell, or pwsh on Linux): the models, then the Blueprinter mod (.nobp) in TaigaBuild/.
# Same steps as the MiG-29 mods' tools/build_mig29.ps1. Unity Hub must be running and signed in.
param(
    [string]$Project = $env:BLUEPRINTER_PROJECT,
    [string]$Unity = $(if ($env:UNITY_EDITOR) { $env:UNITY_EDITOR } elseif ($env:OS -eq 'Windows_NT') { "C:\Program Files\Unity\Hub\Editor\2022.3.62f2\Editor\Unity.exe" } else { "$HOME/Unity/Hub/Editor/2022.3.62f2/Editor/Unity" })
)
$ErrorActionPreference = "Stop"
$py = if ($env:OS -eq 'Windows_NT') { "python" } else { "python3" }
if (-not $Project -or -not (Test-Path (Join-Path $Project "Assets"))) { throw "Pass -Project <Blueprinter-Editor folder> or set BLUEPRINTER_PROJECT" }
$sa = Join-Path $Project "Library/ScriptAssemblies"
$dlls = "Assembly-CSharp.dll", "Assembly-CSharp-firstpass.dll"
$repo = Split-Path $PSScriptRoot

function Invoke-Unity([string[]]$extra, [string]$log) {
    $args = @('-batchmode', '-nographics', '-projectPath', "`"$Project`"", '-logFile', "`"$log`"") + $extra
    $p = Start-Process -FilePath $Unity -ArgumentList $args -PassThru -Wait
    return $p.ExitCode
}

# 0. The editor script into the project (the repo is the source of truth), and the models.
$dst = Join-Path $Project "Assets/Editor/TaigaTools"
New-Item -ItemType Directory -Force $dst | Out-Null
Copy-Item (Join-Path $repo "unity/TaigaTools/*.cs") $dst -Force
& $py (Join-Path $PSScriptRoot "arsenal_gen.py") (Join-Path $Project "TaigaSource") | Out-Null
if ($LASTEXITCODE -ne 0) { throw "arsenal_gen failed" }

# 1. Compile pass (Blueprinter keeps the game's Assembly-CSharp read-only in ScriptAssemblies, which fails batchmode compiles).
foreach ($f in $dlls) { $d = Join-Path $sa $f; if (Test-Path $d) { Set-ItemProperty $d IsReadOnly $false } }
$code = Invoke-Unity @('-quit') (Join-Path $Project "TaigaOut_compile.log")
if ($code -ne 0) { throw "compile pass failed ($code), see TaigaOut_compile.log" }

# 2. Blueprinter's GameAssemblySync (its delayCall never runs in batchmode): the game DLLs back, read-only.
foreach ($f in $dlls) {
    $d = Join-Path $sa $f
    if (Test-Path $d) { Set-ItemProperty $d IsReadOnly $false }
    Copy-Item (Join-Path $Project "Packages/nuclearoption/$f") $d -Force
    Set-ItemProperty $d IsReadOnly $true
}

# 3. Build pass.
$env:TAIGA_OUT = Join-Path $Project "TaigaBuild"
Remove-Item "$env:TAIGA_OUT/*.nobp" -ErrorAction SilentlyContinue
$log = Join-Path $Project "TaigaOut_build.log"
$code = Invoke-Unity @('-executeMethod', 'TaigaTools.TaigaArsenal.BuildAll') $log
Select-String -Path $log -Pattern '\[Taiga\]|\[Blueprinter\]|error CS|Exception' | Where-Object { $_.Line -notmatch 'Licensing' } | ForEach-Object { $_.Line }
Get-ChildItem $env:TAIGA_OUT -Filter *.nobp -ErrorAction SilentlyContinue | ForEach-Object { "OUTPUT $($_.FullName) $($_.Length)" }
if (-not (Get-ChildItem $env:TAIGA_OUT -Filter *.nobp -ErrorAction SilentlyContinue)) { throw "No .nobp came out of the build pass, see $log" }
if ($code -ne 0) { exit $code }

# 4. The release archive (the first release asset; what NOMNOM / NOMM install): unzipped into BepInEx/plugins/TaigaArsenal/.
$ver = [regex]::Match((Get-Content (Join-Path $repo "unity/TaigaTools/TaigaArsenal.cs") -Raw), 'Version = "([\d.]+)"').Groups[1].Value
$nobp = Join-Path $env:TAIGA_OUT "Taiga Arsenal_$ver.nobp"
if (-not (Test-Path $nobp)) { throw "$nobp not found (TaigaArsenal.Version is $ver)" }
$stage = Join-Path $env:TAIGA_OUT "zip"
Remove-Item $stage -Recurse -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $stage | Out-Null
Copy-Item $nobp, (Join-Path $repo "mod/doorman/*"), (Join-Path $repo "CREDITS.md"), (Join-Path $repo "LICENSE") $stage
@"
Taiga Arsenal $ver for Nuclear Option

Install: put these files in BepInEx/plugins/TaigaArsenal/ (delete any older Taiga Arsenal .nobp first).
Requires BepInEx 5, Blueprinter 2.1.1 or newer, and at least one supported aircraft mod:
MiG-29 Fulcrum, MiG-29SMT or Su-30VXN Gandiva.

https://github.com/IornMan1213/TaigaArsenal-NuclearOption
"@ | Set-Content (Join-Path $stage "README.txt")
$zip = Join-Path $env:TAIGA_OUT "TaigaArsenal_$ver.zip"
Remove-Item "$env:TAIGA_OUT/TaigaArsenal_*.zip" -ErrorAction SilentlyContinue
Compress-Archive -Path "$stage/*" -DestinationPath $zip -Force
Remove-Item $stage -Recurse
"OUTPUT $zip $((Get-Item $zip).Length)"
exit 0
