@echo off
cd /d "%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Register-ChemForgeMenu.ps1" %*
:done
echo.
echo Done. Press any key to close.
pause >nul
