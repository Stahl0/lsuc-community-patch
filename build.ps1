param([string]$Version = "0.1.0")
$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$ffdec = Join-Path $root "tools\ffdec\ffdec-cli.jar"
$ruffleDir = Join-Path $root "tools\ruffle"
$csc = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$work = Join-Path $root "work"
$expectedHash = "453E7EDDEEC8DC77E14B07FD8CF7E057ED73A88CC762996C020FF9EF9C486BE9"

function Step($text) { Write-Host "== $text" }
function Need($path) { if (-not (Test-Path $path)) { throw "missing $path" } }
function FFDec([string[]]$arguments) {
    $errFile = Join-Path $work "ffdec-stderr.txt"
    $outFile = Join-Path $work "ffdec-stdout.txt"
    $quoted = @("-jar", "`"$ffdec`"") + ($arguments | ForEach-Object { "`"$_`"" })
    $p = Start-Process java -ArgumentList $quoted -NoNewWindow -Wait -PassThru -RedirectStandardError $errFile -RedirectStandardOutput $outFile
    $errors = Get-Content $errFile -Raw
    if ($p.ExitCode -ne 0 -or ($errors -and $errors -match 'SEVERE|TARGET NOT FOUND|Exception')) { throw "FFDec failed: $errors" }
}
New-Item -ItemType Directory -Force $work | Out-Null

function FindSteamSwf {
    $steamPath = (Get-ItemProperty "HKCU:\Software\Valve\Steam" -ErrorAction SilentlyContinue).SteamPath
    if (-not $steamPath) { return $null }
    $libs = @($steamPath.Replace('/', '\'))
    $vdf = Join-Path $libs[0] "steamapps\libraryfolders.vdf"
    if (Test-Path $vdf) {
        foreach ($m in [regex]::Matches((Get-Content $vdf -Raw), '"path"\s+"(.+?)"')) { $libs += $m.Groups[1].Value.Replace('\\', '\') }
    }
    foreach ($lib in $libs) {
        $swf = Join-Path $lib "steamapps\common\The Last Stand Trilogy\TheLastStandLegacyCollection.app\gamefiles\lsuc.swf"
        if (Test-Path $swf) { return $swf }
    }
    return $null
}

Need $ffdec
Need $csc
Need (Join-Path $ruffleDir "ruffle.exe")

Step "original game file"
$original = Join-Path $root "original\lsuc.swf"
if (-not (Test-Path $original)) {
    $steamSwf = FindSteamSwf
    if (-not $steamSwf) { throw "lsuc.swf not found in original\ or your Steam libraries" }
    New-Item -ItemType Directory -Force (Split-Path $original) | Out-Null
    Copy-Item $steamSwf $original
}
if ((Get-FileHash $original -Algorithm SHA256).Hash -ne $expectedHash) { throw "original\lsuc.swf is not the Steam version this patch targets" }

Step "export game bytecode (cached)"
$export = Join-Path $work "export"
$marker = Join-Path $export "source.sha256"
if (-not (Test-Path $marker) -or (Get-Content $marker) -ne $expectedHash) {
    $all = Join-Path $work "export-all"
    if (Test-Path $all) { Remove-Item $all -Recurse -Force }
    FFDec @("-format", "script:pcode", "-export", "script", $all, $original)
    foreach ($name in "frame_1", "frame_3") {
        New-Item -ItemType Directory -Force (Join-Path $export $name) | Out-Null
        Copy-Item (Join-Path $all "scripts\$name\DoAction.pcode") (Join-Path $export "$name\DoAction.pcode")
    }
    Remove-Item $all -Recurse -Force
    Set-Content $marker $expectedHash
}

Step "compile mod\lscp.as"
$compiled = Join-Path $work "compiled"
$lscpExport = Join-Path $work "lscp-export"
foreach ($d in $compiled, $lscpExport) { if (Test-Path $d) { Remove-Item $d -Recurse -Force } }
New-Item -ItemType Directory -Force $compiled | Out-Null
& py -I (Join-Path $root "scripts\blank_swf.py") (Join-Path $work "blank.swf")
FFDec @("-replace", (Join-Path $work "blank.swf"), (Join-Path $work "lscp.swf"), "\frame_1\DoAction", (Join-Path $root "mod\lscp.as"))
Need (Join-Path $work "lscp.swf")
FFDec @("-format", "script:pcode", "-export", "script", $lscpExport, (Join-Path $work "lscp.swf"))
Copy-Item (Join-Path $lscpExport "scripts\frame_1\DoAction.pcode") (Join-Path $compiled "lscp.pcode")

Step "apply recipe"
$patched = Join-Path $work "patched"
if (Test-Path $patched) { Remove-Item $patched -Recurse -Force }
& py -I (Join-Path $root "scripts\apply_recipe.py") $export (Join-Path $root "mod") $compiled $patched
if ($LASTEXITCODE -ne 0) { throw "recipe failed" }

Step "rebuild game"
$stage = Join-Path $work "stage1.swf"
$built = Join-Path $root "build\lsuc.swf"
New-Item -ItemType Directory -Force (Split-Path $built) | Out-Null
foreach ($f in $stage, $built) { if (Test-Path $f) { Remove-Item $f -Force } }
FFDec @("-replace", $original, $stage, "\frame_1\DoAction", (Join-Path $patched "frame_1\DoAction.pcode"))
Need $stage
FFDec @("-replace", $stage, $built, "\frame_3\DoAction", (Join-Path $patched "frame_3\DoAction.pcode"))
Need $built
Remove-Item $stage -Force

Step "make delta"
$name = "LSUC-Community-Patch"
$dist = Join-Path $root "dist\$name"
if (Test-Path $dist) { Remove-Item $dist -Recurse -Force }
New-Item -ItemType Directory -Force (Join-Path $dist "ruffle") | Out-Null
& py -I (Join-Path $root "scripts\make_delta.py") $original $built (Join-Path $dist "lsuc.delta")
if ($LASTEXITCODE -ne 0) { throw "delta failed" }

Step "compile launcher"
& $csc /nologo /target:winexe /platform:x64 /optimize+ /r:System.Windows.Forms.dll "/out:$(Join-Path $dist 'LSUCLauncher.exe')" (Join-Path $root "launcher\LSUCLauncher.cs")
if ($LASTEXITCODE -ne 0) { throw "launcher compile failed" }
Copy-Item (Join-Path $dist "LSUCLauncher.exe") (Join-Path $root "LSUCLauncher.exe") -Force

Step "assemble release"
foreach ($f in "ruffle.exe", "LICENSE.md", "README.md") { Copy-Item (Join-Path $ruffleDir $f) (Join-Path $dist "ruffle\$f") }
Copy-Item (Join-Path $root "release\README.txt") (Join-Path $dist "README.txt")
Copy-Item (Join-Path $root "release\Play windowed.cmd") (Join-Path $dist "Play windowed.cmd")
Copy-Item (Join-Path $root "LICENSE") (Join-Path $dist "LICENSE.txt")
$zip = Join-Path $root "dist\$name-v$Version.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path $dist -DestinationPath $zip
Write-Host ("built {0}  sha256 {1}" -f $built, (Get-FileHash $built).Hash)
Write-Host ("release {0}  {1:N1} MB" -f $zip, ((Get-Item $zip).Length / 1MB))
