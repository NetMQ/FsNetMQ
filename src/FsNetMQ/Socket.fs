[<RequireQualifiedAccess;CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module FsNetMQ.Socket

open System
open NetMQ

type T = Socket
                 
let dealer () = new T (new Sockets.DealerSocket())
let router () = new T (new Sockets.RouterSocket())
let req () = new T (new Sockets.RequestSocket())
let rep () = new T (new Sockets.ResponseSocket())
let sub () = new T (new Sockets.SubscriberSocket())
let pub () = new T (new Sockets.PublisherSocket())
let xsub () = new T (new Sockets.XSubscriberSocket())
let xpub () = new T (new Sockets.XPublisherSocket())
let push () = new T (new Sockets.PushSocket())
let pull () = new T (new Sockets.PullSocket())
let pair () = new T (new Sockets.PairSocket())
let stream () = new T (new Sockets.StreamSocket())
let peer () = new T (new Sockets.PeerSocket())

let connect (socket:Socket) address = socket.Socket.Connect address
let bind (socket:Socket) address = socket.Socket.Bind address
let disconnect (socket:Socket) address = socket.Socket.Disconnect address
let unbind (socket:Socket) address = socket.Socket.Unbind address

let subscribe (socket:Socket) (subscription:string) = 
    match socket.Socket with 
    | :? Sockets.SubscriberSocket as sub -> sub.Subscribe subscription
    | :? Sockets.XSubscriberSocket as xsub -> xsub.Subscribe subscription
    | _ -> invalidArg "socket" "Socket is not a sub or xsub socket"

let unsubscribe (socket:Socket) (subscription:string) = 
    match socket.Socket with 
    | :? Sockets.SubscriberSocket as sub -> sub.Unsubscribe subscription
    | :? Sockets.XSubscriberSocket as xsub -> xsub.Unsubscribe subscription
    | _ -> invalidArg "socket" "Socket is not a sub or xsub socket"
    
let alt (socket:Socket) =
    async {            
        match Runtime.Current with
        | None ->
            return 
                Async.NoRuntimeError "When using FsNetMQ async operation you must use Async.RunWithRuntime"
                |> raise
        | Some runtime ->
            runtime.Add socket
            let! _ = Async.AwaitEvent socket.Socket.ReceiveReady             
            return socket
    }
    |> Alt.fromAsync