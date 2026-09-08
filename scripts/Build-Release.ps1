param([string]$CompilerPath)
$ErrorActionPreference='Stop'
$projectRoot=Split-Path $PSScriptRoot
Push-Location $projectRoot
try {
    if(!$CompilerPath){
        $candidates=@((Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),(Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'))
        $CompilerPath=$candidates | Where-Object {Test-Path -LiteralPath $_} | Select-Object -First 1
    }
    if(!$CompilerPath){throw 'Inno Setup 6을 설치하거나 -CompilerPath로 ISCC.exe를 지정하세요.'}
    if(Test-Path artifacts\app){throw 'artifacts/app이 이미 있습니다. 새 체크아웃에서 빌드하거나 이전 빌드 폴더를 별도로 보관하세요.'}
    dotnet run --project Tests/CoreTests.csproj -c Release
    if($LASTEXITCODE -ne 0){throw '핵심 테스트 실패'}
    node Tests/klas-reader.test.cjs
    if($LASTEXITCODE -ne 0){throw 'KLAS 테스트 실패'}
    dotnet publish TerminalWidget.csproj -c Release -r win-x64 --self-contained true -p:DebugType=None -p:DebugSymbols=false -o artifacts/app
    if($LASTEXITCODE -ne 0){throw '배포 빌드 실패'}
    & $CompilerPath installer/widget.iss
    if($LASTEXITCODE -ne 0){throw '설치 파일 생성 실패'}
    $setup=Get-Item artifacts/release/windows_todo_widget-setup-1.0.0-win-x64.exe
    $hash=(Get-FileHash $setup.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    [IO.File]::WriteAllText((Join-Path $setup.DirectoryName 'SHA256SUMS.txt'),$hash+'  '+$setup.Name+[Environment]::NewLine,[Text.Encoding]::ASCII)
} finally {Pop-Location}
