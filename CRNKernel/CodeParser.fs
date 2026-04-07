namespace CRNKernel

open System
open Microsoft.Research.CRNEngine

type CodeLanguage =
    | CRN
    | DSD
    | Auto  // 自动检测
    | Unknown

type ParseResult =
    | CRNModel of Crn
    | DSDModel of Crn
    | Error of string

module CodeParser =
    let detectLanguage (code: string) : CodeLanguage =
        let trimmed = code.Trim()

        // CRN 语言特征（优先级更高，避免把含 <-> 的 CRN 误判为 DSD）
        if trimmed.Contains("directive ") ||
           trimmed.Contains("init ") ||
           trimmed.Contains("->") ||
           trimmed.Contains("<->") ||
           trimmed.StartsWith("|") then
            CRN
        // DSD 语言特征
        elif trimmed.Contains("domain ") ||
             trimmed.Contains("strand ") ||
             trimmed.Contains("gate ") ||
             (trimmed.Contains("let ") && trimmed.Contains("new")) ||
             (trimmed.Contains("{") && trimmed.Contains("*}")) then
            DSD
        else
            Auto

    let parseCRN (code: string) : ParseResult =
        try
            // 使用 CRNEngine 的原生解析器 - 支持完整的 CRN 语法
            // 移除 BOM 和规范化换行符
            let cleanCode = 
                code.Replace("\ufeff", "")  // 移除 UTF-8 BOM
                    .Replace("\r\n", "\n")
                    .Replace("\r", "\n")
            
            // CRNEngine 的解析器期望 | 作为分隔符
            // 示例格式：
            // directive simulation {final=100.0}
            // | 10 A
            // | 10 B
            // | A + B ->{k} C
            //
            // 解析器有 Parser.opt pbar 来处理开头的 |
            // 所以我们需要保留 | 前缀，但确保格式正确
            let processedCode = cleanCode
            
            let crn = Parser.from_string Crn.parse processedCode
            CRNModel crn
        with ex ->
            let codePreview = 
                if code.Length > 200 then 
                    code.Substring(0, 200) + "..." 
                else code
            Error (sprintf "CRN parse error: %s\nCode preview:\n%s" ex.Message codePreview)

    let parseDSD (code: string) : ParseResult =
        try
            let bundle = Microsoft.Research.DNA.Dsd.parse code
            let crn = Microsoft.Research.DNA.Dsd.convert_expand bundle
            DSDModel crn
        with ex ->
            Error (sprintf "DSD parse error: %s" ex.Message)

    let parse (code: string) (lang: CodeLanguage) : ParseResult =
        match lang with
        | CRN -> parseCRN code
        | DSD -> parseDSD code
        | Auto ->
            // 自动检测
            let detected = detectLanguage code
            match detected with
            | CRN -> parseCRN code
            | DSD -> parseDSD code
            | _ -> Error "Unable to detect language. Please use %crn or %dsd directive."
        | Unknown -> Error "Unknown language. Please use %crn or %dsd directive."

    let parseFlexible (code: string) : ParseResult =
        parse code Auto
