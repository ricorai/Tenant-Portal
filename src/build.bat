@echo off
setlocal
cd /d "%~dp0"

echo ====================================================================
echo  Building TenantGateway.exe (Native C# .NET 4.8 HttpListener Daemon)
echo ====================================================================

set CSC="C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist %CSC% (
    set CSC="C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe"
)

if not exist %CSC% (
    echo [ERROR] csc.exe compiler not found in standard .NET paths.
    exit /b 1
)

echo [*] Compiler: %CSC%
echo [*] Compiling TenantGateway.cs...

%CSC% /nologo /target:exe /optimize+ /platform:anycpu /r:System.dll,System.Core.dll,System.Web.Extensions.dll /out:"..\TenantGateway.exe" "TenantGateway.cs"

if %errorlevel% neq 0 (
    echo.
    echo [ERROR] Compilation FAILED with exit code %errorlevel%.
    exit /b %errorlevel%
)

echo.
echo ====================================================================
echo  Compilation SUCCESSFUL: ..\TenantGateway.exe
for %%I in ("..\TenantGateway.exe") do echo  Binary Size: %%~zI bytes (~%%~zI / 1024 KB)
echo ====================================================================
exit /b 0
