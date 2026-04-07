namespace CRNKernel

open System
open System.Collections.Generic
open System.Text
open System.Text.RegularExpressions
open Microsoft.Research.CRNEngine

/// 宏定义
type MacroDefinition = {
    Name: string
    InputParams: string list      // 输入参数列表
    OutputParams: string list     // 输出参数列表
    RateParams: Map<string, float>  // 速率参数名->默认值
    Body: string                  // 宏内部代码块（原始文本）
    ValidatedCrn: Crn option      // 验证后的 CRN（如果有效）
    ValidationError: string option // 验证错误信息
}

/// 宏调用信息
type MacroInvocation = {
    MacroName: string
    ActualInputs: string list     // 实际输入参数
    ActualOutputs: string list    // 实际输出参数
    Alias: string option          // 实例别名（可选）
    RateOverrides: Map<string, float>  // 速率参数覆盖（可选）
    InstanceId: int option        // 实例 ID（展开时设置）
}

/// 宏实例信息
type MacroInstance = {
    MacroName: string
    InstanceId: int
    Alias: string option          // 实例别名（如果调用时提供）
    ParentInstanceId: int option  // 父实例 ID（如果是嵌套宏）
    InputMapping: Map<string, string>   // 形参 -> 实参
    OutputMapping: Map<string, string>  // 形参 -> 实参
    RateParams: Map<string, float>      // 合并后的速率参数（默认值 + 覆盖值）
    ExpandedCode: string                // 展开后的代码
    LocalSpeciesMapping: Map<string, string>  // 局部物质映射：原始名->实际重命名名
    ChildInstancesMapping: Map<string, MacroInstance>  // 子实例映射：子别名->子实例信息
}

/// 宏展开结果
type MacroExpansionResult = {
    ExpandedCode: string
    InstanceInfo: MacroInstance option
    Errors: string list
}

/// 宏注册表（全局状态）
type MacroRegistry() =
    let definitions = Dictionary<string, MacroDefinition>()
    let instances = ResizeArray<MacroInstance>()
    let instanceCounter = Dictionary<string, int>()
    let aliases = Dictionary<string, MacroInstance>()  // 别名->实例映射

    /// 注册宏定义（支持覆盖：相同名称的宏会被新版本替换）
    member this.Register(def: MacroDefinition) =
        if definitions.ContainsKey(def.Name) then
            // 宏已存在，覆盖旧版本
            definitions.[def.Name] <- def
            // 重置实例计数器，新版本从 0 开始
            instanceCounter.[def.Name] <- 0
            // 清除旧版本的实例记录
            let oldInstances = instances |> Seq.filter (fun inst -> inst.MacroName <> def.Name) |> Seq.toList
            instances.Clear()
            oldInstances |> List.iter instances.Add
        else
            definitions.[def.Name] <- def
            instanceCounter.[def.Name] <- 0

    /// 获取宏定义
    member this.TryGetDefinition(name: string) : MacroDefinition option =
        if definitions.ContainsKey(name) then Some definitions.[name]
        else None

    /// 获取下一个实例 ID
    member this.GetNextInstanceId(macroName: string) : int =
        if not (instanceCounter.ContainsKey(macroName)) then
            instanceCounter.[macroName] <- 0
        let id = instanceCounter.[macroName]
        instanceCounter.[macroName] <- id + 1
        id

    /// 添加实例记录
    member this.AddInstance(instance: MacroInstance) =
        instances.Add(instance)
        // 如果提供了别名，注册到别名字典
        match instance.Alias with
        | Some alias ->
            if aliases.ContainsKey(alias) then
                failwith (sprintf "Duplicate alias '%s'. Each alias must be unique across the session." alias)
            aliases.[alias] <- instance
        | None -> ()

    /// 通过别名查找实例
    member this.TryGetInstanceByAlias(alias: string) : MacroInstance option =
        if aliases.ContainsKey(alias) then Some aliases.[alias]
        else None

    /// 获取所有实例
    member this.Instances = instances |> Seq.toList

    /// 获取所有定义的宏
    member this.Definitions : (string * MacroDefinition) list =
        definitions |> Seq.map (fun (kvp: KeyValuePair<string, MacroDefinition>) -> (kvp.Key, kvp.Value)) |> Seq.toList

    /// 重置注册表
    member this.Reset() =
        definitions.Clear()
        instances.Clear()
        instanceCounter.Clear()
        aliases.Clear()

    /// 检查是否为空
    member this.IsEmpty = definitions.Count = 0

