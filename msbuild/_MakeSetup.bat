@echo Version = %VERSION%
@echo Build with this version numbers (Ctrl + C to stop build)?
pause

@Echo trying to create Output dir
mkdir Output

call _MakeSetup_msbuild.bat

call _MakeSetup_Copy.bat

call _MakeSetup_Pack.bat

call _MakeSetup_Push.bat

notepad Output\MakeSetup.log

