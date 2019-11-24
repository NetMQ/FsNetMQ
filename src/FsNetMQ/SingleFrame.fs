[<RequireQualifiedAccess>]
module FsNetMQ.SingleFrame

open NetMQ

let private skip (socket:Socket) more = if more then socket.Socket.SkipMultipartMessage ()         
    
let recv socket = 
    let bytes, more = Frame.recv socket
    skip socket more
    bytes
    
let tryRecv socket (timeout:int<milliseconds>) =
    let frame = Frame.tryRecv socket timeout
    
    match frame with
    | Some (bytes, more) -> 
        skip socket more
        Some bytes
    | None -> None
                 
let tryRecvNow socket = tryRecv socket 0<milliseconds>

let recvAsync socket =
    Frame.recvAsync socket ^-> fun (response, more) ->
        skip socket more
        response
    
let tryRecvAsync socket (timeout:int<milliseconds>) =
    Frame.tryRecvAsync socket timeout ^-> function
        | Some (response, more) ->
            skip socket more
            Some response
        | None -> None        