module MacroParser =
    open System
    
    /// 验证宏体 DSL 语法（在嵌套宏展开后调用）
    let validateMacroBody (body: string) : Crn option * string option =
        try
            let crn = Parser.from_string Crn.parse body
            (Some crn, None)
        with ex ->
            (None, Some (sprintf "Macro body syntax error: %s" ex.Message))

    /// 解析宏定义：%define Name (in1, in2) :(out1, out2) [rate params (p1 = v1, ...)] ... %end define
    /// 注意：解析时不立即验证宏体，因为宏体可能包含嵌套宏调用（%invoke）
    /// 验证会在展开嵌套宏之后进行
    let parseMacroDefinition (code: string) : MacroDefinition option =
        let trimmed = code.Trim()

        // 检查是否是宏定义块
        if not (trimmed.StartsWith("%define")) then
            None
        else
            // 提取定义头：%define Name (inputs) :(outputs) [rate params (...)]
            let headerPattern = @"%define\s+(\w+)\s*\(([^)]*)\)\s*:\s*\(([^)]*)\)(?:\s*rate\s+params\s*\(([^)]*)\))?"
            let headerMatch = Regex.Match(trimmed, headerPattern, RegexOptions.IgnoreCase)

            if not headerMatch.Success then
                failwith (sprintf "Invalid macro definition syntax. Expected: %%define Name (inputs) :(outputs) [rate params (...)]")

            let macroName = headerMatch.Groups.[1].Value.Trim()
            let inputStr = headerMatch.Groups.[2].Value.Trim()
            let outputStr = headerMatch.Groups.[3].Value.Trim()
            let rateParamsStr = headerMatch.Groups.[4].Value.Trim()

            // 解析输入参数列表
            let inputParams =
                if String.IsNullOrWhiteSpace(inputStr) then []
                else
                    inputStr.Split([|','|], StringSplitOptions.RemoveEmptyEntries)
                    |> Array.map (fun s -> s.Trim())
                    |> Array.toList

            // 解析输出参数列表
            let outputParams =
                if String.IsNullOrWhiteSpace(outputStr) then []
                else
                    outputStr.Split([|','|], StringSplitOptions.RemoveEmptyEntries)
                    |> Array.map (fun s -> s.Trim())
                    |> Array.toList

            // 解析速率参数列表
            let rateParams =
                if String.IsNullOrWhiteSpace(rateParamsStr) then Map.empty
                else
                    let pairPattern = @"(\w+)\s*=\s*([0-9.eE+-]+)"
                    let matches = Regex.Matches(rateParamsStr, pairPattern)
                    matches
                    |> Seq.cast<Match>
                    |> Seq.map (fun m -> (m.Groups.[1].Value, float m.Groups.[2].Value))
                    |> Map.ofSeq

            // 提取宏体（去掉头部和尾部）
            let bodyStartPattern = @"%define\s+\w+\s*\([^)]*\)\s*:\s*\([^)]*\)(?:\s*rate\s+params\s*\([^)]*\))?"
            let bodyStartMatch = Regex.Match(trimmed, bodyStartPattern, RegexOptions.IgnoreCase)
            let bodyStartIndex = bodyStartMatch.Length

            // 找到 %end define
            let endPattern = @"%end\s+define"
            let endMatch = Regex.Match(trimmed, endPattern, RegexOptions.IgnoreCase)

            if not endMatch.Success then
                failwith "Macro definition missing '%end define'"

            let body = trimmed.Substring(bodyStartIndex, endMatch.Index - bodyStartIndex).Trim()

            // 解析时不验证宏体，因为宏体可能包含嵌套宏调用
            // 验证会在展开嵌套宏之后进行
            Some {
                Name = macroName
                InputParams = inputParams
                OutputParams = outputParams
                RateParams = rateParams
                Body = body
                ValidatedCrn = None
                ValidationError = None
            }
    
    /// 解析宏调用：%invoke Name (actualInputs) :(actualOutputs) [as alias] [with rate (p1 = v1, ...)]
    let parseMacroInvocation (line: string) : MacroInvocation option =
        let trimmed = line.Trim()

        if not (trimmed.StartsWith("%invoke")) then
            None
        else
            // 提取调用信息：%invoke Name (inputs) :(outputs) [as alias] [with rate (...)]
            // 使用更灵活的正则：匹配主体部分，后缀可选
            let invocationPattern = @"%invoke\s+(\w+)\s*\(([^)]*)\)\s*:\s*\(([^)]*)\)(.*)$"
            let matchResult = Regex.Match(trimmed, invocationPattern, RegexOptions.IgnoreCase)

            if not matchResult.Success then
                failwith (sprintf "Invalid macro invocation syntax. Expected: %%invoke Name (inputs) :(outputs) [as alias] [with rate (...)]\nLine: %s" trimmed)

            let macroName = matchResult.Groups.[1].Value.Trim()
            let inputStr = matchResult.Groups.[2].Value.Trim()
            let outputStr = matchResult.Groups.[3].Value.Trim()
            let suffixStr = matchResult.Groups.[4].Value.Trim()

            // 解析实际输入参数
            let actualInputs =
                if String.IsNullOrWhiteSpace(inputStr) then []
                else
                    inputStr.Split([|','|], StringSplitOptions.RemoveEmptyEntries)
                    |> Array.map (fun s -> s.Trim())
                    |> Array.toList

            // 解析实际输出参数
            let actualOutputs =
                if String.IsNullOrWhiteSpace(outputStr) then []
                else
                    outputStr.Split([|','|], StringSplitOptions.RemoveEmptyEntries)
                    |> Array.map (fun s -> s.Trim())
                    |> Array.toList

            // 解析别名：as alias
            let aliasPattern = @"\bas\s+(\w+)"
            let aliasMatch = Regex.Match(suffixStr, aliasPattern, RegexOptions.IgnoreCase)
            let alias =
                if aliasMatch.Success then
                    Some aliasMatch.Groups.[1].Value
                else
                    None

            // 解析速率覆盖：with rate (p1 = v1, ...)
            let ratePattern = @"\bwith\s+rate\s*\(([^)]*)\)"
            let rateMatch = Regex.Match(suffixStr, ratePattern, RegexOptions.IgnoreCase)
            let rateOverrides =
                if rateMatch.Success then
                    let rateStr = rateMatch.Groups.[1].Value.Trim()
                    if String.IsNullOrWhiteSpace(rateStr) then Map.empty
                    else
                        let pairPattern = @"(\w+)\s*=\s*([0-9.eE+-]+)"
                        let matches = Regex.Matches(rateStr, pairPattern)
                        matches
                        |> Seq.cast<Match>
                        |> Seq.map (fun m -> (m.Groups.[1].Value, float m.Groups.[2].Value))
                        |> Map.ofSeq
                else
                    Map.empty

            Some {
                MacroName = macroName
                ActualInputs = actualInputs
                ActualOutputs = actualOutputs
                Alias = alias
                RateOverrides = rateOverrides
                InstanceId = None
            }
    
    /// 从代码中提取所有宏定义
    let extractMacroDefinitions (code: string) : MacroDefinition list * string * bool =
        let lines = code.Split('\n')
        let definitions = ResizeArray<MacroDefinition>()
        let codeLines = ResizeArray<string>()
        let mutable hasDefinitions = false

        let mutable i = 0
        while i < lines.Length do
            let line = lines.[i]
            let trimmed = line.Trim()

            if trimmed.StartsWith("%define") then
                hasDefinitions <- true
                // 收集宏定义块的所有行
                let defLines = ResizeArray<string>()
                defLines.Add(line)
                i <- i + 1
                
                let mutable foundEnd = false
                while i < lines.Length && not foundEnd do
                    let innerLine = lines.[i]
                    defLines.Add(innerLine)
                    if innerLine.Trim().StartsWith("%end") && innerLine.Trim().Contains("define") then
                        foundEnd <- true
                    i <- i + 1
                
                if not foundEnd then
                    failwith "Macro definition missing '%end define'"
                
                // 解析宏定义
                let defCode = String.Join("\n", defLines.ToArray())
                match parseMacroDefinition defCode with
                | Some def -> definitions.Add(def)
                | None -> ()
            else
                codeLines.Add(line)
                i <- i + 1

        let definitionsList = definitions |> Seq.toList
        let remainingCode = String.Join("\n", codeLines.ToArray())

        definitionsList, remainingCode, hasDefinitions

    /// 从代码中提取所有宏调用
    let extractMacroInvocations (code: string) : MacroInvocation list * string =
        let lines = code.Split('\n')
        let invocations = ResizeArray<MacroInvocation>()
        let codeLines = ResizeArray<string>()

        for line in lines do
            let trimmed = line.Trim()
            if trimmed.StartsWith("%invoke") then
                match parseMacroInvocation trimmed with
                | Some inv -> invocations.Add(inv)
                | None -> ()
            else
                codeLines.Add(line)

        let invocationsList = invocations |> Seq.toList
        let remainingCode = String.Join("\n", codeLines.ToArray())

        invocationsList, remainingCode

