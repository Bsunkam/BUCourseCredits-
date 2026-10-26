# Build context = repository root. Builds the React app, publishes the API, and serves both from one container.

FROM node:22-alpine AS web
WORKDIR /web
COPY src/ui/package*.json ./
RUN npm ci
COPY src/ui/ ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /repo
COPY nuget.config ./
COPY src/database/ ./src/database/
COPY src/api/TransferCredit.Api/ ./src/api/TransferCredit.Api/
RUN dotnet publish src/api/TransferCredit.Api/TransferCredit.Api.csproj -c Release -o /app/publish
COPY --from=web /web/dist /app/publish/wwwroot

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
USER app
ENTRYPOINT ["dotnet", "TransferCredit.Api.dll"]
