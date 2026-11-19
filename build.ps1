$root = $PSScriptRoot
$csc = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$fw = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319'
$dist = Join-Path $root 'dist'
if (-not (Test-Path $dist)) { New-Item -ItemType Directory -Path $dist | Out-Null }
$out = Join-Path $dist 'XuexitongWidget.exe'
$refs = @(
  ('/r:' + $fw + '\WPF\PresentationFramework.dll'),
  ('/r:' + $fw + '\WPF\PresentationCore.dll'),
  ('/r:' + $fw + '\WPF\WindowsBase.dll'),
  ('/r:' + $fw + '\System.Xaml.dll'),
  ('/r:' + $fw + '\System.dll'),
  ('/r:' + $fw + '\System.Core.dll'),
  ('/r:' + $fw + '\System.Net.Http.dll'),
  ('/r:' + $fw + '\System.Web.dll'),
  ('/r:' + $fw + '\System.Web.Extensions.dll'),
  ('/r:' + $fw + '\System.Security.dll')
)
$sources = @(
  (Join-Path $root 'src\Models.cs'),
  (Join-Path $root 'src\Config.cs'),
  (Join-Path $root 'src\XxtApi.cs'),
  (Join-Path $root 'src\WidgetWindow.cs'),
  (Join-Path $root 'src\LoginDialog.cs'),
  (Join-Path $root 'src\Program.cs')
)
$cscArgs = @('/nologo','/target:winexe','/platform:anycpu','/codepage:65001','/utf8output', ('/out:' + $out), ('/resource:' + (Join-Path $root 'src\ui.xaml') + ',ui.xaml'))
if (Test-Path (Join-Path $root 'app.manifest')) { $cscArgs += ('/win32manifest:' + (Join-Path $root 'app.manifest')) }
$icon = Join-Path $root 'app.ico'
if (Test-Path $icon) { $cscArgs += ('/win32icon:' + $icon) }
$cscArgs += $refs
$cscArgs += $sources
& $csc $cscArgs
$code = $LASTEXITCODE
Write-Output ('exit=' + $code)
if ($code -ne 0) { exit $code }
if (Test-Path $out) { Write-Output ('size=' + (Get-Item $out).Length + ' time=' + (Get-Item $out).LastWriteTime.ToString('HH:mm:ss')) }
