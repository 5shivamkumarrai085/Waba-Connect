# Stage 1: Build
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy project file and restore dependencies
COPY WhatsAppCampaignApi.csproj .
RUN dotnet restore WhatsAppCampaignApi.csproj

# Copy remaining source code and publish
COPY . .
RUN dotnet publish WhatsAppCampaignApi.csproj -c Release -o /app/publish /p:UseAppHost=false

# Stage 2: Runtime
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

# Render exposes PORT dynamically; ASP.NET Core listens on port 8080
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "WhatsAppCampaignApi.dll"]
