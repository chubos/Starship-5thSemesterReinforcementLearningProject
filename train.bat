@echo off
setlocal
cd /d "%~dp0"

set "PYTHON="
py -3.10 -c "import sys; assert sys.version_info[:2] == (3, 10)" >nul 2>&1
if not errorlevel 1 set "PYTHON=py -3.10"

if not defined PYTHON (
    echo Python 3.10 was not found. Attempting to install Python 3.10.12 with uv...
    where uv >nul 2>&1
    if errorlevel 1 (
        python -m pip install --user uv
        if errorlevel 1 (
            echo ERROR: Could not install uv.
            exit /b 1
        )
    )
    python -m uv python install 3.10.12
    if errorlevel 1 (
        echo ERROR: Could not install Python 3.10.12.
        exit /b 1
    )
    for /f "delims=" %%P in ('python -m uv python find 3.10.12') do set "PYTHON=%%P"
)

if not exist ".venv\Scripts\python.exe" (
    %PYTHON% -m venv ".venv"
    if errorlevel 1 (
        echo ERROR: Could not create the virtual environment.
        exit /b 1
    )
)

call ".venv\Scripts\activate.bat"
if errorlevel 1 (
    echo ERROR: Could not activate the virtual environment.
    exit /b 1
)

python -m pip install --upgrade pip
if errorlevel 1 exit /b 1
python -m pip install "setuptools==77.0.3"
if errorlevel 1 exit /b 1
python -m pip install "mlagents==1.1.0"
if errorlevel 1 exit /b 1
python -m pip install --force-reinstall "torch==2.8.0"
if errorlevel 1 exit /b 1
python -m pip install --force-reinstall "numpy==1.23.5" "onnx==1.15.0" "protobuf==3.20.3"
if errorlevel 1 exit /b 1
python -m pip install "matplotlib" "pandas"
if errorlevel 1 exit /b 1

python -c "import sys, mlagents, matplotlib, numpy, onnx, pandas, torch; assert sys.version_info[:2] == (3, 10); assert numpy.__version__ == '1.23.5'; assert onnx.__version__ == '1.15.0'; assert torch.__version__.startswith('2.8.0'); print('Installation OK - Python', sys.version.split()[0], '- torch', torch.__version__, '- numpy', numpy.__version__, '- onnx', onnx.__version__)"
if errorlevel 1 (
    echo ERROR: Installation verification failed.
    exit /b 1
)

echo Starting ML-Agents training...
mlagents-learn "config\ppo_config.yaml" --run-id=starship_ppo_01
exit /b %errorlevel%
