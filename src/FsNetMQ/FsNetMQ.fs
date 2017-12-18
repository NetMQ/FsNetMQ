namespace FsNetMQ

open System
open NetMQ    

[<Measure>] 
type milliseconds

module Socket =
    type T = 
        | Socket of NetMQSocket
        interface IDisposable with
            member x.Dispose() = 
                match x with
                | Socket s -> s.Dispose()
                     
    let dealer () = Socket (new Sockets.DealerSocket())
    let router () = Socket (new Sockets.RouterSocket())
    let req () = Socket (new Sockets.RequestSocket())
    let rep () = Socket (new Sockets.ResponseSocket())
    let sub () = Socket (new Sockets.SubscriberSocket())
    let pub () = Socket (new Sockets.PublisherSocket())
    let xsub () = Socket (new Sockets.XSubscriberSocket())
    let xpub () = Socket (new Sockets.XPublisherSocket())
    let push () = Socket (new Sockets.PushSocket())
    let pull () = Socket (new Sockets.PullSocket())
    let pair () = Socket (new Sockets.PairSocket())
    let stream () = Socket (new Sockets.StreamSocket())
    let peer () = Socket (new Sockets.PeerSocket())
    
    let connect (Socket socket) address = socket.Connect address
    let bind (Socket socket) address = socket.Bind address
    let disconnect (Socket socket) address = socket.Disconnect address
    let unbind (Socket socket) address = socket.Unbind address
    
    let subscribe (Socket socket) (subscription:string) = 
        match socket with 
        | :? Sockets.SubscriberSocket as sub -> sub.Subscribe subscription
        | :? Sockets.XSubscriberSocket as xsub -> xsub.Subscribe subscription
        | _ -> invalidArg "socket" "Socket is not a sub or xsub socket"
    
    let unsubscribe (Socket socket) (subscription:string) = 
        match socket with 
        | :? Sockets.SubscriberSocket as sub -> sub.Unsubscribe subscription
        | :? Sockets.XSubscriberSocket as xsub -> xsub.Unsubscribe subscription
        | _ -> invalidArg "socket" "Socket is not a sub or xsub socket"
        
module RoutingId =    
    type T = 
        | RoutingId of byte[]           
        
    module TryResult =         
        type T =
            | Ok
            | HostUnreachable
            | TimedOut
        
    module Result =             
        type T = 
            | Ok
            | HostUnreachable        

    let get (Socket.Socket socket) =         
        let bytes = socket.ReceiveFrameBytes ()
        RoutingId bytes
                         
    let tryGet (Socket.Socket socket) (timeout:int<milliseconds>) =
            
        let success, bytes = socket.TryReceiveFrameBytes (TimeSpan.FromMilliseconds (float timeout))
                
        match success with 
        | true -> Some (RoutingId bytes)
        | false -> None      
            
    let set (Socket.Socket socket) (RoutingId routingId) =
        try 
            socket.SendMoreFrame routingId |> ignore
            Result.Ok
        with 
        | :? HostUnreachableException -> Result.HostUnreachable
                
    let trySet (Socket.Socket socket) (RoutingId routingId) (timeout:int<milliseconds>) =
        try
            match socket.TrySendFrame (TimeSpan.FromMilliseconds (float timeout), routingId, true) with 
            | true -> TryResult.Ok
            | false -> TryResult.TimedOut
        with 
            | :? HostUnreachableException -> TryResult.HostUnreachable                
          

module Peer =     
    let connect (Socket.Socket socket) address = 
        socket.Connect address
        RoutingId.RoutingId socket.Options.LastPeerRoutingId
        
module Pair = 
    let createPairs () = 
        let mutable p1 = null
        let mutable p2 = null
        
        Sockets.PairSocket.CreateSocketPair (ref p1, ref p2)        
        Socket.Socket p1, Socket.Socket p2
                                               
module Options =            
    let sendHighWatermark (Socket.Socket socket) = socket.Options.SendHighWatermark        
         
    let setSendHighWatermark (Socket.Socket socket) highWatermark = 
        socket.Options.SendHighWatermark = highWatermark

    let recvHighWatermark (Socket.Socket socket) = socket.Options.ReceiveHighWatermark        
        
    let setRecvHighwatermark (Socket.Socket socket) highWatermark = 
        socket.Options.ReceiveHighWatermark = highWatermark     
        
    let linger (Socket.Socket socket) = 
        (int socket.Options.Linger.TotalMilliseconds) * 1<milliseconds>
        
    let setLinger (Socket.Socket socket) (value:int<milliseconds>) = 
        socket.Options.Linger = TimeSpan.FromMilliseconds (float value)                                                             
        
