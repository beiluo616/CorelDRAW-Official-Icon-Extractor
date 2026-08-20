$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$out = Join-Path $root 'artifacts\win-x64'
$solution = Join-Path $root 'CDRIconExtractor.sln'
$appProject = Join-Path $root 'src\CDRIconExtractor.App\CDRIconExtractor.App.csproj'

Push-Location $root
try {
    Write-Host '== CorelDRAW官方图标提取器 / By北落果 ==' -ForegroundColor Cyan
    Write-Host "SDK: $(dotnet --version)"
    Write-Host '1/3 运行 Release 测试...'
    dotnet test $solution -c Release
    if ($LASTEXITCODE -ne 0) { throw "dotnet test failed with exit code $LASTEXITCODE" }

    Write-Host '2/3 发布 win-x64 自包含单文件...'
    Remove-Item $out -Recurse -Force -ErrorAction SilentlyContinue
    dotnet publish $appProject `
      -c Release -r win-x64 --self-contained true `
      -p:PublishSingleFile=true `
      -p:IncludeNativeLibrariesForSelfExtract=true `
      -p:EnableCompressionInSingleFile=true `
      -p:DebugType=embedded `
      -o $out
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE" }

    Write-Host '3/3 验证发布目录只有一个 EXE...'
    $files = @(Get-ChildItem $out -File)
    if ($files.Count -ne 1 -or $files[0].Extension -ne '.exe') {
        throw "Expected exactly one EXE, got: $($files.Name -join ', ')"
    }
    if ($files[0].Name -ne 'CorelDRAW官方图标提取器.exe') {
        throw "Unexpected executable name: $($files[0].Name)"
    }

    Write-Host "Published: $($files[0].FullName)" -ForegroundColor Green
}
finally {
    Pop-Location
}
