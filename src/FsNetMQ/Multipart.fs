[<RequireQualifiedAccess>]
module FsNetMQ.Multipart

open NetMQ

let send socket (parts:byte[] seq) =         
    use e = parts.GetEnumerator()
        
    let rec send' previous = 
      match previous, e.MoveNext () with
      | None, false -> Frame.send socket Array.empty  
      | None, true -> send' <| Some e.Current
      | Some bytes, false -> Frame.send socket bytes
      | Some bytes, true -> 
            Frame.sendMore socket bytes          
            send' <| Some e.Current
    
    send' None
    
let trySend socket (parts:byte[] seq) (timeout:int<milliseconds>) =
              
    if Seq.isEmpty parts then
        Frame.trySend socket Array.empty timeout                                    
    else 
        let head = Seq.head parts
        let tail = Seq.tail parts
        
        if Seq.isEmpty tail then
            Frame.trySend socket head timeout
        else 
            let success = Frame.trySendMore socket head timeout                
            if success then send socket tail                
            success
                                          
let recv socket =
    let rec recv' parts = 
        let part, more = Frame.recv socket
        
        let parts' = seq {yield! parts; yield part }
    
        match more with
        | true -> recv' parts'
        | false -> parts'

    recv' Seq.empty     
    
let tryRecv socket (timeout:int<milliseconds>) =
    match Frame.tryRecv socket timeout with 
    | Some (bytes, false) -> Some (seq {yield bytes})
    | Some (bytes, true) -> Some (seq {yield bytes; yield! recv socket})
    | None -> None
    
let tryRecvNow socket = tryRecv socket 0<milliseconds>        
let trySendNow socket parts = trySend socket parts 0<milliseconds>

let skip (socket:Socket) = socket.Socket.SkipMultipartMessage ()

let recvAsync socket =
    Frame.recvAsync socket ^-> fun (first, more) ->       
        if more then
            let parts = recv socket
            seq {yield first; yield! parts}
        else
            Seq.singleton first               
    
let tryRecvAsync socket (timeout:int<milliseconds>) =
    Frame.tryRecvAsync socket timeout ^-> function    
        | Some (first, true) ->
            let parts = recv socket
            Some <| seq {yield first; yield! parts}
        | Some (first, false) ->
            Some <| Seq.singleton first
        | None -> None                 
