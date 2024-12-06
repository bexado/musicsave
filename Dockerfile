FROM mcr.microsoft.com/dotnet/sdk:7.0 AS build
WORKDIR /src
COPY ["musicsave/musicsave.csproj", "musicsave/"]
RUN dotnet restore "musicsave/musicsave.csproj"
COPY . .
WORKDIR "/src/musicsave"
RUN dotnet build "musicsave.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "musicsave.csproj" -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/runtime:7.0 AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "musicsave.dll"]