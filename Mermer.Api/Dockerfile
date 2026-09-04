FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY ["Mermer.Api/Mermer.Api.csproj", "Mermer.Api/"]
COPY ["Mermer.Data.Postgres/Mermer.Data.Postgres.csproj", "Mermer.Data.Postgres/"]

RUN dotnet restore "Mermer.Api/Mermer.Api.csproj"

COPY . .
WORKDIR "/src/Mermer.Api"
RUN dotnet publish "Mermer.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app

ENV ASPNETCORE_HTTP_PORTS=8080
ENV ASPNETCORE_ENVIRONMENT=Production

EXPOSE 8080

COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Mermer.Api.dll"]