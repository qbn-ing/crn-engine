# CRNKernel Installation Guide

## Quick Start

### Windows

```bash
# Option 1: Auto-detect Jupyter (recommended)
install-kernel.bat

# Option 2: Install to specific conda environment
install-kernel.bat --conda crn-test

# Option 3: Install to current activated conda environment
conda activate crn-test
install-kernel.bat

# Option 4: Install to virtual environment
install-kernel.bat --prefix C:\path\to\venv
```

### Linux/macOS

```bash
# Option 1: Auto-detect Jupyter (recommended)
./install-kernel.sh

# Option 2: Install to specific conda environment
./install-kernel.sh --conda crn-test

# Option 3: Install to current activated conda environment
conda activate crn-test
./install-kernel.sh

# Option 4: Install to virtual environment
./install-kernel.sh --prefix /path/to/venv

# Option 5: Install to system Jupyter (--user)
./install-kernel.sh --sys
```

## Command Line Options

| Option | Description |
|--------|-------------|
| (none) | Auto-detect Jupyter installation (checks conda, venv, then system) |
| `--conda ENV_NAME` | Install to specific conda environment |
| `--conda` | Install to currently activated conda environment |
| `--prefix PATH` | Install to specific Python environment path |
| `--sys` | Force install to system Jupyter (uses `--user` on Linux/macOS) |
| `--help` | Show help message |

## Prerequisites

1. **.NET Core SDK 3.1** or later
2. **Jupyter Notebook** 5.0 or later
3. **Python** 3.6 or later

## Installation Scenarios

### Scenario 1: Default Installation (Auto-detect)

The script will automatically detect the best installation location:

1. **Active conda environment** - If `CONDA_DEFAULT_ENV` is set
2. **Active virtual environment** - If `VIRTUAL_ENV` is set
3. **System Jupyter** - If Jupyter is found in PATH

```bash
# Windows
install-kernel.bat

# Linux/macOS
./install-kernel.sh
```

### Scenario 2: Conda Environment

#### Specific Environment

```bash
# Windows
install-kernel.bat --conda crn-test

# Linux/macOS
./install-kernel.sh --conda crn-test
```

#### Currently Activated Environment

```bash
# Activate first
conda activate crn-test

# Then install (script will detect active environment)
# Windows
install-kernel.bat

# Linux/macOS
./install-kernel.sh
```

### Scenario 3: Python Virtual Environment

```bash
# Create and activate virtual environment
python -m venv venv
source venv/bin/activate  # Linux/macOS
venv\Scripts\activate     # Windows

# Install jupyter in the venv
pip install jupyter notebook

# Install kernel
# Windows
install-kernel.bat --prefix /path/to/venv

# Linux/macOS
./install-kernel.sh --prefix /path/to/venv
```

### Scenario 4: System-wide Installation

```bash
# Linux/macOS (uses --user flag)
./install-kernel.sh --sys

# Windows (installs to user site-packages)
install-kernel.bat --sys
```

## Verification

After installation, verify the kernel is installed:

```bash
jupyter kernelspec list
```

You should see `crn` in the list of available kernels.

## Usage

Start Jupyter Notebook:

```bash
# If installed to conda environment
conda activate crn-test
jupyter notebook

# If installed to system
jupyter notebook
```

Create a new notebook and select **CRN (F#)** kernel.

## Uninstallation

```bash
# For conda environment
conda activate crn-test
jupyter kernelspec uninstall crn

# For system installation
jupyter kernelspec uninstall crn

# For virtual environment
/path/to/venv/bin/jupyter kernelspec uninstall crn
```

## Troubleshooting

### "Jupyter not found"

Ensure Jupyter is installed:

```bash
pip install jupyter notebook
```

Or in conda:

```bash
conda install jupyter notebook
```

### "CRNKernel.dll not found"

Build the project first:

```bash
cd CRNKernel
dotnet build
```

### "Conda environment not found"

List available conda environments:

```bash
conda env list
```

Create a new environment if needed:

```bash
conda create -n crn-test python=3.9
conda activate crn-test
conda install jupyter notebook
```

### Kernel not showing in Jupyter

1. Restart Jupyter Notebook server
2. Clear browser cache
3. Verify installation: `jupyter kernelspec list`

### Permission denied (Linux/macOS)

Use `--user` flag or `--sys` option:

```bash
./install-kernel.sh --sys
```

Or install to a user-writable location:

```bash
./install-kernel.sh --prefix $HOME/.local
```

## Advanced: Manual Installation

If the automatic installation doesn't work, you can manually install:

1. **Build the project:**
   ```bash
   dotnet build -c Debug
   ```

2. **Create kernel spec directory:**
   ```bash
   mkdir -p $(jupyter --data-dir)/kernels/crn
   ```

3. **Copy kernel.json and update path:**
   ```bash
   sed "s|__KERNEL_PATH__|$(pwd)/bin/Debug/netcoreapp3.1/CRNKernel.dll|g" kernel.json > $(jupyter --data-dir)/kernels/crn/kernel.json
   ```

4. **Copy logo:**
   ```bash
   cp logo-64x64.svg $(jupyter --data-dir)/kernels/crn/
   ```

5. **Verify:**
   ```bash
   jupyter kernelspec list
   ```

## Environment Variables

The installation script respects these environment variables:

- `CONDA_EXE` - Path to conda executable
- `CONDA_DEFAULT_ENV` - Name of active conda environment
- `VIRTUAL_ENV` - Path to active virtual environment
- `PATH` - Used to find jupyter executable

## Cross-Platform Notes

### Windows

- Uses `.bat` script
- Conda is typically installed in `C:\Anaconda3` or `%USERPROFILE%\miniconda3`
- Virtual environments are in `path\to\venv\Scripts\jupyter.exe`

### Linux/macOS

- Uses `.sh` script
- Conda is typically installed in `$HOME/anaconda3` or `$HOME/miniconda3`
- Virtual environments are in `/path/to/venv/bin/jupyter`
- System installation uses `--user` flag by default

## Support

For issues or questions, please check:

- [CRN Engine GitHub](https://github.com/microsoft/CRN)
- [Jupyter Documentation](https://jupyter.org/documentation)
