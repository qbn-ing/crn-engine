@echo off
REM CRNKernel Installation Script for Jupyter (Windows)
REM This script installs the CRN Kernel into Jupyter
REM
REM Usage:
REM   install-kernel.bat                    - Install to default Jupyter (auto-detect)
REM   install-kernel.bat --conda ENV_NAME   - Install to specific conda environment
REM   install-kernel.bat --conda            - Install to current activated conda environment
REM   install-kernel.bat --prefix PATH      - Install to specific Python environment path
REM   install-kernel.bat --sys              - Force install to system Jupyter
REM   install-kernel.bat --help             - Show this help message
REM
REM Examples:
REM   install-kernel.bat
REM   install-kernel.bat --conda crn-test
REM   install-kernel.bat --conda myenv
REM   install-kernel.bat --prefix C:\Users\MyName\venv
REM   install-kernel.bat --sys
taskkill /F /IM dotnet.exe
setlocal EnableDelayedExpansion

set SCRIPT_DIR=%~dp0
set KERNEL_NAME=crn
set KERNEL_DISPLAY_NAME="CRN (F#)"
set BUILD_CONFIG=Debug
set DOTNET_FRAMEWORK=netcoreapp3.1

REM Show help
if "%1"=="--help" (
    echo CRN Kernel Installation Script
    echo.
    echo Usage:
    echo   install-kernel.bat                    - Install to default Jupyter ^(auto-detect^)
    echo   install-kernel.bat --conda ENV_NAME   - Install to specific conda environment
    echo   install-kernel.bat --conda            - Install to current activated conda environment
    echo   install-kernel.bat --prefix PATH      - Install to specific Python environment path
    echo   install-kernel.bat --sys              - Force install to system Jupyter
    echo   install-kernel.bat --help             - Show this help message
    echo.
    echo Examples:
    echo   install-kernel.bat
    echo   install-kernel.bat --conda crn-test
    echo   install-kernel.bat --prefix C:\Users\MyName\venv
    echo.
    exit /b 0
)

REM Check for --prefix argument
if "%1"=="--prefix" (
    if "%2"=="" (
        echo [ERROR] Please specify environment path
        echo Usage: install-kernel.bat --prefix PATH
        exit /b 1
    )
    set ENV_PREFIX=%2
    goto :install_prefix
)

REM Check for --sys argument
if "%1"=="--sys" (
    goto :install_system
)

REM Check if --conda argument is provided
if "%1"=="--conda" (
    if "%2"=="" (
        REM No environment name, try to use current activated conda environment
        if defined CONDA_DEFAULT_ENV (
            set CONDA_ENV=%CONDA_DEFAULT_ENV%
            echo Using current conda environment: %CONDA_ENV%
        ) else (
            echo [ERROR] No conda environment specified and no active conda environment found
            echo.
            echo Usage: install-kernel.bat --conda ENV_NAME
            echo   or:  activate your conda environment first, then run: install-kernel.bat --conda
            exit /b 1
        )
    ) else (
        set CONDA_ENV=%2
    )
    goto :install_conda
)

REM Default installation - auto-detect best location
echo ========================================
echo CRN Kernel Installation
echo ========================================
echo.
echo Detecting Jupyter installation...
echo.

REM Check if we're in an activated conda environment
if defined CONDA_DEFAULT_ENV (
    echo Found active conda environment: %CONDA_DEFAULT_ENV%
    set CONDA_ENV=%CONDA_DEFAULT_ENV%
    goto :install_conda
)

REM Check if we're in a virtual environment
if defined VIRTUAL_ENV (
    echo Found active virtual environment: %VIRTUAL_ENV%
    set ENV_PREFIX=%VIRTUAL_ENV%
    goto :install_prefix
)

REM Try to find Jupyter in system PATH
where jupyter >nul 2>nul
if %ERRORLEVEL% EQU 0 (
    echo Found Jupyter in system PATH
    goto :install_system
)

REM Try to find Jupyter in common locations
if exist "%LOCALAPPDATA%\Programs\Python\Python39\Scripts\jupyter.exe" (
    echo Found Jupyter in default Python installation
    set ENV_PREFIX=%LOCALAPPDATA%\Programs\Python\Python39
    goto :install_prefix
)

if exist "%LOCALAPPDATA%\Programs\Python\Python38\Scripts\jupyter.exe" (
    echo Found Jupyter in default Python installation
    set ENV_PREFIX=%LOCALAPPDATA%\Programs\Python\Python38
    goto :install_prefix
)

if exist "%LOCALAPPDATA%\Programs\Python\Python310\Scripts\jupyter.exe" (
    echo Found Jupyter in default Python installation
    set ENV_PREFIX=%LOCALAPPDATA%\Programs\Python\Python310
    goto :install_prefix
)

