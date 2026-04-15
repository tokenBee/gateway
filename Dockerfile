FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["TokenBee.csproj", "./"]
RUN dotnet restore "TokenBee.csproj"
COPY . .
RUN dotnet publish "TokenBee.csproj" -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 8080
ENTRYPOINT ["dotnet", "TokenBee.dll"]
