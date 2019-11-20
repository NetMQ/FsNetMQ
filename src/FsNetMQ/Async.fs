[<AutoOpen>]
module FsNetMQ.Async

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
            if Interlocked.CompareExchange(&result, Some res, None) = None then
                poller.Stop()
                                        
        Async.StartWithContinuations (cont,
                                      (fun res -> setResult <| Choice1Of3 res),
                                      (fun exc -> setResult <| Choice2Of3 exc),
                                      (fun exc -> setResult <| Choice3Of3 exc),
                                      ?cancellationToken=cancellationToken)
                                           
        poller.Run()
        
        // Detaching and removing all sockets
        Seq.iter (fun (socket:Socket) ->
            poller.Remove (socket.Socket)
            socket.DetachFromRuntime ()                
            ) sockets            
        sockets.Clear()
                    
        current.Value := None
        SynchronizationContext.SetSynchronizationContext prevCtx
        poller.Dispose()                       
                              
        match result with
        | Some (Choice1Of3 result) -> result
        | Some (Choice2Of3 exc) -> raise exc
        | Some (Choice3Of3 exc) -> raise exc
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
         

type Microsoft.FSharp.Control.Async with
    static member RunWithRuntime(cont, ?cancellationToken:CancellationToken) =               
        let runtime = new Runtime()
        runtime.Run (cont, ?cancellationToken=cancellationToken)

    static member Iterate (state:'T1) (f:'T1->Async<Choice<'T1,'T2>>) =
        async {                                             
            let mutable state = state            
            let mutable result = None
            
            while Option.isNone result do
                let! x = f state
                match x with
                | Choice1Of2 x->
                    state <- x
                | Choice2Of2 x->
                    result <- Some x                    
            
            return Option.get result                    
        }        
    
    /// <summary>Creates an asynchronous computation that executes all the given asynchronous computations sequentially,
    /// starting immediately on the current operating system thread</summary>.            
    static member SequentialImmediate (computations: seq<Async<'T>>) : Async<'T []> =        
        let folder a b =
            async.Bind (a, fun xs ->
                async {
                    let! x = b
                    return x :: xs
                }
            )
        
        async {
            let! results =
               computations
               |> Seq.fold folder (async.Return([]))
            
            return List.toArray results                                                           
        }
    
    /// <summary>Creates an asynchronous computation that executes all the given asynchronous computations in parallel,
    /// return the result of the first succeeding computation,
    /// starting immediately on the current operating system thread</summary>.       
    static member ChoiceImmediate(computations : Async<'T option> seq) : Async<'T option> =
        async {            
            let computations = Seq.toArray computations
            
            if computations.Length = 0 then
                return None
            else                
                let! cancellationToken = Async.CancellationToken
                use innerCTS = new CancellationTokenSource()
                use linkedCTS = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, innerCTS.Token)
                let promise = new Promise<'T option>()
                                
                let mutable count = computations.Length
                let complete res =
                    let decrement = Interlocked.Decrement(&count) 
                    
                    if decrement = computations.Length - 1 then
                        match res with
                        | Choice1Of3 x -> promise.SetResult x
                        | Choice2Of3 x -> promise.SetException x
                        | Choice3Of3 x -> promise.SetCanceled x
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
                    
    /// <summary>Creates an asynchronous computation that executes all the given asynchronous computations,
    /// starting immediately on the current operating system thread</summary>.        
    static member ParallelImmediate (computations: seq<Async<'T>>) : Async<'T []> =
        let startImmediate token task =
            Async.FromContinuations (fun (cont, ccont, econt) ->
                let promise = new Promise<'T>()                 
                Async.StartWithContinuations (task,
                                              promise.SetResult,
                                              promise.SetException,
                                              promise.SetCanceled,
                                              token)
                cont promise.Async  
            )
            
        let folder a b =
            async.Bind (a, fun xs ->
                async {
                    let! x = b
                    return x :: xs
                }
            )
        
        async {           
           let! token = Async.CancellationToken
           let! tasks =
               computations
               |> Seq.map (startImmediate token)
               |> Seq.fold folder (async.Return([]))
           let! results = Seq.fold folder (async.Return([])) tasks
           return List.toArray results                            
        }     