# Refúgio 360

**Repositório:** [Last-Global-Solution/dotnet-refugio-360](https://github.com/Last-Global-Solution/dotnet-refugio-360)

API REST de demonstração com relatos comunitários, triagem por moderador, locais de apoio e alertas publicados após revisão. O escopo usa dados fictícios e não substitui orientação oficial de emergência.

## Estrutura

```text
Refugio360/
  Refugio360.sln
  Refugio360.Api/       projeto .NET 10
    Program.cs
    Application/        validações e regras da triagem
    Domain/             modelos e persistência do domínio
    Infrastructure/     SQLite, dados de demonstração, health check, erros e OpenAPI
    Tests/Unit/          testes unitários xUnit
    Tests/Integration/   testes de integração xUnit
```

## Como a API funciona

O Swagger ou um aplicativo envia uma requisição HTTP para uma rota em `Program.cs`. A API verifica o limite de requisições, valida o JWT nas rotas protegidas e confere os dados recebidos com `RequestValidator`. As regras da triagem estão em `TriageRules`. O Entity Framework Core (`RefugioDbContext`) lê ou grava os objetos no SQLite e a API devolve JSON com um código HTTP: por exemplo, `200` para consulta/atualização, `201` para criação, `204` para exclusão lógica, `400` para entrada inválida, `401` para token ausente ou inválido, `403` para papel sem permissão, `404` para registro não encontrado, `409` para mudança de estado proibida e `429` para excesso de requisições.

| Tabela SQLite | Dados guardados |
|---|---|
| `Occurrences` | Relatos com autor, localização e status de triagem |
| `SafeLocations` | Locais de apoio, acessibilidade, disponibilidade e data da última confirmação |
| `Alerts` | Rascunhos e alertas publicados, fonte, validade e estado ativo |
| `IdempotencyRecords` | Chave, usuário e resposta de cada POST de ocorrência processado |

Usuário, senha e segredo JWT **não ficam no SQLite**. Na demonstração, eles vêm de User Secrets ou variáveis de ambiente. O `Idempotency-Key` identifica uma tentativa de criação; repetir a mesma chave para o mesmo usuário devolve a resposta original, sem criar outro relato.

## Executar localmente

### 1. Instale e confira o SDK

Instale o SDK .NET 10 pela [página oficial de downloads](https://dotnet.microsoft.com/pt-br/download/dotnet/10.0). Abra o PowerShell na pasta `Refugio360.Api` e confira a instalação:

```powershell
dotnet --version
```

O comando deve mostrar uma versão `10.0.xxx`. Se estiver na raiz `Refugio360`, entre na pasta do projeto:

```powershell
cd .\Refugio360.Api
```

### 2. Restaure os pacotes e configure o usuário local

Restaure as dependências e inicialize User Secrets (o `init` é feito apenas uma vez por cópia do projeto):

```powershell
dotnet restore
dotnet user-secrets init
dotnet user-secrets set "Jwt:Secret" "SUBSTITUA-POR-UM-SEGREDO-LOCAL-COM-32-BYTES-OU-MAIS"
dotnet user-secrets set "DemoUser:Username" "morador-demo"
dotnet user-secrets set "DemoUser:Password" "SUBSTITUA-POR-UMA-SENHA-LOCAL"
dotnet user-secrets set "DemoUser:Role" "Morador"
```

Substitua os dois valores de exemplo por valores locais antes de usar. O login compara o usuário e a senha recebidos com os valores salvos nos User Secrets, caractere por caractere. Se você executou o comando acima sem substituir `SUBSTITUA-POR-UMA-SENHA-LOCAL`, essa frase inteira virou sua senha de demonstração. Para escolher outra, execute novamente `dotnet user-secrets set "DemoUser:Password" "SUA-SENHA-ESCOLHIDA"` na mesma pasta do `.csproj`, pare a API e inicie-a com `dotnet run`. Se User Secrets já tiver sido inicializado, pule o comando `dotnet user-secrets init`. Não publique os valores no GitHub.

Cada cópia extraída do projeto tem seu próprio `UserSecretsId`. Se você configurou segredos em uma pasta anterior, repita estes comandos nesta cópia. Para apenas iniciar e abrir o Swagger, a API também cria uma chave JWT temporária em Development; para emitir tokens, configure o usuário e a senha acima.

### 3. Inicie a API

Ainda na pasta `Refugio360.Api`, execute:

```powershell
dotnet run
```

Quando o terminal indicar que a aplicação está ouvindo, abra `http://localhost:5000/swagger` ou use o endereço indicado pelo próprio `dotnet run` e acrescente `/swagger`. Para parar, pressione `Ctrl+C` no terminal.

Para testar endpoints de moderação, configure `DemoUser:Role` como `Moderador` usando `dotnet user-secrets set "DemoUser:Role" "Moderador"` e reinicie a API. A autenticação de demonstração usa um usuário configurado; antes de produção, substitua-a por um provedor de identidade adequado.

O SQLite cria `refugio360.db` no diretório de execução (normalmente `Refugio360.Api`). Em Development, a primeira inicialização inclui dois locais de apoio fictícios, um alerta **já publicado somente para demonstração** e, se `DemoUser:Username` estiver configurado, uma ocorrência fictícia desse usuário. Reiniciar a API não duplica esses registros. Os locais fictícios começam com `disponibilidadeVerificadaEmUtc = null`: nenhuma disponibilidade real foi confirmada.

Ao iniciar uma cópia nova do código com um `refugio360.db` de versão anterior, a API acrescenta a coluna de verificação de disponibilidade sem apagar os dados. Alertas antigos com status `Revisado` passam a `Publicado` para manter a visibilidade que já tinham. Isso é uma atualização pontual de esquema para SQLite, não um sistema geral de migrations. Os testes usam SQLite em memória e não alteram seu arquivo local. A carga fictícia não ocorre em Testing ou Production.

Se você extrair o ZIP em outra pasta e quiser manter seus registros, pare a API e copie o arquivo `refugio360.db` antigo para a nova pasta `Refugio360.Api` antes de executar `dotnet run`. Se não copiar, a API cria um banco novo com os dados fictícios de demonstração.

## Papéis e acesso

`Público` significa requisição sem token; não é uma conta cadastrada. `Morador` e `Moderador` são valores do papel dentro do JWT. O login de demonstração tem **um usuário configurado por vez** em User Secrets; para apresentar o outro papel, mude `DemoUser:Role`, reinicie a API e gere um token novo. Tokens antigos mantêm o papel com que foram emitidos até expirarem.

| Ação | Público | Morador | Moderador |
|---|:---:|:---:|:---:|
| Consultar locais ativos e alertas publicados | Sim | Sim | Sim |
| Criar e consultar as próprias ocorrências | Não | Sim | Sim |
| Corrigir ou excluir a própria ocorrência enquanto `Recebida` | Não | Sim | Sim |
| Ver ocorrências de outros usuários para triagem | Não | Não | Sim |
| Mudar status de uma ocorrência na triagem | Não | Não | Sim |
| Criar, atualizar, confirmar disponibilidade ou desativar locais | Não | Não | Sim |
| Criar e editar rascunhos, publicar e desativar alertas | Não | Não | Sim |

O moderador **não altera o texto de uma ocorrência de outro usuário**; ele muda apenas o status de triagem. A sequência permitida é `Recebida` → `EmAnalise` → `Confirmada` ou `Descartada`. Uma ocorrência em análise continua no histórico do autor, mas ele não pode mais corrigi-la nem excluí-la; essas tentativas retornam HTTP `409 Conflict`. Uma transição de status fora dessa sequência também retorna `409`.

Um alerta criado por moderador começa em `EmRevisao` e não aparece na consulta pública. Depois de conferir a informação, a fonte e a validade, o moderador usa a operação **publicar**; só então o status vira `Publicado` e o alerta aparece para todos. Alterar um alerta publicado devolve-o a `EmRevisao`, exigindo nova publicação. A decisão de revisão é manual: a API não associa automaticamente o alerta a uma ocorrência confirmada.

O campo `disponibilidadeVerificadaEmUtc` de um local é `null` até o moderador confirmar sua disponibilidade por uma operação própria. Essa operação grava a data e hora UTC. Se a disponibilidade for modificada pelo `PUT` geral, a data de confirmação volta a `null` até nova verificação. Uma alteração em outros dados do local preserva a data existente.

## Endpoints

| Método | Rota | Acesso | Função |
|---|---|---|---|
| POST | `/api/v1/auth/token` | Público | Emite JWT de demonstração |
| GET | `/api/v1/locais-seguros?acessivel=true` | Público | Lista locais ativos; filtro de acessibilidade opcional |
| GET | `/api/v1/locais-seguros/{id}` | Público | Consulta um local ativo |
| POST | `/api/v1/locais-seguros` | Moderador | Cadastra local com disponibilidade ainda não confirmada |
| PUT | `/api/v1/locais-seguros/{id}` | Moderador | Atualiza o local; mudança de disponibilidade limpa a data de confirmação |
| PATCH | `/api/v1/locais-seguros/{id}/disponibilidade` | Moderador | Confirma disponibilidade e grava data UTC |
| DELETE | `/api/v1/locais-seguros/{id}` | Moderador | Desativa o local |
| POST | `/api/v1/ocorrencias` | JWT | Registra ocorrência (exige `Idempotency-Key`) |
| GET | `/api/v1/ocorrencias` | JWT | Lista ocorrências ativas do usuário autenticado |
| GET | `/api/v1/ocorrencias/{id}` | JWT | Consulta ocorrência própria ativa |
| PUT | `/api/v1/ocorrencias/{id}` | JWT (autor) | Corrige ocorrência própria enquanto `Recebida` |
| DELETE | `/api/v1/ocorrencias/{id}` | JWT (autor) | Exclui logicamente ocorrência própria enquanto `Recebida` |
| GET | `/api/v1/triagem/ocorrencias?status=Recebida` | Moderador | Lista ocorrências por status; padrão `Recebida` |
| GET | `/api/v1/triagem/ocorrencias/{id}` | Moderador | Consulta ocorrência para triagem |
| PATCH | `/api/v1/triagem/ocorrencias/{id}/status` | Moderador | Avança status da triagem |
| GET | `/api/v1/alertas` | Público | Lista alertas publicados, ativos e não expirados |
| GET | `/api/v1/alertas/{id}` | Público | Consulta um alerta publicado, ativo e não expirado |
| POST | `/api/v1/alertas` | Moderador | Cria rascunho `EmRevisao` |
| GET | `/api/v1/triagem/alertas?status=EmRevisao` | Moderador | Lista alertas por status; padrão `EmRevisao` |
| GET | `/api/v1/triagem/alertas/{id}` | Moderador | Consulta rascunho ou alerta publicado |
| PATCH | `/api/v1/triagem/alertas/{id}/publicar` | Moderador | Publica alerta após revisão manual |
| PUT | `/api/v1/alertas/{id}` | Moderador | Atualiza alerta e o devolve à revisão |
| DELETE | `/api/v1/alertas/{id}` | Moderador | Desativa o alerta |

O `PUT` recebe todos os campos editáveis do recurso e retorna HTTP `200` com o registro atualizado. `DELETE` retorna HTTP `204` e faz **exclusão lógica**: a ocorrência recebe status `Excluida`, enquanto locais e alertas ficam com `Ativo = false`. Eles deixam de aparecer nas consultas comuns, mas permanecem no SQLite. Um `id` inexistente, de outro usuário ou já excluído retorna `404` nas rotas aplicáveis. A chave de idempotência de um `POST /ocorrencias` continua registrada mesmo se a ocorrência for atualizada ou excluída; reutilizá-la devolve a resposta original e não cria outra ocorrência.

## Demonstração guiada no Swagger

1. Configure `DemoUser:Role` como `Morador` e reinicie a API. Gere um token em `POST /api/v1/auth/token` com `{"username":"morador-demo","password":"SUA-SENHA-ESCOLHIDA"}`, usando exatamente a senha salva em User Secrets. O texto `SUA-SENHA-ESCOLHIDA` é apenas um exemplo; enviar um valor diferente do salvo retorna `401`.
2. Clique em **Authorize** e cole apenas o `accessToken`; o Swagger acrescenta `Bearer` ao cabeçalho automaticamente. Abra `POST /api/v1/ocorrencias`, clique em **Try it out**, preencha o campo **Idempotency-Key** com `relato-demo-001` e use:

```json
{
  "categoria": "Enchente",
  "descricao": "Ponto de alagamento fictício para demonstração",
  "latitude": -23.55,
  "longitude": -46.63
}
```

3. Clique em **Execute**. A resposta `201` traz o `id` da ocorrência e status `Recebida`. Repita o mesmo POST com a mesma chave: o `id` continua igual, sem duplicação. Enquanto `Recebida`, o morador pode usar `PUT /api/v1/ocorrencias/{id}` para corrigir os quatro campos do relato ou `DELETE` para excluí-lo; para continuar o roteiro, **não exclua** esta ocorrência.
4. No PowerShell, pare a API, execute `dotnet user-secrets set "DemoUser:Role" "Moderador"` na pasta `Refugio360.Api` e inicie com `dotnet run`. Gere **outro token** no Swagger e substitua o anterior em **Authorize**.
5. Execute `GET /api/v1/triagem/ocorrencias` para ver relatos `Recebida`. Use `PATCH /api/v1/triagem/ocorrencias/{id}/status` com `{"status":"EmAnalise"}`. Repita com `{"status":"Confirmada"}`. O relato continua visível ao autor, mas suas rotas `PUT` e `DELETE` agora respondem `409`.
6. Crie um alerta com `POST /api/v1/alertas`, por exemplo `{"titulo":"Alerta fictício","mensagem":"Mensagem de demonstração, sem validade para emergências reais.","fonte":"Simulação acadêmica","expiraEmUtc":"2030-12-31T23:59:00Z"}`. A resposta `201` traz status `EmRevisao` e um `id`. O alerta ainda não aparece em `GET /api/v1/alertas`; o moderador o consulta em `GET /api/v1/triagem/alertas/{id}` e, após revisar o conteúdo, executa `PATCH /api/v1/triagem/alertas/{id}/publicar`. Agora ele aparece na consulta pública. Use uma data futura em relação ao dia do teste.
7. Crie ou escolha um local de apoio em `GET /api/v1/locais-seguros`. Em `PATCH /api/v1/locais-seguros/{id}/disponibilidade`, envie `{"disponibilidade":"Disponível nesta simulação"}`. A resposta mostra `disponibilidadeVerificadaEmUtc` preenchido com a hora UTC da confirmação.
8. Consulte `/health/live` e `/health/ready`. Os testes podem ser executados com `dotnet test Refugio360.sln` a partir da pasta que contém a solução.

## Executar testes

Pare a API se ela estiver rodando. Abra outro PowerShell na raiz `Refugio360` (a pasta que contém `Refugio360.sln`, com API e testes no mesmo `.csproj`) e execute:

```powershell
dotnet test Refugio360.sln
```

O comando compila o único projeto e executa os testes unitários e de integração. Eles cobrem regras de transição de status, atualização de um banco SQLite antigo, CRUD, permissões e fluxo de revisão/publicação. Os testes de integração usam SQLite em memória, credenciais aleatórias e uma chave JWT própria; não alteram o banco local da aplicação.

Em um clone novo, **não é preciso configurar User Secrets para executar `dotnet test`**. As fábricas dos testes de integração fornecem a chave JWT antes da inicialização da API, independentemente dos segredos da sua máquina. Para executar a API com `dotnet run` e emitir tokens no Swagger, siga a configuração de usuário local descrita acima.

## Configuração

| Chave | Obrigatória | Padrão/uso |
|---|---:|---|
| `Jwt:Secret` / `REFUGIO_JWT_SECRET` | Sim | Segredo com pelo menos 32 bytes |
| `Jwt:Issuer` | Não | `Refugio360` |
| `Jwt:Audience` | Não | `Refugio360.Mobile` |
| `DemoUser:Username` / `REFUGIO_DEMO_USER` | Sim para emitir token | Usuário de demonstração |
| `DemoUser:Password` / `REFUGIO_DEMO_PASSWORD` | Sim para emitir token | Senha de demonstração |
| `DemoUser:Role` / `REFUGIO_DEMO_ROLE` | Não | `Morador`; use `Moderador` para demo moderada |
| `ConnectionStrings:Refugio` | Não | `Data Source=refugio360.db` |
| `RateLimit:PermitLimit` | Não | 60 requisições por janela |
| `RateLimit:WindowMinutes` | Não | 1 minuto |
