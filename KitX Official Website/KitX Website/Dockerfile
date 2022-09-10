#See https://aka.ms/containerfastmode to understand how Visual Studio uses this Dockerfile to build your images for faster debugging.

FROM mcr.microsoft.com/dotnet/aspnet:6.0 AS base
WORKDIR /app
EXPOSE 80
EXPOSE 443

FROM mcr.microsoft.com/dotnet/sdk:6.0 AS build
WORKDIR /src
COPY ["nuget.config", "."]
COPY ["KitX Official Website/KitX Website/KitX Website.csproj", "KitX Official Website/KitX Website/"]
RUN dotnet restore "KitX Official Website/KitX Website/KitX Website.csproj"
COPY . .
WORKDIR "/src/KitX Official Website/KitX Website"
RUN dotnet build "KitX Website.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "KitX Website.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "KitX Website.dll"]