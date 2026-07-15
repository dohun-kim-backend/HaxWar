FROM --platform=linux/amd64 mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /app

# 캐시 최적화를 위해 csproj 파일 복사 및 복원
COPY HexWar.sln ./
COPY src/HexWar.Server/HexWar.Server.csproj src/HexWar.Server/
COPY src/HexWar.Infrastructure/HexWar.Infrastructure.csproj src/HexWar.Infrastructure/
COPY src/HexWar.Application/HexWar.Application.csproj src/HexWar.Application/
COPY src/HexWar.Domain/HexWar.Domain.csproj src/HexWar.Domain/
COPY src/HexWar.Matchmaking/HexWar.Matchmaking.csproj src/HexWar.Matchmaking/
COPY src/HexWar.Client/HexWar.Client.csproj src/HexWar.Client/

RUN dotnet restore src/HexWar.Server/HexWar.Server.csproj

# 소스코드 전체 복사
COPY src/ src/

WORKDIR /app/src/HexWar.Server
RUN dotnet publish -c Release -o /publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app

# 헬스체크를 위한 curl 및 OTel Auto-Instrumentation 에이전트 설치용 unzip/wget 설치
RUN apt-get update && apt-get install -y curl unzip wget && \
    # OTel Auto-Instrumentation v1.9.0 다운로드 및 설치
    wget -q https://github.com/open-telemetry/opentelemetry-dotnet-instrumentation/releases/download/v1.9.0/opentelemetry-dotnet-instrumentation-linux-glibc.zip && \
    mkdir -p /otel && \
    unzip -q opentelemetry-dotnet-instrumentation-linux-glibc.zip -d /otel && \
    rm opentelemetry-dotnet-instrumentation-linux-glibc.zip && \
    chmod +x /otel/instrument.sh && \
    rm -rf /var/lib/apt/lists/*

COPY --from=build /publish .
EXPOSE 5000
ENV ASPNETCORE_URLS=http://+:5000
ENTRYPOINT ["dotnet", "HexWar.Server.dll"]
