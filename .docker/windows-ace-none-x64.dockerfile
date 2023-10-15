# asdfescape=`

FROM mcr.microsoft.com/windows:ltsc2019-amd64

ARG ARCHITECTURE=x64

ARG PS_VERSION=7.3.8
ARG PS_PACKAGE_FILE=PowerShell-$PS_VERSION-win-$ARCHITECTURE.msi
ARG PS_PACKAGE_URL=https://github.com/PowerShell/PowerShell/releases/download/v$PS_VERSION/$PS_PACKAGE_FILE

ENV DOTNET_CLI_TELEMETRY_OPTOUT=true \
    DOTNET_SKIP_FIRST_TIME_EXPERIENCE=true \
    DOTNET_NOLOGO=true

# Ignore any Development.props file by default.
ENV IgnoreLocalRepositories=true

#
# Install PowerShell:
#

SHELL ["powershell", "-Command", "$ErrorActionPreference = 'Stop'; $ProgressPreference = 'SilentlyContinue';"]

RUN Invoke-WebRequest $env:PS_PACKAGE_URL -OutFile $env:PS_PACKAGE_FILE; \
    msiexec.exe /package $env:PS_PACKAGE_FILE /quiet ADD_PATH=1 DISABLE_TELEMETRY=1 | Out-Default

SHELL ["pwsh", "-Command", "$ErrorActionPreference = 'Stop'; $ProgressPreference = 'SilentlyContinue';"]

RUN $PSVersionTable; \
    Set-ExecutionPolicy Unrestricted 

#
# Install .NET SDK:
#

COPY global.json dotnet-install-global.json

RUN echo '.NET Information Before SDK Install'; \
    try { dotnet --info } catch { echo "No $env:ARCHITECTURE .NET SDK installed." } \
    echo 'Install .NET SDK'; \
    &([scriptblock]::Create((Invoke-WebRequest -UseBasicParsing 'https://dot.net/v1/dotnet-install.ps1'))) -JSonFile dotnet-install-global.json -Architecture $env:ARCHITECTURE -InstallDir "C:\dotnet_$env:ARCHITECTURE" -Verbose && \
    [Environment]::SetEnvironmentVariable('Path', $env:Path, 'Machine'); \
    $env:Path; \
    try { dotnet --info } catch { echo "No $env:ARCHITECTURE .NET SDK installed." }

#
# Output provider information:
#

RUN 'DAO:'; \
    Get-ChildItem 'HKLM:\SOFTWARE\Classes\DAO.DBEngine*' | Select-Object; \
    'OLE DB:'; \
    foreach ($provider in [System.Data.OleDb.OleDbEnumerator]::GetRootEnumerator()) { \
        $v = New-Object PSObject; \
        for ($i = 0; $i -lt $provider.FieldCount; $i++) { \
           Add-Member -in $v NoteProperty $provider.GetName($i) $provider.GetValue($i); \
        } \
        $v; \
    } \
    echo "Architecture: $([Environment]::Is64BitProcess ? 'x64' : 'x86')" 

RUN mkdir 'C:\Source'
WORKDIR 'C:\Source'

ENTRYPOINT ["pwsh", "-c"]
CMD ["pwsh"]