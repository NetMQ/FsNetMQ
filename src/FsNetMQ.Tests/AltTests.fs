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
            let handleServer = Frame.recvAsync server ^-> fun _ ->
                Frame.send server "World"B
                Choice1Of2 ()            
            
            Frame.send client "Hello"B         
                                   
            let cont =
                Alt.Choose [
                    handleClient
                    handleServer
                ]                 
                                    
            Alt.Iterate () (fun _ -> cont)
            |> Alt.Run           
    ]

