FROM mcr.microsoft.com/dotnet/runtime:8.0 AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["lokitminebot_telegram.csproj", "."]
RUN dotnet restore "lokitminebot_telegram.csproj"
COPY . .
RUN dotnet publish "lokitminebot_telegram.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "lokitminebot_telegram.dll"]