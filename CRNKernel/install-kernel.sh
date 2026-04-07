#!/usr/bin/env bash

# CRNKernel Installation Script for Jupyter (Linux/macOS)
# This script installs the CRN Kernel into Jupyter
#
# Usage:
#   ./install-kernel.sh                    - Install to default Jupyter (auto-detect)
#   ./install-kernel.sh --conda ENV_NAME   - Install to specific conda environment
#   ./install-kernel.sh --conda            - Install to current activated conda environment
#   ./install-kernel.sh --prefix PATH      - Install to specific Python environment path
#   ./install-kernel.sh --sys              - Force install to system Jupyter (--user)
#   ./install-kernel.sh --help             - Show this help message
#
# Examples:
#   ./install-kernel.sh
#   ./install-kernel.sh --conda crn-test
#   ./install-kernel.sh --prefix /home/user/venv
#   ./install-kernel.sh --sys

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
KERNEL_NAME="crn"
KERNEL_DISPLAY_NAME="CRN (F#)"
BUILD_CONFIG="Debug"
DOTNET_FRAMEWORK="netcoreapp3.1"

# Show help
show_help() {
    echo "CRN Kernel Installation Script"
    echo ""
    echo "Usage:"
    echo "  ./install-kernel.sh                    - Install to default Jupyter (auto-detect)"
    echo "  ./install-kernel.sh --conda ENV_NAME   - Install to specific conda environment"
    echo "  ./install-kernel.sh --conda            - Install to current activated conda environment"
    echo "  ./install-kernel.sh --prefix PATH      - Install to specific Python environment path"
    echo "  ./install-kernel.sh --sys              - Force install to system Jupyter (--user)"
    echo "  ./install-kernel.sh --help             - Show this help message"
    echo ""
    echo "Examples:"
    echo "  ./install-kernel.sh"
    echo "  ./install-kernel.sh --conda crn-test"
    echo "  ./install-kernel.sh --prefix /home/user/venv"
    echo ""
}

# Check for help
if [[ "$1" == "--help" ]]; then
    show_help
    exit 0
fi

# Function to build the project
build_project() {
    echo "Building CRNKernel project..."
    cd "$SCRIPT_DIR"
    dotnet build -c "$BUILD_CONFIG"
    echo "Build completed."
    echo ""
}

# Function to get kernel path
get_kernel_path() {
    echo "$SCRIPT_DIR/bin/$BUILD_CONFIG/$DOTNET_FRAMEWORK/CRNKernel.dll"
}

# Function to install kernel spec
install_kernel_spec() {
    local JUPYTER_CMD="$1"
    local EXTRA_ARGS="${@:2}"
    
    # Build first
    build_project
    
    # Get the absolute path to the compiled DLL
    local KERNEL_PATH
    KERNEL_PATH=$(get_kernel_path)
    
    if [[ ! -f "$KERNEL_PATH" ]]; then
        echo "[ERROR] CRNKernel.dll not found at: $KERNEL_PATH"
        exit 1
    fi
    
    echo "Kernel DLL path: $KERNEL_PATH"
    echo ""
    
    # Create temporary directory for kernel spec
    local TEMP_DIR
    TEMP_DIR=$(mktemp -d)
    local KERNEL_DIR="$TEMP_DIR/$KERNEL_NAME"
    mkdir -p "$KERNEL_DIR"
    
    # Copy kernel.json and update path (escape backslashes for JSON)
    local ESCAPED_PATH
    ESCAPED_PATH=$(echo "$KERNEL_PATH" | sed 's/\\/\\\\/g')
    sed "s|__KERNEL_PATH__|$ESCAPED_PATH|g" "$SCRIPT_DIR/kernel.json" > "$KERNEL_DIR/kernel.json"
    
    # Copy logo if exists
    if [[ -f "$SCRIPT_DIR/logo-64x64.svg" ]]; then
        cp "$SCRIPT_DIR/logo-64x64.svg" "$KERNEL_DIR/logo-64x64.svg"
        echo "Logo copied"
    fi
    
    # Install kernel spec
    echo "Installing kernel spec..."
    "$JUPYTER_CMD" kernelspec install "$KERNEL_DIR" --name "$KERNEL_NAME" $EXTRA_ARGS
    
    # Cleanup
    rm -rf "$TEMP_DIR"
    
    echo ""
    echo "Kernel spec installed successfully."
    echo ""
}

