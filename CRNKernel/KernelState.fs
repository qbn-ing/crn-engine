namespace CRNKernel

open System
open System.Collections.Generic
open Microsoft.Research.CRNEngine

/// 内核状态
type KernelStatus =
    | Starting
    | Idle
    | Busy
    | ShuttingDown

/// 单元格执行结果
type CellResult =
    | CellSuccess of speciesData: (string * float) list * times: float list * plotData: (string * float list) list * numPoints: int * debugOutput: string * title: string option
    | CellError of ename: string * evalue: string * traceback: string list

/// 内核全局状态
type KernelState() =
    let mutable executionCount = 0
    let mutable status = Starting
    let previousState = Dictionary<string, float>()  // 全局累积的物质状态
    let mutable preserveState = true
    let mutable lastResult: CellResult option = None
    let logMessages = List<string>()
    let macroRegistry = MacroRegistry()
    let mutable exportExpanded = false
    let mutable debugEnabled = false  // 控制 debug 输出
    // 记录上一个执行单元格的 ID 和状态快照（用于重复执行时恢复）
    let mutable lastExecutionCount = -1
    let mutable lastCellSnapshot = Dictionary<string, float>()  // 执行前的状态快照

    member _.ExecutionCount = executionCount

    member _.IncrementExecutionCount() =
        executionCount <- executionCount + 1
        executionCount

    member _.Status
        with get() = status
        and set(value) = status <- value

    member _.PreviousState =
        previousState |> Seq.toList |> List.map (fun kvp -> kvp.Key, kvp.Value)

    member _.SetPreviousState (state: (string * float) list) =
        previousState.Clear()
        state |> List.iter (fun (species, conc) -> previousState.[species] <- conc)

    member _.PreserveState
        with get() = preserveState
        and set(value) = preserveState <- value

    member _.LastResult = lastResult

    member _.SetLastResult (result: CellResult) =
        lastResult <- Some result

    member _.AddLog (message: string) =
        logMessages.Add(message)

    member _.GetLogs() =
        logMessages |> Seq.toList

    member _.ClearLogs() =
        logMessages.Clear()

    member _.MacroRegistry = macroRegistry :> MacroRegistry

    member _.ExportExpanded
        with get() = exportExpanded
        and set(value) = exportExpanded <- value

    member _.DebugEnabled
        with get() = debugEnabled
        and set(value) = debugEnabled <- value

    member _.Reset() =
        // 重置所有状态（清空试管，清除宏定义、调试设置等）
        previousState.Clear()
        lastCellSnapshot.Clear()
        lastExecutionCount <- -1
        preserveState <- true
        lastResult <- None
        logMessages.Clear()
        exportExpanded <- false
        debugEnabled <- false
        macroRegistry.Reset()

    member _.ResetStateOnly() =
        // 只清空试管，保留宏定义和其他设置
        previousState.Clear()
        lastCellSnapshot.Clear()
        lastExecutionCount <- -1
        preserveState <- true
        lastResult <- None
        exportExpanded <- false
        // debugEnabled 保持不变

    member _.GetAccumulatedState () : (string * float) list =
        // 获取当前试管中的物质状态（累积状态）
        previousState |> Seq.toList |> List.map (fun kvp -> kvp.Key, kvp.Value)

    member _.SaveSnapshotBeforeCell (execCount: int) =
        // 保存单元格执行前的状态快照
        // 如果 execCount == lastExecutionCount，说明是重复运行同一个单元格，不更新快照
        if execCount <> lastExecutionCount then
            lastCellSnapshot.Clear()
            previousState |> Seq.iter (fun kvp -> lastCellSnapshot.[kvp.Key] <- kvp.Value)
            lastExecutionCount <- execCount

    member _.GetSnapshotBeforeCell () : (string * float) list =
        // 获取上一次执行前的状态快照
        lastCellSnapshot |> Seq.toList |> List.map (fun kvp -> kvp.Key, kvp.Value)

    member _.UpdateAccumulatedState (execCount: int) (newSpeciesData: (string * float) list) =
        // 更新试管中的物质状态：
        // 直接用终态替换试管状态（不恢复快照）
        // %preserve 的作用是在 simulate 函数之前处理（模拟前过滤全局状态）

        // 清空试管
        previousState.Clear()

        // 直接添加新物质（用终态替换）
        newSpeciesData |> List.iter (fun (species, conc) ->
            previousState.[species] <- conc
        )

        // 不更新快照，保持"运行前的状态"

    member _.ClearCellState (cellId: int) =
        // 兼容方法，无实际操作
        ()

/// 模拟配置
type SimulationConfig = {
    FinalTime: float
    NumPoints: int
    Trajectories: int
    Simulator: Simulator
    Seed: int option
    PreserveState: bool
    PlotSpecies: string list
    ExportCsv: bool
    CsvTitle: string option
    PreservedSpecies: string list option  // 当前单元格要保留的物质（仅当前单元格有效，不累加）
}

module SimulationConfig =
    let defaultConfig = {
        FinalTime = 100.0
        NumPoints = 100
        Trajectories = 1
        Simulator = Simulator.SSA
        Seed = None
        PreserveState = true
        PlotSpecies = []
        ExportCsv = false  // 默认不导出 CSV
        CsvTitle = None    // 默认没有标题
        PreservedSpecies = None  // 默认不启用保留模式
    }

