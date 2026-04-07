# CRNKernel Installation Script for Windows
# Usage: .\install-kernel.ps1 [-CondaEnv <env_name>] [-KernelPath <path>]
# Default: Install to system Jupyter

param(
    [string]$CondaEnv = "",
    [string]$KernelPath = ""
)

$SCRIPT_DIR = Split-Path -Parent $MyInvocation.MyCommand.Path
$KERNEL_NAME = "crn"

# Build the project if KernelPath not specified
if ([string]::IsNullOrEmpty($KernelPath)) {
    Write-Host "Building CRNKernel..."
    Push-Location $SCRIPT_DIR
    dotnet build -c Release | Out-Null
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Build failed!"
        exit 1
    }
    Pop-Location
    $KERNEL_PATH = "$SCRIPT_DIR\bin\Release\netcoreapp3.1\CRNKernel.dll"
} else {
    $KERNEL_PATH = $KernelPath
}

if (-not (Test-Path $KERNEL_PATH)) {
    Write-Error "CRNKernel.dll not found at: $KERNEL_PATH"
    exit 1
}

Write-Host "Kernel path: $KERNEL_PATH"

# Determine Jupyter executable
if (-not [string]::IsNullOrEmpty($CondaEnv)) {
    # Get conda path
    $CONDA_EXE = $env:CONDA_EXE
    if ([string]::IsNullOrEmpty($CONDA_EXE)) {
        $CONDA_EXE = "conda"
    }
    
    # Get environment path
    $ENV_LIST = & $CONDA_EXE env list --json 2>$null
    if ($?) {
        $ENV_JSON = $ENV_LIST | ConvertFrom-Json
        $ENV_PATHS = $ENV_JSON.envs
        $TARGET_PATH = $ENV_PATHS | Where-Object { $_ -like "*\$CondaEnv" -or $_ -like "*\envs\$CondaEnv" } | Select-Object -First 1
        if ([string]::IsNullOrEmpty($TARGET_PATH)) {
            Write-Error "Conda environment '$CondaEnv' not found!"
            exit 1
        }
        $JUPYTER_EXE = "$TARGET_PATH\Scripts\jupyter.exe"
    } else {
        # Fallback: assume default conda location
        $CONDA_ROOT = Split-Path (Split-Path $CONDA_EXE)
        $JUPYTER_EXE = "$CONDA_ROOT\envs\$CondaEnv\Scripts\jupyter.exe"
    }
    
    if (-not (Test-Path $JUPYTER_EXE)) {
        Write-Error "Jupyter not found in conda environment '$CondaEnv'!"
        Write-Host "Please run: conda activate $CondaEnv && conda install jupyter notebook"
        exit 1
    }
    Write-Host "Jupyter executable: $JUPYTER_EXE"
} else {
    # System Jupyter
    $JUPYTER_EXE = "jupyter"
}

# Create temporary directory
$TEMP_DIR = [System.IO.Path]::GetTempPath() + "crnkernel_" + [System.Guid]::NewGuid().ToString()
$KERNEL_DIR = "$TEMP_DIR\$KERNEL_NAME"
New-Item -ItemType Directory -Path $KERNEL_DIR -Force | Out-Null

# Create kernel.json with proper path escaping
# Use forward slashes for JSON compatibility (works on Windows too)
$FORWARD_SLASH_PATH = $KERNEL_PATH.Replace('\', '/')
$KERNEL_JSON = @"
{
    "argv": [
        "dotnet",
        "$FORWARD_SLASH_PATH",
        "--connection-file",
        "{connection_file}"
    ],
    "display_name": "CRN (F#)",
    "language": "crn",
    "name": "crn",
    "codemirror_mode": "text/plain",
    "env": {
        "JUPYTER_CONNECTION_FILE": "{connection_file}"
    }
}
"@

# Write without BOM
$UTF8_NO_BOM = New-Object System.Text.UTF8Encoding $false
[System.IO.File]::WriteAllText("$KERNEL_DIR\kernel.json", $KERNEL_JSON, $UTF8_NO_BOM)

# Copy logo
if (Test-Path "$SCRIPT_DIR\logo-64x64.svg") {
    Copy-Item "$SCRIPT_DIR\logo-64x64.svg" "$KERNEL_DIR\logo-64x64.svg"
    Write-Host "Logo copied."
}

# Install kernel
Write-Host "Installing kernel spec..."
& $JUPYTER_EXE kernelspec install "$KERNEL_DIR" --name "$KERNEL_NAME" --user

if ($LASTEXITCODE -ne 0) {
    Write-Error "Failed to install kernel spec!"
    Remove-Item -Recurse -Force $TEMP_DIR
    exit 1
}

# Cleanup
Remove-Item -Recurse -Force $TEMP_DIR

Write-Host ""
Write-Host "========================================"
Write-Host "CRN Kernel installed successfully!"
Write-Host "========================================"
Write-Host ""
if (-not [string]::IsNullOrEmpty($CondaEnv)) {
    Write-Host "Environment: $CondaEnv"
    Write-Host "To activate: conda activate $CondaEnv"
}
Write-Host "Kernel name: $KERNEL_NAME"
Write-Host ""
Write-Host "To start Jupyter Notebook:"
if (-not [string]::IsNullOrEmpty($CondaEnv)) {
    Write-Host "  conda activate $CondaEnv"
}
Write-Host "  jupyter notebook"
Write-Host ""
Write-Host "To verify:"
Write-Host "  jupyter kernelspec list"
Write-Host ""
Write-Host "To uninstall:"
Write-Host "  jupyter kernelspec uninstall $KERNEL_NAME"
