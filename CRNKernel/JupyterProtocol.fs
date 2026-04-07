namespace CRNKernel

open System
open System.IO
open Newtonsoft.Json
open Newtonsoft.Json.Linq

/// Jupyter 消息类型
type MessageType =
    | MTShellMessage
    | MTControlMessage
    | MTStdinMessage
    | MTIOPubMessage
    | MTHeartbeatMessage
    | MTExecuteRequest
    | MTExecuteReply
    | MTExecuteInput
    | MTExecuteResult
    | MTDisplayData
    | MTStream
    | MTError
    | MTStatus
    | MTKernelInfoRequest
    | MTKernelInfoReply
    | MTShutdownRequest
    | MTShutdownReply
    | MTCompleteRequest
    | MTCompleteReply
    | MTInspectRequest
    | MTInspectReply
    | MTHistoryRequest
    | MTHistoryReply
    | MTCommOpen
    | MTCommMsg
    | MTCommClose
    | MTInterruptRequest
    | MTInterruptReply

/// Jupyter 消息头
type Header = {
    msg_id: string
    msg_type: string
    username: string
    session: string
    date: string
    version: string
}

/// Jupyter 消息内容基类
type Content = interface end

/// 通用内容类型（用于未定义的消息类型）
type GenericContent = {
    data: JObject
} with
    interface Content

/// Execute request 内容
type ExecuteRequestContent = {
    code: string
    silent: bool
    store_history: bool
    user_expressions: JObject
    allow_stdin: bool
    stop_on_error: bool
} with
    interface Content

/// Execute reply 内容
type ExecuteReplyContent = {
    status: string
    execution_count: int
    payload: JObject list
    user_expressions: JObject
} with
    interface Content

/// Stream 内容
type StreamContent = {
    name: string
    text: string
} with
    interface Content

/// Execute input 内容
type ExecuteInputContent = {
    code: string
    execution_count: int
} with
    interface Content

/// Display data 内容
type DisplayDataContent = {
    data: JObject
    metadata: JObject
    transient: JObject option
} with
    interface Content

/// Error 内容
type ErrorContent = {
    ename: string
    evalue: string
    traceback: string list
} with
    interface Content

/// Status 内容
type StatusContent = {
    execution_state: string
} with
    interface Content

/// Kernel info reply 内容
type KernelInfoReplyContent = {
    status: string
    protocol_version: string
    implementation: string
    implementation_version: string
    language_info: LanguageInfo
    banner: string
    help_links: HelpLink list
} with
    interface Content

and LanguageInfo = {
    name: string
    version: string
    mimetype: string
    file_extension: string
    pygments_lexer: string option
    codemirror_mode: JObject option
    nbconvert_exporter: string option
}

and HelpLink = {
    text: string
    url: string
}

/// Shutdown request 内容
type ShutdownRequestContent = {
    restart: bool
} with
    interface Content

/// Shutdown reply 内容
type ShutdownReplyContent = {
    restart: bool
} with
    interface Content

/// Jupyter 消息
type JupyterMessage = {
    header: Header
    parent_header: Header option
    metadata: Map<string, JToken>
    content: Content
}

/// 连接文件信息
type ConnectionInfo = {
    control_port: int
    shell_port: int
    stdin_port: int
    iopub_port: int
    hb_port: int
    ip: string
    key: string
    transport: string
    signature_scheme: string
    kernel_name: string
}

