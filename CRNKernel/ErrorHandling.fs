namespace CRNKernel

open System

/// CRN 错误类型
type CrnErrorType =
    | UndefinedParameter of string
    | UndefinedSpecies of string
    | InvalidReaction of string
    | SimulationError of string
    | ParseError of string
    | OtherError of string

/// 错误处理模块
module ErrorHandling =

    /// 从异常消息中提取参数名称
    let extractParameterName (message: string) : string =
        if message.Contains("environment does not contain") then
            message.Replace("The environment does not contain: ", "").Trim()
        else
            "unknown"

    /// 从异常消息中提取物种名称
    let extractSpeciesName (message: string) : string =
        if message.Contains("non-existent species") then
            let parts = message.Split(' ')
            parts |> Array.tryFind (fun p -> p.Length > 0 && Char.IsUpper(p.[0])) |> Option.defaultValue "unknown"
        else
            "unknown"

    /// 分析异常并返回友好的错误消息
    let getFriendlyErrorMessage (ex: Exception) : string =
        let message = ex.Message
        
        // 1. 未定义的参数
        if message.Contains("environment does not contain") then
            let paramName = extractParameterName message
            sprintf "❌ Parameter '%s' is not defined.\n   💡 Solution: Define it using: directive parameters [ %s = <value> ]" paramName paramName
        
        // 2. 不存在的物种
        elif message.Contains("non-existent species") then
            let speciesName = extractSpeciesName message
            sprintf "❌ Species '%s' is used but not defined.\n   💡 Solution: Add initial concentration: | <value> %s" speciesName speciesName
        
        // 3. 反应错误
        elif message.Contains("reaction") && message.Contains("rate") then
            sprintf "❌ Error in reaction rate calculation.\n   💡 Solution: Check if all parameters in rate expressions are defined.\n   Details: %s" message
        
        // 4. 解析错误
        elif message.Contains("parse") || message.Contains("Parse") then
            sprintf "❌ Parse Error: %s" message
        
        // 5. 模拟错误
        elif message.Contains("Simulation") || message.Contains("simulation") then
            sprintf "❌ Simulation Error: %s" message
        
        // 6. 其他错误
        else
            sprintf "❌ Error: %s" message

    /// 获取错误的简短摘要（用于日志）
    let getErrorSummary (ex: Exception) : string =
        let message = ex.Message
        
        if message.Contains("environment does not contain") then
            let paramName = extractParameterName message
            sprintf "[Undefined Parameter] %s" paramName
        elif message.Contains("non-existent species") then
            let speciesName = extractSpeciesName message
            sprintf "[Undefined Species] %s" speciesName
        elif message.Contains("reaction") then
            "[Reaction Error]"
        else
            sprintf "[Error] %s" (message.Substring(0, min 50 message.Length))

    /// 是否是可以恢复的错误（用户输入错误）
    let isUserError (ex: Exception) : bool =
        let message = ex.Message
        message.Contains("environment does not contain") ||
        message.Contains("non-existent species") ||
        message.Contains("parse") ||
        message.Contains("Parse")

    /// 获取错误类型
    let getErrorType (ex: Exception) : CrnErrorType =
        let message = ex.Message
        
        if message.Contains("environment does not contain") then
            UndefinedParameter (extractParameterName message)
        elif message.Contains("non-existent species") then
            UndefinedSpecies (extractSpeciesName message)
        elif message.Contains("reaction") then
            InvalidReaction message
        elif message.Contains("parse") || message.Contains("Parse") then
            ParseError message
        elif message.Contains("Simulation") || message.Contains("simulation") then
            SimulationError message
        else
            OtherError message

    /// 根据错误类型提供建议
    let getSuggestion (errorType: CrnErrorType) : string =
        match errorType with
        | UndefinedParameter paramName ->
            sprintf "Define the parameter '%s' in the parameters directive:\n   directive parameters [ %s = 0.01 ]" paramName paramName
        | UndefinedSpecies speciesName ->
            sprintf "Add initial concentration for '%s':\n   | 10 %s" speciesName speciesName
        | InvalidReaction _ ->
            "Check reaction syntax and ensure all species and parameters are defined."
        | ParseError _ ->
            "Check CRN syntax. Ensure reactions use correct format: A + B ->{k} C"
        | SimulationError _ ->
            "Check simulation parameters (final time, points, etc.) and try again."
        | OtherError _ ->
            "Review your CRN code for syntax errors or undefined variables."
