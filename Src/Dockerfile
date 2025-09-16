FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY . .
RUN dotnet restore Src/Service.csproj
RUN dotnet publish Src/Service.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/asp.net:8.0
WORKDIR /app
COPY --from=build /app .
RUN apt-get update && apt-get install -y powershell
RUN pwsh -Command "Install-Module -Name ExchangeOnlineManagement -Force -Scope AllUsers"
EXPOSE 80
ENTRYPOINT ["dotnet", "Service.dll"]