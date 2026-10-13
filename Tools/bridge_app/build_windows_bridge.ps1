# Builds the self-contained Windows camera bridge that ships in the Diva Windows release (DivaTracking\diva_bridge\),
# so players need no Python. Same recipe as build_mac_bridge.sh; PyInstaller has to run on Windows for this.
# Needs Python 3.10+ (the v1.1 release used 3.13). From the repo root, in PowerShell:
#   powershell -ExecutionPolicy Bypass -File Tools\bridge_app\build_windows_bridge.ps1 -Work C:\diva_bridge_build
# Output: <Work>\dist\diva_bridge\ (diva_bridge.exe + _internal with the models and the gesture config), checked by
# --selftest. Copy that folder to <game folder>\DivaTracking\diva_bridge: Diva.exe starts it by itself
# (DigiPhantCameraPreview.LaunchPackagedBridge). bridge.py and the gesture settings are used unchanged.
param(
  [Parameter(Mandatory = $true)] [string] $Work,
  # a folder with bridge.py, diva_gestures.py, diva_gestures.json, diva_bridge.py, requirements.txt (default: this repo)
  [string] $Sources = '',
  # a folder with pose_landmarker_full.task, hand_landmarker.task, gesture_recognizer.task (default: download them)
  [string] $Models = '',
  [string] $Python = 'python'
)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path "$PSScriptRoot\..\..").Path
New-Item -ItemType Directory "$Work\src", "$Work\models" -Force | Out-Null
if ($Sources) {
  Copy-Item "$Sources\bridge.py", "$Sources\diva_gestures.py", "$Sources\diva_gestures.json", "$Sources\diva_bridge.py", "$Sources\requirements.txt" "$Work\src"
} else {
  Copy-Item "$root\TrackingOverrides\bridge.py", "$root\TrackingOverrides\diva_gestures.py", "$root\TrackingOverrides\diva_gestures.json", `
            "$root\TrackingOverrides\requirements.txt", "$root\Tools\bridge_app\diva_bridge.py" "$Work\src"
}
$base = 'https://storage.googleapis.com/mediapipe-models'
$urls = [ordered]@{
  'pose_landmarker_full.task' = "$base/pose_landmarker/pose_landmarker_full/float16/latest/pose_landmarker_full.task"
  'hand_landmarker.task'      = "$base/hand_landmarker/hand_landmarker/float16/latest/hand_landmarker.task"
  'gesture_recognizer.task'   = "$base/gesture_recognizer/gesture_recognizer/float16/latest/gesture_recognizer.task"
}
foreach ($name in $urls.Keys) {
  if ($Models) { Copy-Item "$Models\$name" "$Work\models" -Force }
  elseif (-not (Test-Path "$Work\models\$name")) { Invoke-WebRequest $urls[$name] -OutFile "$Work\models\$name" }
}
Set-Location $Work
if (-not (Test-Path 'venv\Scripts\python.exe')) { & $Python -m venv venv; if ($LASTEXITCODE -ne 0) { throw 'Need Python 3.10 or newer' } }
venv\Scripts\python.exe -m pip install -q --upgrade pip
venv\Scripts\python.exe -m pip install -q -r src\requirements.txt pyinstaller
if ($LASTEXITCODE -ne 0) { throw 'pip install failed' }
# Windows separates --add-data source and destination with ';'
venv\Scripts\pyinstaller.exe --noconfirm --clean --log-level WARN --onedir --name diva_bridge `
  --paths src --hidden-import bridge --hidden-import diva_gestures --collect-all mediapipe `
  --add-data "src\diva_gestures.json;." --add-data "models\pose_landmarker_full.task;." `
  --add-data "models\hand_landmarker.task;." --add-data "models\gesture_recognizer.task;." src\diva_bridge.py
if ($LASTEXITCODE -ne 0) { throw 'pyinstaller failed' }
dist\diva_bridge\diva_bridge.exe --selftest
if ($LASTEXITCODE -ne 0) { throw 'self-test failed' }
"Built $Work\dist\diva_bridge"