module Timer = 
    type T = 
        | Timer of NetMQTimer                               

    let create (interval:int<milliseconds>) = Timer (new NetMQTimer (int interval))
    let disable (Timer t) = t.Enable <- false
    let enable (Timer t) = t.Enable <- true
    let isEnabled (Timer t) = t.Enable
    let reset (Timer t) = t.EnableAndReset ()
            
module Actor =    
    type T = 
        | Actor of NetMQActor
        interface IDisposable with 
            member x.Dispose () = 
                match x with 
                | Actor actor -> actor.Dispose ()

    let endMessage = NetMQActor.EndShimMessage             
                 
    let create shim = 
        let handler (pipe:Sockets.PairSocket) = shim (Socket.Socket pipe)
        Actor (NetMQActor.Create (handler))
        
    let signal (Socket.Socket socket) = socket.SignalOK ()
             
    let asSocket (Actor actor) = 
        let socketPollable = actor :> ISocketPollable    
        Socket.Socket socketPollable.Socket                                   

module Frame =     
    let sendMore (Socket.Socket socket) (bytes:byte[]) = socket.SendMoreFrame (bytes) |> ignore                
    let send (Socket.Socket socket) (bytes:byte[]) = socket.SendFrame (bytes) |> ignore                                           
    let recv (Socket.Socket socket) = 
        let frame, more = socket.ReceiveFrameBytes ()
        (frame, more)
        
    let trySendMore (Socket.Socket socket) (bytes:byte[]) (timeout:int<milliseconds>)  =
        socket.TrySendFrame (TimeSpan.FromMilliseconds (float timeout), bytes, true)
            
    let trySend (Socket.Socket socket) (bytes:byte[]) (timeout:int<milliseconds>) =
        socket.TrySendFrame (TimeSpan.FromMilliseconds (float timeout), bytes, false)
        
    let tryRecv (Socket.Socket socket) (timeout:int<milliseconds>)  =
        let success, bytes, more = socket.TryReceiveFrameBytes (TimeSpan.FromMilliseconds (float timeout))
        
        match success with 
        | true -> Some (bytes, more)
        | false -> None
         
    let trySendMoreNow socket (bytes:byte[]) = trySendMore socket bytes 0<milliseconds>                 
    let trySendNow socket (bytes:byte[]) = trySend socket bytes 0<milliseconds>    
    let tryRecvNow socket = tryRecv socket 0<milliseconds>
    
module SingleFrame =
    let private skip (Socket.Socket socket) more = if more then socket.SkipMultipartMessage ()         
        
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
    
module Multipart =
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
    
    let skip (Socket.Socket socket) = socket.SkipMultipartMessage ()                     
           
module Poller = 
    type T = 
        | Poller of NetMQPoller 
        interface IDisposable with
          member x.Dispose() = 
             match x with
             | Poller p -> p.Dispose()
             
    let create () = Poller (new NetMQ.NetMQPoller())
    let run (Poller poller) = poller.Run ()
    let stop (Poller poller) = poller.Stop () 
    
    let addSocket (Poller poller) (Socket.Socket socket) = 
        poller.Add socket        
        Observable.map (fun _ -> socket) socket.ReceiveReady
        
    let addActor (Poller poller) (Actor.Actor actor) =
         poller.Add actor        
         Observable.map (fun _ -> actor) actor.ReceiveReady                  
    
    let addTimer (Poller poller) (Timer.Timer timer)=
        poller.Add timer
        Observable.map (fun _ -> timer) timer.Elapsed
         
    let removeSocket (Poller poller) (Socket.Socket socket) = poller.Remove socket
    let removeActor (Poller poller) (Actor.Actor actor) = poller.Remove actor
    let removeTimer (Poller poller) (Timer.Timer timer) = poller.Remove timer 
    
    let registerEndMessage poller shim = 
        let handler _ = 
            let msg = SingleFrame.recv shim
            let msg' = System.Text.Encoding.UTF8.GetString (msg)
            
            if msg' = NetMQActor.EndShimMessage then                    
              removeSocket poller shim
              stop poller
        
        let observer = 
            addSocket poller shim 
            |> Observable.subscribe handler
                    
        observer
           
