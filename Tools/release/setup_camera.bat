@echo off
rem Diva camera gestures on Windows: run once. Creates a Python environment next to this file and installs the
rem tracking packages (MediaPipe, OpenCV). Needs Python 3.10 or newer from https://www.python.org/downloads/
rem (tick "Add python.exe to PATH" in its installer). After this, start Diva.exe: the camera starts by itself.
cd /d "%~dp0"
set PY=
where py >nul 2>nul && set PY=py -3
if "%PY%"=="" where python >nul 2>nul && set PY=python
if "%PY%"=="" (
  echo Python was not found. Install Python 3.10 or newer from https://www.python.org/downloads/ and run this again.
  pause
  exit /b 1
)
%PY% -c "import sys; sys.exit(0 if sys.version_info >= (3, 10) else 1)"
if errorlevel 1 (
  echo Python 3.10 or newer is needed. Install it from https://www.python.org/downloads/ and run this again.
  pause
  exit /b 1
)
if not exist .venv\Scripts\python.exe %PY% -m venv .venv
.venv\Scripts\python.exe -m pip install --upgrade pip
.venv\Scripts\python.exe -m pip install -r requirements.txt
if errorlevel 1 (
  echo Installing the tracking packages failed. Check the internet connection and run this again.
  pause
  exit /b 1
)
echo.
echo Camera gestures are ready. Start Diva.exe; the camera starts by itself.
echo If Windows blocks the camera: Settings ^> Privacy ^& security ^> Camera ^> allow desktop apps.
pause