# Check for --prefix argument
if [[ "$1" == "--prefix" ]]; then
    if [[ -z "$2" ]]; then
        echo "[ERROR] Please specify environment path"
        echo "Usage: ./install-kernel.sh --prefix PATH"
        exit 1
    fi
    
    ENV_PREFIX="$2"
    echo "========================================"
    echo "CRN Kernel Installation"
    echo "========================================"
    echo ""
    echo "Installing to environment: $ENV_PREFIX"
    echo ""
    
    JUPYTER_EXE="$ENV_PREFIX/bin/jupyter"
    if [[ ! -f "$JUPYTER_EXE" ]]; then
        echo "[ERROR] Jupyter not found at: $JUPYTER_EXE"
        echo "Please install jupyter in this environment:"
        echo "  pip install jupyter notebook"
        exit 1
    fi
    
    install_kernel_spec "$JUPYTER_EXE"
    
    echo "========================================"
    echo "CRN Kernel installed successfully!"
    echo "========================================"
    echo ""
    echo "Environment: $ENV_PREFIX"
    echo "Kernel Name: $KERNEL_NAME"
    echo ""
    echo "To start Jupyter Notebook:"
    echo "  $ENV_PREFIX/bin/jupyter notebook"
    echo ""
    echo "To verify installation:"
    echo "  $ENV_PREFIX/bin/jupyter kernelspec list"
    echo ""
    echo "To uninstall:"
    echo "  $ENV_PREFIX/bin/jupyter kernelspec uninstall $KERNEL_NAME"
    echo ""
    exit 0
fi

# Check for --sys argument
if [[ "$1" == "--sys" ]]; then
    echo "========================================"
    echo "CRN Kernel Installation"
    echo "========================================"
    echo ""
    echo "Installing to system Jupyter (--user)..."
    echo ""
    
    install_kernel_spec "jupyter" "--user"
    
    echo "========================================"
    echo "CRN Kernel installed successfully!"
    echo "========================================"
    echo ""
    echo "Installation: System Jupyter (--user)"
    echo "Kernel Name: $KERNEL_NAME"
    echo ""
    echo "To start Jupyter Notebook:"
    echo "  jupyter notebook"
    echo ""
    echo "To verify installation:"
    echo "  jupyter kernelspec list"
    echo ""
    echo "To uninstall:"
    echo "  jupyter kernelspec uninstall $KERNEL_NAME"
    echo ""
    exit 0
fi

# Check for --conda argument
if [[ "$1" == "--conda" ]]; then
    echo "========================================"
    echo "CRN Kernel Installation for Conda"
    echo "========================================"
    echo ""
    
    if [[ -z "$2" ]]; then
        # No environment name, try to use current activated conda environment
        if [[ -n "$CONDA_DEFAULT_ENV" ]]; then
            CONDA_ENV="$CONDA_DEFAULT_ENV"
            echo "Using current conda environment: $CONDA_ENV"
        else
            echo "[ERROR] No conda environment specified and no active conda environment found"
            echo ""
            echo "Usage: ./install-kernel.sh --conda ENV_NAME"
            echo "  or:  source activate myenv, then run: ./install-kernel.sh --conda"
            exit 1
        fi
    else
        CONDA_ENV="$2"
    fi
    
    echo "Target Conda Environment: $CONDA_ENV"
    echo ""
    
    # Find conda executable
    if [[ -n "$CONDA_EXE" ]]; then
        CONDA_EXE_PATH="$CONDA_EXE"
    elif command -v conda &> /dev/null; then
        CONDA_EXE_PATH=$(command -v conda)
    else
        # Try common conda installation locations
        if [[ -f "$HOME/anaconda3/bin/conda" ]]; then
            CONDA_EXE_PATH="$HOME/anaconda3/bin/conda"
        elif [[ -f "$HOME/miniconda3/bin/conda" ]]; then
            CONDA_EXE_PATH="$HOME/miniconda3/bin/conda"
        elif [[ -f "/opt/conda/bin/conda" ]]; then
            CONDA_EXE_PATH="/opt/conda/bin/conda"
        else
            echo "[ERROR] conda not found in common locations"
            echo "Please ensure conda is installed and in your PATH"
            exit 1
        fi
    fi
    
    echo "Conda executable: $CONDA_EXE_PATH"
    echo ""
    
    # Check if the specified conda environment exists
    echo "Checking conda environment '$CONDA_ENV'..."
    if ! "$CONDA_EXE_PATH" env list | grep -q "$CONDA_ENV"; then
        echo "[ERROR] Conda environment '$CONDA_ENV' not found!"
        echo ""
        echo "Available environments:"
        "$CONDA_EXE_PATH" env list
        exit 1
    fi
    
    echo "Conda environment '$CONDA_ENV' found."
    echo ""
    
    # Get the path to the conda environment
    CONDA_ENV_PATH=$("$CONDA_EXE_PATH" env list --json | python -c "import sys, json; envs = json.load(sys.stdin)['envs']; print(envs.get('$CONDA_ENV', '$CONDA_EXE_PATH/../envs/$CONDA_ENV'))" 2>/dev/null || echo "$CONDA_EXE_PATH/../envs/$CONDA_ENV")
    
    if [[ ! -d "$CONDA_ENV_PATH" ]]; then
        echo "[ERROR] Conda environment path not found: $CONDA_ENV_PATH"
        exit 1
    fi
    
    echo "Environment path: $CONDA_ENV_PATH"
    echo ""
    
    # Get the jupyter executable from the conda environment
    JUPYTER_EXE="$CONDA_ENV_PATH/bin/jupyter"
    
    if [[ ! -f "$JUPYTER_EXE" ]]; then
        echo "[ERROR] Jupyter not found in conda environment '$CONDA_ENV'!"
        echo "Please install jupyter in the conda environment:"
        echo "  conda activate $CONDA_ENV"
        echo "  conda install jupyter notebook"
        exit 1
    fi
    
    echo "Jupyter executable: $JUPYTER_EXE"
    echo ""
    
    install_kernel_spec "$JUPYTER_EXE"
    
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
    echo "To start Jupyter Notebook:"
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
    exit 0
