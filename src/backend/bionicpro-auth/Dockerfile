FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Копируем csproj и восстанавливаем зависимости
COPY ["bionicpro-auth.csproj", "."]
RUN dotnet restore

# Копируем остальной код и собираем
COPY . .
RUN dotnet publish -c Release -o /app/publish

# Runtime image
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
EXPOSE 8080

COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "bionicpro-auth.dll"]
