# ===== build =====
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY *.csproj ./
RUN dotnet restore

COPY . .
RUN dotnet publish -c Release -o /app/out --no-restore

# ===== runtime =====
FROM mcr.microsoft.com/dotnet/aspnet:8.0-alpine AS runtime
WORKDIR /app
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false \
    ASPNETCORE_URLS=http://0.0.0.0:5152
RUN apk add --no-cache icu-libs icu-data-full wget
COPY --from=build /app/out ./
EXPOSE 5152
HEALTHCHECK --interval=30s --timeout=5s --retries=3 CMD wget -qO- http://127.0.0.1:5152/ || exit 1
ENTRYPOINT ["dotnet", "V2rayApi.dll"]



# ===== runtime =====
# FROM mcr.microsoft.com/dotnet/aspnet:8.0-alpine AS runtime
# WORKDIR /app
# ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false \
#     ASPNETCORE_URLS=http://0.0.0.0:5153
# RUN apk add --no-cache icu-libs icu-data-full wget
# COPY --from=build /app/out ./
# EXPOSE 5153
# HEALTHCHECK --interval=30s --timeout=5s --retries=3 CMD wget -qO- http://127.0.0.1:5153/ || exit 1
# ENTRYPOINT ["dotnet", "V2rayApi.dll"]