@echo off
SETLOCAL

set "asm_name=LabyrinthianFacilities.dll"

set game_dir=%STEAM%/"Lethal Company"
set plugin_dir=%STEAM%/"Lethal Company"/BepInEx/plugins

set asm_path=./output/bin/LabyrinthianFacilities/debug
set from_asm_path=../../../..

dotnet build --artifacts-path output
if %ErrorLevel% NEQ 0 (exit /B 1)

set managed="%STEAM%/Lethal Company/Lethal Company_Data/Managed"
set harmony="%STEAM%/Lethal Company/BepInEx/core/0Harmony.dll"
netcode-patch %asm_path%/%asm_name% %managed% %harmony%
if %ErrorLevel% NEQ 0 (exit /B 1)

cd %asm_path%
if %1 NEQ -l (
	mkdir %plugin_dir%
	move /y %asm_name% %plugin_dir%
) else (
	move /y %asm_name% %from_asm_path%
)
cd %from_asm_path%

if %1==-r (start %game_dir%/"Lethal Company.exe")
if %1==-m (
	start %game_dir%/"Lethal Company.exe"
	timeout /t 5 /nobreak
	start %game_dir%/"Lethal Company.exe"
)

ENDLOCAL