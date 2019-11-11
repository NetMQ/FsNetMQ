module FsNetMQ.Peer

let connect (socket:Socket) address = 
        socket.Socket.Connect address
        RoutingId.RoutingId socket.Socket.Options.LastPeerRoutingId

