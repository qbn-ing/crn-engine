#!/bin/bash
# CRNKernel Installation Script for Conda Environment (Linux/macOS)
# This script installs the CRN Kernel into a specific conda environment
# Usage: ./install-to-conda.sh [environment_name]
# Default environment: crn-test

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
KERNEL_NAME="crn"
KERNEL_DISPLAY_NAME="CRN (F#)"
CONDA_ENV="${1:-crn-test}"

echo "========================================"
echo "CRN Kernel Installation for Conda"
echo "========================================"
echo ""
echo "Target Conda Environment: $CONDA_ENV"
echo ""

# Check if conda is available
if ! command -v conda &> /dev/null; then
    echo "[ERROR] conda command not found!"
    echo "Please ensure conda is installed and added to PATH."
    exit 1
fi

# Check if the specified conda environment exists
echo "Checking conda environment '$CONDA_ENV'..."
if ! conda env list | grep -q "$CONDA_ENV"; then
    echo "[ERROR] Conda environment '$CONDA_ENV' not found!"
    echo ""
    echo "Available environments:"
    conda env list
    echo ""
    echo "To create the environment:"
    echo "  conda create -n $CONDA_ENV python=3.8 jupyter notebook"
    exit 1
fi

echo "Conda environment '$CONDA_ENV' found."
echo ""

# Get the path to the conda environment
CONDA_PATH=$(conda env list --json | python -c "import sys,json; envs=json.load(sys.stdin)['envs']; print([e for e in envs if e.endswith('/$CONDA_ENV') or e.endswith('/$CONDA_ENV/')[0] if any(e.endswith('/$CONDA_ENV') for e in envs) else '']" 2>/dev/null || echo "")

if [ -z "$CONDA_PATH" ]; then
    # Fallback: try to get path from conda info
    CONDA_PATH=$(conda info --base)/envs/$CONDA_ENV
fi

if [ ! -d "$CONDA_PATH" ]; then
    echo "[ERROR] Could not determine path for conda environment '$CONDA_ENV'"
    exit 1
fi

echo "Conda environment path: $CONDA_PATH"
echo ""

# Get the jupyter executable from the conda environment
if [[ "$OSTYPE" == "msys" || "$OSTYPE" == "win32" ]]; then
    JUPYTER_EXE="$CONDA_PATH/Scripts/jupyter.exe"
else
    JUPYTER_EXE="$CONDA_PATH/bin/jupyter"
fi

if [ ! -f "$JUPYTER_EXE" ]; then
    echo "[ERROR] Jupyter not found in conda environment '$CONDA_ENV'!"
    echo ""
    echo "Please install jupyter in the conda environment:"
    echo "  conda activate $CONDA_ENV"
    echo "  conda install jupyter notebook"
    exit 1
fi

echo "Jupyter executable: $JUPYTER_EXE"
echo ""

# Build the CRNKernel project first
echo "Building CRNKernel project..."
cd "$SCRIPT_DIR"
dotnet build -c Release
if [ $? -ne 0 ]; then
    echo "[ERROR] Build failed!"
    exit 1
fi
echo "Build completed."
echo ""

# Get the absolute path to the compiled DLL
if [[ "$OSTYPE" == "msys" || "$OSTYPE" == "win32" ]]; then
    KERNEL_PATH="$SCRIPT_DIR/bin/Release/netcoreapp3.1/CRNKernel.dll"
else
    KERNEL_PATH="$SCRIPT_DIR/bin/Release/netcoreapp3.1/CRNKernel.dll"
fi

if [ ! -f "$KERNEL_PATH" ]; then
    echo "[ERROR] CRNKernel.dll not found at: $KERNEL_PATH"
    echo "Please build the project first."
    exit 1
fi

echo "Kernel DLL path: $KERNEL_PATH"
echo ""

# Create temporary directory for kernel spec
TEMP_DIR=$(mktemp -d -t crnkernel.XXXXXX)
KERNEL_DIR="$TEMP_DIR/$KERNEL_NAME"
mkdir -p "$KERNEL_DIR"

echo "Creating kernel spec directory: $KERNEL_DIR"
echo ""

# Copy kernel.json and update path
KERNEL_PATH_ESCAPED=$(echo "$KERNEL_PATH" | sed 's/\\/\\\\/g')
if [[ "$OSTYPE" == "msys" || "$OSTYPE" == "win32" ]]; then
    # Windows: use PowerShell for replacement
    powershell -Command "(Get-Content '$SCRIPT_DIR/kernel.json') -replace '__KERNEL_PATH__', '$KERNEL_PATH_ESCAPED' | Set-Content '$KERNEL_DIR/kernel.json'"
else
    # Linux/macOS: use sed
    sed "s|__KERNEL_PATH__|$KERNEL_PATH|g" "$SCRIPT_DIR/kernel.json" > "$KERNEL_DIR/kernel.json"
fi

# Copy logo
if [ -f "$SCRIPT_DIR/logo-64x64.svg" ]; then
    cp "$SCRIPT_DIR/logo-64x64.svg" "$KERNEL_DIR/logo-64x64.svg"
    echo "Logo copied."
fi

echo "Kernel spec files created."
echo ""

# Install kernel spec to the conda environment
echo "Installing kernel spec to conda environment '$CONDA_ENV'..."
"$JUPYTER_EXE" kernelspec install "$KERNEL_DIR" --name "$KERNEL_NAME" --user

if [ $? -ne 0 ]; then
    echo "[ERROR] Failed to install kernel spec!"
    rm -rf "$TEMP_DIR"
    exit 1
fi

# Cleanup
rm -rf "$TEMP_DIR"

echo ""
echo "========================================"
echo "CRN Kernel installed successfully!"
echo "========================================"
echo ""
echo "Environment: $CONDA_ENV"
echo "Kernel Name: $KERNEL_NAME"
echo ""
echo "To activate the conda environment:"
echo "  conda activate $CONDA_ENV"
echo ""
echo "To start Jupyter Notebook with CRN Kernel:"
echo "  conda activate $CONDA_ENV"
echo "  jupyter notebook"
echo ""
echo "To verify installation:"
echo "  conda activate $CONDA_ENV"
echo "  jupyter kernelspec list"
echo ""
echo "To uninstall:"
echo "  conda activate $CONDA_ENV"
echo "  jupyter kernelspec uninstall $KERNEL_NAME"
echo ""
