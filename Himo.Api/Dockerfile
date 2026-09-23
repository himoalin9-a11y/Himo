# Himo.Api - Linux container deployment
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Himo.Api.csproj ./
RUN dotnet restore Himo.Api.csproj
COPY . ./
RUN dotnet publish Himo.Api.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_URLS=http://0.0.0.0:8080
COPY --from=build /app/publish .
EXPOSE 8080
ENTRYPOINT ["dotnet", "Himo.Api.dll"]
