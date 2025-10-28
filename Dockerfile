FROM mcr.microsoft.com/dotnet/sdk:8.0-alpine AS base

RUN apk add libmsquic
RUN apk add --upgrade --no-cache ca-certificates && update-ca-certificates

WORKDIR /app
VOLUME /cache

COPY . .

RUN dotnet build -c Release -o /app/build

ENV PORT=8080

EXPOSE 8080

ENTRYPOINT [ "dotnet", "/app/build/PassThrough.dll" ]