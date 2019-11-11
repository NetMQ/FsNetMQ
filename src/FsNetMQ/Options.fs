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