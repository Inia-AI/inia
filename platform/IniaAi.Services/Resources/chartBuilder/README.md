# Chart bundle for the Word export

Every file here except this one is a copy of `chart-builder/` in the Vue app. Do not edit them here.

The Vue app draws a Dynamic Chart block's chart with `src/api/dynamicChart/buildChartOptions.ts`. Its build, `npm run build:chart-builder`, bundles that code with Apache ECharts into `charts.global.js`, and writes the licence and notice files of the code it embeds beside it. `ChartImageRenderService` loads the bundle into headless Chrome and calls `IniaCharts.drawForExport`, so a chart in the Word export is drawn by the same code as the chart on screen.

When a change to the Vue app rebuilds `chart-builder/`, copy every file of `chart-builder/` over the files here, and keep this README.

