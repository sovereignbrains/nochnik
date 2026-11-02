$ErrorActionPreference = 'Stop'
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
& $csc /nologo /target:winexe /optimize+ /codepage:65001 /platform:anycpu `
  /win32manifest:app.manifest `
  /r:System.Windows.Forms.dll /r:System.Drawing.dll `
  /out:Night.exe Night.cs
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Write-Output 'built'
