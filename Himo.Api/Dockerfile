# Multi-stage .NET 10 container for Render.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Himo.Api.csproj ./
RUN dotnet restore Himo.Api.csproj
COPY . ./
RUN dotnet publish Himo.Api.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
# Render injects PORT at runtime; Program.cs binds to that value.
# This value is a safe fallback for local Docker runs.
ENV ASPNETCORE_URLS=http://0.0.0.0:10000
ENV ASPNETCORE_ENVIRONMENT=Production
COPY --from=build /app/publish .
EXPOSE 10000
ENTRYPOINT ["dotnet", "Himo.Api.dll"]