/// 指令类型
type KernelDirective =
    | SetTime of float
    | SetPoints of int
    | SetTrajectories of int
    | SetSimulator of Simulator
    | SetSeed of int
    | SetPreserve of bool
    | SetLanguage of string
    | Reset
    | ResetState
    | Plot of string list
    | Help
    | SetExportCsv of bool
    | SetCsvTitle of string
    | MacroReset
    | MacroList
    | ExportExpanded
    | ExportMacro of string  // 导出指定宏的展开代码（按形参展开）
    | SetDebug of bool
    | SetPreservedSpecies of string list  // 设置当前单元格要保留的物质
    | Unknown of string

/// 指令解析器
module DirectiveParser =
    open System

    let parseSimulator (s: string) : Simulator =
        match s.ToLower() with
        | "stochastic" -> Simulator.SSA
        | "deterministic" -> Simulator.Oslo
        | "deterministicstiff" -> Simulator.Oslo
        | "jit" -> Simulator.SSA
        | "sundials" -> Simulator.Sundials
        | "sundialsstiff" -> Simulator.Sundials
        | "cme" -> Simulator.CME
        | "cmesundials" -> Simulator.CMESundials
        | "cmesundialsstiff" -> Simulator.CMESundials
        | "mc" -> Simulator.MC
        | _ -> Simulator.SSA

    let parse (code: string) : KernelDirective list =
        let lines = code.Split('\n')
        lines
        |> Array.toList
        |> List.choose (fun line ->
            let trimmed = line.Trim()
            if not (trimmed.StartsWith("%")) then None
            else
                let parts = trimmed.[1..].Split([|' '|], StringSplitOptions.RemoveEmptyEntries)
                if parts.Length = 0 then None
                else
                    let cmd = parts.[0].ToLower()

                    match cmd with
                    | "crn" -> Some (SetLanguage "crn")
                    | "dsd" -> Some (SetLanguage "dsd")
                    | "reset" ->
                        if parts.Length > 1 && parts.[1].ToLower() = "all" then
                            Some Reset
                        else
                            Some ResetState
                    | "help" -> Some Help
                    | "csv" -> Some (SetExportCsv true)
                    | "title" ->
                        if parts.Length > 1 then
                            // 获取 title 后面的字符串（去掉引号）
                            let title = String.Join(" ", parts.[1..]).Trim('"').Trim()
                            Some (SetCsvTitle title)
                        else
                            Some (Unknown "%title requires a string argument")
                    | "export" ->
                        if parts.Length >= 2 then
                            let secondArg = parts.[1].ToLower()
                            if secondArg = "expanded" then
                                Some ExportExpanded
                            elif secondArg = "macro" && parts.Length >= 3 then
                                // %export macro <MacroName>
                                let macroName = parts.[2]
                                Some (ExportMacro macroName)
                            else
                                Some (Unknown "%export expanded - Export expanded macro code")
                        else
                            Some (Unknown "%export expanded - Export expanded macro code")
                    | "debug" ->
                        if parts.Length >= 2 then
                            let secondArg = parts.[1].ToLower()
                            if secondArg = "on" || secondArg = "true" || secondArg = "1" then
                                Some (SetDebug true)
                            elif secondArg = "off" || secondArg = "false" || secondArg = "0" then
                                Some (SetDebug false)
                            else
                                Some (Unknown "%debug on|off - Enable or disable debug output")
                        else
                            Some (Unknown "%debug on|off - Enable or disable debug output")
                    | "macro" ->
                        if parts.Length > 1 then
                            match parts.[1].ToLower() with
                            | "reset" -> Some MacroReset
                            | "list" -> Some MacroList
                            | _ -> Some (Unknown "Unknown macro command. Use %macro reset or %macro list")
                        else
                            Some (Unknown "%macro requires a subcommand: reset or list")
                    | "preserve" | "保留" ->
                        // %preserve species1 species2 ... (支持中文%保留)
                        if parts.Length > 1 then
                            let speciesList = parts.[1..] |> Array.toList
                            Some (SetPreservedSpecies speciesList)
                        else
                            Some (Unknown "%preserve requires at least one species name")
                    | _ -> Some (Unknown (sprintf "Unknown directive: %s (use %%crn, %%dsd, %%reset, %%reset state, %%help, %%csv, %%title, %%export expanded, %%debug, %%macro, or %%preserve)" cmd))
        )

    let extractDirectives (code: string) : KernelDirective list * string =
        let lines = code.Split('\n')
        let directives = ResizeArray<KernelDirective>()
        let codeLines = ResizeArray<string>()

        // 跟踪是否在宏定义块内
        let mutable inMacroDefinition = false

        for line in lines do
            let trimmed = line.Trim()

            // 检查是否是宏定义开始
            if trimmed.StartsWith("%define") then
                inMacroDefinition <- true
                codeLines.Add(line)  // 保留宏定义行
            elif trimmed.StartsWith("%end") && trimmed.Contains("define") then
                inMacroDefinition <- false
                codeLines.Add(line)  // 保留宏定义结束行
            elif inMacroDefinition then
                // 在宏定义块内，保留所有行
                codeLines.Add(line)
            elif trimmed.StartsWith("%invoke") then
                // 宏调用，保留为代码让 MacroProcessor 处理
                codeLines.Add(line)
            elif trimmed.StartsWith("%") then
                // Jupyter 风格的指令
                match parse trimmed with
                | [dir] -> directives.Add(dir)
                | dirs -> dirs |> List.iter directives.Add
            elif trimmed.StartsWith("directive ") then
                // CRNEngine 原生 directive 语法，保留为代码的一部分
                codeLines.Add(line)
            else
                // 普通代码行
                codeLines.Add(line)

        let directivesList = directives |> Seq.toList
        // 保留原始换行符格式，确保最后一行有换行符
        let pureCode = String.Join("\n", codeLines.ToArray()) + "\n"

        directivesList, pureCode
