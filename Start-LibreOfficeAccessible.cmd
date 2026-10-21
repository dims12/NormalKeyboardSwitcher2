@echo off
setlocal
rem Modern LibreOffice starts its accessibility bridge on demand.
set "SAL_FORCE_IACCESSIBLE2="
start "" "C:\Program Files\LibreOffice\program\soffice.exe" --calc
endlocal
