FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS base

RUN sed -i 's/providers = provider_sect/providers = provider_sect\n\
ssl_conf = ssl_sect\n\
\n\
[ssl_sect]\n\
system_default = system_default_sect\n\
\n\
[system_default_sect]\n\
Options = UnsafeLegacyRenegotiation/' /etc/ssl/openssl.cnf

RUN apk add libmsquic
RUN apk add --upgrade --no-cache ca-certificates && update-ca-certificates


WORKDIR /app
VOLUME /cache

COPY . .

RUN dotnet build -c Release -o /app/build

ENV PORT=8080

EXPOSE 8080

ENTRYPOINT [ "dotnet", "/app/build/PassThrough.dll" ]