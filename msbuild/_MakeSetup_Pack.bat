@echo Deleting al package files >> Output\Makesetup.log
del /q Output\*.nupkg >> Output\Makesetup.log
nuget pack EFCore.Jet.nuspec -version %VERSION% -OutputDirectory Output >> Output\Makesetup.log
