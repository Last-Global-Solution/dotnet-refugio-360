FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

WORKDIR /src
COPY Refugio360.Api/Refugio360.Api.csproj Refugio360.Api/
RUN dotnet restore Refugio360.Api/Refugio360.Api.csproj
COPY . .
RUN dotnet publish Refugio360.Api/Refugio360.Api.csproj -c Release --no-restore -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_ENVIRONMENT=Production
EXPOSE 10000
ENTRYPOINT ["sh", "-c", "exec dotnet Refugio360.Api.dll --urls http://0.0.0.0:${PORT:-10000}"]
