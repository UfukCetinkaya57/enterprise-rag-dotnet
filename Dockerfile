# --- Build aşaması ---
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Önce csproj'ları kopyala + restore (katman cache'i için)
COPY KurumsalRAG.slnx ./
COPY src/KurumsalRAG.Domain/KurumsalRAG.Domain.csproj src/KurumsalRAG.Domain/
COPY src/KurumsalRAG.Application/KurumsalRAG.Application.csproj src/KurumsalRAG.Application/
COPY src/KurumsalRAG.Infrastructure/KurumsalRAG.Infrastructure.csproj src/KurumsalRAG.Infrastructure/
COPY src/KurumsalRAG.Api/KurumsalRAG.Api.csproj src/KurumsalRAG.Api/
RUN dotnet restore src/KurumsalRAG.Api/KurumsalRAG.Api.csproj

# Kaynağı kopyala + publish
COPY src/ src/
RUN dotnet publish src/KurumsalRAG.Api/KurumsalRAG.Api.csproj -c Release -o /app --no-restore

# --- Runtime aşaması ---
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Npgsql'in Kerberos/GSS probe'u için (yoksa "libgssapi_krb5.so.2" log gürültüsü).
RUN apt-get update \
    && apt-get install -y --no-install-recommends libgssapi-krb5-2 \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app ./

# Container içinde 8080'i dinle (dışarı map edilmez; compose 127.0.0.1'e bağlar).
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

# Non-root çalıştır (aspnet imajında hazır 'app' kullanıcısı).
USER app

ENTRYPOINT ["dotnet", "KurumsalRAG.Api.dll"]
