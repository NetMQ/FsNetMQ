module FsNetMQ.Tests.AltTests

open System
open Expecto
open FsNetMQ

[<Tests>]
let tests =
    testList "Alt Tests" [
        ftestCase "Recv from multiple sockets" <| fun () ->
            use server = Socket.dealer ()
            Socket.bind server "tcp://*:5555"
            
            use client = Socket.dealer ()
            Socket.connect client "tcp://127.0.0.1:5555"
            
            let handleClient () = async {
                let! _ = Frame.recvAsync client
                return Choice2Of2 ()               
            }
            
            let handleServer () = async {
                let! _ = Frame.recvAsync server
                printfn "Server"
                Frame.send server "World"B
                return Choice1Of2 ()
            }
            
            Frame.send client "Hello"B         
                                   
            let t = Alt.choose [
                Socket.alt client ^=> handleClient
                Socket.alt server ^=> handleServer
            ]
            
            Async.Iterate () (fun _ -> t)
            |> Async.RunWithRuntime                                               
    ]

