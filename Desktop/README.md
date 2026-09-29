# AOI PTH desktop — v0.4

Aplicação WPF em C#/.NET 10 com OpenCvSharp 4.11 para receitas e comparação offline. O console continua em .NET 8.

## Executar

No Windows com SDK .NET 10 x64:

```powershell
dotnet run --project .\Desktop\AOI.PTH.Desktop.csproj
```

## Login, usuários e perfis

1. No primeiro acesso, cadastre o administrador inicial. Não há senha padrão.
2. Use login único e senha numérica de 5 a 128 dígitos (0–9), com confirmação. Zeros iniciais são preservados.
3. Abra **Perfis e acesso → Gerenciar usuários e perfis**.
4. Clique em **Novo usuário** e informe login, senha e estado da conta.
5. No mesmo formulário, marque as abas e ações nos cartões de permissões. Inspeção e julgamento ficam disponíveis para todas as contas ativas; carregar foto de teste é uma permissão separada.
6. Para editar, selecione uma conta e clique em **Editar usuário selecionado**. Altere seus dados e permissões e clique em **Salvar usuário e permissões**. Senhas vazias mantêm a atual. A edição não altera outras contas que compartilham um perfil antigo.
7. **Sair da conta** retorna ao login; rascunhos demonstrativos não são transferidos.

Logins não distinguem maiúsculas/minúsculas. Ativar receita exige acesso a Receitas e Inspeção. Administrar acessos permite conceder outras permissões, portanto deve ser reservado a administradores. O último administrador ativo é protegido. As próximas ações consultam as permissões persistidas, inclusive após edição em outra instância.

Senhas usam PBKDF2-HMAC-SHA256, salt aleatório e 600.000 iterações. Cinco falhas bloqueiam a conta por um minuto. O cadastro fica em `%LOCALAPPDATA%\AOI.PTH\acesso\usuarios.json`.

Este cadastro é local por conta Windows. Não há identidade central nem auditoria corporativa. Proteja a pasta com permissões do Windows: quem consegue alterar/apagar o cadastro local pode modificar os acessos. Para produção compartilhada será necessário um serviço central ou armazenamento administrado pela TI.

## Ciclo da receita

1. Abra **Receitas → Nova receita**, informe modelo e revisão e selecione fotos limpa e corretamente montada do mesmo modelo, lado e revisão.
2. Ajuste threshold, área mínima, margem e união; clique em **Alinhar e gerar ROIs**.
3. Revise as caixas: desenhe, renomeie, edite coordenadas/tamanho, desative ou remova regiões.
4. Selecione cada ROI e configure os critérios de comparação.
5. Confirme a revisão e salve. Alterações de geometria ou critérios invalidam a confirmação. O autor é o login autenticado.
6. **Editar / nova versão** preserva a versão anterior. Exporte/importe o pacote completo `.aoireceita`.
7. Selecione **Usar receita → Carregar foto de teste**. O usuário escolhe o modelo/receita; não há identificação automática entre vários modelos.

A biblioteca fica em `%LOCALAPPDATA%\AOI.PTH\receitas`. Importação e ativação validam integridade, dimensões, coordenadas, máscara e cobertura das regiões.

## Papel dos arquivos

| Arquivo | Uso |
|---|---|
| `receita.json` | Modelo, revisão, versão, autor, diagnóstico, parâmetros e ROIs com critérios. Coordenadas em pixels da referência limpa. |
| `imagens/placa_limpa.png` | Sistema de coordenadas e referência de ausência. |
| `imagens/placa_montada.png` | Montada alinhada à limpa: referência de presença e alvo do alinhamento da foto de teste. |
| `imagens/montada_original.png` | Preserva a captura original para regenerar alinhamento e ROIs em outra versão. Não é comparada diretamente nas coordenadas da limpa. |
| `imagens/area_valida.png` | Máscara binária da cobertura da montada. Intersectada com a máscara da foto de teste para limitar os pixels comparáveis. |
| `integridade.json` | SHA-256 dos cinco arquivos anteriores. Detecta corrupção/alteração; não é assinatura de autoria. |

## Critérios por ROI

A comparação calcula diferenças médias absolutas em cinza suavizado entre teste/limpa e teste/montada, apenas nos pixels válidos. Os números não são probabilidades.

| Critério | Inicial | Uso |
|---|---:|---|
| Cobertura mínima | 0,98 | Fração observável; intervalo permitido 0,90–1. |
| Diferença mínima limpa/montada | 8 | Contraste necessário entre referências, escala 0–255. |
| Margem de decisão | 0,15 | Separação relativa entre as distâncias; maior que 0 e menor que 1. |
| Distância máxima à referência | 35 | Máximo para a melhor correspondência, escala 0–255. |

Cobertura ou contraste insuficientes, aparência distante das duas referências ou distâncias próximas resultam em **REVISÃO MANUAL**. Maior semelhança com a montada indica **PRESENTE**; com a limpa, **AUSENTE**. Receitas antigas sem critérios recebem os valores iniciais; a nova validação da máscara pode exigir revisão.

Esta heurística experimental foi testada com imagens sintéticas. Precisa de calibração com fotos reais e medição de falsos positivos/negativos. Não detecta especificamente polaridade, jumper, solda ou valor do componente e não libera produção.

## Limites restantes

- Julgamentos demonstrativos e resultados offline não geram histórico real de produção.
- Câmera, SQL Server, fila persistente, indicadores, catálogo editável e configurações de estação ainda não estão integrados.
- Contas e receitas persistem localmente; não há sincronização de usuários entre contas Windows.

## Verificar

```powershell
dotnet run --project .\Desktop\AOI.PTH.Desktop.csproj -- --verify-ui .\verificacao-interface-v04
```

Os testes usam dados isolados na pasta indicada. Geram `verification.json` e capturas PNG; falhas geram `verification-error.txt` e saída 1. Consulte `VERIFICACAO.md`.
