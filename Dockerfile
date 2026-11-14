# syntax=docker/dockerfile:1
# Imagem da API CPTM.Manutencao.Api (.NET 10). Contexto de build: raiz do repositório.
#   docker build -t cptm-manutencao-api .

# ---------- build ----------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restaura primeiro só com o .csproj para aproveitar o cache de camadas
COPY CPTM.Manutencao.Api/CPTM.Manutencao.Api.csproj CPTM.Manutencao.Api/
RUN dotnet restore CPTM.Manutencao.Api/CPTM.Manutencao.Api.csproj

COPY CPTM.Manutencao.Api/ CPTM.Manutencao.Api/
RUN dotnet publish CPTM.Manutencao.Api/CPTM.Manutencao.Api.csproj \
    -c Release -o /app/publish --no-restore /p:UseAppHost=false

# ---------- runtime ----------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# /data guarda o banco SQLite e as fotos (monte um volume aqui; sem volume, some ao reiniciar)
RUN mkdir -p /data/uploads && chown -R $APP_UID:$APP_UID /data

ENV ASPNETCORE_HTTP_PORTS=8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_FORWARDEDHEADERS_ENABLED=true \
    ConnectionStrings__Sqlite="Data Source=/data/cptm-manutencao.db" \
    Uploads__Raiz=/data/uploads

COPY --from=build /app/publish .

# Sem root. Segredos (Jwt__Key etc.) entram por variável de ambiente, nunca na imagem.
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "CPTM.Manutencao.Api.dll"]