module MacroExpander =
    open System.Text.RegularExpressions

    /// 解析内部引用路径：$alias.species 或 $parent.child.species
    /// 返回 (路径组件列表，剩余物质名)
    /// 例如："$add1.Y" -> (["add1"], "Y")
    ///       "$parent.child.species" -> (["parent", "child"], "species")
    let parseInternalReference (refStr: string) : (string list * string) option =
        if not (refStr.StartsWith("$")) then None
        else
            let refContent = refStr.Substring(1)
            let parts = refContent.Split('.') |> Array.toList
            match parts with
            | [] -> None
            | [single] -> Some ([], single)  // 只有物质名，没有别名
            | xs ->
                // 最后一个是物质名，前面的是路径
                let path = List.take (List.length xs - 1) xs
                let species = List.last xs
                Some (path, species)

    /// 从注册表中解析内部引用
    /// 支持：$alias.species（顶层实例）和 $parent.child.species（嵌套实例）
    let resolveInternalReference (registry: MacroRegistry) (path: string list) (speciesName: string) : Result<string, string> =
        // 从根实例开始查找
        let rec findInstance (currentInstance: MacroInstance option) (remainingPath: string list) : Result<MacroInstance, string> =
            match remainingPath with
            | [] ->
                match currentInstance with
                | Some inst -> Ok inst
                | None -> Error "Internal reference path is empty"
            | alias :: rest ->
                // 从当前实例的子实例映射中查找
                match currentInstance with
                | Some inst when inst.ChildInstancesMapping.ContainsKey(alias) ->
                    let childInst = inst.ChildInstancesMapping.[alias]
                    findInstance (Some childInst) rest
                | Some inst ->
                    Error (sprintf "Child instance '%s' not found under instance '%s_%d'" alias inst.MacroName inst.InstanceId)
                | None ->
                    // 从根级别别名字典查找（只适用于路径的第一个组件）
                    if registry.TryGetInstanceByAlias(alias) |> Option.isSome then
                        let inst = registry.TryGetInstanceByAlias(alias).Value
                        findInstance (Some inst) rest
                    else
                        Error (sprintf "Alias '%s' not found in registry" alias)

        // 查找目标实例
        match findInstance None path with
        | Ok instance ->
            // 在实例的局部物质映射中查找
            if instance.LocalSpeciesMapping.ContainsKey(speciesName) then
                Ok instance.LocalSpeciesMapping.[speciesName]
            else
                Error (sprintf "Species '%s' not found in instance '%s_%d'" speciesName instance.MacroName instance.InstanceId)
        | Error err -> Error err

    /// 替换代码中的 $alias.species 引用
    let replaceInternalReferences (registry: MacroRegistry) (code: string) : Result<string, string list> =
        // 匹配 $identifier 或 $identifier.identifier...
        let refPattern = @"\$([A-Za-z_][A-Za-z0-9_]*(?:\.[A-Za-z_][A-Za-z0-9_]*)*)"
        
        let errors = ResizeArray<string>()
        
        let replacedCode =
            Regex.Replace(code, refPattern, (fun (m: Match) ->
                let refStr = "$" + m.Groups.[1].Value
                match parseInternalReference refStr with
                | Some (path, species) ->
                    match resolveInternalReference registry path species with
                    | Ok actualName -> actualName
                    | Error err ->
                        errors.Add(err)
                        refStr  // 保留原引用，等待后续错误处理
                | None ->
                    errors.Add(sprintf "Invalid internal reference syntax: %s" refStr)
                    refStr
            ))
        
        if errors.Count > 0 then
            Error (errors |> Seq.toList)
        else
            Ok replacedCode

    /// 提取并移除所有 directive parameters 块（支持单行与多行）
    /// 返回：(参数名->值映射，移除参数块后的代码)
    let extractAndStripParameterDirectives (body: string) : Map<string, float> * string =
        let parametersBlockPattern = @"directive\s+parameters\s*\[(.*?)\]"
        let parameterPairPattern = @"([a-zA-Z_][a-zA-Z0-9_]*)\s*=\s*([0-9.eE+-]+)"

        let blockMatches = Regex.Matches(body, parametersBlockPattern, RegexOptions.IgnoreCase ||| RegexOptions.Singleline)

        let paramValues =
            blockMatches
            |> Seq.cast<Match>
            |> Seq.collect (fun m ->
                let blockContent = m.Groups.[1].Value
                Regex.Matches(blockContent, parameterPairPattern)
                |> Seq.cast<Match>
                |> Seq.map (fun pm -> (pm.Groups.[1].Value, float pm.Groups.[2].Value)))
            |> Seq.toList
            |> Map.ofList

        let bodyWithoutParameterDirectives =
            Regex.Replace(body, parametersBlockPattern, "", RegexOptions.IgnoreCase ||| RegexOptions.Singleline)

        paramValues, bodyWithoutParameterDirectives

    /// 生成重命名后的物质名称
    let renameSpecies (macroName: string) (instanceId: int) (speciesType: string) (originalName: string) =
        // 格式：MacroName_InstanceID_type_OriginalName
        // 例如：Add_1_input_X_1, Multiply_2_local_Intermediate
        sprintf "%s_%d_%s_%s" macroName instanceId speciesType originalName

    /// 从代码中提取所有物质名称（使用 CRNEngine 解析器）
    let extractAllSpecies (code: string) : string list =
        try
            // 尝试使用 CRNEngine 的原生解析器
            let crn = Parser.from_string Crn.parse code
            
            // 从 initials 中提取物质名称
            let initialSpecies = 
                crn.initials 
                |> List.map (fun i -> i.species.name)
            
            // 从 reactions 中提取物质名称
            let reactionSpecies =
                crn.reactions
                |> List.collect (fun r ->
                    // 反应物 - Mset 类型需要使用 iter 或 to_mlist
                    let reactants = 
                        r.reactants 
                        |> Mset.to_mlist 
                        |> List.map (fun entry -> entry.element.name)
                    // 生成物
                    let products = 
                        r.products 
                        |> Mset.to_mlist 
                        |> List.map (fun entry -> entry.element.name)
                    reactants @ products
                )
            
            // 合并并去重
            (initialSpecies @ reactionSpecies) |> List.distinct
        with _ ->
            // 如果解析失败，返回空列表（可能是只有宏定义的代码）
            []

    /// 获取局部物质（排除输入输出参数）
    let getLocalSpecies (code: string) (inputParams: string list) (outputParams: string list) =
        let allSpecies = extractAllSpecies code
        let paramNames = Set.ofList (inputParams @ outputParams)
        allSpecies |> List.filter (fun s -> not (paramNames.Contains(s)))

    /// 构建物质名称替换正则：支持可选数值前缀（整数/小数/科学计数法）以及可选空白
    let buildSpeciesReplacePattern (name: string) =
        sprintf @"(?<![A-Za-z0-9_])((?:\d+(?:\.\d+)?(?:[eE][+-]?\d+)?\s*)?)%s(?![A-Za-z0-9_])" (Regex.Escape(name))

    /// 替换代码中的物质名称
    let replaceSpeciesName (code: string) (oldName: string) (newName: string) =
        let pattern = buildSpeciesReplacePattern oldName
        // 保留可选计量系数（例如 2Y_2 -> 2EXP_0_local_Y_2）
        Regex.Replace(code, pattern, (fun (m: Match) -> m.Groups.[1].Value + newName))

    /// 原子替换多个物质名称（避免 A->B, B->X 这种链式串改）
    let replaceSpeciesNamesAtomically (code: string) (replacements: (string * string) list) =
        if List.isEmpty replacements then
            code
        else
            let batchId = Guid.NewGuid().ToString("N")
            let placeholderMappings =
                replacements
                |> List.mapi (fun i (oldName, newName) ->
                    (oldName, newName, sprintf "__MACRO_TMP_%s_%d__" batchId i))

            // 第一阶段：oldName -> placeholder
            let withPlaceholders =
                placeholderMappings
                |> List.fold (fun currentCode (oldName, _, placeholder) ->
                    let pattern = buildSpeciesReplacePattern oldName
                    Regex.Replace(currentCode, pattern, (fun (m: Match) -> m.Groups.[1].Value + placeholder))
                ) code

            // 第二阶段：placeholder -> newName
            placeholderMappings
            |> List.fold (fun currentCode (_, newName, placeholder) ->
                currentCode.Replace(placeholder, newName)
            ) withPlaceholders

    /// 获取指定父实例的所有子实例
    /// 参数：registry - 注册表，parentInstanceId - 父实例 ID
    let getChildInstances (registry: MacroRegistry) (parentInstanceId: int) : Map<string, MacroInstance> =
        // 获取所有实例
        let allInstances = registry.Instances
        // 过滤出父实例 ID 匹配的实例
        let childInstances =
            allInstances
            |> List.filter (fun inst ->
                inst.ParentInstanceId = Some parentInstanceId
            )
        // 通过别名构建映射（只有有別名的子实例才能被引用）
        childInstances
        |> List.choose (fun inst ->
            match inst.Alias with
            | Some alias -> Some (alias, inst)
            | None -> None
        )
        |> Map.ofList

    /// 展开单个宏调用（支持嵌套宏、速率参数、内部引用）
    /// parentInstanceId: 父实例 ID（如果是嵌套宏调用）
    let rec expandInvocation (registry: MacroRegistry) (invocation: MacroInvocation) (parentInstanceId: int option) : MacroExpansionResult =
        try
            // 1. 查找宏定义
            match registry.TryGetDefinition(invocation.MacroName) with
            | None ->
                {
                    ExpandedCode = ""
                    InstanceInfo = None
                    Errors = [sprintf "Macro '%s' is not defined" invocation.MacroName]
                }

            | Some def ->
                // 2. 验证参数数量
                if def.InputParams.Length <> invocation.ActualInputs.Length then
                    {
                        ExpandedCode = ""
                        InstanceInfo = None
                        Errors = [sprintf "Macro '%s' expects %d input parameters, got %d"
                                    invocation.MacroName def.InputParams.Length invocation.ActualInputs.Length]
                    }
                elif def.OutputParams.Length <> invocation.ActualOutputs.Length then
                    {
                        ExpandedCode = ""
                        InstanceInfo = None
                        Errors = [sprintf "Macro '%s' expects %d output parameters, got %d"
                                    invocation.MacroName def.OutputParams.Length invocation.ActualOutputs.Length]
                    }
                else
                    // 3. 生成实例 ID
                    let instanceId = registry.GetNextInstanceId(invocation.MacroName)

                    // 4. 构建参数映射
                    let inputMapping = Map.ofList (List.zip def.InputParams invocation.ActualInputs)
                    let outputMapping = Map.ofList (List.zip def.OutputParams invocation.ActualOutputs)

                    // 5. 合并速率参数：默认值 + 覆盖值 + directive parameters 中的参数
                    // 首先从 rate params 默认值开始
                    let rateParamsFromDecl : Map<string, float> =
                        Map.fold (fun acc (key: string) (value: float) ->
                            // 覆盖值总是优先
                            acc.Add(key, value)
                        ) def.RateParams invocation.RateOverrides

                    // 6. 预处理宏体：提取并移除 directive parameters 块（支持多行）
                    // 同时获取 directive parameters 中定义的参数
                    let paramValuesFromDirective, bodyWithoutParameterDirectives = extractAndStripParameterDirectives def.Body

                    // 合并 directive parameters 中的参数（如果 rate params 中没有定义）
                    // directive parameters 的优先级低于 with rate 覆盖值
                    let rateParams : Map<string, float> =
                        paramValuesFromDirective
                        |> Map.fold (fun (acc: Map<string, float>) (key: string) (value: float) ->
                            if acc.ContainsKey(key) then
                                // rate params 或 with rate 已经定义了，优先使用
                                acc
                            else
                                // 使用 directive parameters 中的值
                                acc.Add(key, value)
                        ) rateParamsFromDecl

                    // extractAndStripParameterDirectives 已经移除了 directive parameters 块
                    // 这里直接使用处理后的宏体，保留其他 directive（如 directive simulation 等）
                    let bodyWithoutDirectives = bodyWithoutParameterDirectives

                    // 7. 替换速率参数值为实际数值（在物质替换之前）
                    // 只在数值上下文中替换（如 {...} 括号内的速率表达式）
                    let bodyWithRateParamsReplaced =
                        rateParams |> Map.fold (fun code paramName paramValue ->
                            // 匹配数值上下文中的参数名：
                            // 1. {...} 中的参数（反应速率）
                            // 2. [...] 中的参数（directive parameters 已移除，但可能有其他用途）
                            // 3. 数值表达式中的参数（如 k*2, k+1 等）
                            // 使用更精确的模式：参数名前后是数值运算符或括号
                            let pattern = sprintf @"(?<=[\{\[\s\+\-\*/=,])(%s)(?=[\s\+\-\*/\}\],])" (Regex.Escape(paramName))
                            Regex.Replace(code, pattern, (fun (m: Match) -> string paramValue))
                        ) bodyWithoutDirectives

                    // 8. 先递归展开宏体内的嵌套宏调用（在替换输入输出参数之前）
                    // 传递当前实例 ID 作为父实例 ID，以便子实例关联
                    let nestedExpandResult = expandNested registry bodyWithRateParamsReplaced (Some instanceId)
                    if not (List.isEmpty nestedExpandResult.Errors) then
                        {
                            ExpandedCode = ""
                            InstanceInfo = None
                            Errors = nestedExpandResult.Errors
                        }
                    else
                        // 使用展开后的宏体继续处理
                        let expandedBody = nestedExpandResult.ExpandedCode

                        // 9. 验证展开后的宏体 DSL 语法
                        let _, validationError = MacroParser.validateMacroBody expandedBody
                        match validationError with
                        | Some error ->
                            {
                                ExpandedCode = ""
                                InstanceInfo = None
                                Errors = [sprintf "Macro '%s' has syntax errors after expansion: %s" invocation.MacroName error]
                            }
                        | None ->
                            // 10. 获取局部物质（排除输入输出参数）
                            let localSpecies = getLocalSpecies expandedBody def.InputParams def.OutputParams

                            // 11. 构建局部物质映射：原始名->重命名名
                            let localSpeciesMapping =
                                localSpecies
                                |> List.map (fun s -> (s, renameSpecies invocation.MacroName instanceId "local" s))
                                |> Map.ofList

                            // 12. 先重命名局部物质，再替换输入输出参数（避免变量名捕获）
                            let mutable finalExpandedCode = expandedBody

                            // 重命名局部物质
                            for kvp in localSpeciesMapping do
                                finalExpandedCode <- replaceSpeciesName finalExpandedCode kvp.Key kvp.Value

                            // 替换输入参数
                            finalExpandedCode <- replaceSpeciesNamesAtomically finalExpandedCode (inputMapping |> Map.toList)

                            // 替换输出参数
                            finalExpandedCode <- replaceSpeciesNamesAtomically finalExpandedCode (outputMapping |> Map.toList)

                            // 13. 收集子实例信息（从嵌套展开结果中获取）
                            // 子实例是在 expandNested 中创建的，需要从 registry 中获取当前宏的子实例
                            let childInstancesMapping : Map<string, MacroInstance> =
                                getChildInstances registry instanceId

                            // 14. 添加宏展开标记注释
                            let startComment = sprintf "// === BEGIN MACRO EXPANSION: %s_%d (%s) ===" invocation.MacroName instanceId invocation.MacroName
                            let endComment = sprintf "// === END MACRO EXPANSION: %s_%d ===" invocation.MacroName instanceId
                            let markedExpandedCode = sprintf "%s\n%s\n%s" startComment finalExpandedCode endComment

                            // 15. 创建实例记录
                            let instance = {
                                MacroName = invocation.MacroName
                                InstanceId = instanceId
                                Alias = invocation.Alias
                                ParentInstanceId = parentInstanceId
                                InputMapping = inputMapping
                                OutputMapping = outputMapping
                                RateParams = rateParams
                                ExpandedCode = markedExpandedCode
                                LocalSpeciesMapping = localSpeciesMapping
                                ChildInstancesMapping = childInstancesMapping
                            }

                            registry.AddInstance(instance)

                            {
                                ExpandedCode = markedExpandedCode
                                InstanceInfo = Some instance
                                Errors = []
                            }
        with ex ->
            {
                ExpandedCode = ""
                InstanceInfo = None
                Errors = [sprintf "Error expanding macro: %s" ex.Message]
            }

    /// 递归展开嵌套宏调用（按顺序展开所有宏调用，支持多层嵌套）
    /// parentInstanceId: 父实例 ID（用于追踪子实例关系）
    /// 注意：引用替换推迟到宏展开之后进行，以确保子宏已注册
    and expandNested (registry: MacroRegistry) (code: string) (parentInstanceId: int option) : MacroExpansionResult =
        let hasActiveInvocations (text: string) =
            text.Split('\n')
            |> Array.exists (fun line -> line.Trim().StartsWith("%invoke"))

        let maxPasses = 1000

        let rec loop (currentCode: string) (pass: int) : MacroExpansionResult =
            if pass > maxPasses then
                {
                    ExpandedCode = ""
                    InstanceInfo = None
                    Errors = [sprintf "Macro expansion exceeded max passes (%d). Possible recursive macro invocation loop." maxPasses]
                }
            else
                let lines = currentCode.Split('\n')
                let expandedLines = ResizeArray<string>()
                let errors = ResizeArray<string>()

                for line in lines do
                    let trimmed = line.Trim()
                    if trimmed.StartsWith("%invoke") then
                        // 先展开宏调用
                        match MacroParser.parseMacroInvocation trimmed with
                        | Some invocation ->
                            let result = expandInvocation registry invocation parentInstanceId
                            if List.isEmpty result.Errors then
                                // 原地替换：保持宏调用在原代码中的位置顺序
                                expandedLines.Add(result.ExpandedCode)
                            else
                                result.Errors |> List.iter errors.Add
                        | None ->
                            expandedLines.Add(line)
                    else
                        expandedLines.Add(line)

                if not (Seq.isEmpty errors) then
                    {
                        ExpandedCode = ""
                        InstanceInfo = None
                        Errors = errors |> Seq.toList
                    }
                else
                    let expandedOnce = String.Join("\n", expandedLines)
                    if hasActiveInvocations expandedOnce then
                        // 继续递归，处理新产生的宏调用（若有）
                        loop expandedOnce (pass + 1)
                    else
                        // 所有宏调用展开完成后，再替换引用
                        match replaceInternalReferences registry expandedOnce with
                        | Error errs ->
                            {
                                ExpandedCode = ""
                                InstanceInfo = None
                                Errors = errs
                            }
                        | Ok codeWithRefsReplaced ->
                            {
                                ExpandedCode = codeWithRefsReplaced
                                InstanceInfo = None
                                Errors = []
                            }

        loop code 1

    /// 处理完整的代码（包括宏定义和调用）
    let processCode (registry: MacroRegistry) (code: string) (appendDebug: string -> unit) (debugEnabled: bool) : MacroExpansionResult =
        try
            // 1. 先提取并注册所有宏定义（解析时不验证，验证在展开时进行）
            let definitions, codeWithoutDefs, _ = MacroParser.extractMacroDefinitions code

            // 注册新的宏定义
            for def in definitions do
                try
                    registry.Register(def)
                with ex ->
                    // 如果宏已存在，忽略（允许重复定义同一个宏）
                    ()

            // 2. 使用 expandNested 递归展开所有宏调用（包括嵌套宏）
            let fullExpandedCode =
                let invocations, _ = MacroParser.extractMacroInvocations codeWithoutDefs
                let errors = ResizeArray<string>()

                if List.isEmpty invocations then
                    // 没有宏调用，返回原始代码（不含宏定义）
                    codeWithoutDefs
                else
                    // 使用 expandNested 递归展开所有宏调用
                    // 顶层调用没有父实例，传递 None
                    let result = expandNested registry codeWithoutDefs None
                    if not (List.isEmpty result.Errors) then
                        // 收集错误
                        result.Errors |> List.iter errors.Add
                        failwith (String.concat "\n" (errors |> Seq.toList))
                    else
                        result.ExpandedCode

            // 调试：输出代码长度（只在 debugEnabled 时）
            if debugEnabled then begin
                appendDebug (sprintf "[DEBUG processCode] code.Length = %d" code.Length)
                appendDebug (sprintf "[DEBUG processCode] codeWithoutDefs.Length = %d" codeWithoutDefs.Length)
                appendDebug (sprintf "[DEBUG processCode] fullExpandedCode.Length = %d" fullExpandedCode.Length)
                appendDebug (sprintf "[DEBUG processCode] registry.Instances.Count = %d" (registry.Instances |> List.length))
                appendDebug (sprintf "[DEBUG processCode] registry.Definitions.Count = %d" (registry.Definitions |> List.length))
            end

            // 3. 返回结果
            {
                ExpandedCode = fullExpandedCode
                InstanceInfo = None
                Errors = []
            }
        with ex ->
            {
                ExpandedCode = code
                InstanceInfo = None
                Errors = [sprintf "Error processing macros: %s" ex.Message]
            }

    /// 格式化展开后的代码用于显示
    let formatExpandedCode (result: MacroExpansionResult) (registry: MacroRegistry) (expandedCode: string) : string =
        let sb = StringBuilder()

        // 显示已注册的宏定义
        if not (registry.Definitions |> List.isEmpty) then
            sb.AppendLine("// Registered Macros:") |> ignore
            for (name, def) in registry.Definitions do
                sb.AppendLine(sprintf "//   %s(%s) :(%s) [rate params: %s]"
                    name
                    (String.concat ", " def.InputParams)
                    (String.concat ", " def.OutputParams)
                    (def.RateParams |> Map.toList |> List.map (fun (k, v) -> sprintf "%s=%f" k v) |> String.concat ", ")) |> ignore

                // 显示验证状态
                match def.ValidationError with
                | Some error -> sb.AppendLine(sprintf "//     Error: %s" error) |> ignore
                | None -> sb.AppendLine("//     OK") |> ignore

            sb.AppendLine() |> ignore

        // 显示完整的展开后代码
        sb.AppendLine("// Full Expanded Code:") |> ignore
        sb.AppendLine(expandedCode) |> ignore

        // 显示实例信息
        if not (List.isEmpty registry.Instances) then
            sb.AppendLine() |> ignore
            sb.AppendLine("// Macro Instances:") |> ignore
            for inst in registry.Instances do
                sb.AppendLine(sprintf "//   %s_%d%s:"
                    inst.MacroName
                    inst.InstanceId
                    (match inst.Alias with Some a -> sprintf " (alias: %s)" a | None -> "")) |> ignore
                // 显示输入输出映射
                sb.AppendLine(sprintf "//     Inputs: %A" inst.InputMapping) |> ignore
                sb.AppendLine(sprintf "//     Outputs: %A" inst.OutputMapping) |> ignore
                // 显示速率参数
                if not (inst.RateParams |> Map.isEmpty) then
                    sb.AppendLine(sprintf "//     Rate Params: %A" (inst.RateParams |> Map.toList)) |> ignore
                // 显示局部物质映射
                if not (inst.LocalSpeciesMapping |> Map.isEmpty) then
                    sb.AppendLine(sprintf "//     Local Species Mapping: %A" (inst.LocalSpeciesMapping |> Map.toList)) |> ignore

        sb.ToString()

    /// 按形参展开单个宏定义（用于 %export macro <Name>）
    /// 使用形参名作为"虚拟实参"展开宏体，并递归展开内部的 %invoke 调用
    let expandMacroDefinition (registry: MacroRegistry) (macroName: string) : MacroExpansionResult =
        // 1. 查找宏定义
        match registry.TryGetDefinition(macroName) with
        | None ->
            {
                ExpandedCode = ""
                InstanceInfo = None
                Errors = [sprintf "Macro '%s' is not defined" macroName]
            }
        | Some def ->
            // 2. 使用形参名作为虚拟实参
            // 例如：宏定义为 SQUAREC(AP, AN, CB) :(CP, CN, CB)
            // 虚拟调用为：SQUAREC(AP, AN, CB) :(CP, CN, CB)
            let virtualInvocation = {
                MacroName = macroName
                ActualInputs = def.InputParams  // 使用形参名作为实参
                ActualOutputs = def.OutputParams  // 使用形参名作为实参
                Alias = None
                RateOverrides = Map.empty
                InstanceId = None
            }

            // 3. 创建一个临时的子注册表用于展开（避免污染全局注册表）
            // 但我们需要复用已定义的宏（如 MULC），所以引用原注册表
            // 4. 展开宏（包括内部嵌套的 %invoke）
            let result = expandInvocation registry virtualInvocation None
            
            if not (List.isEmpty result.Errors) then
                {
                    ExpandedCode = ""
                    InstanceInfo = None
                    Errors = result.Errors
                }
            else
                result
