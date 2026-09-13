@echo off
rem Builds src\Soundswap.Core\native\win-x64\soundswap_vorbis.dll: native\soundswap_vorbis.c with the reference
rem libogg and libvorbis (Xiph.Org, BSD licence) compiled in, from pinned release tags, with the static C runtime
rem so the DLL needs nothing else installed. Needs Visual Studio's C++ tools and git.
setlocal
set "ROOT=%~dp0.."
set "OUT=%ROOT%\src\Soundswap.Core\native\win-x64"
set "WORK=%ROOT%\artifacts\vorbis"
set "OGG_TAG=v1.3.5"
set "VORBIS_TAG=v1.3.7"

set "VSWHERE=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe"
if not exist "%VSWHERE%" (echo vswhere.exe not found: install Visual Studio with the C++ tools. & exit /b 1)
for /f "usebackq delims=" %%i in (`"%VSWHERE%" -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath`) do set "VS=%%i"
if not defined VS (echo Visual Studio's C++ tools were not found. & exit /b 1)
call "%VS%\VC\Auxiliary\Build\vcvars64.bat" >nul || exit /b 1

if not exist "%WORK%\ogg\src\framing.c" git clone -q --depth 1 --branch %OGG_TAG% https://github.com/xiph/ogg "%WORK%\ogg" || exit /b 1
if not exist "%WORK%\vorbis\lib\vorbisenc.c" git clone -q --depth 1 --branch %VORBIS_TAG% https://github.com/xiph/vorbis "%WORK%\vorbis" || exit /b 1
if not exist "%OUT%" mkdir "%OUT%"
if not exist "%WORK%\obj" mkdir "%WORK%\obj"

set "V=%WORK%\vorbis\lib"
cl /nologo /O2 /MT /LD /W0 /DNDEBUG /D_CRT_SECURE_NO_WARNINGS /Fo"%WORK%\obj\\" ^
   /I "%WORK%\ogg\include" /I "%WORK%\vorbis\include" /I "%V%" ^
   "%WORK%\ogg\src\bitwise.c" "%WORK%\ogg\src\framing.c" ^
   "%V%\analysis.c" "%V%\bitrate.c" "%V%\block.c" "%V%\codebook.c" "%V%\envelope.c" "%V%\floor0.c" "%V%\floor1.c" ^
   "%V%\info.c" "%V%\lookup.c" "%V%\lpc.c" "%V%\lsp.c" "%V%\mapping0.c" "%V%\mdct.c" "%V%\psy.c" "%V%\registry.c" ^
   "%V%\res0.c" "%V%\sharedbook.c" "%V%\smallft.c" "%V%\synthesis.c" "%V%\vorbisenc.c" "%V%\window.c" "%V%\vorbisfile.c" ^
   "%ROOT%\native\soundswap_vorbis.c" ^
   /Fe"%OUT%\soundswap_vorbis.dll" /link /NOLOGO || exit /b 1
del /q "%OUT%\soundswap_vorbis.lib" "%OUT%\soundswap_vorbis.exp" 2>nul
echo Built %OUT%\soundswap_vorbis.dll (libogg %OGG_TAG%, libvorbis %VORBIS_TAG%)
