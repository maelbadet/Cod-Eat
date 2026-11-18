FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source
COPY src/CodEat.Web/CodEat.Web.csproj src/CodEat.Web/
RUN dotnet restore src/CodEat.Web
COPY src/CodEat.Web src/CodEat.Web
RUN dotnet publish src/CodEat.Web -c Release -o /out --no-restore /p:UseAppHost=false
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /out .
USER root
RUN mkdir /keys && chown $APP_UID:$APP_UID /keys
USER $APP_UID
ENV ASPNETCORE_HTTP_PORTS=8080 DataProtection__Path=/keys
EXPOSE 8080
ENTRYPOINT ["dotnet","CodEat.Web.dll"]
