namespace CRNKernel

open System

/// 重置模式
type ResetMode =
    | StateOnly  // 只重置物质状态
    | All        // 重置所有（包括宏定义）

/// 预处理结果
type PreprocessorResult = {
    ExportExpanded: bool
    ExportMacro: string option  // 导出指定宏的展开代码（按形参展开）
    DebugEnabled: bool
    ShowHelp: bool
    ResetMode: ResetMode option
    Warnings: string list
    CodeWithoutPreprocessor: string
}

/// 预处理指令解析器模块
module Preprocessor =
    open System

    /// 提取并解析所有预处理指令
    let extractDirectives (code: string) : PreprocessorResult =
        let lines = code.Split('\n')
        let mutable exportExpanded = false
        let mutable exportMacro: string option = None
        let mutable debugEnabled = false
        let mutable showHelp = false
        let mutable resetMode: ResetMode option = None
        let warnings = ResizeArray<string>()
        let codeLines = ResizeArray<string>()

        for line in lines do
            let trimmed = line.Trim()
            if trimmed.StartsWith("%") then
                let parts = trimmed.[1..].Split([|' '|], StringSplitOptions.RemoveEmptyEntries)
                if parts.Length > 0 then
                    let cmd = parts.[0].ToLower()

                    match cmd with
                    | "export" ->
                        if parts.Length >= 2 then
                            let secondArg = parts.[1].ToLower()
                            if secondArg = "expanded" then
                                exportExpanded <- true
                            elif secondArg = "macro" && parts.Length >= 3 then
                                // %export macro <MacroName>
                                exportMacro <- Some parts.[2]
                            else
                                warnings.Add(sprintf "Warning: %s" "%export expanded - Export expanded macro code")
                        else
                            warnings.Add(sprintf "Warning: %s" "%export expanded - Export expanded macro code")
                    | "debug" ->
                        if parts.Length >= 2 then
                            let secondArg = parts.[1].ToLower()
                            if secondArg = "on" || secondArg = "true" || secondArg = "1" then
                                debugEnabled <- true
                            elif secondArg = "off" || secondArg = "false" || secondArg = "0" then
                                debugEnabled <- false
                            else
                                warnings.Add(sprintf "Warning: %s" "%debug on|off - Enable or disable debug output")
                        else
                            warnings.Add(sprintf "Warning: %s" "%debug on|off - Enable or disable debug output")
                    | "reset" ->
                        if parts.Length > 1 then
                            match parts.[1].ToLower() with
                            | "all" -> resetMode <- Some All
                            | _ -> resetMode <- Some StateOnly
                        else
                            resetMode <- Some StateOnly  // 默认只重置物质状态
                        // 从代码中移除 reset 指令
                    | "help" ->
                        showHelp <- true
                        // 从代码中移除 help 指令
                    | _ ->
                        // 其他指令保留为代码
                        codeLines.Add(line)
                else
                    codeLines.Add(line)
            else
                codeLines.Add(line)

        let pureCode = String.Join("\n", codeLines.ToArray()) + "\n"

        {
            ExportExpanded = exportExpanded
            ExportMacro = exportMacro
            DebugEnabled = debugEnabled
            ShowHelp = showHelp
            ResetMode = resetMode
            Warnings = warnings |> Seq.toList
            CodeWithoutPreprocessor = pureCode
        }

    /// 获取帮助文本
    let getHelpText () =
        """
CRN Kernel - Available Directives:

%crn                 - Use CRN (Chemical Reaction Networks) syntax
%dsd                 - Use DSD (DNA Strand Displacement) syntax
%reset               - Reset material state only (keeps macro definitions)
%reset state         - Reset material state only (keeps macro definitions)
%reset all           - Reset all state (including macro definitions)
%help                - Show this help message
%csv                 - Enable CSV export for current cell (default: disabled)
%title "name"        - Set custom title for CSV export (used with %csv)
%export expanded     - Export and display the expanded macro code
%export macro <Name> - Export and display a macro's expanded code template (by formal parameters)
%debug on|off        - Enable or disable debug output (default: off)
%macro reset         - Reset macro registry only (clear all defined macros)
%macro list          - List all defined macros

Macro Definition and Usage:
  %define Name (input1, input2) :(output1, output2)
    // CRN code here
  %end define
  
  %invoke Name (actualIn1, actualIn2) :(actualOut1, actualOut2)

Note: Macro definitions (%define blocks) are automatically registered when executed.
      You can define macros in one cell and use them in subsequent cells.
      Use %reset state to clear material concentrations while keeping macros.
      Use %reset or %reset all to clear everything including macros.

CRN Example with Macros:
```
%define Add (X1, X2) :(Y)
  directive parameters [ k = 0.1 ]
  | 0 Y
  X1 + X2 ->{k} Y
%end define

%invoke Add (A, B) :(Sum)
```
"""
