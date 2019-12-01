[<AutoOpen>]
module FsNetMQ.Async

open System.Runtime.ExceptionServices
open System.Threading
open System.Threading.Tasks
open NetMQ

type internal Runtime() =
    inherit SynchronizationContext()
    static let current = new ThreadLocal<Runtime option ref>(fun () -> ref None)        
    
    let poller = new NetMQPoller()
    let sockets = new System.Collections.Generic.HashSet<Socket>()
    
    static member internal Current =            
        current.Value.Value
    
    member this.Add(socket:Socket) =
        if sockets.Add socket then
            poller.Add socket.Socket
            
            { new System.IDisposable with
                member __.Dispose() =
                     if sockets.Remove socket then
                        poller.Remove socket.Socket
            }
            |> socket.AttachToRuntime        
            
    member this.Run (cont, ?cancellationToken) =
        current.Value := Some this
        let prevCtx = SynchronizationContext.Current
        SynchronizationContext.SetSynchronizationContext this
                    
        let mutable result = None                                        
        let setResult res =
            if Interlocked.CompareExchange(&result, Some res, None) = None && poller.IsRunning then
                poller.Stop()
                                        
        Async.StartWithContinuations (cont,
                                      (fun res -> setResult <| Choice1Of3 res),
                                      (fun exc -> setResult <| Choice2Of3 (ExceptionDispatchInfo.Capture exc)),
                                      (fun exc -> setResult <| Choice3Of3 (ExceptionDispatchInfo.Capture exc)),
                                      ?cancellationToken=cancellationToken)
        
        if Option.isNone result then                                            
            poller.Run()        
        
        // Detaching and removing all sockets
        Seq.iter (fun (socket:Socket) ->
            poller.Remove (socket.Socket)
            socket.DetachFromRuntime ()                
            ) sockets            
        sockets.Clear()
                    
        current.Value := None
        SynchronizationContext.SetSynchronizationContext prevCtx
        try
            poller.Dispose()
        with
        | _ -> () // NetMQ bug throws exception if poller never ran
                              
        match result with
        | Some (Choice1Of3 result) -> result
        | Some (Choice2Of3 edi) ->
            edi.Throw()
            failwith "unreachable"
        | Some (Choice3Of3 edi) ->
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

type Promise<'T>() =
    let mutable result = None
    let mutable setResult = fun result' -> result <- Some result'
        
    let async = Async.FromContinuations<'T> (fun (cont, econt, ccont) ->
        match result with
        | Some (Choice1Of3 result) -> cont result
        | Some (Choice2Of3 ex) -> econt ex
        | Some (Choice3Of3 ex) -> ccont ex
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
    


                                      