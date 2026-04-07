module CRNKernel.Program

open System
open System.IO

[<EntryPoint>]
let main args =
    // 设置文化环境
    Threading.Thread.CurrentThread.CurrentCulture <- Globalization.CultureInfo.InvariantCulture

    printfn "CRNKernel - Jupyter Kernel for Chemical Reaction Networks"
    printfn "=========================================================="
    printfn "Arguments: %A" args
    printfn ""

    // 查找连接文件
    let connectionFile : string option =
        if args.Length >= 2 && args.[0] = "--connection-file" then
            Some args.[1]
        elif args.Length = 1 then
            Some args.[0]
        else
            // 尝试从环境变量获取
            let envFile = Environment.GetEnvironmentVariable("JUPYTER_CONNECTION_FILE")
            if not (String.IsNullOrEmpty(envFile)) then
                Some envFile
            else
                None

    match connectionFile with
    | None ->
        printfn "Error: No connection file specified"
        printfn ""
        printfn "Usage: CRNKernel [--connection-file <file>]"
        printfn ""
        printfn "Options:"
        printfn "  --connection-file <file>  Path to Jupyter connection file"
        printfn ""
        printfn "If no connection file is specified, the kernel will look for"
        printfn "the JUPYTER_CONNECTION_FILE environment variable."
        1
    | Some file ->
        // 验证连接文件
        if not (System.IO.File.Exists(file)) then
            printfn "Error: Connection file not found: %s" file
            1
        else
            printfn "Using connection file: %s" file
            printfn "Connection file exists: %b" (System.IO.File.Exists(file))
            printfn ""

            try
                // 运行内核
                printfn "Starting kernel..."
                CRNKernel.Kernel.run file
                printfn "Kernel exited normally"
                0
            with ex ->
                printfn "Fatal error: %s" ex.Message
                printfn "Stack trace: %s" ex.StackTrace
                1
