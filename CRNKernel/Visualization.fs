namespace CRNKernel

open System
open System.Text
open Newtonsoft.Json
open Newtonsoft.Json.Linq

module Visualization =

    let createPlotlyChart (times: float list) (plotData: (string * float list) list) (title: string option) : JObject =
        // 如果没有数据，返回空图表
        if List.isEmpty times || List.isEmpty plotData then
            let emptyChart = """{ "data": [], "layout": { "title": "No data to plot", "xaxis": {"title": "Time"}, "yaxis": {"title": "Concentration"} } }"""
            JObject.Parse(emptyChart)
        else
            let traces =
                plotData
                |> List.mapi (fun i (name, values) ->
                    let timeArray = times |> List.map string |> String.concat ","
                    let valuesArray = values |> List.map string |> String.concat ","
                    sprintf """{
                        "x": [%s],
                        "y": [%s],
                        "mode": "lines",
                        "name": "%s",
                        "line": {"width": 2}
                    }""" timeArray valuesArray name
                )
                |> String.concat ",\n"

            // 计算 x 轴和 y 轴的范围
            let xMin = if List.isEmpty times then 0.0 else List.head times
            let xMax = if List.isEmpty times then 100.0 else List.last times
            
            // 计算 y 轴范围：支持负值
            let allValues = plotData |> List.collect snd
            let yMin = allValues |> List.fold min System.Double.MaxValue
            let yMax = allValues |> List.fold max System.Double.MinValue
            
            // 添加 10% 的边距，如果最小值和最大值相同则添加默认范围
            let yRange = yMax - yMin
            let (yRangeMin, yRangeMax) =
                if yRange = 0.0 then
                    // 所有值相同，添加默认范围
                    (yMin - 10.0, yMax + 10.0)
                else
                    // 添加 10% 边距
                    let padding = yRange * 0.1
                    (yMin - padding, yMax + padding)

            // 使用自定义标题或默认标题
            let chartTitle = defaultArg title "Species Concentration Over Time"

            let layoutStr =
                "{" +
                sprintf """ "title": "%s", "xaxis": {"title": "Time", "showgrid": true, "range": [%f, %f]}, "yaxis": {"title": "Concentration", "showgrid": true, "range": [%f, %f]}, """ chartTitle xMin xMax yRangeMin yRangeMax +
                """ "legend": {"x": 1, "y": 1, "xanchor": "right", "yanchor": "top", "bgcolor": "rgba(0,0,0,0)"}, "plot_bgcolor": "rgba(0,0,0,0)", "paper_bgcolor": "rgba(0,0,0,0)", "hovermode": "x unified", "margin": {"t": 50, "r": 50, "b": 50, "l": 70}, "font": {"color": "#333"} }"""

            let chartStr = sprintf """{ "data": [%s], "layout": %s }""" traces layoutStr

            JObject.Parse(chartStr)

    let createTextTable (speciesData: (string * float) list) : string =
        let sb = StringBuilder()
        sb.AppendLine("Final Species Concentrations:") |> ignore
        sb.AppendLine("==============================") |> ignore
        for (species, conc) in speciesData do
            sb.AppendLine(sprintf "%-20s: %g" species conc) |> ignore
        sb.ToString()

    let createMarkdownResult (speciesData: (string * float) list) (times: float list) (numPoints: int) : string =
        let sb = StringBuilder()
        sb.AppendLine("### Simulation Results") |> ignore
        sb.AppendLine() |> ignore

        if not (List.isEmpty times) then
            let startTime = times |> List.head
            let endTime = times |> List.last
            let actualPoints = List.length times
            sb.AppendLine(sprintf "**Simulation Time:** %.2f to %.2f" startTime endTime) |> ignore
            sb.AppendLine(sprintf "**Number of Time Points:** %d (requested: %d)" actualPoints numPoints) |> ignore
            sb.AppendLine() |> ignore

        sb.AppendLine("#### Final Concentrations") |> ignore
        sb.AppendLine() |> ignore
        sb.AppendLine("| Species | Concentration |") |> ignore
        sb.AppendLine("|---------|---------------|") |> ignore

        for (species, conc) in speciesData do
            sb.AppendLine(sprintf "| %s | %g |" species conc) |> ignore

        sb.AppendLine() |> ignore
        sb.ToString()

    let createDisplayData (speciesData: (string * float) list) (times: float list) (plotData: (string * float list) list) (numPoints: int) (title: string option) : Map<string, JToken> =
        let plotlyJson = createPlotlyChart times plotData title
        let markdownText = createMarkdownResult speciesData times numPoints
        let textTable = createTextTable speciesData

        Map.ofList [
            "application/vnd.plotly.v1+json", plotlyJson :> JToken
            "text/markdown", JToken.FromObject(markdownText)
            "text/plain", JToken.FromObject(textTable)
        ]

    let createMetadata () : Map<string, JObject> =
        Map.empty

    let formatResult (speciesData: (string * float) list) (times: float list) (plotData: (string * float list) list) (numPoints: int) : string =
        let sb = StringBuilder()
        sb.AppendLine("Simulation completed successfully.") |> ignore
        sb.AppendLine() |> ignore

        if not (List.isEmpty times) then
            sb.AppendLine(sprintf "Time range: %.2f to %.2f" (List.head times) (List.last times)) |> ignore
            sb.AppendLine(sprintf "Number of time points: %d (requested: %d)" (List.length times) numPoints) |> ignore
            sb.AppendLine() |> ignore

        // 从 plotData 中提取最后一个时间点的浓度用于显示
        // 这样即使浓度为 0 也会显示（只要用户在 plots 中指定了）
        let finalConcentrations =
            plotData
            |> List.map (fun (name, values) -> 
                if not (List.isEmpty values) then
                    Some (name, List.last values)
                else
                    None
            )
            |> List.choose id

        sb.AppendLine(sprintf "Number of species: %d" (List.length finalConcentrations)) |> ignore
        sb.AppendLine() |> ignore
        sb.AppendLine("Final concentrations:") |> ignore

        for (species, conc) in finalConcentrations do
            sb.AppendLine(sprintf "  %s: %g" species conc) |> ignore

        sb.ToString()

    let formatError (ename: string) (evalue: string) (traceback: string list) : string =
        let sb = StringBuilder()
        
        // 添加错误标题
        sb.AppendLine(sprintf "❌ %s: %s" ename evalue) |> ignore
        sb.AppendLine() |> ignore
        
        // 显示 traceback 中的友好错误消息
        if not (List.isEmpty traceback) then
            // 先显示非 Traceback 开头的行（这些是友好错误消息）
            traceback 
            |> List.filter (fun line -> not (line.Contains("Traceback:")) && not (line.Contains("at ")))
            |> List.iter (fun line -> sb.AppendLine(line) |> ignore)
            
            sb.AppendLine() |> ignore
            sb.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━") |> ignore
            sb.AppendLine() |> ignore
            
            // 显示详细堆栈信息
            sb.AppendLine("📋 Detailed Traceback:") |> ignore
            sb.AppendLine() |> ignore
            for line in traceback do
                if line.Contains("at ") then
                    sb.AppendLine(line) |> ignore
        
        sb.ToString()
