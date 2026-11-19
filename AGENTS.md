# Convenções da API Refúgio 360 (.NET)

- Preserve alterações locais de outros integrantes; não inclua `.idea`, bancos SQLite ou secrets em commits/imagens.
- O projeto `Refugio360.Api` contém a API e os testes. Use `dotnet test Refugio360.sln` e SQLite em memória nas validações.
- Em hospedagem, mantenha `ASPNETCORE_ENVIRONMENT=Production` e `Jwt__Secret` ou `REFUGIO_JWT_SECRET` externo com pelo menos 32 bytes UTF-8; nunca desative validações JWT.
- `DemoData__Enabled` é `false` por padrão. Ative explicitamente para dados fictícios hospedados; preserve IDs estáveis, registros existentes e os rótulos `DEMO`.
- O Dockerfile usa a raiz deste repositório como contexto. Nunca copie o arquivo SQLite local para a imagem.
- Considere `/health/ready` para a disponibilidade do SQLite e `/health/live` apenas para verificar o processo.
