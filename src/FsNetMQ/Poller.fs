[<RequireQualifiedAccess;CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module FsNetMQ.Poller

open NetMQ

type T = Poller 
             
let create () = Poller (new NetMQ.NetMQPoller())
let run (Poller poller) = poller.Run ()
let stop (Poller poller) = poller.Stop () 

let addSocket (Poller poller) (socket:Socket) = 
    poller.Add socket.Socket        
    Observable.map (fun _ -> socket) socket.Socket.ReceiveReady
    
let addActor (Poller poller) (Actor.Actor (actor,_)) =
     poller.Add actor        
     Observable.map (fun _ -> actor) actor.ReceiveReady                  

let addTimer (Poller poller) (Timer.Timer timer)=
    poller.Add timer
    Observable.map (fun _ -> timer) timer.Elapsed
     
let removeSocket (Poller poller) (socket:Socket) = poller.Remove socket.Socket
let removeActor (Poller poller) (Actor.Actor (actor,_)) = poller.Remove actor
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

