namespace FsNetMQ

open System
open NetMQ

[<Measure>] 
type milliseconds

type RoutingId = RoutingId of byte[]
type Timer = Timer of NetMQTimer
type Stream = Stream of (byte[] * int)

type Socket(socket:NetMQSocket) =
    let mutable runtimeSubscription:System.IDisposable option = None
    
    member x.Socket = socket
    
    member internal x.AttachToRuntime(subscription) =
        runtimeSubscription <- Some subscription
        
    member internal x.DetachFromRuntime() =
        runtimeSubscription <- None
    
    interface IDisposable with
        member x.Dispose() =
            Option.iter (fun (subscription:System.IDisposable) -> subscription.Dispose()) runtimeSubscription
            runtimeSubscription <- None
            socket.Dispose()

type Actor =
    | Actor of NetMQActor*Socket
    interface IDisposable with 
        member x.Dispose () = 
            let (Actor (actor,_)) = x
            actor.Dispose ()
            
type Poller =
    | Poller of NetMQPoller 
    interface System.IDisposable with
      member x.Dispose() = 
         match x with
         | Poller p -> p.Dispose()