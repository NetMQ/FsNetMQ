module FsNetMQ.Tests.AsyncTests

open Expecto
open FsNetMQ
open FsNetMQ.Async

[<Tests>]
let tests =
    testList "Async Tests" [
        testCase "Request-Response" <| fun () ->
            use server = Socket.dealer ()
            Socket.bind server "tcp://127.0.0.1:5556"
                        
            use client = Socket.dealer ()
            Socket.connect client "tcp://127.0.0.1:5556"
            
            let client =
                async {
                    Frame.send client "hello"B
                    let! (response,_) = Frame.recvAsync client
                    ()
                }
            
            let server =
                async {
                    let! (request, _) = Frame.recvAsync server
                    Frame.send server "world"B
                    ()
                }                            
                                    
            Async.ParallelImmediate [client; server]                                            
            |> Async.RunWithRuntime
            |> ignore
        
        testCase "with timeout expired" <| fun () ->
           use client = Socket.dealer ()
            
           async {
                let! _ = Frame.tryRecvAsync client 10<milliseconds>
                ()
           }
           |> Async.RunWithRuntime
           
        
           
    ]