if exist "%LOCALAPPDATA%\Programs\Python\Python311\Scripts\jupyter.exe" (
    echo Found Jupyter in default Python installation
    set ENV_PREFIX=%LOCALAPPDATA%\Programs\Python\Python311
    goto :install_prefix
)

echo [ERROR] Jupyter not found!
echo.
echo Please ensure Jupyter is installed and in your PATH, or use one of these options:
echo.
echo   1. Activate a conda environment with Jupyter:
echo      conda activate myenv
echo      install-kernel.bat
echo.
echo   2. Specify conda environment explicitly:
echo      install-kernel.bat --conda myenv
echo.
echo   3. Specify virtual environment path:
echo      install-kernel.bat --prefix C:\path\to\venv
echo.
echo   4. Install Jupyter:
echo      pip install jupyter notebook
echo.
exit /b 1

:install_system
REM Installation to system Jupyter
echo Installing CRN Kernel to system Jupyter...
echo.
call :install_to_jupyter "jupyter"
if %ERRORLEVEL% NEQ 0 (
    exit /b 1
)
goto :success_system

:install_prefix
REM Installation to specific environment prefix
echo Installing CRN Kernel to environment: %ENV_PREFIX%
echo.
set JUPYTER_EXE=%ENV_PREFIX%\Scripts\jupyter.exe
if not exist "%JUPYTER_EXE%" (
    echo [ERROR] Jupyter not found at: %JUPYTER_EXE%
    echo Please install jupyter in this environment:
    echo   pip install jupyter notebook
    exit /b 1
)
call :install_to_jupyter "%JUPYTER_EXE%"
if %ERRORLEVEL% NEQ 0 (
    exit /b 1
)
goto :success_prefix

:install_conda
REM Installation to conda environment
echo ========================================
echo CRN Kernel Installation for Conda
echo ========================================
echo.
echo Target Conda Environment: %CONDA_ENV%
echo.

REM Try to find conda executable
REM Keep existing CONDA_EXE from environment if available

if not defined CONDA_EXE (
    REM Try common conda installation locations
    if exist "%USERPROFILE%\Anaconda3\Scripts\conda.exe" (
        set CONDA_EXE=%USERPROFILE%\Anaconda3\Scripts\conda.exe
    ) else if exist "%USERPROFILE%\miniconda3\Scripts\conda.exe" (
        set CONDA_EXE=%USERPROFILE%\miniconda3\Scripts\conda.exe
    ) else if exist "C:\Anaconda3\Scripts\conda.exe" (
        set CONDA_EXE=C:\Anaconda3\Scripts\conda.exe
    ) else if exist "C:\miniconda3\Scripts\conda.exe" (
        set CONDA_EXE=C:\miniconda3\Scripts\conda.exe
    ) else if exist "%LOCALAPPDATA%\miniconda3\Scripts\conda.exe" (
        set CONDA_EXE=%LOCALAPPDATA%\miniconda3\Scripts\conda.exe
    ) else if exist "%ProgramData%\miniconda3\Scripts\conda.exe" (
        set CONDA_EXE=%ProgramData%\miniconda3\Scripts\conda.exe
    ) else if exist "D:\Software\Anaconda3\Scripts\conda.exe" (
        set CONDA_EXE=D:\Software\Anaconda3\Scripts\conda.exe
    )
)

if not defined CONDA_EXE (
    REM Try to find conda in PATH
    where conda >nul 2>nul
    if %ERRORLEVEL% EQU 0 (
        for /f "delims=" %%i in ('where conda') do set "CONDA_EXE=%%i"
    )
)

if not defined CONDA_EXE (
    echo [ERROR] conda.exe not found in common locations
    echo Please ensure conda is installed and in your PATH
    exit /b 1
)

echo Conda executable: %CONDA_EXE%
echo.

REM Check if the specified conda environment exists
echo Checking conda environment '%CONDA_ENV%'...
"%CONDA_EXE%" env list 2>nul | findstr /C:"%CONDA_ENV%" >nul 2>nul
if %ERRORLEVEL% NEQ 0 (
    echo [ERROR] Conda environment '%CONDA_ENV%' not found!
    echo.
    echo Available environments:
    "%CONDA_EXE%" env list
    exit /b 1
)

echo Conda environment '%CONDA_ENV%' found.
echo.

REM Get the path to the conda environment using conda info
set CONDA_ENV_PATH=
for /f "tokens=*" %%i in ('"%CONDA_EXE%" info --base 2^>nul') do set CONDA_BASE=%%i

if "%CONDA_ENV%"=="base" (
    set CONDA_ENV_PATH=!CONDA_BASE!
) else (
    set CONDA_ENV_PATH=!CONDA_BASE!\envs\%CONDA_ENV%
)

if not exist "%CONDA_ENV_PATH%" (
    echo [ERROR] Conda environment path not found: %CONDA_ENV_PATH%
    exit /b 1
)

