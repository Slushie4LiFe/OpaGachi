@echo off
rem Converts the sprite template clips (..\1TEMPLATES by default) into assets\sprites\_template.
rem Extra arguments are passed through, e.g.  build-assets.cmd --height 200 --fps 15
setlocal
cd /d "%~dp0"
if not exist ".tools\venv\Scripts\python.exe" (
  echo Creating tools virtualenv...
  python -m venv .tools\venv || goto :fail
  ".tools\venv\Scripts\python.exe" -m pip install -q -r tools\requirements.txt || goto :fail
)
".tools\venv\Scripts\python.exe" tools\build_assets.py %* || goto :fail
exit /b 0
:fail
echo Asset build failed.
exit /b 1
