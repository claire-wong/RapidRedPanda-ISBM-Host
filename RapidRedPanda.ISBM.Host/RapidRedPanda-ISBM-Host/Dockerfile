# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["RapidRedPanda-ISBM-Host/RapidRedPanda.ISBM.Host.csproj", "RapidRedPanda-ISBM-Host/"]
RUN dotnet restore "RapidRedPanda-ISBM-Host/RapidRedPanda.ISBM.Host.csproj"

COPY ["RapidRedPanda-ISBM-Host/", "RapidRedPanda-ISBM-Host/"]
COPY ["Database/", "Database/"]
RUN dotnet publish "RapidRedPanda-ISBM-Host/RapidRedPanda.ISBM.Host.csproj" \
    --configuration Release \
    --no-restore \
    --output /app/publish \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

COPY --from=build /app/publish .

USER app
ENTRYPOINT ["dotnet", "RapidRedPanda.ISBM.Host.dll"]
