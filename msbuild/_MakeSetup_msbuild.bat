@echo ========================================================= > Output\MakeSetup.log
SET PATH=C:\Program Files (x86)\Microsoft Visual Studio\2017\Professional\MSBuild\15.0\Bin;%PATH%
msbuild ..\EntityFrameworkCore.Jet.sln /p:Configuration=Release /p:Platform="Any CPU" >> Output\MakeSetup.log
