namespace CRNKernel

open System
open System.Text
open NetMQ
open NetMQ.Sockets
open Newtonsoft.Json
open Newtonsoft.Json.Linq

module Kernel =
    module HMAC =
        open System.Security.Cryptography

        let sign (key: string) (message: string) : string =
            use hmac = new HMACSHA256(Encoding.UTF8.GetBytes(key))
            let data = Encoding.UTF8.GetBytes(message)
            let hash = hmac.ComputeHash(data)
            BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant()

    let serializeZMQMessage (key: string) (sessionId: string) (msg: JupyterMessage) : byte[][] =
        let headerJson = MessageBuilder.serializeHeader msg.header
        let parentHeaderJson = 
            match msg.parent_header with
            | Some ph -> MessageBuilder.serializeHeader ph
            | None -> "{}"
        let metadataJson = 
            if msg.metadata.IsEmpty then "{}"
            else 
                let metadataObj = JObject.FromObject(msg.metadata)
                metadataObj.ToString(Formatting.None)
        let contentJson = MessageBuilder.serializeContent msg.content
        // HMAC 签名 = HMAC(key, header + parent_header + metadata + content)
        let signature = HMAC.sign key (headerJson + parentHeaderJson + metadataJson + contentJson)
        // Jupyter wire protocol 格式：
        // [delimiter, signature, header, parent_header, metadata, content]
        // sendShell 会在前面加上 identity
        [|
            Encoding.UTF8.GetBytes("<IDS|MSG>")     // [0] delimiter
            Encoding.UTF8.GetBytes(signature)       // [1] signature
            Encoding.UTF8.GetBytes(headerJson)      // [2] header
            Encoding.UTF8.GetBytes(parentHeaderJson) // [3] parent_header
            Encoding.UTF8.GetBytes(metadataJson)    // [4] metadata
            Encoding.UTF8.GetBytes(contentJson)     // [5] content
        |]

    let parseZMQMessage (frames: byte[][]) : JupyterMessage =
        printfn "DEBUG: parseZMQMessage received %d frames" frames.Length
        for i = 0 to min 9 (frames.Length - 1) do
            let framePreview = 
                if frames.[i].Length > 100 then
                    Encoding.UTF8.GetString(frames.[i], 0, 100) + "..."
                else
                    Encoding.UTF8.GetString(frames.[i])
            printfn "DEBUG: Frame %d (%d bytes): %s" i frames.[i].Length framePreview
        
        // Jupyter 协议格式（ZeroMQ 消息）：
        // [0] 身份标识 (identity prefix) - 1 个或多个
        // [1] 分隔符 "<IDS|MSG>"
        // [2] HMAC 签名
        // [3] header (序列化 JSON)
        // [4] parent_header (序列化 JSON)
        // [5] metadata (序列化 JSON)
        // [6] content (序列化 JSON)
        // [7+] 可选的缓冲区
        
        if frames.Length < 6 then
            failwith (sprintf "Invalid Jupyter message format: expected at least 6 frames, got %d" frames.Length)
        
        // 找到分隔符的位置
        let delimiterIndex =
            frames |> Array.mapi (fun i frame -> (i, Encoding.UTF8.GetString(frame)))
                   |> Array.tryFind (fun (_, content) -> content = "<IDS|MSG>")
                   |> Option.map fst
        
        match delimiterIndex with
        | None -> failwith "Invalid Jupyter message format: delimiter <IDS|MSG> not found"
        | Some delimIdx ->
            // Jupyter 协议格式（ZeroMQ 消息）：
            // [delimIdx] 分隔符 "<IDS|MSG>"
            // [delimIdx+1] HMAC 签名
            // [delimIdx+2] header (序列化 JSON)
            // [delimIdx+3] parent_header (序列化 JSON)
            // [delimIdx+4] metadata (序列化 JSON)
            // [delimIdx+5] content (序列化 JSON)
            
            let headerIndex = delimIdx + 2
            let parentHeaderIndex = delimIdx + 3
            let metadataIndex = delimIdx + 4
            let contentIndex = delimIdx + 5
            
            if contentIndex >= frames.Length then
                failwith (sprintf "Invalid Jupyter message format: content frame index %d out of range (frames: %d)" contentIndex frames.Length)
            
            let headerJson = Encoding.UTF8.GetString(frames.[headerIndex])
            let parentHeaderJson = Encoding.UTF8.GetString(frames.[parentHeaderIndex])
            let metadataJson = Encoding.UTF8.GetString(frames.[metadataIndex])
            let contentJson = Encoding.UTF8.GetString(frames.[contentIndex])
            
            printfn "DEBUG: Header JSON: %s" headerJson
            printfn "DEBUG: Parent Header JSON: %s" parentHeaderJson
            printfn "DEBUG: Metadata JSON: %s" metadataJson
            printfn "DEBUG: Content JSON: %s" contentJson
            
            // 解析 header 获取 msg_type
            let headerObj = JObject.Parse(headerJson)
            let msgType = headerObj.["msg_type"].ToString()
            printfn "DEBUG: Message type: %s" msgType
            
            let header = headerObj.ToObject<Header>()
            
            let parentHeader =
                if parentHeaderJson <> "{}" && parentHeaderJson <> ""
                then Some (JObject.Parse(parentHeaderJson).ToObject<Header>())
                else None
            
            let metadata =
                if metadataJson <> "{}" && metadataJson <> ""
                then JObject.Parse(metadataJson).ToObject<Map<string, JToken>>()
                else Map.empty
            
            let content : Content =
                match msgType with
                | "execute_request" -> box (JObject.Parse(contentJson).ToObject<ExecuteRequestContent>()) :?> Content
                | "execute_input" -> box (JObject.Parse(contentJson).ToObject<ExecuteInputContent>()) :?> Content
                | "display_data" -> box (JObject.Parse(contentJson).ToObject<DisplayDataContent>()) :?> Content
                | "stream" -> box (JObject.Parse(contentJson).ToObject<StreamContent>()) :?> Content
                | "error" -> box (JObject.Parse(contentJson).ToObject<ErrorContent>()) :?> Content
                | "status" -> box (JObject.Parse(contentJson).ToObject<StatusContent>()) :?> Content
                | "kernel_info_reply" -> box (JObject.Parse(contentJson).ToObject<KernelInfoReplyContent>()) :?> Content
                | "shutdown_request" -> box (JObject.Parse(contentJson).ToObject<ShutdownRequestContent>()) :?> Content
                | "kernel_info_request" -> { data = JObject.Parse(contentJson) } :> Content
                | _ -> { data = JObject.Parse(contentJson) } :> Content
            
            {
                header = header
                parent_header = parentHeader
                metadata = metadata
                content = content
            }

    type KernelImpl(connectionInfo: ConnectionInfo) =
        let mutable running = true
        let kernelState = new KernelState()
        let sessionId = Guid.NewGuid().ToString("N")

        let shellSocket = new RouterSocket()
        let controlSocket = new RouterSocket()
        let stdinSocket = new RouterSocket()
        let heartbeatSocket = new RouterSocket()
        let iopubSocket = new PublisherSocket()

        do
            printfn "Connection Info:"
            printfn "  IP: %s" connectionInfo.ip
            printfn "  Shell Port: %d" connectionInfo.shell_port
            printfn "  Control Port: %d" connectionInfo.control_port
            printfn "  Stdin Port: %d" connectionInfo.stdin_port
            printfn "  IOPub Port: %d" connectionInfo.iopub_port
            printfn "  HB Port: %d" connectionInfo.hb_port
            printfn "  Transport: %s" connectionInfo.transport
            printfn "  Session ID: %s" sessionId

        do
            let shellAddr = sprintf "%s://%s:%d" connectionInfo.transport connectionInfo.ip connectionInfo.shell_port
            let controlAddr = sprintf "%s://%s:%d" connectionInfo.transport connectionInfo.ip connectionInfo.control_port
            let stdinAddr = sprintf "%s://%s:%d" connectionInfo.transport connectionInfo.ip connectionInfo.stdin_port
            let hbAddr = sprintf "%s://%s:%d" connectionInfo.transport connectionInfo.ip connectionInfo.hb_port
            let iopubAddr = sprintf "%s://%s:%d" connectionInfo.transport connectionInfo.ip connectionInfo.iopub_port

            shellSocket.Bind(shellAddr)
            controlSocket.Bind(controlAddr)
            stdinSocket.Bind(stdinAddr)
            heartbeatSocket.Bind(hbAddr)
            iopubSocket.Bind(iopubAddr)

            printfn "Kernel sockets bound:"
            printfn "  Shell: %s" shellAddr
            printfn "  Control: %s" controlAddr
            printfn "  Stdin: %s" stdinAddr
            printfn "  Heartbeat: %s" hbAddr
            printfn "  IOPub: %s" iopubAddr

        member this.sendIOPub (msg: JupyterMessage) =
            // IOPub 消息也需要 wire protocol 格式
            // 但 IOPub 使用 XPUB/SUB，第一个 frame 是 topic（如 "kernel.{uuid}.status"）
            let response = serializeZMQMessage connectionInfo.key sessionId msg
            // IOPub 格式：
            // [topic, delimiter, signature, header, parent_header, metadata, content]
            // 使用 msg_type 作为 topic
            let topic = msg.header.msg_type
            iopubSocket.SendMoreFrame(topic)
                .SendMoreFrame(response.[0])            // delimiter <IDS|MSG>
                .SendMoreFrame(response.[1])            // signature
                .SendMoreFrame(response.[2])            // header
                .SendMoreFrame(response.[3])            // parent_header
                .SendMoreFrame(response.[4])            // metadata
                .SendFrame(response.[5])                // content

        member this.sendShell (parentFrames: byte[][]) (msg: JupyterMessage) =
            let shellId = parentFrames.[0]
            let response = serializeZMQMessage connectionInfo.key sessionId msg
            // Jupyter wire protocol 格式（内核发送回复）：
            // [0] identity (来自请求的 frame 0)
            // [1] 分隔符 <IDS|MSG>
            // [2] HMAC 签名
            // [3] header
            // [4] parent_header
            // [5] metadata
            // [6] content
            // 
            // response 数组：
            // [0] = <IDS|MSG>
            // [1] = signature
            // [2] = header
            // [3] = parent_header
            // [4] = metadata
            // [5] = content
            shellSocket.SendMoreFrame(shellId)          // identity
                .SendMoreFrame(response.[0])            // delimiter <IDS|MSG>
                .SendMoreFrame(response.[1])            // signature
                .SendMoreFrame(response.[2])            // header
                .SendMoreFrame(response.[3])            // parent_header
                .SendMoreFrame(response.[4])            // metadata
                .SendFrame(response.[5])                // content

        member this.sendStatus (parentHeader: Header option) (state: string) =
            match parentHeader with
            | Some header ->
                let msg = MessageBuilder.createStatusMessage header state
                this.sendIOPub msg
            | None -> ()

        member this.sendStream (parentHeader: Header option) (name: string) (text: string) =
            match parentHeader with
            | Some header ->
                let msg = MessageBuilder.createStreamMessage header name text
                this.sendIOPub msg
            | None -> ()

        member this.sendDisplayData (parentHeader: Header) (data: Map<string, JToken>) (metadata: Map<string, JObject>) =
            let msg = MessageBuilder.createDisplayDataMessage parentHeader data metadata
            this.sendIOPub msg

        member this.sendExecuteResult (parentHeader: Header) (executionCount: int) (data: Map<string, JToken>) (metadata: Map<string, JObject>) =
            let msg = MessageBuilder.createExecuteResultMessage parentHeader executionCount data metadata
            this.sendIOPub msg

        member this.handleExecuteRequest (parentFrames: byte[][]) (msg: JupyterMessage) =
            match msg.content with
            | :? ExecuteRequestContent as content ->
                this.sendStatus (Some msg.header) "busy"
                let execCount = kernelState.IncrementExecutionCount()

                try
                    if content.code.Trim() = "%help" then
                        let helpText = ExecutionEngine.getHelpText ()
                        this.sendStream (Some msg.header) "stdout" helpText
                    else
                        let result : CellResult = ExecutionEngine.executeCell kernelState content.code

                        match result with
                        | CellSuccess (speciesData, times, plotData, numPoints, debugOutput, title) ->
                            // 如果没有数据（纯宏定义），只发送文本输出
                            if List.isEmpty speciesData && List.isEmpty plotData then
                                // 纯宏定义或无物质情况，不发送图表
                                let inputMsg = MessageBuilder.createExecuteInputMessage msg.header execCount content.code
                                this.sendIOPub inputMsg
                                // 只发送一次调试输出
                                if not (System.String.IsNullOrEmpty(debugOutput)) then
                                    this.sendStream (Some msg.header) "stdout" debugOutput
                            else
                                // 有数据，发送图表和文本
                                // 先发送调试输出
                                if not (System.String.IsNullOrEmpty(debugOutput)) then
                                    this.sendStream (Some msg.header) "stdout" debugOutput

                                let displayData = Visualization.createDisplayData speciesData times plotData numPoints title
                                let metadata = Visualization.createMetadata ()

                                let inputMsg = MessageBuilder.createExecuteInputMessage msg.header execCount content.code
                                this.sendIOPub inputMsg

                                // 发送 execute_result
                                this.sendExecuteResult msg.header execCount displayData metadata
                                this.sendStream (Some msg.header) "stdout" (Visualization.formatResult speciesData times plotData numPoints)

                        | CellError (ename, evalue, traceback) ->
                            // 格式化错误消息
                            let errorData = Visualization.formatError ename evalue traceback
                            
                            // 发送到 stderr 流
                            this.sendStream (Some msg.header) "stderr" errorData

                            // 发送 Jupyter 错误消息（用于在笔记本中显示错误框）
                            let errorContentObj = {
                                ename = ename
                                evalue = evalue
                                traceback = traceback
                            }
                            let errorContent = box errorContentObj :?> Content
                            let errorMsg = {
                                header = MessageBuilder.createHeader "error" sessionId
                                parent_header = Some msg.header
                                metadata = Map.empty
                                content = errorContent
                            }
                            this.sendIOPub errorMsg

                with ex ->
                    let errorMsg = sprintf "Internal error - %s\nStackTrace: %s\n" ex.Message ex.StackTrace
                    this.sendStream (Some msg.header) "stderr" errorMsg

                this.sendStatus (Some msg.header) "idle"
                let reply = MessageBuilder.createExecuteReply msg.header "ok" execCount
                this.sendShell parentFrames reply

            | _ ->
                printfn "Invalid execute_request content"

        member this.handleKernelInfoRequest (parentFrames: byte[][]) (msg: JupyterMessage) =
            printfn "Creating kernel info reply..."
            this.sendStatus (Some msg.header) "busy"
            let reply = MessageBuilder.createKernelInfoReply msg.header
            printfn "Sending kernel info reply to shell..."
            this.sendShell parentFrames reply
            this.sendStatus (Some msg.header) "idle"
            printfn "Kernel info reply sent"

        member this.handleShutdownRequest (parentFrames: byte[][]) (msg: JupyterMessage) =
            match msg.content with
            | :? ShutdownRequestContent as content ->
                running <- false
                kernelState.Status <- ShuttingDown

                let replyContentObj = {
                    restart = content.restart
                }
                let replyContent = box replyContentObj :?> Content

                let reply = {
                    header = MessageBuilder.createHeader "shutdown_reply" sessionId
                    parent_header = Some msg.header
                    metadata = Map.empty
                    content = replyContent
                }
                this.sendShell parentFrames reply

            | _ ->
                printfn "Invalid shutdown_request content"

        member this.handleMessage (frames: byte[][]) =
            try
                printfn "Processing message with %d frames" frames.Length
                let msg = parseZMQMessage frames
                printfn "Message type: %s" msg.header.msg_type
                printfn "Full message header: %A" msg.header

                match msg.header.msg_type with
                | "execute_request" -> 
                    printfn "Handling execute request"
                    this.handleExecuteRequest frames msg
                | "kernel_info_request" -> 
                    printfn "Handling kernel info request"
                    this.handleKernelInfoRequest frames msg
                | "shutdown_request" -> 
                    printfn "Handling shutdown request"
                    this.handleShutdownRequest frames msg
                | "connect_request" ->
                    printfn "Handling connect request"
                | "inspect_request" ->
                    printfn "Handling inspect request"
                | "complete_request" ->
                    printfn "Handling complete request"
                | msgType ->
                    printfn "Unhandled message type: %s" msgType
                    printfn "Message content: %A" msg.content
            with ex ->
                printfn "Error handling message: %s" ex.Message
                printfn "Stack trace: %s" ex.StackTrace

        member this.runHeartbeat () =
            async {
                while running do
                    do! Async.Sleep 1000
                    let mutable frameStr = ""
                    let mutable more = false
                    let hasMessage = heartbeatSocket.TryReceiveFrameString(TimeSpan.FromMilliseconds(100.0), &frameStr, &more)
                    if hasMessage then
                        heartbeatSocket.SendFrame(frameStr)
            }

        member this.run () =
            kernelState.Status <- Idle
            printfn "CRN Kernel started and ready"
            printfn "Waiting for messages..."
            printfn "Poll timeout: 1000ms"
            
            // 发送初始状态消息到 IOPub
            let initialHeader = MessageBuilder.createHeader "status" sessionId
            this.sendStatus (Some initialHeader) "starting"
            this.sendStatus (Some initialHeader) "idle"

            let heartbeatTask = this.runHeartbeat () |> Async.StartAsTask

            // 使用 NetMQPoller 同时监听多个 socket
            use poller = new NetMQ.NetMQPoller()

            // 添加 shell socket 到轮询器
            poller.Add(shellSocket)
            shellSocket.ReceiveReady.Add(fun args ->
                printfn "=========================================="
                printfn "Received message on shell socket"
                let frames = shellSocket.ReceiveMultipartBytes() |> Seq.toArray
                printfn "Message frames count: %d" frames.Length
                for i = 0 to min 9 (frames.Length - 1) do
                    printfn "  Frame %d: %d bytes" i frames.[i].Length
                this.handleMessage frames
            )

            // 添加 control socket 到轮询器
            poller.Add(controlSocket)
            controlSocket.ReceiveReady.Add(fun args ->
                printfn "=========================================="
                printfn "Received message on control socket"
                let frames = controlSocket.ReceiveMultipartBytes() |> Seq.toArray
                printfn "Message frames count: %d" frames.Length
                for i = 0 to min 9 (frames.Length - 1) do
                    printfn "  Frame %d: %d bytes" i frames.[i].Length
                this.handleMessage frames
            )

            printfn "Starting poller..."
            poller.Run()

            shellSocket.Dispose()
            controlSocket.Dispose()
            stdinSocket.Dispose()
            heartbeatSocket.Dispose()
            iopubSocket.Dispose()
            NetMQ.NetMQConfig.Cleanup()
            printfn "Kernel shutdown complete"

    let createFromConnectionFile (connectionFile: string) : KernelImpl =
        let json = System.IO.File.ReadAllText(connectionFile)
        let connObj = JObject.Parse(json)

        let connectionInfo = {
            control_port = connObj.["control_port"].Value<int>()
            shell_port = connObj.["shell_port"].Value<int>()
            stdin_port = connObj.["stdin_port"].Value<int>()
            iopub_port = connObj.["iopub_port"].Value<int>()
            hb_port = connObj.["hb_port"].Value<int>()
            ip = connObj.["ip"].Value<string>()
            key = connObj.["key"].Value<string>()
            transport = connObj.["transport"].Value<string>()
            signature_scheme = connObj.["signature_scheme"].Value<string>()
            kernel_name =
                if connObj.["kernel_name"] <> null then
                    connObj.["kernel_name"].Value<string>()
                else
                    "crn"
        }

        KernelImpl(connectionInfo)

    let run (connectionFile: string) =
        let kernel = createFromConnectionFile connectionFile
        kernel.run()
