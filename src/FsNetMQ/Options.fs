[<RequireQualifiedAccess>]
module FsNetMQ.Options

open System

let sendHighWatermark (socket:Socket) = socket.Socket.Options.SendHighWatermark        
     
let setSendHighWatermark (socket:Socket) highWatermark = 
    socket.Socket.Options.SendHighWatermark <- highWatermark

let recvHighWatermark (socket:Socket) = socket.Socket.Options.ReceiveHighWatermark        
    
let setRecvHighwatermark (socket:Socket) highWatermark = 
    socket.Socket.Options.ReceiveHighWatermark <- highWatermark     
    
let linger (socket:Socket) = 
    (int socket.Socket.Options.Linger.TotalMilliseconds) * 1<milliseconds>
    
let setLinger (socket:Socket) (value:int<milliseconds>) = 
    socket.Socket.Options.Linger <- TimeSpan.FromMilliseconds (float value)    

let setHelloMessage (socket:Socket) (value:byte[]) =
    socket.Socket.Options.HelloMessage <- value

let heartbeatInterval (socket:Socket) = 
    (int socket.Socket.Options.HeartbeatInterval.TotalMilliseconds) * 1<milliseconds>
    
let setHeartbeatInterval (socket:Socket) (value:int<milliseconds>) = 
    socket.Socket.Options.HeartbeatInterval <- TimeSpan.FromMilliseconds (float value)    

let heartbeatTtl (socket:Socket) = 
    (int socket.Socket.Options.HeartbeatTtl.TotalMilliseconds) * 1<milliseconds>
    
let setHeartbeatTtl (socket:Socket) (value:int<milliseconds>) = 
    socket.Socket.Options.HeartbeatTtl <- TimeSpan.FromMilliseconds (float value)    

let heartbeatTimeout (socket:Socket) = 
    (int socket.Socket.Options.HeartbeatTimeout.TotalMilliseconds) * 1<milliseconds>
    
let setHeartbeatTimeout (socket:Socket) (value:int<milliseconds>) = 
    socket.Socket.Options.HeartbeatTimeout <- TimeSpan.FromMilliseconds (float value)    


