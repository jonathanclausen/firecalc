FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/FireCalc.Api/FireCalc.Api.csproj src/FireCalc.Api/
RUN dotnet restore src/FireCalc.Api/FireCalc.Api.csproj
COPY src/ src/
RUN dotnet publish src/FireCalc.Api/FireCalc.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
# Cloud Run sends traffic to port 8080, which is also the image default.
ENV ASPNETCORE_HTTP_PORTS=8080
USER $APP_UID
ENTRYPOINT ["dotnet", "FireCalc.Api.dll"]
