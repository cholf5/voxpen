$scriptPath = Join-Path $PSScriptRoot '..\..\scripts\package.ps1'

Describe 'package.ps1' {
    It '提供可选版本参数和本地开发版本回退' {
        Test-Path $scriptPath | Should Be $true
        $content = Get-Content -Raw $scriptPath
        $content | Should Match '\[string\]\$Version'
        $content | Should Match 'dev\.'
    }

    It '发布 Windows x64 的自包含与运行时依赖单文件应用' {
        $content = Get-Content -Raw $scriptPath
        $content | Should Match "'publish', 'src/VoxPen.App/VoxPen.App.csproj'"
        $content | Should Match 'selfContainedArgument'
        $content | Should Match 'SelfContained \$true'
        $content | Should Match 'SelfContained \$false'
        $content | Should Match 'if \(\$SelfContained\).*EnableCompressionInSingleFile=true'
        $content | Should Match 'PublishSingleFile=true'
        $content | Should Match 'IncludeNativeLibrariesForSelfExtract=true'
        $content | Should Match 'EnableCompressionInSingleFile=true'
    }

    It '生成发布 zip、SHA256 与所需文档' {
        $content = Get-Content -Raw $scriptPath
        $content | Should Match 'Compress-Archive'
        $content | Should Match 'Get-FileHash'
        $content | Should Match 'README.md'
        $content | Should Match 'FIRST-RUN.txt'
    }

    It '用文件名说明小体积包需要 .NET 8 Runtime' {
        $content = Get-Content -Raw $scriptPath
        $content | Should Match 'requires-dotnet-8-runtime'
    }

    It '由发布工作流复用' {
        $workflow = Get-Content -Raw (Join-Path $PSScriptRoot '..\\..\\.github\\workflows\\release.yml')
        $workflow | Should Match 'scripts/package.ps1 -Version \$env:VERSION'
        $workflow | Should Match 'requires-dotnet-8-runtime\.zip'
        $workflow | Should Match '\$runtimeZip\.sha256'
    }

    It '忽略本地打包产物' {
        & git check-ignore -q -- staging/placeholder
        $LASTEXITCODE | Should Be 0
        & git check-ignore -q -- VoxPen-0.1.0-dev.20260724000000-win-x64.zip
        $LASTEXITCODE | Should Be 0
        & git check-ignore -q -- VoxPen-0.1.0-dev.20260724000000-win-x64.zip.sha256
        $LASTEXITCODE | Should Be 0
        & git check-ignore -q -- VoxPen-0.1.0-dev.20260724000000-win-x64-requires-dotnet-8-runtime.zip
        $LASTEXITCODE | Should Be 0
        & git check-ignore -q -- VoxPen-0.1.0-dev.20260724000000-win-x64-requires-dotnet-8-runtime.zip.sha256
        $LASTEXITCODE | Should Be 0
    }
}
