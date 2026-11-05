# Builds the APK, installs it on the connected Quest and starts it. Close the Unity Editor first:
# only one Unity can have the project open.
#
#   .\build_and_run.ps1             build, install, run
#   .\build_and_run.ps1 -NoBuild    reinstall the last build and run it
#   .\build_and_run.ps1 -Log        also follow the game's log afterwards (Ctrl+C to stop)

param(
    [switch]$NoBuild,
    [switch]$Log
)

$editor = 'C:\Program Files\Unity\Hub\Editor\6000.6.7f1\Editor'
$adb = "$editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe"
$package = 'com.alberthan.repulsortest'
$apk = "$PSScriptRoot\Builds\RepulsorTest.apk"

if (-not $NoBuild) {
    Write-Host 'Building (a few minutes the first time, faster after)...'
    $buildLog = "$PSScriptRoot\Logs\build.log"
    $unityArgs = @('-batchmode', '-nographics', '-projectPath', "`"$PSScriptRoot`"", '-buildTarget', 'Android',
        '-executeMethod', 'QuestBuild.Build', '-quit', '-logFile', "`"$buildLog`"")
    $unity = Start-Process -FilePath "$editor\Unity.exe" -ArgumentList $unityArgs -PassThru -Wait
    if ($unity.ExitCode -ne 0) {
        Get-Content $buildLog -Tail 30
        throw "Build failed; full log: $buildLog"
    }
}

if (-not (& $adb devices | Select-String "\sdevice$")) {
    & $adb devices
    throw 'No authorized headset. Plug it in, put it on and accept "Allow USB debugging".'
}

Write-Host 'Installing...'
& $adb install -r -g $apk
if ($LASTEXITCODE -ne 0) { throw 'Install failed.' }

& $adb shell monkey -p $package -c android.intent.category.LAUNCHER 1 | Out-Null
Write-Host 'Running on the headset.'

if ($Log) {
    & $adb logcat -c
    & $adb logcat -s Unity
}
