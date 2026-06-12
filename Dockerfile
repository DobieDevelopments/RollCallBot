# Build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /app

COPY . .
RUN dotnet publish -c Release -o /app/publish

# Runtime stage (ASP.NET runtime, not plain runtime)
FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app/publish .

EXPOSE 10000

ENTRYPOINT ["dotnet", "RollCallBot.dll"]