/// JSON 转换器模块
module JsonConverters =
    open System.IO

    /// 序列化 MessageType
    let messageTypeToString (mt: MessageType) : string =
        match mt with
        | MTExecuteRequest -> "execute_request"
        | MTExecuteReply -> "execute_reply"
        | MTExecuteInput -> "execute_input"
        | MTExecuteResult -> "execute_result"
        | MTDisplayData -> "display_data"
        | MTStream -> "stream"
        | MTError -> "error"
        | MTStatus -> "status"
        | MTKernelInfoRequest -> "kernel_info_request"
        | MTKernelInfoReply -> "kernel_info_reply"
        | MTShutdownRequest -> "shutdown_request"
        | MTShutdownReply -> "shutdown_reply"
        | MTCompleteRequest -> "complete_request"
        | MTCompleteReply -> "complete_reply"
        | MTInspectRequest -> "inspect_request"
        | MTInspectReply -> "inspect_reply"
        | MTHistoryRequest -> "history_request"
        | MTHistoryReply -> "history_reply"
        | MTCommOpen -> "comm_open"
        | MTCommMsg -> "comm_msg"
        | MTCommClose -> "comm_close"
        | MTInterruptRequest -> "interrupt_request"
        | MTInterruptReply -> "interrupt_reply"
        | MTShellMessage -> "shell_message"
        | MTControlMessage -> "control_message"
        | MTStdinMessage -> "stdin_message"
        | MTIOPubMessage -> "iopub_message"
        | MTHeartbeatMessage -> "heartbeat_message"

    /// 反序列化 MessageType
    let stringToMessageType (s: string) : MessageType =
        match s with
        | "execute_request" -> (MTExecuteRequest : MessageType)
        | "execute_reply" -> (MTExecuteReply : MessageType)
        | "execute_input" -> (MTExecuteInput : MessageType)
        | "execute_result" -> (MTExecuteResult : MessageType)
        | "display_data" -> (MTDisplayData : MessageType)
        | "stream" -> (MTStream : MessageType)
        | "error" -> (MTError : MessageType)
        | "status" -> (MTStatus : MessageType)
        | "kernel_info_request" -> (MTKernelInfoRequest : MessageType)
        | "kernel_info_reply" -> (MTKernelInfoReply : MessageType)
        | "shutdown_request" -> (MTShutdownRequest : MessageType)
        | "shutdown_reply" -> (MTShutdownReply : MessageType)
        | "complete_request" -> (MTCompleteRequest : MessageType)
        | "complete_reply" -> (MTCompleteReply : MessageType)
        | "inspect_request" -> (MTInspectRequest : MessageType)
        | "inspect_reply" -> (MTInspectReply : MessageType)
        | "history_request" -> (MTHistoryRequest : MessageType)
        | "history_reply" -> (MTHistoryReply : MessageType)
        | "comm_open" -> (MTCommOpen : MessageType)
        | "comm_msg" -> (MTCommMsg : MessageType)
        | "comm_close" -> (MTCommClose : MessageType)
        | "interrupt_request" -> (MTInterruptRequest : MessageType)
        | "interrupt_reply" -> (MTInterruptReply : MessageType)
        | "shell_message" -> (MTShellMessage : MessageType)
        | "control_message" -> (MTControlMessage : MessageType)
        | "stdin_message" -> (MTStdinMessage : MessageType)
        | "iopub_message" -> (MTIOPubMessage : MessageType)
        | "heartbeat_message" -> (MTHeartbeatMessage : MessageType)
        | _ -> (MTShellMessage : MessageType)

/// 消息解析模块
module MessageParser =
    /// 解析 Jupyter 消息
    let parseMessage (json: string) : JupyterMessage =
        let obj = JObject.Parse(json)

        let header =
            obj.["header"].ToObject<Header>()

        let parentHeader =
            if obj.["parent_header"] <> null && obj.["parent_header"].HasValues
            then Some (obj.["parent_header"].ToObject<Header>())
            else None

        let metadata =
            if obj.["metadata"] <> null
            then
                let metadataObj = obj.["metadata"].ToObject<Map<string, JObject>>()
                metadataObj |> Map.map (fun _ v -> v :> JToken)
            else Map.empty

        let msgType = obj.["header"].["msg_type"].ToString()
        let contentObj = obj.["content"]

        let content : Content =
            match msgType with
            | "execute_request" -> box (contentObj.ToObject<ExecuteRequestContent>()) :?> Content
            | "execute_input" -> box (contentObj.ToObject<ExecuteInputContent>()) :?> Content
            | "display_data" -> box (contentObj.ToObject<DisplayDataContent>()) :?> Content
            | "stream" -> box (contentObj.ToObject<StreamContent>()) :?> Content
            | "error" -> box (contentObj.ToObject<ErrorContent>()) :?> Content
            | "status" -> box (contentObj.ToObject<StatusContent>()) :?> Content
            | "kernel_info_reply" -> box (contentObj.ToObject<KernelInfoReplyContent>()) :?> Content
            | "shutdown_request" -> box (contentObj.ToObject<ShutdownRequestContent>()) :?> Content
            | _ -> box contentObj :?> Content

        {
            header = header
            parent_header = parentHeader
            metadata = metadata
            content = content
        }

