@echo off
REM CRNKernel installer script for end users
REM Installs a prebuilt CRN Kernel into Jupyter
REM
REM Usage:
REM   install.bat                    - Install to system Jupyter
REM   install.bat --conda ENV_NAME   - Install to specific conda environment
REM   install.bat --help             - Show help

REM Switch to UTF-8 for localized output
if /I "%~1"=="--help" goto :show_help
chcp 65001 >nul

setlocal EnableDelayedExpansion

set SCRIPT_DIR=%~dp0
set KERNEL_NAME=crn

REM 自动检测 Conda 安装位置
set CONDA_ROOT=
if defined CONDA_EXE (
    REM 如果 CONDA_EXE 已设置，从中提取 CONDA_ROOT
    for %%I in ("%CONDA_EXE%") do set "CONDA_ROOT=%%~dpI.."
)

if not defined CONDA_ROOT (
    REM 尝试从 PATH 中查找 conda
    where conda >nul 2>nul
    if %ERRORLEVEL% EQU 0 (
        for /f "delims=" %%i in ('where conda') do (
            for %%I in ("%%i") do set "CONDA_ROOT=%%~dpI.."
            goto :found_conda
        )
    )
)

:found_conda
if not defined CONDA_ROOT (
    REM 尝试常见安装位置
    if exist "%USERPROFILE%\Anaconda3\Scripts\conda.exe" (
        set "CONDA_ROOT=%USERPROFILE%\Anaconda3"
    ) else if exist "%USERPROFILE%\miniconda3\Scripts\conda.exe" (
        set "CONDA_ROOT=%USERPROFILE%\miniconda3"
    ) else if exist "C:\ProgramData\miniconda3\Scripts\conda.exe" (
        set "CONDA_ROOT=C:\ProgramData\miniconda3"
    ) else if exist "C:\miniconda3\Scripts\conda.exe" (
        set "CONDA_ROOT=C:\miniconda3"
    )
)

REM 检查内核文件是否存在
set KERNEL_PATH=%SCRIPT_DIR%CRNKernel.dll
if not exist "%KERNEL_PATH%" (
    echo [错误] 未找到 CRNKernel.dll
    echo 位置：%KERNEL_PATH%
    echo.
    echo 请确保此脚本与 CRNKernel.dll 在同一目录下
    exit /b 1
)

echo ========================================
echo CRN Kernel 安装
echo ========================================
echo.

REM 检查是否指定了 conda 环境
if "%1"=="--conda" (
    if "%2"=="" (
        echo [错误] 请指定 conda 环境名称
        echo 用法：install.bat --conda ENV_NAME
        exit /b 1
    )
    set CONDA_ENV=%2
    goto :install_conda
)

REM 安装到系统 Jupyter
echo 正在安装到系统 Jupyter...
echo.

REM 创建临时目录
set TEMP_DIR=%TEMP%\crnkernel_%RANDOM%
set KERNEL_DIR=%TEMP_DIR%\%KERNEL_NAME%
mkdir "%KERNEL_DIR%"

REM 复制并配置 kernel.json
echo 配置内核...
REM 使用 PowerShell 创建 JSON 文件以正确处理路径转义
powershell -Command "$kernelPath = '%KERNEL_PATH%'.Replace('\', '\\'); $kernelJson = @{ argv = @('dotnet', $kernelPath, '--connection-file', '{connection_file}'); display_name = 'CRN (F#)'; language = 'crn'; name = 'crn'; codemirror_mode = 'text/plain'; env = @{ JUPYTER_CONNECTION_FILE = '{connection_file}' } }; $kernelJson | ConvertTo-Json -Depth 10 | Out-File -FilePath '%KERNEL_DIR%\kernel.json' -Encoding UTF8 -NoNewline"

REM 复制图标
if exist "%SCRIPT_DIR%logo-64x64.svg" (
    copy "%SCRIPT_DIR%logo-64x64.svg" "%KERNEL_DIR%\logo-64x64.svg" > nul 2>&1
)

REM 安装内核
echo 正在注册内核...
jupyter kernelspec install "%KERNEL_DIR%" --name "%KERNEL_NAME%"

if %ERRORLEVEL% NEQ 0 (
    echo [错误] 内核安装失败
    echo 请确保已安装 Jupyter:
    echo   pip install jupyter notebook
    rmdir /s /q "%TEMP_DIR%"
    exit /b 1
)

REM 清理临时目录
rmdir /s /q "%TEMP_DIR%"

echo.
echo ========================================
echo 安装成功!
echo ========================================
echo.
echo 内核名称：%KERNEL_NAME%
echo.
echo 启动 Jupyter Notebook:
echo   jupyter notebook
echo.
echo 验证安装:
echo   jupyter kernelspec list
echo.
echo 卸载内核:
echo   jupyter kernelspec uninstall %KERNEL_NAME%
echo.

endlocal
goto :eof