fi

# Default installation - auto-detect best location
echo "========================================"
echo "CRN Kernel Installation"
echo "========================================"
echo ""
echo "Detecting Jupyter installation..."
echo ""

# Check if we're in an activated conda environment
if [[ -n "$CONDA_DEFAULT_ENV" ]]; then
    echo "Found active conda environment: $CONDA_DEFAULT_ENV"
    CONDA_ENV="$CONDA_DEFAULT_ENV"
    
    # Find conda executable
    if [[ -n "$CONDA_EXE" ]]; then
        CONDA_EXE_PATH="$CONDA_EXE"
    elif command -v conda &> /dev/null; then
        CONDA_EXE_PATH=$(command -v conda)
    else
        CONDA_EXE_PATH="$HOME/anaconda3/bin/conda"
    fi
    
    CONDA_ENV_PATH=$("$CONDA_EXE_PATH" env list --json | python -c "import sys, json; envs = json.load(sys.stdin)['envs']; print(envs.get('$CONDA_ENV', '$CONDA_EXE_PATH/../envs/$CONDA_ENV'))" 2>/dev/null || echo "$CONDA_EXE_PATH/../envs/$CONDA_ENV")
    JUPYTER_EXE="$CONDA_ENV_PATH/bin/jupyter"
    
    if [[ -f "$JUPYTER_EXE" ]]; then
        install_kernel_spec "$JUPYTER_EXE"
        
        echo "========================================"
        echo "CRN Kernel installed successfully!"
        echo "========================================"
        echo ""
        echo "Environment: $CONDA_ENV"
        echo "Kernel Name: $KERNEL_NAME"
        echo ""
        echo "To start Jupyter Notebook:"
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
        exit 0
    fi
fi

# Check if we're in a virtual environment
if [[ -n "$VIRTUAL_ENV" ]]; then
    echo "Found active virtual environment: $VIRTUAL_ENV"
    JUPYTER_EXE="$VIRTUAL_ENV/bin/jupyter"
    
    if [[ -f "$JUPYTER_EXE" ]]; then
        install_kernel_spec "$JUPYTER_EXE"
        
        echo "========================================"
        echo "CRN Kernel installed successfully!"
        echo "========================================"
        echo ""
        echo "Environment: $VIRTUAL_ENV"
        echo "Kernel Name: $KERNEL_NAME"
        echo ""
        echo "To start Jupyter Notebook:"
        echo "  $VIRTUAL_ENV/bin/jupyter notebook"
        echo ""
        echo "To verify installation:"
        echo "  $VIRTUAL_ENV/bin/jupyter kernelspec list"
        echo ""
        echo "To uninstall:"
        echo "  $VIRTUAL_ENV/bin/jupyter kernelspec uninstall $KERNEL_NAME"
        echo ""
        exit 0
    fi
fi

# Try to find Jupyter in PATH
if command -v jupyter &> /dev/null; then
    echo "Found Jupyter in PATH"
    install_kernel_spec "jupyter" "--user"
    
    echo "========================================"
    echo "CRN Kernel installed successfully!"
    echo "========================================"
    echo ""
    echo "Installation: System Jupyter (--user)"
    echo "Kernel Name: $KERNEL_NAME"
    echo ""
    echo "To start Jupyter Notebook:"
    echo "  jupyter notebook"
    echo ""
    echo "To verify installation:"
    echo "  jupyter kernelspec list"
    echo ""
    echo "To uninstall:"
    echo "  jupyter kernelspec uninstall $KERNEL_NAME"
    echo ""
    exit 0
fi

echo "[ERROR] Jupyter not found!"
echo ""
echo "Please ensure Jupyter is installed and in your PATH, or use one of these options:"
echo ""
echo "  1. Activate a conda environment with Jupyter:"
echo "     conda activate myenv"
echo "     ./install-kernel.sh"
echo ""
echo "  2. Specify conda environment explicitly:"
echo "     ./install-kernel.sh --conda myenv"
echo ""
echo "  3. Specify virtual environment path:"
echo "     ./install-kernel.sh --prefix /path/to/venv"
echo ""
echo "  4. Install Jupyter:"
echo "     pip install jupyter notebook"
echo ""
exit 1
