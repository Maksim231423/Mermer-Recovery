FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app

ENV ASPNETCORE_HTTP_PORTS=5050
ENV ASPNETCORE_ENVIRONMENT=Production

EXPOSE 5050

COPY publish_api/ .
ENTRYPOINT ["dotnet", "Mermer.Api.dll"]