:show_help
echo CRN Kernel 安装脚本
echo.
echo 用法:
echo   install.bat                    - 安装到系统 Jupyter
echo   install.bat --conda ENV_NAME   - 安装到指定 conda 环境
echo   install.bat --help             - 显示帮助信息
echo.
exit /b 0

:install_conda
echo 正在安装到 Conda 环境：%CONDA_ENV%
echo.

REM 检查 conda 可执行文件
REM 保留环境变量中的 CONDA_EXE（如果有）

if not defined CONDA_EXE (
    if exist "%CONDA_ROOT%\Scripts\conda.exe" (
        set "CONDA_EXE=%CONDA_ROOT%\Scripts\conda.exe"
    ) else if exist "%USERPROFILE%\miniconda3\Scripts\conda.exe" (
        set "CONDA_EXE=%USERPROFILE%\miniconda3\Scripts\conda.exe"
    )
)

if not defined CONDA_EXE (
    where conda >nul 2>nul
    if %ERRORLEVEL% EQU 0 (
        for /f "delims=" %%i in ('where conda') do set "CONDA_EXE=%%i"
    )
)

if not defined CONDA_EXE (
    echo [错误] 未找到 conda
    exit /b 1
)

echo Conda: %CONDA_EXE%
echo.

REM 检查环境是否存在
"%CONDA_EXE%" env list 2>nul | findstr /C:"%CONDA_ENV%" >nul 2>nul
if %ERRORLEVEL% NEQ 0 (
    echo [错误] Conda 环境 '%CONDA_ENV%' 不存在
    echo.
    echo 可用环境:
    "%CONDA_EXE%" env list
    exit /b 1
)

REM 获取环境路径
set CONDA_ENV_PATH=
for /f "tokens=1,2*" %%a in ('"%CONDA_EXE%" env list 2^>nul ^| findstr /R /C:"^[ ]*%CONDA_ENV%[ ]"') do (
    if "%%b"=="*" (
        REM 当前环境行格式: envName * path
        set "CONDA_ENV_PATH=%%c"
    ) else (
        REM 非当前环境行格式: envName path
        set "CONDA_ENV_PATH=%%b"
    )
)

if not defined CONDA_ENV_PATH (
    set CONDA_ENV_PATH=%CONDA_ROOT%\envs\%CONDA_ENV%
)

REM 检查 Jupyter 是否存在
set JUPYTER_EXE=%CONDA_ENV_PATH%\Scripts\jupyter.exe
if not exist "%JUPYTER_EXE%" (
    echo [错误] 在环境 '%CONDA_ENV%' 中未找到 Jupyter
    echo 请运行：conda activate %CONDA_ENV% ^&^& conda install jupyter notebook
    exit /b 1
)

echo Jupyter: %JUPYTER_EXE%
echo.

REM 创建临时目录
set TEMP_DIR=%TEMP%\crnkernel_%RANDOM%
set KERNEL_DIR=%TEMP_DIR%\%KERNEL_NAME%
mkdir "%KERNEL_DIR%"

REM 复制并配置 kernel.json
echo 配置内核...
REM 使用 PowerShell 创建 JSON 文件以正确处理路径转义
powershell -Command "$kernelPath = '%KERNEL_PATH%'.Replace('\', '\\'); $kernelJson = @{ argv = @('dotnet', $kernelPath, '--connection-file', '{connection_file}'); display_name = 'CRN (F#)'; language = 'crn'; name = 'crn'; codemirror_mode = 'text/plain'; env = @{ JUPYTER_CONNECTION_FILE = '{connection_file}' } }; $kernelJson | ConvertTo-Json -Depth 10 | Out-File -FilePath '%KERNEL_DIR%\kernel.json' -Encoding UTF8 -NoNewline"

REM 复制图标
if exist "%SCRIPT_DIR%logo-64x64.svg" (
    copy "%SCRIPT_DIR%logo-64x64.svg" "%KERNEL_DIR%\logo-64x64.svg" > nul 2>&1
)

REM 安装内核
echo 正在注册内核...
"%JUPYTER_EXE%" kernelspec install "%KERNEL_DIR%" --name "%KERNEL_NAME%"

if %ERRORLEVEL% NEQ 0 (
    echo [错误] 内核安装失败
    rmdir /s /q "%TEMP_DIR%"
    exit /b 1
)

REM 清理临时目录
rmdir /s /q "%TEMP_DIR%"

echo.
echo ========================================
echo 安装成功!
echo ========================================
echo.
echo 环境：%CONDA_ENV%
echo 内核名称：%KERNEL_NAME%
echo.
echo 启动 Jupyter Notebook:
echo   conda activate %CONDA_ENV%
echo   jupyter notebook
echo.
echo 验证安装:
echo   conda activate %CONDA_ENV%
echo   jupyter kernelspec list
echo.
echo 卸载内核:
echo   conda activate %CONDA_ENV%
echo   jupyter kernelspec uninstall %KERNEL_NAME%
echo.

endlocal
