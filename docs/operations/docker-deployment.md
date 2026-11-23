# Run the Host with Docker

Use a Linux AMD64 Docker Engine. The Host container connects to an external SQL Server database and Azure Service Bus namespace. Before starting it, install the database schema and configure the Service Bus namespace using the [Installation guide](../getting-started/installation.md).

The container must be able to resolve and reach both services and trust their TLS certificates. For the required SQL and Service Bus connection settings, see [Configuration](../getting-started/configuration.md#required-connection-settings).

## Load or build the image

If you have the distribution archive and its SHA-256 checksum, verify the archive against that checksum before loading it. Run these commands from the directory containing `artifacts/`.

```bash
sha256sum artifacts/rapidredpanda-isbm-host-dev-linux-amd64.tar.gz
docker load --input artifacts/rapidredpanda-isbm-host-dev-linux-amd64.tar.gz
docker image inspect rapidredpanda-isbm-host:dev \
  --format '{{.Os}}/{{.Architecture}} user={{.Config.User}}'
```

The inspect command should report `linux/amd64 user=app`. If you are building from source, run this command from the repository root. The build context includes the Host project and its sibling database scripts.

```bash
docker buildx build --platform linux/amd64 --load \
  --tag rapidredpanda-isbm-host:dev \
  --file RapidRedPanda.ISBM.Host/RapidRedPanda-ISBM-Host/Dockerfile \
  RapidRedPanda.ISBM.Host
```

## Start a local HTTPS container

Have a trusted HTTPS certificate for `localhost` in a password-protected PFX file. Give the container's non-root `app` user read access to the file. The command binds HTTPS to the Docker host's loopback interface and prompts for the runtime secrets. Run it in Bash.

```bash
(
  set -e
  read -r -p 'Absolute path to the HTTPS PFX file: ' host_pfx
  read -r -s -p 'SQL Server connection string: ' ConnectionStrings__ISBMHost
  printf '\n'
  read -r -s -p 'Azure Service Bus namespace connection string: ' AzureServiceBus__ConnectionString
  printf '\n'
  read -r -s -p 'HTTPS PFX password: ' ASPNETCORE_Kestrel__Certificates__Default__Password
  printf '\n'
  export ConnectionStrings__ISBMHost AzureServiceBus__ConnectionString
  export ASPNETCORE_Kestrel__Certificates__Default__Password

  docker run --rm --name rapidredpanda-isbm-host \
    --publish 127.0.0.1:8443:8443 \
    --mount "type=bind,src=${host_pfx},dst=/https/host.pfx,readonly" \
    --env ASPNETCORE_ENVIRONMENT=Production \
    --env ASPNETCORE_URLS=https://+:8443 \
    --env ASPNETCORE_Kestrel__Certificates__Default__Path=/https/host.pfx \
    --env ASPNETCORE_Kestrel__Certificates__Default__Password \
    --env Isbm__ConfigurationDiscovery__IsTlsEnabled=true \
    --env ConnectionStrings__ISBMHost \
    --env AzureServiceBus__ConnectionString \
    rapidredpanda-isbm-host:dev
)
```

Docker administrators can inspect container environment variables. Restrict access to the Docker daemon and supply secrets through your deployment's protected configuration mechanism when moving beyond local evaluation.

From another terminal on the Docker host, check configuration discovery over HTTPS. Then verify database and broker access with the [first publication workflow](../getting-started/first-message.md), using `https://localhost:8443` as the Host base URL.

```bash
curl --fail --show-error https://localhost:8443/isbm/2.0/configuration/supported-operations
```

The repository's [Development configuration example](../../RapidRedPanda.ISBM.Host/RapidRedPanda-ISBM-Host/appsettings.Development.example.json) is for running the Host from source. Copy it to `appsettings.Development.json` beside the example and supply local values through that ignored file, development user secrets, or environment variables. The container command supplies the required settings at runtime.
