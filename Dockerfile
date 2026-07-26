FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Memogram/Memogram.csproj Memogram/
RUN dotnet restore Memogram/Memogram.csproj
COPY . .
RUN dotnet publish Memogram/Memogram.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
ENV Memogram__ServerAddr=localhost:5230
ENV Telegram__BotToken=your_telegram_bot_token
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Memogram.dll"]
