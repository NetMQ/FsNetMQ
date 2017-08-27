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
                 
    let create shim = 
        let handler (pipe:Sockets.PairSocket) = shim (Socket.Socket pipe)
        Actor (NetMQActor.Create (handler))
        
    let signal (Socket.Socket socket) = socket.SignalOK ()
             
    let asSocket (Actor actor) = 
        let socketPollable = actor :> ISocketPollable    
        Socket.Socket socketPollable.Socket     
        
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
                 
        
           
        