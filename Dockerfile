FROM mcr.microsoft.com/dotnet/sdk:10.0-noble AS build
WORKDIR /src
COPY VideoEditor.csproj ./
RUN dotnet restore VideoEditor.csproj
COPY . .
RUN dotnet publish VideoEditor.csproj -c Release -o /app/publish --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble AS runtime
USER root
RUN apt-get update \
    && apt-get install -y --no-install-recommends ffmpeg fonts-noto-core fontconfig \
    && fc-cache -f \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_ENVIRONMENT=Production
ENV FRAME_CLOUD_MODE=true
EXPOSE 8080
USER app
ENTRYPOINT ["/bin/sh", "-c", "export ASPNETCORE_URLS=http://0.0.0.0:${PORT:-8080}; exec dotnet VideoEditor.dll"]
