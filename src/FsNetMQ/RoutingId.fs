[<RequireQualifiedAccess;CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module FsNetMQ.RoutingId

open System
open NetMQ

type T = RoutingId
               
module TryResult =         
    type T =
        | Ok
        | HostUnreachable
        | TimedOut
    
module Result =             
    type T = 
        | Ok
        | HostUnreachable        

let get (socket:Socket) =         
    let bytes = socket.Socket.ReceiveFrameBytes ()
    RoutingId bytes   
                     
let tryGet (socket:Socket) (timeout:int<milliseconds>) =        
    let success, bytes = socket.Socket.TryReceiveFrameBytes (TimeSpan.FromMilliseconds (float timeout))
            
    match success with 
    | true -> Some (RoutingId bytes)
    | false -> None    
        
let set (socket:Socket) (RoutingId routingId) =
    try 
        socket.Socket.SendMoreFrame routingId |> ignore
        Result.Ok
    with 
    | :? HostUnreachableException -> Result.HostUnreachable
            
let trySet (socket:Socket) (RoutingId routingId) (timeout:int<milliseconds>) =
    try
        match socket.Socket.TrySendFrame (TimeSpan.FromMilliseconds (float timeout), routingId, true) with 
        | true -> TryResult.Ok
        | false -> TryResult.TimedOut
    with 
        | :? HostUnreachableException -> TryResult.HostUnreachable        
        
let getAsync socket =
    Frame.recvAsync socket ^-> (fst >> RoutingId)
    
let tryGetAsync socket (timeout:int<milliseconds>) =
    Frame.tryRecvAsync socket timeout ^-> function    
        | Some (bytes, _) -> Some <| RoutingId bytes
        | None -> None
              