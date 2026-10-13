# Zemires.N8n.Api

A [Kiota](https://learn.microsoft.com/openapi/kiota/)-generated .NET client for the [n8n public REST API](https://docs.n8n.io/api/).

Usually consumed through the `Zemires.Aspire.N8n` client integration, which registers `N8nClient` with authentication and a health check.

## Install the package

```dotnetcli
dotnet add package Zemires.N8n.Api
```

## Regenerate the client

Generated code must not be edited by hand. From this folder run:

```shell
kiota generate -d .\openapi.yml -l csharp -n Zemires.N8n.Api -c N8nClient -o .
```

Hand-written files: `N8nAuthenticationProvider.cs`, `N8nClient.Extensions.cs`.

## Feedback & contributing

https://github.com/zemires/zemires-aspire
