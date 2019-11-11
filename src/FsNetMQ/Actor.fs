[<RequireQualifiedAccess;CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module FsNetMQ.Actor

open NetMQ
open System

type T = FsNetMQ.Actor

let endMessage = NetMQActor.EndShimMessage             
             
let create shim = 
    let handler (pipe:Sockets.PairSocket) = shim (new Socket.T (pipe))
    let actor = NetMQActor.Create (handler)
    let socketPollable = actor :> ISocketPollable    
    let socket = new Socket.T(socketPollable.Socket)
    
    Actor (actor, socket)
    
let signal (socket:Socket) = socket.Socket.SignalOK ()
         
let asSocket (Actor (_, socket)) = socket 

