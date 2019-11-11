module FsNetMQ.Pair

open NetMQ

let createPairs () = 
    let p1,p2 = Sockets.PairSocket.CreateSocketPair ()        
    new Socket.T (p1), new Socket.T (p2)


