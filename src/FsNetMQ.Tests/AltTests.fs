module FsNetMQ.Tests.AltTests

open System
open Expecto
open FsNetMQ

[<Tests>]
let tests =
    testList "Alt Tests" [
        testCase "Recv from multiple sockets" <| fun () ->
            use server = Socket.dealer ()
            Socket.bind server "tcp://*:5555"
            
            use client = Socket.dealer ()
            Socket.connect client "tcp://127.0.0.1:5555"                        
            
            let handleClient = Frame.recvAsync client ^->. Choice2Of2 ()
            let handleServer = Frame.recvAsync server ^=> fun _ -> async {
                Frame.send server "World"B
                return Choice1Of2 ()
            }
            
            Frame.send client "Hello"B         
                                   
            let cont =
                Alt.Choose [
                    handleClient
                    handleServer
                ]                 
                                    
            Alt.Iterate () (fun _ -> cont)
            |> Alt.Run
            
        testCase "awaiting Alt expression" <| fun () ->
            use server = Socket.dealer ()
            Socket.bind server "tcp://*:5556"
            
            use client = Socket.dealer ()
            Socket.connect client "tcp://127.0.0.1:5556"
            
            let comp = async {
                Frame.send client "Hello"B
                let! _ = Frame.recvAsync server
                return ()
            }
            
            Alt.Run comp
    ]

