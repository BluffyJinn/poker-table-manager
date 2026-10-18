# Сборка встроенным компилятором C# из .NET Framework 4.8 — ничего ставить не нужно.
# Запуск: pwsh -File build.ps1   (или из PowerShell: .\build.ps1)
# .\build.ps1 -Keys — сборка с клавишами за столами в bin-keys; обычная в bin не трогается.
param([switch]$Keys)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if ($Keys) {
    $bin = Join-Path $root 'bin-keys'
    $name = 'TableManagerKeys.exe'
    $define = @('/define:ACTION_KEYS')
} else {
    $bin = Join-Path $root 'bin'
    $name = 'TableManager.exe'
    $define = @()
}
New-Item -ItemType Directory -Force $bin | Out-Null

$refs = @('/r:System.dll', '/r:System.Core.dll', '/r:System.Drawing.dll',
          '/r:System.Windows.Forms.dll', '/r:System.Web.Extensions.dll')
$src = Get-ChildItem (Join-Path $root 'src') -Filter *.cs | ForEach-Object FullName
$tests = Join-Path $root 'tests\LayoutTests.cs'

function Build($arguments) {
    & $csc /nologo /utf8output /codepage:65001 @arguments
    if ($LASTEXITCODE -ne 0) { throw "Ошибка компиляции" }
}

# Запущенный расстановщик держит свой exe: файл можно переименовать (Windows
# это разрешает), новая версия встанет рядом и заработает после перезапуска.
$exe = Join-Path $bin $name
if (Test-Path $exe) {
    Remove-Item "$exe.old" -ErrorAction SilentlyContinue
    if (Test-Path "$exe.old") { Rename-Item "$exe.old" ("$name.old." + (Get-Random)) }
    Rename-Item $exe "$name.old"
}

Build (@('/target:winexe', '/optimize+', "/out:$exe",
         "/win32manifest:$root\app.manifest") + $define + $refs + $src)

if ($Keys) {
    # Тесты с клавишами за столами: им нужны все исходники, точка входа — тесты.
    Build (@('/target:exe', '/main:LayoutTests', "/out:$bin\LayoutTests.exe") + $define + $refs + $src + $tests)
    # Стартовые настройки — от обычного расстановщика (его румы и указанные столы).
    $config = Join-Path $bin 'config.json'
    $plain = Join-Path $root 'bin\config.json'
    if (-not (Test-Path $config) -and (Test-Path $plain)) { Copy-Item $plain $config }
} else {
    # Тесты раскладки (консоль): Layout + Config без окон.
    $core = @('Layout.cs', 'Native.cs', 'Config.cs', 'Desktop.cs', 'Rules.cs') | ForEach-Object { Join-Path $root "src\$_" }
    Build (@('/target:exe', "/out:$bin\LayoutTests.exe") + $refs + $core + $tests)
}

# Тестовые «столы» для проверки без настоящих клиентов.
Build (@('/target:winexe', "/out:$bin\FakeTable.exe", "/win32manifest:$root\app.manifest") + $refs +
       (Join-Path $root 'tools\FakeTable.cs'))

Write-Host "Готово: $exe"