/// 消息创建模块
module MessageBuilder =
    /// 创建消息头
    let createHeader (msgType: string) (sessionId: string) =
        {
            msg_id = Guid.NewGuid().ToString("N")
            msg_type = msgType
            username = "kernel"
            session = sessionId
            date = DateTime.UtcNow.ToString("o")
            version = "5.3"
        }

    /// 创建 execute reply 消息
    let createExecuteReply (parentHeader: Header) (status: string) (executionCount: int) =
        let contentObj : ExecuteReplyContent = {
            status = status
            execution_count = executionCount
            payload = []
            user_expressions = JObject()
        }
        let content = box contentObj :?> Content
        {
            header = createHeader "execute_reply" parentHeader.session
            parent_header = Some parentHeader
            metadata = Map.empty
            content = content
        }

    /// 创建 stream 消息
    let createStreamMessage (parentHeader: Header) (name: string) (text: string) =
        let contentObj : StreamContent = {
            name = name
            text = text
        }
        let content = box contentObj :?> Content
        {
            header = createHeader "stream" parentHeader.session
            parent_header = Some parentHeader
            metadata = Map.empty
            content = content
        }

    /// 创建 execute input 消息
    let createExecuteInputMessage (parentHeader: Header) (executionCount: int) (code: string) =
        let contentObj : ExecuteInputContent = {
            code = code
            execution_count = executionCount
        }
        let content = box contentObj :?> Content
        {
            header = createHeader "execute_input" parentHeader.session
            parent_header = Some parentHeader
            metadata = Map.empty
            content = content
        }

    /// 创建 display data 消息
    let createDisplayDataMessage (parentHeader: Header) (data: Map<string, JToken>) (metadata: Map<string, JObject>) =
        let dataObj = JObject.FromObject(data)
        let metadataObj = JObject.FromObject(metadata)
        let contentObj : DisplayDataContent = {
            data = dataObj
            metadata = metadataObj
            transient = None
        }
        let content = box contentObj :?> Content
        {
            header = createHeader "display_data" parentHeader.session
            parent_header = Some parentHeader
            metadata = Map.empty
            content = content
        }

    /// 创建 execute result 消息
    let createExecuteResultMessage (parentHeader: Header) (executionCount: int) (data: Map<string, JToken>) (metadata: Map<string, JObject>) =
        let dataObj = JObject.FromObject(data)
        let metadataObj = JObject.FromObject(metadata)
        let contentObj : DisplayDataContent = {
            data = dataObj
            metadata = metadataObj
            transient = None
        }
        let content = box contentObj :?> Content
        {
            header = createHeader "execute_result" parentHeader.session
            parent_header = Some parentHeader
            metadata = Map.empty
            content = content
        }

    /// 创建 error 消息
    let createErrorMessage (parentHeader: Header) (ename: string) (evalue: string) (traceback: string list) =
        let contentObj : ErrorContent = {
            ename = ename
            evalue = evalue
            traceback = traceback
        }
        let content = box contentObj :?> Content
        {
            header = createHeader "error" parentHeader.session
            parent_header = Some parentHeader
            metadata = Map.empty
            content = content
        }

    /// 创建 status 消息
    let createStatusMessage (parentHeader: Header) (executionState: string) =
        let contentObj : StatusContent = {
            execution_state = executionState
        }
        let content = box contentObj :?> Content
        {
            header = createHeader "status" parentHeader.session
            parent_header = Some parentHeader
            metadata = Map.empty
            content = content
        }

    /// 创建 kernel info reply 消息
    let createKernelInfoReply (parentHeader: Header) =
        let contentObj : KernelInfoReplyContent = {
            status = "ok"
            protocol_version = "5.3"
            implementation = "CRNKernel"
            implementation_version = "1.0.0"
            language_info = {
                name = "crn"
                version = "1.0.0"
                mimetype = "text/x-crn"
                file_extension = ".crn"
                pygments_lexer = None
                codemirror_mode = Some (JObject.Parse("{\"mode\": \"null\"}"))
                nbconvert_exporter = Some "script"
            }
            banner = "CRNKernel - Chemical Reaction Networks Engine"
            help_links = [
                { text = "CRN Engine"; url = "https://github.com/microsoft/CRN" }
                { text = "Visual DSD"; url = "https://ph1ll1ps.github.io/project/visualdsd/" }
                { text = "Visual GEC"; url = "https://ph1ll1ps.github.io/project/visualgec/" }
            ]
        }
        let content = box contentObj :?> Content
        {
            header = createHeader "kernel_info_reply" parentHeader.session
            parent_header = Some parentHeader
            metadata = Map.empty
            content = content
        }

    /// 序列化 header 为 JSON
    let serializeHeader (header: Header) : string =
        JsonConvert.SerializeObject(header, Formatting.None)

    /// 序列化 content 为 JSON
    let serializeContent (content: Content) : string =
        JsonConvert.SerializeObject(content, Formatting.None)

    /// 序列化消息为 JSON
    let serializeMessage (msg: JupyterMessage) : string =
        let settings = JsonSerializerSettings()
        settings.NullValueHandling <- NullValueHandling.Ignore
        JsonConvert.SerializeObject(msg, settings)
