[<AutoOpen>]
module FsNetMQ.Async

open System.Runtime.ExceptionServices
open System
open System.Threading
open NetMQ

type internal Runtime() =
    inherit SynchronizationContext()
    static let current = new ThreadLocal<Runtime option ref>(fun () -> ref None)

    let poller = new NetMQPoller()
    let sockets = new System.Collections.Generic.HashSet<Socket>()

    static member internal Current =
        current.Value.Value

    member this.Add(socket: Socket) =
        if sockets.Add socket then
            poller.Add socket.Socket

            { new System.IDisposable with
                member __.Dispose() =
                     if sockets.Remove socket then
                        poller.Remove socket.Socket
            }
            |> socket.AttachToRuntime

    member this.Run(cont, ?cancellationToken) =
        current.Value := Some this
        let prevCtx = SynchronizationContext.Current
        SynchronizationContext.SetSynchronizationContext this

        let mutable result = None
        let setResult res =
            if Interlocked.CompareExchange(&result, Some res, None) = None && poller.IsRunning then
                poller.Stop()

        Async.StartWithContinuations (cont,
                                      (fun res -> setResult <| Choice1Of3 res),
                                      (fun exc -> setResult <| Choice2Of3(ExceptionDispatchInfo.Capture exc)),
                                      (fun exc -> setResult <| Choice3Of3(ExceptionDispatchInfo.Capture exc)),
                                      ?cancellationToken = cancellationToken)

        if Option.isNone result then
            poller.Run()

        // Detaching and removing all sockets
        Seq.iter (fun (socket: Socket) ->
            poller.Remove(socket.Socket)
            socket.DetachFromRuntime()
            ) sockets
        sockets.Clear()

        current.Value := None
        SynchronizationContext.SetSynchronizationContext prevCtx
        try
            poller.Dispose()
        with
        | _ -> () // NetMQ bug throws exception if poller never ran

        match result with
        | Some(Choice1Of3 result) -> result
        | Some(Choice2Of3 edi) ->
            edi.Throw()
            failwith "unreachable"
        | Some(Choice3Of3 edi) ->
            edi.Throw()
            failwith "unreachable"
        | None -> failwith "Result is missing"

    override this.Post(d, state) =
        // TODO: enqueue action directly on the poller, without task
        let task = new Tasks.Task(fun () -> d.Invoke(state))
        task.Start(poller)

    override this.Send(d, state) =
        // TODO: enqueue action directly on the poller, without task
        let task = new Tasks.Task(fun () -> d.Invoke(state))
        task.Start(poller)
        task.Wait()


exception NoRuntimeError of string

type private Promise<'T>() =
    let mutable result = None
    let mutable setResult = fun result' -> result <- Some result'

    let async = Async.FromContinuations<'T>(fun (cont, econt, ccont) ->
        match result with
        | Some(Choice1Of3 result) -> cont result
        | Some(Choice2Of3 ex) -> econt ex
        | Some(Choice3Of3 ex) -> ccont ex
        | None ->
            setResult <- function
                | Choice1Of3 result -> cont result
                | Choice2Of3 ex -> econt ex
                | Choice3Of3 ex -> ccont ex
        )

    member __.SetResult x = setResult (Choice1Of3 x)
    member __.SetException x = setResult (Choice2Of3 x)
    member __.SetCanceled x = setResult (Choice3Of3 x)
    member __.Async = async


let internal (^=>.) comp (y: 'y) : Async<'y> = async {
    let! _ = comp
    return y
}

let internal (^=>) comp (f: 'x->'y) : Async<'y> = async {
    let! x = comp
    return f (x)
}       

type FSharp.Control.Async with
    static member RunWithRuntime(cont, ?cancellationToken:CancellationToken) =               
        let runtime = new Runtime()
        runtime.Run (cont, ?cancellationToken=cancellationToken)
    
    static member ParallelInContext (computations: seq<Async<'T>>) : Async<'T []> =
        let startInContext token task =           
            let promise = new Promise<'x>()
            Async.StartWithContinuations (task,
                                          promise.SetResult,
                                          promise.SetException,
                                          promise.SetCanceled,                                              
                                          token)
            promise.Async
            
        let folder a b = async {
            let! xs = a
            let! x = b
            return x :: xs
        }            

        async {           
           let! token = Async.CancellationToken
           let! results =
               computations
               |> Seq.map (startInContext token)
               |> Seq.fold folder (async.Return([]))
           return List.toArray results                            
        } 
    
    static member ChooseInContext(computations : Async<'T> seq) : Async<'T> = async {
        let computations = Seq.toArray computations

        if computations.Length = 0 then
            return invalidArg "computations" "computations must not be empty"
        else                
            let! cancellationToken = Async.CancellationToken
            use innerCTS = new CancellationTokenSource()
            use linkedCTS = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, innerCTS.Token)
            let promise = new Promise<'T>()

            let mutable count = computations.Length
            let complete res =
                let decrement = Interlocked.Decrement(&count) 

                if decrement = computations.Length - 1 then
                    match res with
                    | Choice1Of3 x -> promise.SetResult x
                    | Choice2Of3 (x:exn) -> promise.SetException x
                    | Choice3Of3 (x:OperationCanceledException) -> promise.SetCanceled x 
                    innerCTS.Cancel()

                if decrement = 0 then
                    linkedCTS.Dispose()
                    innerCTS.Dispose()

            Array.iter (fun cont ->
                Async.StartWithContinuations (cont,
                                              (fun res -> complete (Choice1Of3 res)),
                                              (fun exc -> complete (Choice2Of3 exc)),
                                              (fun exc -> complete (Choice3Of3 exc)),
                                              linkedCTS.Token)) computations

            return! promise.Async
    }
