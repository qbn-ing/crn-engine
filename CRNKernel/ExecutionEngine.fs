namespace CRNKernel

open System
open System.Text
open System.Collections.Generic
open Microsoft.Research.CRNEngine
open Preprocessor

module ExecutionEngine =

    // 用于收集调试输出的 StringBuilder
    let private debugOutput = new StringBuilder()

    let private clearDebugOutput () = debugOutput.Clear() |> ignore

    let private appendDebug (s: string) = 
        debugOutput.AppendLine(s) |> ignore

    let private getDebugOutput () = debugOutput.ToString()

    let applyDirectives (config: SimulationConfig) (directives: KernelDirective list) (kernelState: KernelState) : SimulationConfig =
        let mutable newConfig : SimulationConfig = config
        for (dir: KernelDirective) in directives do
            match dir with
            | KernelDirective.SetLanguage _ -> () // 语言指令在别处处理
            | KernelDirective.Reset -> ()
            | KernelDirective.ResetState -> ()
            | KernelDirective.Help -> ()
            | KernelDirective.SetExportCsv enabled -> newConfig <- { newConfig with ExportCsv = enabled }
            | KernelDirective.SetCsvTitle title -> newConfig <- { newConfig with CsvTitle = Some title }
            | KernelDirective.MacroReset ->
                kernelState.MacroRegistry.Reset()
                appendDebug "Macro registry has been reset."
            | KernelDirective.MacroList ->
                let macros = kernelState.MacroRegistry.Definitions
                if List.isEmpty macros then
                    appendDebug "No macros defined."
                else
                    appendDebug "=== Defined Macros ==="
                    for (name, def) in macros do
                        appendDebug (sprintf "  %s(%s) :(%s)"
                            name
                            (String.concat ", " def.InputParams)
                            (String.concat ", " def.OutputParams))
            | KernelDirective.ExportExpanded ->
                kernelState.ExportExpanded <- true
            | KernelDirective.ExportMacro macroName ->
                // 标记需要导出指定宏的展开代码
                kernelState.ExportExpanded <- true
                // 在 debug 输出中存储宏名称，稍后处理
                appendDebug (sprintf "[EXPORT_MACRO] %s" macroName)
            | KernelDirective.SetDebug enabled ->
                kernelState.DebugEnabled <- enabled
                if enabled then
                    appendDebug "Debug output enabled."
                else
                    appendDebug "Debug output disabled."
            | KernelDirective.SetPreservedSpecies speciesList ->
                // 设置当前单元格要保留的物质（不累加）
                newConfig <- { newConfig with PreservedSpecies = Some speciesList }
                appendDebug (sprintf "Preserved species set: %s" (String.concat ", " speciesList))
            | KernelDirective.Unknown (msg: string) -> appendDebug (sprintf "Warning: %s" msg)
            | _ -> () // 忽略其他指令（时间、点数等都从 CRN settings 读取）
        newConfig

    /// 使用 CRNEngine 进行模拟
    let simulate (kernelState: KernelState) (crn: Crn) (config: SimulationConfig) (previousState: (string * float) list) : CellResult =
        try
            let crnWithInitials = crn.saturate_initials()

            // 获取代码中定义的物种名称
            let codeDefinedSpecies =
                crnWithInitials.initials
                |> List.map (fun i -> i.species.name)
                |> Set.ofList

            // 检查是否启用了保留模式
            let preservedSpeciesSet =
                match config.PreservedSpecies with
                | Some speciesList -> Set.ofList speciesList
                | None -> Set.empty

            let isPreserveMode = not (Set.isEmpty preservedSpeciesSet)

            // 应用 previous state - 实现 "old = last new or 0" 逻辑
            // 默认模式：
            //   1. 当前单元格定义的物种，如果在前一个单元格中存在，则累加浓度
            //   2. previous state 中独有的物种会被保留
            // 保留模式 (%preserve / %保留)：
            //   1. 保留的物质：上一单元格终态 + 当前声明值（累加）
            //   2. 其他物质：只用当前声明值（不累加，相当于重置）
            let crnWithPreviousState =
                if List.isEmpty previousState then
                    crnWithInitials
                else
                    // 将 previous state 转换为 Map 方便查找
                    let previousMap = previousState |> Map.ofList

                    // 处理当前单元格的 initials
                    let mergedInitials =
                        crnWithInitials.initials
                        |> List.map (fun i ->
                            let speciesName = i.species.name
                            
                            // 检查是否是保留的物质
                            if isPreserveMode && Set.contains speciesName preservedSpeciesSet then
                                // 保留的物质：累加（上一单元格终态 + 当前声明值）
                                match Map.tryFind speciesName previousMap with
                                | Some prevConc ->
                                    let currentValue =
                                        match i.value with
                                        | Microsoft.Research.CRNEngine.Expression.Float f -> f
                                        | _ -> 0.0
                                    let newConc = prevConc + currentValue
                                    { i with value = Microsoft.Research.CRNEngine.Expression.Float newConc }
                                | None ->
                                    // previous state 中没有这个物种，保持原值
                                    i
                            else
                                // 非保留的物质：不累加，只用当前声明值
                                i
                        )

                    // 在保留模式下，添加 previous state 中保留的物质（当前单元格未定义的）
                    let additionalInitials =
                        if isPreserveMode then
                            // 只添加保留的物质（当前单元格未定义的）
                            previousState
                            |> List.filter (fun (name, _) ->
                                Set.contains name preservedSpeciesSet &&
                                not (Set.contains name codeDefinedSpecies)
                            )
                            |> List.map (fun (speciesName, concentration) ->
                                let species = { Microsoft.Research.CRNEngine.Species.name = speciesName }
                                Microsoft.Research.CRNEngine.Initial.create(Microsoft.Research.CRNEngine.Expression.Float concentration, species, None)
                            )
                        else
                            // 默认模式：添加所有 previous state 中独有的物种
                            let currentSpeciesNames =
                                mergedInitials
                                |> List.map (fun i -> i.species.name)
                                |> Set.ofList
                            previousState
                            |> List.filter (fun (name, _) -> not (Set.contains name currentSpeciesNames))
                            |> List.map (fun (speciesName, concentration) ->
                                let species = { Microsoft.Research.CRNEngine.Species.name = speciesName }
                                Microsoft.Research.CRNEngine.Initial.create(Microsoft.Research.CRNEngine.Expression.Float concentration, species, None)
                            )

                    { crnWithInitials with initials = mergedInitials @ additionalInitials }

            // 从 CRN settings 中读取模拟参数
            let simSettings = crnWithPreviousState.settings.simulation
            let stochasticSettings = crnWithPreviousState.settings.stochastic
            let deterministicSettings = crnWithPreviousState.settings.deterministic

            // 应用模拟配置到 CRN
            let finalTime = if simSettings.final > 0.0 then simSettings.final else config.FinalTime
            let numPoints = if simSettings.points > 0 then simSettings.points else config.NumPoints
            let initialTime = simSettings.initial
            let scale = stochasticSettings.scale

            // 生成时间向量 - 这对于 Oslo 求解器正确输出点数至关重要
            let printInterval = (finalTime - initialTime) / float numPoints
            let times = List.init (numPoints + 1) (fun i -> initialTime + float i * printInterval)

            // 保存原始 plots 用于后续输出筛选
            let originalPlots = crnWithPreviousState.settings.simulation.plots

            // 获取所有物种名称
            let allSpeciesNames =
                crnWithPreviousState.all_species()
                |> List.map (fun s -> s.name)
            
            // 将所有物种转换为 plots 格式
            let allSpeciesForPlots =
                allSpeciesNames
                |> List.map (fun name -> Microsoft.Research.CRNEngine.Expression.Key (Microsoft.Research.CRNEngine.Key.Species {name = name}))

            // 合并原始 plots 和所有物种，让模拟器返回所有数据
            let combinedPlots = originalPlots @ allSpeciesForPlots

            let simCrn =
                { crnWithPreviousState with
                    settings = { crnWithPreviousState.settings with
                        simulation = { crnWithPreviousState.settings.simulation with
                            final = finalTime
                            points = numPoints
                            times = times
                            plots = combinedPlots  // 包含原始 plots 和所有物种
                        }
                    }
                }

            // 调试输出：根据模拟器类型动态显示相关参数（只在 DebugEnabled 时显示）
            if kernelState.DebugEnabled then begin
                appendDebug "[DEBUG] === Simulation Parameters ==="
                appendDebug (sprintf "[DEBUG] Simulator: %A" simCrn.settings.simulator)

                // 显示保留模式信息
                if isPreserveMode then
                    appendDebug (sprintf "[DEBUG] Preserve Mode: ON (species: %s)" (String.concat ", " (Set.toList preservedSpeciesSet)))
                else
                    appendDebug "[DEBUG] Preserve Mode: OFF (default accumulation)"

                // 显示时间设置
                if simSettings.initial <> 0.0 || simSettings.final <> 0.0 then
                    appendDebug (sprintf "[DEBUG] Time: initial=%.6f, final=%.6f" simSettings.initial finalTime)
                if simSettings.points <> 0 then
                    appendDebug (sprintf "[DEBUG] Points: %d" simSettings.points)
                if simSettings.seed.IsSome then
                    appendDebug (sprintf "[DEBUG] Seed: %d" simSettings.seed.Value)

                // 根据模拟器类型显示相关参数
                match config.Simulator with
                | Simulator.SSA ->
                    appendDebug (sprintf "[DEBUG] Stochastic Scale: %f" scale)
                    if config.Seed.IsSome then
                        appendDebug (sprintf "[DEBUG] Random Seed: %d" (config.Seed.Value))
                | Simulator.Oslo ->
                    appendDebug "[DEBUG] ODE Solver: Oslo (Deterministic)"
                | Simulator.Sundials ->
                    appendDebug "[DEBUG] ODE Solver: SUNDIALS (Deterministic)"
                | Simulator.LNA ->
                    appendDebug "[DEBUG] Solver: Linear Noise Approximation"
                | Simulator.CME ->
                    appendDebug "[DEBUG] Solver: Chemical Master Equation"
                | Simulator.MC ->
                    appendDebug "[DEBUG] Solver: Moment Closure"
                | _ ->
                    ()

                // 显示 plots 设置
                if not (List.isEmpty (List.ofSeq simSettings.plots)) then
                    let plotsStr =
                        simSettings.plots
                        |> List.map (fun p ->
                            match p with
                            | Microsoft.Research.CRNEngine.Expression.Key (Microsoft.Research.CRNEngine.Key.Species s) -> s.ToString()
                            | _ -> p.ToString()
                        )
                        |> String.concat ", "
                    appendDebug (sprintf "[DEBUG] Plots: %s" plotsStr)

                // 显示参与物质及其初始浓度（包括保留的物种）
                appendDebug "\n[DEBUG] === Species and Initial Concentrations ==="
                simCrn.initials
                |> List.iter (fun (i: Microsoft.Research.CRNEngine.Initial<Microsoft.Research.CRNEngine.Species, Microsoft.Research.CRNEngine.Value>) ->
                    let initValue = Microsoft.Research.CRNEngine.Expression.to_string id i.value
                    // 保留模式下，标注保留的物质
                    if isPreserveMode && Set.contains i.species.name preservedSpeciesSet then
                        appendDebug (sprintf "[DEBUG]   %s: %s (preserved from previous cell)" i.species.name initValue)
                    else
                        appendDebug (sprintf "[DEBUG]   %s: %s" i.species.name initValue)
                )

                // 显示保留的物种信息
                if not (List.isEmpty previousState) then
                    let preservedSpecies =
                        previousState
                        |> List.filter (fun (name, _) -> not (Set.contains name codeDefinedSpecies))
                    if not (List.isEmpty preservedSpecies) then
                        appendDebug "\n[DEBUG] === Preserved from Previous Cell ==="
                        preservedSpecies
                        |> List.iter (fun (name, conc) -> appendDebug (sprintf "[DEBUG]   %s: %f" name conc))
                    else
                        appendDebug "\n[DEBUG] === No Species Preserved (all defined in current cell) ==="

                appendDebug (sprintf "\n[DEBUG] === Reactions (%d total) ===" (List.length simCrn.reactions))
                appendDebug ""
            end

            // 执行模拟 - 使用 CRNEngine 的模拟引擎，统一转换为 Table<float>
            // 使用 CRN 中解析的模拟器类型
            let (times: float list, columnMap: Map<string, float list>) =
                try
                    // 使用 CRN settings 中的模拟器类型
                    let simulatorToUse = simCrn.settings.simulator

                    // 只在 DebugEnabled 时显示详细调试信息
                    if kernelState.DebugEnabled then begin
                        appendDebug (sprintf "[DEBUG] Using Simulator: %A" simulatorToUse)
                        appendDebug (sprintf "[DEBUG] CRN settings simulator: %A" simCrn.settings.simulator)
                    end

                    // 先模拟，然后处理结果
                    let resultObj =
                        match simulatorToUse with
                        | Simulator.SSA -> box (simCrn.to_ssa().simulate())
                        | Simulator.Oslo -> box (simCrn.to_oslo().simulate())
                        | Simulator.Sundials -> box (simCrn.to_sundials().simulate())
                        | Simulator.LNA -> box (simCrn.to_lna().simulate())
                        | Simulator.CME -> box (simCrn.to_ssa().simulate())
                        | Simulator.MC -> box (simCrn.to_ssa().simulate())
                        | Simulator.CMESundials -> box (simCrn.to_sundials().simulate())
                        | _ -> box (simCrn.to_oslo().simulate())

                    // 处理不同类型的 Table
                    match resultObj with
                    | :? Microsoft.Research.CRNEngine.Table<float> as floatTable ->
                        if kernelState.DebugEnabled then
                            appendDebug (sprintf "[DEBUG] Table<float> - times count: %d, columns count: %d" (List.length floatTable.times) (List.length floatTable.columns))
                        let columnMap =
                            floatTable.columns
                            |> List.map (fun c -> c.name, c.values)
                            |> Map.ofList
                        (floatTable.times, columnMap)
                    | :? Microsoft.Research.CRNEngine.Table<Microsoft.Research.CRNEngine.Point> as pointTable ->
                        if kernelState.DebugEnabled then
                            appendDebug (sprintf "[DEBUG] Table<Point> - times count: %d, columns count: %d" (List.length pointTable.times) (List.length pointTable.columns))
                        // 调试输出第一个 column 的信息
                        if kernelState.DebugEnabled then
                            match pointTable.columns with
                            | col :: _ ->
                                appendDebug (sprintf "[DEBUG] First column '%s' has %d points" col.name (List.length col.values))
                                match col.values with
                                | p :: _ -> appendDebug (sprintf "[DEBUG] First point - mean=%f" p.mean)
                                | _ -> ()
                            | _ -> ()
                        let columnMap =
                            pointTable.columns
                            |> List.map (fun c -> c.name, c.values |> List.map (fun p -> p.mean))
                            |> Map.ofList
                        (pointTable.times, columnMap)
                    | _ ->
                        if kernelState.DebugEnabled then
                            appendDebug "[DEBUG] Unknown table type"
                        ([], Map.empty)
                with
                | :? System.Exception as ex ->
                    // 捕获模拟过程中的错误，使用错误处理模块给出友好的提示
                    let errorMsg = ErrorHandling.getFriendlyErrorMessage ex
                    let suggestion = ErrorHandling.getSuggestion (ErrorHandling.getErrorType ex)
                    
                    appendDebug errorMsg
                    appendDebug ""
                    appendDebug (sprintf " Suggestion: %s" suggestion)
                    appendDebug ""
                    raise ex  // 重新抛出异常，让外层处理

            // 获取物种列表 - 使用 saturate_initials 后的 CRN
            let speciesList = simCrn.all_species() |> List.map (fun s -> s.name)

            // 根据 plots 配置过滤物种 - 使用保存的 originalPlots（用户原始设置）
            // 从 originalPlots 中提取要显示的物种/速率名称
            let speciesToPlot =
                if not (List.isEmpty (List.ofSeq originalPlots)) then
                    // 从原始 CRN 的 plots 设置中获取，过滤掉宏内部物质（名称包含 _local_ 的）
                    originalPlots
                    |> List.map (fun p ->
                        match p with
                        | Microsoft.Research.CRNEngine.Expression.Key (Microsoft.Research.CRNEngine.Key.Species s) -> s.name
                        | Microsoft.Research.CRNEngine.Expression.Key (Microsoft.Research.CRNEngine.Key.Rate r) -> "[" + r + "]"  // 速率名称加方括号
                        | _ -> ""
                    )
                    |> List.filter (fun s ->
                        s <> "" &&
                        not (s.Contains("_local_"))  // 过滤掉宏内部物质
                    )
                else
                    // 没有指定 plots，绘制所有物种（但过滤掉宏内部物质）
                    speciesList
                    |> List.filter (fun s -> not (s.Contains("_local_")))

            // 生成绘图数据 - 根据 plots 配置过滤
            let plotData =
                if not (List.isEmpty speciesToPlot) then
                    // 只绘制 plots 中指定的物种
                    columnMap
                    |> Map.toList
                    |> List.filter (fun (name, _) -> List.contains name speciesToPlot)
                    |> List.map (fun (name, values) -> (name, values))
                else
                    // 没有指定 plots，绘制所有物种
                    columnMap
                    |> Map.toList
                    |> List.map (fun (name, values) -> (name, values))

            // 调试输出：检查 plotData 是否为空（只在 DebugEnabled 时显示）
            if kernelState.DebugEnabled then begin
                appendDebug (sprintf "[DEBUG] plotData has %d species" (List.length plotData))
                if not (List.isEmpty plotData) then
                    appendDebug (sprintf "[DEBUG] First species in plotData: %s with %d points"
                        (fst (List.head plotData)) (List.length (snd (List.head plotData))))
            end

            // 生成终态数据 - 只保存非零浓度的物种用于试管模型物质积累
            // 调试输出：检查 columnMap 包含的物种
            if kernelState.DebugEnabled then begin
                appendDebug (sprintf "[DEBUG] columnMap has %d species" (Map.count columnMap))
                columnMap |> Map.iter (fun name values ->
                    appendDebug (sprintf "[DEBUG]   %s: %f (final)" name (List.last values))
                )
            end

            let speciesData =
                columnMap
                |> Map.toList
                |> List.choose (fun (name, values) ->
                    if not (List.isEmpty values) then
                        let finalConc = List.last values
                        // 只保存非零浓度的物种
                        if finalConc <> 0.0 then
                            Some (name, finalConc)
                        else
                            None
                    else
                        None
                )
            
            // 调试输出：检查 speciesData 包含的物种
            if kernelState.DebugEnabled then begin
                appendDebug (sprintf "[DEBUG] speciesData (for accumulation) has %d species" (List.length speciesData))
                speciesData |> List.iter (fun (name, conc) ->
                    appendDebug (sprintf "[DEBUG]   %s: %f" name conc)
                )
            end
            
            // 导出 CSV 文件（仅在启用 %csv 指令时）
            if config.ExportCsv then
                try
                    // 使用 speciesToPlot 过滤 CSV 导出的物种（与绘图保持一致）
                    let speciesForCsv =
                        if not (List.isEmpty speciesToPlot) then
                            speciesToPlot
                        else
                            columnMap |> Map.toList |> List.map fst

                    let csvContent =
                        let header = "Time," + (speciesForCsv |> String.concat ",")
                        let rows =
                            [0..List.length times - 1]
                            |> List.map (fun i ->
                                let timeStr = times.[i].ToString()
                                let values =
                                    speciesForCsv
                                    |> List.map (fun name ->
                                        match Map.tryFind name columnMap with
                                        | Some vals -> if i < List.length vals then vals.[i].ToString() else ""
                                        | None -> ""
                                    )
                                    |> String.concat ","
                                timeStr + "," + values
                            )
                        header + "\n" + (rows |> String.concat "\n")

                    // 生成文件名：使用 title 或 单元格索引 + 时间戳（支持中文）
                    let timestamp = System.DateTime.Now.ToString("yyyyMMdd_HHmmss")
                    let fileName =
                        match config.CsvTitle with
                        | Some title when not (System.String.IsNullOrEmpty(title)) ->
                            // 清理标题中的非法文件名字符，保留中文
                            let validTitle = title.Replace(" ", "_").Replace("/", "_").Replace("\\", "_")
                                                   .Replace(":", "_").Replace("*", "_").Replace("?", "_")
                                                   .Replace("\"", "_").Replace("<", "_").Replace(">", "_").Replace("|", "_")
                            sprintf "%s_%s.csv" validTitle timestamp
                        | _ ->
                            let cellIndex = kernelState.ExecutionCount
                            sprintf "simulation_cell%d_%s.csv" cellIndex timestamp

                    let filePath = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), fileName)
                    // 使用 UTF-8 编码写入文件，支持中文文件名和内容
                    System.IO.File.WriteAllText(filePath, csvContent, System.Text.Encoding.UTF8)
                    appendDebug "\n=== CSV Exported ==="
                    appendDebug (sprintf "Saved to: %s" filePath)
                with ex ->
                    appendDebug (sprintf "Warning: Failed to export CSV - %s" ex.Message)

            // 返回调试输出
            let debugText = getDebugOutput ()
            clearDebugOutput ()

            CellSuccess (speciesData, times, plotData, numPoints, debugText, config.CsvTitle)
        with ex ->
            CellError ("Simulation", ex.Message, [ex.ToString()])

    let executeCell (kernelState: KernelState) (code: string) : CellResult =
        let directives, pureCode = DirectiveParser.extractDirectives code
        // 预处理：%指令控制（作用于去除 Jupyter 指令后的代码）
        let preprocessorResult = Preprocessor.extractDirectives pureCode

        // 检查是否有语言指定指令
        let specifiedLang =
            directives
            |> List.tryFind (function KernelDirective.SetLanguage _ -> true | _ -> false)
            |> function
                | Some (KernelDirective.SetLanguage lang) ->
                    match lang.ToLower() with
                    | "crn" -> Some CodeLanguage.CRN
                    | "dsd" -> Some CodeLanguage.DSD
                    | _ -> None
                | _ -> None

        // 统一解析 reset 模式（Jupyter 指令 + Preprocessor 指令），后出现的指令优先
        let directiveResetMode =
            directives
            |> List.fold (fun acc dir ->
                match dir with
                | KernelDirective.Reset -> Some ResetMode.All
                | KernelDirective.ResetState -> Some ResetMode.StateOnly
                | _ -> acc
            ) None

        let effectiveResetMode =
            match preprocessorResult.ResetMode with
            | Some mode -> Some mode
            | None -> directiveResetMode

        // 先执行 reset，再应用其他指令，避免 reset 清掉当前单元的 %export expanded / %debug 等设置
        match effectiveResetMode with
        | Some ResetMode.StateOnly ->
            kernelState.ResetStateOnly()
            appendDebug "Material state reset (macros and settings preserved)."
        | Some ResetMode.All ->
            kernelState.Reset()
            appendDebug "All state reset (including macros and settings)."
        | None -> ()

        let config =
            SimulationConfig.defaultConfig
            |> fun c -> { c with PreserveState = kernelState.PreserveState }
            |> fun c -> applyDirectives c directives kernelState

        kernelState.PreserveState <- config.PreserveState

        for dir in directives do
            match dir with
            | KernelDirective.Help -> ()
            | _ -> ()

        // 显示帮助
        if preprocessorResult.ShowHelp then
            appendDebug (Preprocessor.getHelpText ())

        // reset 已在上面统一处理，避免重复执行与状态被意外覆盖
        
        // 显示预处理警告
        preprocessorResult.Warnings |> List.iter (fun msg -> appendDebug msg)

        // 检查是否有 ExportMacro 指令
        let exportMacroName =
            directives
            |> List.choose (function
                | KernelDirective.ExportMacro name -> Some name
                | _ -> None
            )
            |> List.tryHead

        // 处理宏：展开宏定义和调用
        let macroResult = MacroExpander.processCode kernelState.MacroRegistry preprocessorResult.CodeWithoutPreprocessor appendDebug kernelState.DebugEnabled

        // 如果启用了导出展开代码，先显示展开后的代码
        if kernelState.ExportExpanded then
            // 检查是否是 ExportMacro 指令
            match exportMacroName with
            | Some macroName ->
                // 展开指定的宏定义（按形参展开）
                let expandResult = MacroExpander.expandMacroDefinition kernelState.MacroRegistry macroName
                if not (List.isEmpty expandResult.Errors) then
                    // 展开失败，显示错误
                    let errorMsg = String.concat "\n" expandResult.Errors
                    appendDebug (sprintf "Error expanding macro '%s': %s" macroName errorMsg)
                else
                    // 显示展开后的代码
                    appendDebug (sprintf "// === Exported Macro: %s (expanded by formal parameters) ===" macroName)
                    appendDebug expandResult.ExpandedCode
                    appendDebug (sprintf "// === End of expanded macro %s ===" macroName)
                kernelState.ExportExpanded <- false
            | None ->
                // 标准的 %export expanded 行为
                let expandedDisplay = MacroExpander.formatExpandedCode macroResult kernelState.MacroRegistry macroResult.ExpandedCode
                // 逐行添加，避免 StringBuilder.AppendLine 添加额外的换行符
                expandedDisplay.Split('\n') |> Array.iter (fun line -> appendDebug line)
                kernelState.ExportExpanded <- false  // 重置标志

        // 检查宏处理是否有错误
        if not (List.isEmpty macroResult.Errors) then
            let errorMsg = String.concat "\n" macroResult.Errors
            CellError ("Macro Expansion", errorMsg, [sprintf "Failed to expand macros:\n%s" errorMsg])
        else
            // 使用展开后的代码进行解析
            let finalCode = macroResult.ExpandedCode

            // 如果展开后的代码为空，说明只有宏定义
            if String.IsNullOrWhiteSpace(finalCode) then
                let macroCount = kernelState.MacroRegistry.Definitions |> List.length
                appendDebug (sprintf "Macro definitions registered: %d" macroCount)
                appendDebug "No simulation performed (macro definitions only)"
                appendDebug "\nNote: Macro definitions are stored but not executed."
                appendDebug "   Use %invoke to call macros in subsequent cells."
                CellSuccess ([], [0.0], [], 0, getDebugOutput (), None)
            else
                // 使用指定的语言或自动检测
                let parseResult =
                    match specifiedLang with
                    | Some lang -> CodeParser.parse finalCode lang
                    | None -> CodeParser.parseFlexible finalCode

                match parseResult with
                | Error msg ->
                    CellError ("Parse", msg, [sprintf "Failed to parse code: %s" msg])

                | CRNModel crn ->
                    // 使用累积的物质状态（默认开启）
                    // 实现 "old = last new or 0" 逻辑：
                    // 1. 跨单元格累积：上一个单元格的终态 → 当前单元格的初态
                    // 2. 重复运行同一单元格：清除该单元格的累积影响，恢复到运行前的状态

                    // 获取全局累积状态
                    let globalState =
                        if kernelState.PreserveState then
                            kernelState.GetAccumulatedState()
                        else
                            []

                    // 保存执行前的快照（只在第一次执行该单元格时）
                    kernelState.SaveSnapshotBeforeCell kernelState.ExecutionCount

                    // 如果启用了 %preserve，在模拟前直接修改全局状态（清除不在保留列表中的物质）
                    match config.PreservedSpecies with
                    | Some preservedList ->
                        let preservedSet = Set.ofList preservedList
                        // 直接修改全局状态，只保留指定的物质
                        let filteredState =
                            globalState
                            |> List.filter (fun (name, _) -> Set.contains name preservedSet)
                        kernelState.SetPreviousState filteredState
                    | None ->
                        ()  // 不过滤

                    // previousState = 修改后的全局状态（用于累加到当前单元格的初始浓度）
                    let previousState = kernelState.GetAccumulatedState()

                    if kernelState.PreserveState && not (List.isEmpty previousState) then
                        appendDebug "\n=== State from Previous Run (old = last new or 0) ==="
                        previousState
                        |> List.iter (fun (name, conc) -> appendDebug (sprintf "  %s: %f" name conc))
                        appendDebug ""

                    try
                        let result : CellResult = simulate kernelState crn config previousState

                        match result with
                        | CellSuccess (speciesData, times, plotData, numPoints, simDebugOutput, title) ->
                            // 更新累积状态（恢复快照后添加新物质）
                            kernelState.UpdateAccumulatedState kernelState.ExecutionCount speciesData
                            // 使用模拟的调试输出（展开代码已经在上面直接输出到 debug）
                            CellSuccess (speciesData, times, plotData, numPoints, simDebugOutput, title)
                        | CellError (ename, evalue, traceback) ->
                            CellError (ename, evalue, traceback)
                    with
                    | ex ->
                        // 捕获模拟异常，返回友好的错误消息
                        let errorMsg = ErrorHandling.getFriendlyErrorMessage ex
                        let suggestion = ErrorHandling.getSuggestion (ErrorHandling.getErrorType ex)

                        CellError (
                            ErrorHandling.getErrorSummary ex,
                            ex.Message,
                            [errorMsg; ""; suggestion; ""; ex.ToString()]
                        )

                | DSDModel crn ->
                    // 使用累积的物质状态（默认开启）
                    // 如果是第一次执行该单元格，保存执行前的快照
                    if kernelState.PreserveState then
                        kernelState.SaveSnapshotBeforeCell kernelState.ExecutionCount

                    // 获取全局累积状态
                    let globalState =
                        if kernelState.PreserveState then
                            kernelState.GetAccumulatedState()
                        else
                            []

                    // 如果启用了 %preserve，在模拟前直接修改全局状态（清除不在保留列表中的物质）
                    match config.PreservedSpecies with
                    | Some preservedList ->
                        let preservedSet = Set.ofList preservedList
                        // 直接修改全局状态，只保留指定的物质
                        let filteredState =
                            globalState
                            |> List.filter (fun (name, _) -> Set.contains name preservedSet)
                        kernelState.SetPreviousState filteredState
                    | None ->
                        ()  // 不过滤

                    // previousState = 修改后的全局状态（用于累加到当前单元格的初始浓度）
                    let previousState = kernelState.GetAccumulatedState()

                    if kernelState.PreserveState && not (List.isEmpty previousState) then
                        appendDebug "\n=== Accumulated State from Previous Cells ==="
                        previousState
                        |> List.iter (fun (name, conc) -> appendDebug (sprintf "  %s: %f" name conc))
                        appendDebug ""

                    try
                        let result : CellResult = simulate kernelState crn config previousState

                        match result with
                        | CellSuccess (speciesData, times, plotData, numPoints, simDebugOutput, title) ->
                            // 更新累积状态（恢复快照后添加新物质）
                            kernelState.UpdateAccumulatedState kernelState.ExecutionCount speciesData
                            // 使用模拟的调试输出（展开代码已经在上面直接输出到 debug）
                            CellSuccess (speciesData, times, plotData, numPoints, simDebugOutput, title)
                        | CellError (ename, evalue, traceback) ->
                            CellError (ename, evalue, traceback)
                    with
                    | ex ->
                        // 捕获模拟异常，返回友好的错误消息
                        let errorMsg = ErrorHandling.getFriendlyErrorMessage ex
                        let suggestion = ErrorHandling.getSuggestion (ErrorHandling.getErrorType ex)

                        CellError (
                            ErrorHandling.getErrorSummary ex,
                            ex.Message,
                            [errorMsg; ""; suggestion; ""; ex.ToString()]
                        )

    let getHelpText () = Preprocessor.getHelpText ()
