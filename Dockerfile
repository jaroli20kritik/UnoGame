# Use the official Microsoft .NET 8 SDK image to build and publish the app
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy the project file and restore any dependencies
COPY ["UnoGame.API/UnoGame.API.csproj", "UnoGame.API/"]
RUN dotnet restore "UnoGame.API/UnoGame.API.csproj"

# Copy the remaining source code
COPY . .

# Build and publish a release version to the /app/publish directory
WORKDIR "/src/UnoGame.API"
RUN dotnet publish "UnoGame.API.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Use the official Microsoft .NET 8 ASP.NET runtime image to run the app
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

# Expose port (Render sets PORT environment variable dynamically)
EXPOSE 80

# Configure the app to listen on the port Render assigns, or default to 80
ENV ASPNETCORE_URLS=http://+:80

# Start the application
ENTRYPOINT ["dotnet", "UnoGame.API.dll"]
