# Build Angular into API wwwroot, then run the .NET API
FROM node:22-alpine AS frontend
WORKDIR /frontend
COPY frontend/package*.json ./
RUN npm ci
COPY frontend/ ./
RUN npm run build -- --configuration=production

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY backend/BuildVision.Api/BuildVision.Api.csproj backend/BuildVision.Api/
RUN dotnet restore backend/BuildVision.Api/BuildVision.Api.csproj
COPY backend/BuildVision.Api/ backend/BuildVision.Api/
COPY --from=frontend /frontend/dist/frontend/browser/ backend/BuildVision.Api/wwwroot/
RUN dotnet publish backend/BuildVision.Api/BuildVision.Api.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://0.0.0.0:8080
ENV ASPNETCORE_ENVIRONMENT=Production
EXPOSE 8080
RUN mkdir -p /app/App_Data
VOLUME ["/app/App_Data"]
ENTRYPOINT ["dotnet", "BuildVision.Api.dll"]
