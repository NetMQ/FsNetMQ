[<RequireQualifiedAccess>]
module FsNetMQ.Frame

open FsNetMQ
open System
open NetMQ

let sendMore (socket:Socket) (bytes:byte[]) = socket.Socket.SendMoreFrame (bytes) |> ignore                
let send (socket:Socket) (bytes:byte[]) = socket.Socket.SendFrame (bytes) |> ignore                                           
let recv (socket:Socket) = 
    let frame, more = socket.Socket.ReceiveFrameBytes ()
    (frame, more)
    
let trySendMore (socket:Socket) (bytes:byte[]) (timeout:int<milliseconds>)  =
    socket.Socket.TrySendFrame (TimeSpan.FromMilliseconds (float timeout), bytes, true)
        
let trySend (socket:Socket) (bytes:byte[]) (timeout:int<milliseconds>) =
    socket.Socket.TrySendFrame (TimeSpan.FromMilliseconds (float timeout), bytes, false)
    
let tryRecv (socket:Socket) (timeout:int<milliseconds>)  =
    let success, bytes, more = socket.Socket.TryReceiveFrameBytes (TimeSpan.FromMilliseconds (float timeout))
    
    match success with 
    | true -> Some (bytes, more)
    | false -> None
     
let trySendMoreNow socket (bytes:byte[]) = trySendMore socket bytes 0<milliseconds>                 
let trySendNow socket (bytes:byte[]) = trySend socket bytes 0<milliseconds>    
let tryRecvNow socket = tryRecv socket 0<milliseconds>

let recvAsync socket : Alt<byte[]*bool> =
    let alt = fun (ctx:AltContext) ->
        async {            
            do! ctx.Acquire()            
            match tryRecvNow socket with
            | Some frame ->                
                do! ctx.TakeRelease()
                return frame
            | None ->               
                do! ctx.Release()
                match Runtime.Current with
                | None ->
                    return 
                        Async.NoRuntimeError "When using FsNetMQ async operation you must use Alt.Run"
                        |> raise
                | Some runtime ->
                    runtime.Add socket                    
                    let! _ = Async.AwaitEvent socket.Socket.ReceiveReady                    
                    do! ctx.Take()
                    let frame = recv socket                    
                    return frame
        }
    
    let comp = async {        
        match tryRecvNow socket with
        | Some frame ->            
            return frame
        | None ->            
            match Runtime.Current with
            | None ->
                return 
                    Async.NoRuntimeError "When using FsNetMQ async operation you must use Alt.Run"
                    |> raise
            | Some runtime ->
                runtime.Add socket
                let! _ = Async.AwaitEvent socket.Socket.ReceiveReady                
                let frame = recv socket                    
                return frame
    }
    
    Alt.makeAlt alt comp

let tryRecvAsync socket (timeout:int<milliseconds>) =            
    Alt.Choose [
        Alt.Sleep (int timeout) ^->. None
        recvAsync socket ^-> Some
    ]    