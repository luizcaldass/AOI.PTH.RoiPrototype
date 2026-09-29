# AOI PTH - protótipo offline de criação de ROI

## Nova interface gráfica

A interface WPF está na subpasta `Desktop`, no mesmo projeto de trabalho. Requer **SDK .NET 10 x64**. Para abrir a interface a partir desta pasta:

```powershell
dotnet run --project .\Desktop\AOI.PTH.Desktop.csproj
```

Consulte `Desktop/README.md` para o login, perfis configuráveis, ciclo de receitas e limites da comparação offline. O console descrito abaixo permanece em .NET 8, sem mudança no algoritmo de visão.

Primeira etapa do projeto de inspeção óptica automática, executada somente com duas fotografias:

- **placa limpa**: referência da PCB sem componentes;
- **placa montada**: referência da PCB corretamente montada.

O programa não usa câmera. Ele alinha digitalmente a foto montada sobre a placa limpa, rejeita alinhamentos inseguros e só então procura regiões onde surgiram componentes.

## Pré-requisitos

- Windows 10/11;
- SDK .NET 8 (x64);
- duas imagens JPG ou PNG com a placa inteira visível.

As fotos podem ter resoluções e pequenas diferenças de posição, rotação ou perspectiva. Para obter ROIs úteis, mantenha foco, iluminação, lado da placa e distância tão semelhantes quanto possível.

## Organização sugerida das imagens

```text
Images/
  placa_limpa.jpg
  placa_montada.jpg
```

## Executar

Abra um terminal nesta pasta e rode:

```powershell
dotnet restore
dotnet run -- .\Images\placa_limpa.jpg .\Images\placa_montada.jpg .\resultado
```

Parâmetros que normalmente serão calibrados com as fotos reais:

```powershell
dotnet run -- .\Images\placa_limpa.jpg .\Images\placa_montada.jpg .\resultado `
  --threshold 32 --min-area 250 --padding 10 --merge-distance 16
```

## Arquivos gerados

- `01_montada_alinhada.png`: fotografia montada estabilizada na geometria da placa limpa;
- `02_diferenca.png`: intensidade da diferença entre as referências;
- `03_mascara_roi.png`: pixels considerados candidatos a componentes;
- `04_rois_detectadas.png`: caixas numeradas sobre a placa montada;
- `rois.json`: diagnóstico do alinhamento, parâmetros e coordenadas absolutas/normalizadas.

## Segurança da decisão

O programa interrompe a geração quando há poucos pontos correspondentes, poucos *inliers* ou erro de reprojeção alto. Nesse caso o processo retorna `ERROR`; ele não inventa ROIs e não considera a placa aprovada.

As caixas desta fase são **ROIs candidatas**, não uma decisão de qualidade. Depois de revisar e ajustar as caixas com as imagens reais, elas formarão uma receita de PCB. A interface desktop oferece comparação experimental por ROI, ainda sujeita à calibração com fotos reais, e classifica `PRESENTE`, `AUSENTE` ou `REVISÃO MANUAL`.

## Limites desta primeira versão

- não compensa variações grandes de iluminação, foco ou escala;
- componentes próximos podem ser unidos na mesma ROI;
- objetos visualmente parecidos com trilhas/serigrafia podem exigir ajuste de `threshold` e `min-area`;
- jumper deve receber um detector especializado numa etapa posterior.
