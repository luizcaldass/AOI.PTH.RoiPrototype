# Interface AOI PTH — v0.2

Interface desktop WPF em C#/.NET 10, dentro da pasta original do projeto. Paleta azul, branco e cinza inspirada na Panasonic; não constitui aplicação oficialmente homologada pela marca. O desenho da PCB é uma ilustração vetorial, não uma foto real nem resultado do detector.

## Abrir

Instale o SDK .NET 10 x64 (https://dotnet.microsoft.com/en-us/download/dotnet/10.0), reabra o VS Code e, no terminal da pasta principal AOI.PTH.RoiPrototype, execute:

```powershell
dotnet run --project .\Desktop\AOI.PTH.Desktop.csproj
```

Não há pacotes externos na interface. O SDK inclui as referências WPF. O console de visão original continua com seu projeto .NET 8 e seus pacotes OpenCvSharp.

## Disponível nesta versão

- Menu Arquivo: importar/exportar como ações sinalizadas para integração; Sair fecha a janela.
- Navegação entre Inspeção, Receitas, Defeitos, Histórico, Produção, Configurações e Perfis e acesso.
- Três perfis demonstrativos: Operador, Engenharia e Administrador. Permissões centralizadas e verificadas pelos comandos, não somente pela aparência dos botões.
- Seleção de três divergências fictícias; atualização dos recortes ilustrativos e números de exemplo.
- Aceitar ou marcar defeito em memória; cancelar o julgamento selecionado devolve a região para Pendente.
- Finalização simulada somente quando todas as regiões foram julgadas. Reiniciar demonstração restaura os exemplos.
- Produção e status de infraestrutura mostram ausência de integração, sem fingir dados medidos.

## Limites e próxima etapa

O seletor de perfil é uma ferramenta de demonstração, NÃO é login nem controle de segurança real. A autenticação será vinculada a uma identidade validada e as permissões também serão verificadas pelos serviços. Não usar esta interface para liberar placas em produção.

Importação de .aoireceita, edição de ROIs, fotos reais, SQL Server, pesquisa, exportação, cadastro de usuários e fila persistente não estão conectados. Os botões exibem um aviso de integração pendente. Os campos de configuração não persistem valores. Nenhuma credencial é solicitada ou gravada.

Todos os julgamentos são rascunhos em memória, descartados ao fechar a aplicação. Os números são exemplos, não resultados calculados nem probabilidades. O histórico contém somente exemplos e a simulação não altera contadores.

## Estrutura

- `Theme.xaml`: cores e estilos compartilhados.
- `MainWindow.xaml`: estrutura, menu, navegação e status.
- `Views/`: tela de inspeção e telas administrativas.
- `ViewModels/ShellViewModel.cs`: navegação, permissões e estado de demonstração.
- `Controls/BoardPreview.cs`: ilustrações vetoriais leves, sem imagens externas.

## Verificação reproduzível

O modo abaixo testa permissões e estados de julgamento e renderiza as telas WPF em PNG. Use uma pasta de saída nova. A janela de verificação fica fora da área visível e fecha ao terminar.

```powershell
dotnet run --project .\Desktop\AOI.PTH.Desktop.csproj -- --verify-ui .\verificacao-interface
```

O resultado estará em `verification.json`. Capturas incluem todas as abas e a interface em 1280 × 720; nessa resolução, o conteúdo da inspeção pode ser rolado verticalmente.
