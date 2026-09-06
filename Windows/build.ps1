param(
    [ValidateSet('build', 'test', 'run', 'demo', 'publish')]
    [string]$Task = 'build',
    [ValidateSet('win-x64', 'win-arm64')]
    [string]$Runtime = 'win-x64'
)
$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot 'Codenotch/Codenotch.csproj'
switch ($Task) {
    'build' { dotnet build $project -c Release }
    'test' { dotnet run --project (Join-Path $PSScriptRoot 'Codenotch.Tests') -c Release }
    'run' { dotnet run --project $project -c Release }
    'demo' { dotnet run --project $project -c Release -- --demo }
    'publish' {
        $output = Join-Path $PSScriptRoot "artifacts/$Runtime"
        dotnet publish $project -c Release -r $Runtime --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $output
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot '../LICENSE') -Destination $output
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'README.md') -Destination $output
        Compress-Archive -Path "$output/*" -DestinationPath (Join-Path $PSScriptRoot "artifacts/Codenotch-$Runtime.zip") -Force
    }
}
exit $LASTEXITCODE
