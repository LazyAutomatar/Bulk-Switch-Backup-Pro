@echo off
echo Building Bulk Switch Backup Executable...

REM Find customtkinter installation path dynamically using python
FOR /F "tokens=*" %%g IN ('python -c "import customtkinter, os; print(os.path.dirname(customtkinter.__file__))"') do (SET CTK_PATH=%%g)

IF "%CTK_PATH%"=="" (
    echo Error: Could not find customtkinter installation path. Ensure it is installed.
    exit /b 1
)

echo CustomTkinter found at: %CTK_PATH%

python -m PyInstaller --noconfirm --onefile --windowed --add-data "%CTK_PATH%;customtkinter/" app.py

echo.
echo Build complete. You can find the standalone executable (app.exe) in the 'dist' folder.
pause