module Stream =    
    type T = Stream of (byte[] * int)                                                            
    
    let create size = Stream (Array.create size 0uy,0)    
    let getBuffer (Stream (buffer, _)) = buffer
    let getOffset (Stream (_, offset)) = offset
    
    let recv socket =
        let buffer, more = Frame.recv socket
        Stream (buffer, 0), more
        
    let send socket (Stream (buffer, offset)) =
        let frame = 
            if Array.length buffer = offset then
                buffer
            else
                Array.sub buffer 0 offset
                
        Frame.send socket frame
    
    let bytesLeft (Stream (buffer, offset)) = (Array.length buffer) - offset    
    let isEnoughBytesLeft s bytes = bytesLeft s >= bytes  
    
    let reset (Stream (buffer, _)) = Stream (buffer, 0)
    let advance (Stream (buffer, offset)) by = Stream (buffer, offset + by)            
    let extendBy stream by =
        match isEnoughBytesLeft stream by with
        | true -> stream
        | _ -> 
            let oldBuffer = getBuffer stream
            let offset = getOffset stream
            let oldLength = Array.length oldBuffer
                        
            let newLength = if oldLength > by then oldLength * 2 else oldLength + by
            
            let newBuffer = Array.create newLength 0uy 
            
            Buffer.BlockCopy (oldBuffer,0, newBuffer,0, offset)
            Stream (newBuffer, offset)
        
    module Reader = 
        type Reader<'a> = T -> 'a option*T
        
        type ReaderBuilder() = 
            member this.Bind (r,f) =
                fun (stream:T) ->
                    let x, stream = r stream
                    match x with
                    | None -> None, stream 
                    | Some x -> f x stream 
            member this.Return x = fun buffer -> Some x,buffer
            member this.Yield x = fun buffer -> Some x,buffer
            member this.YieldFrom (r:Reader<'a>) = fun (stream:T) -> r stream
            member this.For (seq,body) = 
                fun (stream:T) -> 
                    let xs,stream = Seq.fold (fun (list, stream) i -> 
                                        match list with 
                                        | None -> None, stream
                                        | Some list -> 
                                            let x,stream = body i stream
                                            match x with 
                                            | None -> None,stream
                                            | Some x -> Some (x :: list),stream                                                                                                     
                                    ) (Some [], stream) seq
                    match xs with 
                    | None -> None, stream
                    | Some xs -> Some (Seq.rev xs), stream 
                                                    
        let reader = new ReaderBuilder()        
                     
        let run (r:Reader<'a>) buffer = 
            let x,_ = r buffer
            x
            
        let check b stream =
            if b then Some (),stream else None, stream
            
        let checkEnoughBytes size stream = 
            if isEnoughBytesLeft stream size then Some (), stream else None, stream            
    
    let writeNumber1 (n:byte) stream =
        let stream = extendBy stream 1 
        let offset = getOffset stream        
        let buffer = getBuffer stream
        buffer.[offset] <- n
        advance stream 1
        
    let readNumber1 stream =
        match isEnoughBytesLeft stream 1 with
        | false -> None, stream
        | _ ->
            let buffer = getBuffer stream
            let offset = getOffset stream             
            let x = buffer.[offset]
            Some x, (advance stream 1)                     
        
    let writeNumber2 (n:UInt16) stream =
        let stream = extendBy stream 2 
        let offset = getOffset stream        
        let buffer = getBuffer stream
     
        buffer.[offset] <- byte ((n >>> 8) &&& 255us)
        buffer.[offset+1] <- byte (n &&& 255us)        
        advance stream 2
        
    let readNumber2 stream =           
        match isEnoughBytesLeft stream 2 with
        | false -> None, stream
        | _ -> 
                let buffer = getBuffer stream
                let offset = getOffset stream
                let number = Some (
                                    ((uint16 (buffer.[offset])) <<< 8) + 
                                    (uint16 buffer.[offset+1]))
                
                number, (advance stream 2)
             
    let writeNumber4 (n:UInt32) stream =
        let stream = extendBy stream 4 
        let offset = getOffset stream        
        let buffer = getBuffer stream  
        buffer.[offset] <- byte ((n >>> 24) &&& 255u)
        buffer.[offset+1] <- byte ((n >>> 16) &&& 255u) 
        buffer.[offset+2] <- byte ((n >>> 8) &&& 255u) 
        buffer.[offset+3] <- byte ((n) &&& 255u)
        advance stream 4                           
             
    let readNumber4 stream =        
        match isEnoughBytesLeft stream 4 with
        | false -> None, stream
        | _ ->
            let buffer = getBuffer stream
            let offset = getOffset stream 
            let number = Some (
                                ((uint32 (buffer.[offset])) <<< 24) +
                                ((uint32 (buffer.[offset+1])) <<< 16) +
                                ((uint32 (buffer.[offset+2])) <<< 8) +
                                (uint32 buffer.[offset+3]))
            
            number, (advance stream 4)                                                        
        
    let writeNumber8 (n:UInt64) stream =
        let stream = extendBy stream 8 
        let offset = getOffset stream        
        let buffer = getBuffer stream  
        buffer.[offset] <- (byte) ((n >>> 56) &&& 255UL)
        buffer.[offset+1] <- (byte) ((n >>> 48) &&& 255UL)
        buffer.[offset+2] <- (byte) ((n >>> 40) &&& 255UL)
        buffer.[offset+3] <- (byte) ((n >>> 32) &&& 255UL)
        buffer.[offset+4] <- (byte) ((n >>> 24) &&& 255UL) 
        buffer.[offset+5] <- (byte) ((n >>> 16) &&& 255UL)
        buffer.[offset+6] <- (byte) ((n >>> 8)  &&& 255UL)
        buffer.[offset+7] <- (byte) ((n)       &&& 255UL)
                        
        advance stream 8
        
    let readNumber8 stream =            
        match isEnoughBytesLeft stream 8 with
        | false -> None, stream
        | _ ->
            let buffer = getBuffer stream
            let offset = getOffset stream 
            let number = Some (
                                ((uint64 (buffer.[offset]) <<< 56)) +
                                ((uint64 (buffer.[offset+1]) <<< 48)) +
                                ((uint64 (buffer.[offset+2]) <<< 40)) +
                                ((uint64 (buffer.[offset+3]) <<< 32)) +
                                ((uint64 (buffer.[offset+4]) <<< 24)) +
                                ((uint64 (buffer.[offset+5]) <<< 16)) +
                                ((uint64 (buffer.[offset+6]) <<< 8)) +
                                (uint64 buffer.[offset+7]))
                
            number, (advance stream 8)         
        
    let writeBytes (host:byte[]) size stream =
        let stream = extendBy stream size 
        let offset = getOffset stream
        let buffer = getBuffer stream
        System.Buffer.BlockCopy (host, 0, buffer, offset, size)
        advance stream size        
        
    let readBytes size (Stream (buffer,offset)) =
        match offset + size > Array.length buffer with
        | true -> None, Stream (buffer, offset)
        | _ -> 
            let bytes = Array.sub buffer offset size
            Some bytes, Stream (buffer, offset + size)                 
        
    let writeString (host:string) stream =
        let length = System.Text.Encoding.UTF8.GetByteCount(host)        
        let length' = if length > 255 then 255 else length                        
        
        let writeString' stream =
            let stream = extendBy stream length'
            let offset = getOffset stream
            let buffer = getBuffer stream
            System.Text.Encoding.UTF8.GetBytes(host, 0, length', buffer, offset) |> ignore
            advance stream length'
        
        stream 
        |> writeNumber1 (byte length')
        |> writeString'         
            
    let readString =
        let readString' length stream =
            let buffer = getBuffer stream
            let offset = getOffset stream 
            let value = System.Text.Encoding.UTF8.GetString (buffer, offset, length)            
            Some value, (advance stream length)
    
        Reader.reader {
            do! Reader.checkEnoughBytes 1
            let! length = readNumber1            
            do! Reader.checkEnoughBytes (int length)
            let! string = readString' (int length)
            
            return string
        }                                                       
        
    let writeLongString (host:string) stream =
        let length = System.Text.Encoding.UTF8.GetByteCount(host)        
                                        
        let writeString' stream =
            let stream = extendBy stream length
            let offset = getOffset stream
            let buffer = getBuffer stream
            System.Text.Encoding.UTF8.GetBytes(host, 0, length, buffer, offset) |> ignore
            advance stream length
        
        stream 
        |> writeNumber4 (uint32 length)
        |> writeString'       
            
    let readLongString =
        let readString' length stream =
            let buffer = getBuffer stream
            let offset = getOffset stream
            let value = System.Text.Encoding.UTF8.GetString (buffer, offset, length) 
            Some value, (advance stream length)
    
        Reader.reader {
            do! Reader.checkEnoughBytes 4
            let! length = readNumber4
            do! Reader.checkEnoughBytes (int length)
            let! string = readString' (int length)
            
            return string
        }               
        
    let writeStrings strings stream =
        let stream = writeNumber4 (uint32 (Seq.length strings)) stream 
        List.fold (fun state value -> writeLongString value state) stream strings
        
    let readStrings =    
        let readStrings = Reader.reader {
            let! length = readNumber4                        
            for _ in [1ul..length] do                    
                yield! readLongString                                                                                                      
        }
        
        Reader.reader {
            let! strings = readStrings                                    
            return List.ofSeq strings
        }                                               
            
    let writeMap map s =             
        let s' = writeNumber4 (uint32 (Seq.length map)) s
        Map.fold (fun b key value -> b |> writeString key |> writeLongString value) s' map
                        
    let readMap =                             
           let readPairs =
               Reader.reader {
                   let! length = readNumber4                   
                                      
                   for _ in [1ul..length] do
                       let! key = readString
                       let! value = readLongString
                       
                       yield (key,value)                                                              
               }
                   
           Reader.reader {
               let! pairs = readPairs
               return Map.ofSeq pairs
           }