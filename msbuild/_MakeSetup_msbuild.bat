@echo ========================================================= > Output\MakeSetup.log
call "%VS140COMNTOOLS%\vsvars32.bat"
msbuild ..\EntityFrameworkCore.Jet.sln /p:Configuration=Release /p:Platform="Any CPU" >> Output\MakeSetup.log