echo Environment path: %CONDA_ENV_PATH%
echo.

REM Get the jupyter executable from the conda environment
set JUPYTER_EXE=%CONDA_ENV_PATH%\Scripts\jupyter.exe

if not exist "%JUPYTER_EXE%" (
    echo [ERROR] Jupyter not found in conda environment '%CONDA_ENV%'!
    echo Please install jupyter in the conda environment:
    echo   conda activate %CONDA_ENV%
    echo   conda install jupyter notebook
    exit /b 1
)

echo Jupyter executable: %JUPYTER_EXE%
echo.

call :install_to_jupyter "%JUPYTER_EXE%"
if %ERRORLEVEL% NEQ 0 (
    exit /b 1
)
goto :success_conda

goto :eof

REM ============================================================
REM Installation function - builds and installs to specified jupyter
REM ============================================================
:install_to_jupyter
set JUPYTER_CMD=%~1

REM Build the CRNKernel project first
echo Building CRNKernel project...
cd /d "%SCRIPT_DIR%"
dotnet build -c %BUILD_CONFIG%
if %ERRORLEVEL% NEQ 0 (
    echo [ERROR] Build failed!
    exit /b 1
)
echo Build completed.
echo.

REM Get the absolute path to the compiled DLL
set KERNEL_PATH=%SCRIPT_DIR%bin\%BUILD_CONFIG%\%DOTNET_FRAMEWORK%\CRNKernel.dll

if not exist "%KERNEL_PATH%" (
    echo [ERROR] CRNKernel.dll not found at: %KERNEL_PATH%
    exit /b 1
)

echo Kernel DLL path: %KERNEL_PATH%
echo.

REM Create temporary directory for kernel spec
set TEMP_DIR=%TEMP%\crnkernel_%RANDOM%
set KERNEL_DIR=%TEMP_DIR%\%KERNEL_NAME%
mkdir "%KERNEL_DIR%"

REM Copy kernel.json and update path (escape backslashes for JSON)
powershell -Command "(Get-Content '%SCRIPT_DIR%kernel.json') -replace '__KERNEL_PATH__', '%KERNEL_PATH:\=\\%' | Set-Content '%KERNEL_DIR%\kernel.json'"

REM Copy logo if exists
if exist "%SCRIPT_DIR%logo-64x64.svg" (
    copy "%SCRIPT_DIR%logo-64x64.svg" "%KERNEL_DIR%\logo-64x64.svg" > nul 2>&1
    echo Logo copied
)

REM Install kernel spec
echo Installing kernel spec...
"%JUPYTER_CMD%" kernelspec install "%KERNEL_DIR%" --name "%KERNEL_NAME%"

if %ERRORLEVEL% NEQ 0 (
    echo [ERROR] Failed to install kernel spec!
    rmdir /s /q "%TEMP_DIR%"
    exit /b 1
)

REM Cleanup
rmdir /s /q "%TEMP_DIR%"

echo.
echo Kernel spec installed successfully.
echo.
exit /b 0

REM ============================================================
REM Success handlers
REM ============================================================
:success_system
echo ========================================
echo CRN Kernel installed successfully!
echo ========================================
echo.
echo Installation: System Jupyter
echo Kernel Name: %KERNEL_NAME%
echo.
echo To start Jupyter Notebook:
echo   jupyter notebook
echo.
echo To verify installation:
echo   jupyter kernelspec list
echo.
echo To uninstall:
echo   jupyter kernelspec uninstall %KERNEL_NAME%
echo.
exit /b 0

:success_prefix
echo ========================================
echo CRN Kernel installed successfully!
echo ========================================
echo.
echo Environment: %ENV_PREFIX%
echo Kernel Name: %KERNEL_NAME%
echo.
echo To start Jupyter Notebook:
echo   %ENV_PREFIX%\Scripts\jupyter.exe notebook
echo.
echo To verify installation:
echo   %ENV_PREFIX%\Scripts\jupyter.exe kernelspec list
echo.
echo To uninstall:
echo   %ENV_PREFIX%\Scripts\jupyter.exe kernelspec uninstall %KERNEL_NAME%
echo.
exit /b 0

:success_conda
echo ========================================
echo CRN Kernel installed successfully!
echo ========================================
echo.
echo Environment: %CONDA_ENV%
echo Kernel Name: %KERNEL_NAME%
echo.
echo To activate the conda environment:
echo   conda activate %CONDA_ENV%
echo.
echo To start Jupyter Notebook:
echo   conda activate %CONDA_ENV%
echo   jupyter notebook
echo.
echo To verify installation:
echo   conda activate %CONDA_ENV%
echo   jupyter kernelspec list
echo.
echo To uninstall:
echo   conda activate %CONDA_ENV%
echo   jupyter kernelspec uninstall %KERNEL_NAME%
echo.
exit /b 0

endlocal
