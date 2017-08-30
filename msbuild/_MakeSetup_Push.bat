@echo Press enter to upload nuget files
@pause
SET PARAMETERS= -Verbosity detail
FOR %%F IN (output\*.nupkg) DO nuget push %%F %PARAMETERS% >>  output\Makesetup.log